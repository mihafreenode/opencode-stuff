using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.LocalClient;
using OpenCode.Workspace.Mcp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

using LocalProgressLevel = OpenCode.Workspace.LocalClient.WorkspaceOperationProgressLevel;
using LocalTemplateDetailModel = OpenCode.Workspace.LocalClient.WorkspaceTemplateDetailModel;
using LocalTemplateSummaryModel = OpenCode.Workspace.LocalClient.WorkspaceTemplateSummaryModel;
using LocalWorkspaceRecordModel = OpenCode.Workspace.LocalClient.WorkspaceRecordModel;

namespace OpenCode.Workspace.Api;

public static class LocalHostServiceCollectionExtensions
{
    public static IServiceCollection AddOpenCodeWorkspaceLocalHostServices(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new OpenCodeWorkspaceMcpOptions();
        configuration.GetSection("mcp").Bind(options);
        var localHostStateOptions = new LocalHostStateOptions
        {
            StateRoot = configuration["localHost:stateRoot"]
                ?? configuration["mcp:workspaceStateRoot"]
                ?? string.Empty,
        };
        services.AddSingleton(options);
        services.AddSingleton(localHostStateOptions);
        services.AddSingleton<ILocalHostStatePathProvider>(sp => new DefaultLocalHostStatePathProvider(sp.GetRequiredService<LocalHostStateOptions>()));
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<IOpenCodeWorkspaceMcpService, OpenCodeWorkspaceMcpService>();
        services.AddSignalR();
        services.AddSingleton<LocalHostStateStore>();
        services.AddSingleton<LocalHostEventService>();
        services.AddSingleton<IWorkspaceOperationService, WorkspaceOperationService>();
        services.AddSingleton<ControllerSessionService>();
        services.AddSingleton<WorkspaceInstanceService>();
        services.AddSingleton<InteractiveAgentSessionService>();
        services.AddSingleton<InteractiveAttachmentLeasePolicy>();
        services.AddSingleton<InteractiveSessionLaunchDescriptorFactory>();
        services.AddSingleton<InteractiveSessionAttachmentService>();
        services.AddSingleton<LocalHostApplicationService>();
        services.AddHostedService<LocalHostDescriptorHostedService>();
        services.AddHostedService(sp => (WorkspaceOperationService)sp.GetRequiredService<IWorkspaceOperationService>());
        return services;
    }
}

public sealed class LocalHostEventHub : Hub;

public sealed class LocalHostEventService(IHubContext<LocalHostEventHub> hubContext)
{
    private long _sequence;

    public async Task PublishAsync(string eventKind, object payload, string hostInstanceId, string workspaceInstanceId = "", string operationId = "", string controllerSessionId = "", string interactiveAgentSessionId = "", string attachmentId = "")
    {
        var envelope = new WorkspaceEventEnvelope
        {
            HostInstanceId = hostInstanceId,
            Sequence = Interlocked.Increment(ref _sequence),
            EventId = Guid.NewGuid().ToString("n"),
            TimestampUtc = DateTimeOffset.UtcNow,
            EventKind = eventKind,
            WorkspaceInstanceId = workspaceInstanceId,
            OperationId = operationId,
            ControllerSessionId = controllerSessionId,
            InteractiveAgentSessionId = interactiveAgentSessionId,
            AttachmentId = attachmentId,
            Payload = JsonSerializer.SerializeToElement(payload, LocalHostContract.JsonOptions),
        };
        await hubContext.Clients.All.SendAsync("event", envelope);
    }
}

public sealed class LocalHostStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = LocalHostContract.JsonOptions;
    private static readonly JsonSerializerOptions CompactJsonOptions = LocalHostContract.CompactJsonOptions;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILocalHostStatePathProvider _paths;

    public string LocalHostRoot => _paths.LocalHostRoot;
    public string StateRoot => _paths.StateRoot;
    public string ShutdownMarkerPath => Path.Combine(LocalHostRoot, "graceful-shutdown.json");
    public string WorkspaceInstancesRoot => _paths.WorkspaceInstancesRoot;
    public string ControllerSessionsRoot => _paths.ControllerSessionsRoot;
    public string InteractiveSessionsRoot => _paths.InteractiveSessionsRoot;
    public string OperationsRoot => _paths.OperationsRoot;

    public string DescriptorPath => _paths.DescriptorPath;
    public string LockPath => _paths.LockPath;
    public bool WasPreviousShutdownClean { get; }

    public LocalHostStateStore(ILocalHostStatePathProvider paths)
    {
        _paths = paths;
        WasPreviousShutdownClean = File.Exists(ShutdownMarkerPath);
        Directory.CreateDirectory(LocalHostRoot);
        Directory.CreateDirectory(WorkspaceInstancesRoot);
        Directory.CreateDirectory(ControllerSessionsRoot);
        Directory.CreateDirectory(InteractiveSessionsRoot);
        Directory.CreateDirectory(OperationsRoot);
    }

    public async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tempPath = path + ".tmp";
            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
    }

    public async Task AppendJsonLineAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.AppendAllTextAsync(path, JsonSerializer.Serialize(value, CompactJsonOptions) + Environment.NewLine, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public interface IWorkspaceOperationService
{
    IReadOnlyList<WorkspaceOperationRecord> List();
    WorkspaceOperationRecord Get(string operationId, long? afterSequence = null, int? maxEvents = null);
    Task<WorkspaceOperationRecord> StartAsync(string operationKind, WorkspaceOperationScope scope, string workspaceId, string workspaceInstanceId, OperationInitiator initiatedBy, Func<WorkspaceOperationReporter, CancellationToken, Task<object>> work, string dedupeKey, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationRecord> CancelAsync(string operationId, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceOperationService : BackgroundService, IWorkspaceOperationService
{
    private readonly ConcurrentDictionary<string, WorkspaceOperationRuntimeState> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _dedupe = new(StringComparer.OrdinalIgnoreCase);
    private readonly LocalHostStateStore _stateStore;
    private readonly LocalHostEventService _events;
    private readonly ILogger<WorkspaceOperationService> _logger;
    private readonly string _hostInstanceId = Guid.NewGuid().ToString("n");

    public WorkspaceOperationService(LocalHostStateStore stateStore, LocalHostEventService events, ILogger<WorkspaceOperationService> logger)
    {
        _stateStore = stateStore;
        _events = events;
        _logger = logger;
        LoadPersistedOperations();
    }

    public IReadOnlyList<WorkspaceOperationRecord> List()
        => _active.Values.Select(item => item.Record).OrderByDescending(item => item.CreatedUtc).ToArray();

    public WorkspaceOperationRecord Get(string operationId, long? afterSequence = null, int? maxEvents = null)
    {
        if (!_active.TryGetValue(operationId, out var state))
        {
            throw new OpenCodeWorkspaceMcpException("operation_not_found", $"Operation '{operationId}' was not found.", "Refresh the operation list and retry.");
        }

        if (!afterSequence.HasValue && !maxEvents.HasValue)
        {
            return state.Record;
        }

        var recentEvents = state.Record.RecentEvents.Where(item => !afterSequence.HasValue || item.Sequence > afterSequence.Value);
        if (maxEvents.HasValue)
        {
            recentEvents = recentEvents.TakeLast(Math.Max(1, maxEvents.Value));
        }

        return state.Record with { RecentEvents = recentEvents.ToArray() };
    }

    public async Task<WorkspaceOperationRecord> StartAsync(string operationKind, WorkspaceOperationScope scope, string workspaceId, string workspaceInstanceId, OperationInitiator initiatedBy, Func<WorkspaceOperationReporter, CancellationToken, Task<object>> work, string dedupeKey, CancellationToken cancellationToken = default)
    {
        if (_dedupe.TryGetValue(dedupeKey, out var existingId) && _active.TryGetValue(existingId, out var existingState) && !IsTerminal(existingState.Record.Status))
        {
            return existingState.Record;
        }

        var operationId = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;
        var operationRoot = Path.Combine(_stateStore.OperationsRoot, operationId);
        Directory.CreateDirectory(operationRoot);
        var state = new WorkspaceOperationRuntimeState(
            new WorkspaceOperationRecord
            {
                OperationId = operationId,
                OperationKind = operationKind,
                OperationScope = scope,
                WorkspaceId = workspaceId,
                WorkspaceInstanceId = workspaceInstanceId,
                Status = WorkspaceOperationStatus.Pending,
                CurrentPhase = "queued",
                ProgressMessage = string.Empty,
                CreatedUtc = now,
                LastUpdatedUtc = now,
                InitiatedBy = initiatedBy,
                CancellationState = WorkspaceOperationCancellationState.None,
                PhaseHistory = ["queued"],
                ArtifactReferences =
                [
                    BuildArtifact(operationId, workspaceInstanceId, "operation-progress-jsonl", Path.Combine(operationRoot, "operation-progress.jsonl"), "application/x-ndjson"),
                    BuildArtifact(operationId, workspaceInstanceId, "operation-progress-text", Path.Combine(operationRoot, "operation-progress.txt"), "text/plain"),
                ],
            },
            operationRoot,
            new CancellationTokenSource());

        _active[operationId] = state;
        _dedupe[dedupeKey] = operationId;
        await PersistOperationAsync(state, cancellationToken);
        await _events.PublishAsync("operationStarted", state.Record, _hostInstanceId, workspaceInstanceId: workspaceInstanceId, operationId: operationId);

        _ = Task.Run(async () =>
        {
            var reporter = new WorkspaceOperationReporter(state, PersistOperationAsync, _events, _hostInstanceId, _logger);
            try
            {
                reporter.MarkStarted("queued", "Operation started.");
                var result = await work(reporter, state.TokenSource.Token);
                reporter.ApplyResult(result);
            }
            catch (OperationCanceledException)
            {
                reporter.MarkCancelled();
            }
            catch (Exception exception)
            {
                reporter.MarkFailed(exception);
            }
            finally
            {
                reporter.MarkCompleted();
            }
        }, cancellationToken);

        return state.Record;
    }

    public async Task<WorkspaceOperationRecord> CancelAsync(string operationId, CancellationToken cancellationToken = default)
    {
        if (!_active.TryGetValue(operationId, out var state))
        {
            throw new OpenCodeWorkspaceMcpException("operation_not_found", $"Operation '{operationId}' was not found.", "Refresh the operation list and retry.");
        }

        state.Record = state.Record with
        {
            Version = state.Record.Version + 1,
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            CancellationState = WorkspaceOperationCancellationState.Requested,
        };
        state.TokenSource.Cancel();
        await PersistOperationAsync(state, cancellationToken);
        await _events.PublishAsync("operationCancellationRequested", state.Record, _hostInstanceId, workspaceInstanceId: state.Record.WorkspaceInstanceId, operationId: state.Record.OperationId);
        return state.Record;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.CompletedTask;

    private async Task PersistOperationAsync(WorkspaceOperationRuntimeState state, CancellationToken cancellationToken = default)
    {
        await _stateStore.WriteJsonAsync(Path.Combine(state.OperationRoot, "operation.json"), state.Record, cancellationToken);
    }

    private void LoadPersistedOperations()
    {
        foreach (var directory in Directory.EnumerateDirectories(_stateStore.OperationsRoot))
        {
            var path = Path.Combine(directory, "operation.json");
            var record = _stateStore.ReadJson<WorkspaceOperationRecord>(path);
            if (record is null)
            {
                continue;
            }

            if (record.Status is WorkspaceOperationStatus.Pending or WorkspaceOperationStatus.Running)
            {
                record = record with
                {
                    Version = record.Version + 1,
                    Status = WorkspaceOperationStatus.Interrupted,
                    CompletedUtc = DateTimeOffset.UtcNow,
                    LastUpdatedUtc = DateTimeOffset.UtcNow,
                    OriginalFailure = record.OriginalFailure ?? new WorkspaceOperationFailure { Classification = "interrupted", Message = "LocalHost stopped before the operation finished." },
                };
                File.WriteAllText(path, JsonSerializer.Serialize(record, LocalHostContract.JsonOptions), Encoding.UTF8);
            }

            _active[record.OperationId] = new WorkspaceOperationRuntimeState(record, directory, new CancellationTokenSource());
        }
    }

    private static bool IsTerminal(WorkspaceOperationStatus status)
        => status is WorkspaceOperationStatus.Succeeded or WorkspaceOperationStatus.Failed or WorkspaceOperationStatus.Cancelled or WorkspaceOperationStatus.Interrupted;

    private static WorkspaceOperationArtifactReference BuildArtifact(string operationId, string workspaceInstanceId, string kind, string path, string contentType)
        => new()
        {
            ArtifactId = Guid.NewGuid().ToString("n"),
            Kind = kind,
            DisplayName = Path.GetFileName(path),
            CreatedUtc = DateTimeOffset.UtcNow,
            OperationId = operationId,
            WorkspaceInstanceId = workspaceInstanceId,
            ContentType = contentType,
            Durability = "durable",
            SafeLocalReference = path,
        };
}

public sealed class WorkspaceOperationReporter
{
    private readonly WorkspaceOperationRuntimeState _state;
    private readonly Func<WorkspaceOperationRuntimeState, CancellationToken, Task> _persistAsync;
    private readonly LocalHostEventService _events;
    private readonly string _hostInstanceId;
    private readonly ILogger _logger;
    private readonly object _syncRoot;

    internal WorkspaceOperationReporter(WorkspaceOperationRuntimeState state, Func<WorkspaceOperationRuntimeState, CancellationToken, Task> persistAsync, LocalHostEventService events, string hostInstanceId, ILogger logger)
    {
        _state = state;
        _persistAsync = persistAsync;
        _events = events;
        _hostInstanceId = hostInstanceId;
        _logger = logger;
        _syncRoot = state.SyncRoot;
    }

    public void MarkStarted(string phase, string message) => Update(phase, message, WorkspaceOperationStatus.Running, LocalProgressLevel.Information);

    public void ReportProgress(CommandLogEntry entry)
        => Update(string.IsNullOrWhiteSpace(entry.Phase) ? "running" : entry.Phase, entry.Message, WorkspaceOperationStatus.Running, MapLevel(entry.Severity), entry.Percent, entry.CurrentStep, entry.TotalSteps, entry.ArtifactReference, entry.Source);

    public void ReportProgress(WorkspaceSmokeProgressUpdate update)
        => Update(update.Phase, update.Message, WorkspaceOperationStatus.Running, LocalProgressLevel.Information, source: string.IsNullOrWhiteSpace(update.TemplateId) ? "smoke" : update.TemplateId);

    public void ApplyResult(object result)
    {
        _state.Record = _state.Record with
        {
            Version = _state.Record.Version + 1,
            Status = ResolveStatus(result),
            CurrentPhase = "completed",
            CompletedUtc = DateTimeOffset.UtcNow,
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            Result = JsonSerializer.SerializeToElement(result, LocalHostContract.JsonOptions),
        };
        PersistAndPublishAsync("operationCompleted").GetAwaiter().GetResult();
    }

    public void MarkCancelled()
    {
        _state.Record = _state.Record with
        {
            Version = _state.Record.Version + 1,
            Status = WorkspaceOperationStatus.Cancelled,
            CurrentPhase = "cleaningUp",
            CompletedUtc = DateTimeOffset.UtcNow,
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            CancellationState = WorkspaceOperationCancellationState.Cancelled,
            OriginalFailure = new WorkspaceOperationFailure { Classification = "cancelled", Message = "Operation was cancelled." },
        };
        PersistAndPublishAsync("operationCancelled").GetAwaiter().GetResult();
    }

    public void MarkFailed(Exception exception)
    {
        _logger.LogWarning(exception, "LocalHost operation {OperationId} failed.", _state.Record.OperationId);
        _state.Record = _state.Record with
        {
            Version = _state.Record.Version + 1,
            Status = WorkspaceOperationStatus.Failed,
            CurrentPhase = "failed",
            CompletedUtc = DateTimeOffset.UtcNow,
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            OriginalFailure = new WorkspaceOperationFailure { Classification = exception.GetType().Name, Message = exception.Message },
        };
        PersistAndPublishAsync("operationFailed").GetAwaiter().GetResult();
    }

    public void MarkCompleted()
    {
        if (_state.Record.CompletedUtc is null)
        {
            _state.Record = _state.Record with { Version = _state.Record.Version + 1, CompletedUtc = DateTimeOffset.UtcNow, LastUpdatedUtc = DateTimeOffset.UtcNow };
            PersistAndPublishAsync("operationCompleted").GetAwaiter().GetResult();
        }
    }

    private void Update(string phase, string message, WorkspaceOperationStatus status, LocalProgressLevel level, double? percent = null, int? currentStep = null, int? totalSteps = null, string? artifactReference = null, string source = "app")
    {
        lock (_syncRoot)
        {
            var sequence = _state.Record.LastEventSequence + 1;
            var nextEvent = new OpenCode.Workspace.LocalClient.WorkspaceOperationProgressEvent
            {
                Sequence = sequence,
                TimestampUtc = DateTimeOffset.UtcNow,
                Level = level,
                Phase = phase,
                Message = message,
                Percent = percent,
                CurrentStep = currentStep,
                TotalSteps = totalSteps,
                ArtifactReference = artifactReference ?? string.Empty,
                Source = source,
            };
            var history = _state.Record.PhaseHistory.ToList();
            if (history.Count == 0 || !string.Equals(history[^1], phase, StringComparison.OrdinalIgnoreCase))
            {
                history.Add(phase);
            }

            var recent = _state.Record.RecentEvents.Concat([nextEvent]).TakeLast(200).ToArray();
            _state.Record = _state.Record with
            {
                Version = _state.Record.Version + 1,
                Status = status,
            StartedUtc = _state.Record.StartedUtc ?? DateTimeOffset.UtcNow,
            CurrentPhase = phase,
            ProgressMessage = message,
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            LastEventSequence = sequence,
                PhaseHistory = history,
                RecentEvents = recent,
                EventsTruncated = _state.Record.RecentEvents.Count + 1 > 200,
            };
            File.AppendAllText(Path.Combine(_state.OperationRoot, "operation-progress.jsonl"), JsonSerializer.Serialize(nextEvent, LocalHostContract.CompactJsonOptions) + Environment.NewLine, Encoding.UTF8);
            File.AppendAllText(Path.Combine(_state.OperationRoot, "operation-progress.txt"), $"[{nextEvent.TimestampUtc:O}] {phase}: {message}{Environment.NewLine}", Encoding.UTF8);
        }
        PersistAndPublishAsync("operationProgressed").GetAwaiter().GetResult();
    }

    private async Task PersistAndPublishAsync(string eventKind)
    {
        await _persistAsync(_state, CancellationToken.None);
        await _events.PublishAsync(eventKind, _state.Record, _hostInstanceId, workspaceInstanceId: _state.Record.WorkspaceInstanceId, operationId: _state.Record.OperationId);
    }

    private static LocalProgressLevel MapLevel(DiagnosticSeverity severity)
        => severity switch
        {
            DiagnosticSeverity.Warning => LocalProgressLevel.Warning,
            DiagnosticSeverity.Error => LocalProgressLevel.Error,
            _ => LocalProgressLevel.Information,
        };

    private static WorkspaceOperationStatus ResolveStatus(object result)
        => result switch
        {
            WorkspaceSmokeResult smoke => smoke.Status == WorkspaceSmokeStatus.Cancelled ? WorkspaceOperationStatus.Cancelled : smoke.Status is WorkspaceSmokeStatus.Passed or WorkspaceSmokeStatus.Skipped ? WorkspaceOperationStatus.Succeeded : WorkspaceOperationStatus.Failed,
            WorkspaceSmokeMatrixResult matrix => matrix.Status == WorkspaceSmokeStatus.Cancelled ? WorkspaceOperationStatus.Cancelled : matrix.Status is WorkspaceSmokeStatus.Passed or WorkspaceSmokeStatus.Skipped ? WorkspaceOperationStatus.Succeeded : WorkspaceOperationStatus.Failed,
            _ => WorkspaceOperationStatus.Succeeded,
        };
}

public sealed class ControllerSessionService(LocalHostStateStore stateStore)
{
    private readonly ConcurrentDictionary<string, ControllerSessionRecord> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ControllerSessionRecord> List() => _sessions.Values.OrderByDescending(item => item.LastActivityUtc).ToArray();

    public async Task<ControllerSessionRecord> UpsertAsync(ControllerSessionUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var record = _sessions.AddOrUpdate(
            request.ControllerSessionId,
            _ => new ControllerSessionRecord
            {
                ControllerSessionId = request.ControllerSessionId,
                ClientKind = request.ClientKind,
                ClientName = request.ClientName,
                ClientVersion = request.ClientVersion,
                ClientInstanceId = request.ClientInstanceId,
                ConnectedUtc = now,
                LastActivityUtc = now,
                Status = ControllerSessionStatus.Connected,
                Metadata = request.Metadata,
            },
            (_, existing) => existing with
            {
                Version = existing.Version + 1,
                LastActivityUtc = now,
                Status = ControllerSessionStatus.Connected,
                DisconnectedUtc = null,
                Metadata = request.Metadata,
            });
        await stateStore.WriteJsonAsync(Path.Combine(stateStore.ControllerSessionsRoot, $"{record.ControllerSessionId}.json"), record, cancellationToken);
        return record;
    }

    public async Task<ControllerSessionRecord> DisconnectAsync(string controllerSessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(controllerSessionId, out var existing))
        {
            throw new OpenCodeWorkspaceMcpException("controller_session_not_found", $"Controller session '{controllerSessionId}' was not found.", "Refresh controller sessions and retry.");
        }

        var updated = existing with { Version = existing.Version + 1, LastActivityUtc = DateTimeOffset.UtcNow, DisconnectedUtc = DateTimeOffset.UtcNow, Status = ControllerSessionStatus.Disconnected };
        _sessions[controllerSessionId] = updated;
        await stateStore.WriteJsonAsync(Path.Combine(stateStore.ControllerSessionsRoot, $"{updated.ControllerSessionId}.json"), updated, cancellationToken);
        return updated;
    }
}

public sealed class WorkspaceInstanceService(LocalHostStateStore stateStore, IOpenCodeWorkspaceMcpService service, IWorkspaceOperationService operations)
{
    private readonly ConcurrentDictionary<string, WorkspaceInstanceRecord> _instances = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<WorkspaceInstanceRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        var workspaces = await service.ListWorkspacesAsync(cancellationToken);
        foreach (var workspace in workspaces)
        {
            await RefreshWorkspaceAsync(workspace, cancellationToken);
        }

        return _instances.Values.OrderBy(item => item.WorkspaceName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<WorkspaceInstanceRecord> GetAsync(string workspaceInstanceId, CancellationToken cancellationToken = default)
    {
        await ListAsync(cancellationToken);
        return _instances.TryGetValue(workspaceInstanceId, out var record)
            ? record
            : throw new OpenCodeWorkspaceMcpException("workspace_not_found", $"Workspace instance '{workspaceInstanceId}' was not found.", "Refresh workspace instances and retry.");
    }

    public async Task<WorkspaceInstanceRecord> RefreshByWorkspaceIdAsync(string workspaceId, CancellationToken cancellationToken = default)
        => await RefreshWorkspaceAsync(await service.GetWorkspaceAsync(workspaceId, cancellationToken), cancellationToken);

    private async Task<WorkspaceInstanceRecord> RefreshWorkspaceAsync(OpenCode.Workspace.Mcp.WorkspaceRecordModel workspace, CancellationToken cancellationToken)
    {
        var active = operations.List().Where(item => string.Equals(item.WorkspaceId, workspace.WorkspaceId, StringComparison.OrdinalIgnoreCase) && item.Status is WorkspaceOperationStatus.Pending or WorkspaceOperationStatus.Running).Select(item => item.OperationId).ToArray();
        var recent = operations.List().Where(item => string.Equals(item.WorkspaceId, workspace.WorkspaceId, StringComparison.OrdinalIgnoreCase)).Select(item => item.OperationId).Take(10).ToArray();
        var record = new WorkspaceInstanceRecord
        {
            WorkspaceInstanceId = BuildWorkspaceInstanceId(workspace.WorkspaceId),
            WorkspaceId = workspace.WorkspaceId,
            WorkspaceName = workspace.Name,
            TemplateId = workspace.Template,
            Status = workspace.Status,
            CreatedUtc = DateTimeOffset.UtcNow,
            LastActivityUtc = DateTimeOffset.UtcNow,
            RuntimeState = workspace.RuntimeState,
            ActiveOperationIds = active,
            RecentOperationIds = recent,
            RecoveryState = workspace.Readiness,
            Workspace = LocalHostModelMapper.MapToLocal<LocalWorkspaceRecordModel>(workspace),
        };
        _instances[record.WorkspaceInstanceId] = record;
        await stateStore.WriteJsonAsync(Path.Combine(stateStore.WorkspaceInstancesRoot, $"{record.WorkspaceInstanceId}.json"), record, cancellationToken);
        return record;
    }

    public static string BuildWorkspaceInstanceId(string workspaceId) => $"workspace-{workspaceId}";
}

public sealed class InteractiveAttachmentLeasePolicy
{
    public TimeSpan StartupLeaseDuration { get; init; } = TimeSpan.FromSeconds(90);
    public TimeSpan ActiveLeaseDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan TransferTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan TransferPollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan DetachGracePeriod { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RecoveryWindow { get; init; } = TimeSpan.FromMinutes(3);
    public TimeSpan RecoveryRetryBackoff { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan RecoveryAttemptTimeout { get; init; } = TimeSpan.FromSeconds(20);
}

public sealed class InteractiveAgentSessionService(
    LocalHostStateStore stateStore,
    IOpenCodeWorkspaceMcpService service,
    InteractiveAttachmentLeasePolicy leasePolicy,
    InteractiveSessionLaunchDescriptorFactory launchDescriptorFactory,
    ISystemClock clock)
{
    private readonly ConcurrentDictionary<string, InteractiveAgentSessionRecord> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<InteractiveSessionAttachmentRecord>> _attachmentHistory = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AttachmentRuntimeState> _attachmentRuntime = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AttachmentRuntimeState> _completedAttachmentRuntime = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sync = new(1, 1);
    private int _loaded;

    public async Task<IReadOnlyList<InteractiveAgentSessionRecord>> ListAsync(string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        var sessions = _sessions.Values
            .Where(item => string.IsNullOrWhiteSpace(workspaceId) || string.Equals(item.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.LastActivityUtc)
            .ThenByDescending(item => item.UpdatedUtc)
            .ThenBy(item => item.InteractiveAgentSessionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return sessions;
    }

    public async Task<InteractiveAgentSessionRecord> CreateAsync(CreateInteractiveAgentSessionRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        _ = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var existing = _sessions.Values.FirstOrDefault(item => string.Equals(item.WorkspaceId, request.WorkspaceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.CreateCommandId, request.CommandId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(request.WorkspaceId);
        var now = DateTimeOffset.UtcNow;
        var sessionId = $"interactive-{request.WorkspaceId}-{Guid.NewGuid():n}";
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? $"OpenCode session - {workspace.Name}"
            : request.Title.Trim();
        var record = new InteractiveAgentSessionRecord
        {
            InteractiveAgentSessionId = sessionId,
            WorkspaceInstanceId = workspaceInstanceId,
            WorkspaceId = request.WorkspaceId,
            Title = title,
            Status = InteractiveAgentSessionStatus.Detached,
            CreatedUtc = now,
            UpdatedUtc = now,
            LastActivityUtc = now,
            CreateCommandId = request.CommandId,
            CreatedByControllerSessionId = request.RequestedByControllerSessionId,
            LastUpdatedByControllerSessionId = request.RequestedByControllerSessionId,
        };
        _sessions[sessionId] = record;
        await PersistAsync(record, cancellationToken);
        return record;
    }

    public async Task<InteractiveAgentSessionRecord> GetAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _sessions.TryGetValue(interactiveAgentSessionId, out var session)
            ? session
            : throw new OpenCodeWorkspaceMcpException("interactive_session_not_found", $"Interactive session '{interactiveAgentSessionId}' was not found.", "Refresh interactive sessions and retry.");
    }

    public async Task<IReadOnlyList<InteractiveSessionAttachmentRecord>> GetAttachmentsAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _attachmentHistory.TryGetValue(interactiveAgentSessionId, out var attachmentsForSession)
            ? attachmentsForSession.OrderByDescending(item => item.AttachedUtc == default ? item.CreatedUtc : item.AttachedUtc).ToArray()
            : Array.Empty<InteractiveSessionAttachmentRecord>();
    }

    public async Task<InteractiveSessionAttachResult> AttachAsync(string interactiveAgentSessionId, AttachInteractiveSessionRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.SessionId) && !string.Equals(request.SessionId, interactiveAgentSessionId, StringComparison.OrdinalIgnoreCase))
        {
            throw new OpenCodeWorkspaceMcpException("invalid_request", $"Session id '{request.SessionId}' does not match route session id '{interactiveAgentSessionId}'.", "Retry with matching session ids.");
        }

        var session = await GetAsync(interactiveAgentSessionId, cancellationToken);
        var workspace = await service.GetWorkspaceAsync(session.WorkspaceId, cancellationToken);

        while (true)
        {
            await _sync.WaitAsync(cancellationToken);
            try
            {
                session = RequireSession(interactiveAgentSessionId);
                session = await ExpireLeaseIfNeededUnsafeAsync(session, cancellationToken);
                if (string.IsNullOrWhiteSpace(session.ActiveAttachmentId))
                {
                    var now = clock.UtcNow;
                    var attachmentId = Guid.NewGuid().ToString("n");
                    var attachmentToken = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
                    var attachmentRecoveryId = Guid.NewGuid().ToString("n");
                    var recoverySecret = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant() + Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
                    var launchCorrelationId = Guid.NewGuid().ToString("n");
                    var descriptors = launchDescriptorFactory.Build(workspace.Snapshot, stateStore.StateRoot, interactiveAgentSessionId, attachmentId, attachmentToken, attachmentRecoveryId, recoverySecret, !string.IsNullOrWhiteSpace(session.ProviderSessionId));
                    var attachment = new InteractiveSessionAttachmentRecord
                    {
                        AttachmentId = attachmentId,
                        InteractiveAgentSessionId = interactiveAgentSessionId,
                        Kind = request.AttachmentKind,
                        Status = InteractiveAttachmentStatus.Pending,
                        ClientInstanceId = request.ClientInstanceId,
                        WindowIdentity = $"OpenCode Stuff - {workspace.Snapshot.Definition.Workspace.Name}",
                        CreatedUtc = now,
                        AttachedUtc = now,
                        LastActivityUtc = now,
                        LastHeartbeatUtc = now,
                        LeaseExpiresUtc = now + leasePolicy.StartupLeaseDuration,
                        ProviderSessionId = session.ProviderSessionId ?? string.Empty,
                        ProviderSessionIdentitySource = string.IsNullOrWhiteSpace(session.ProviderSessionId) ? ProviderSessionIdentitySource.None : session.ProviderSessionIdentitySource,
                        ProviderSessionIdentityVerifiedUtc = session.ProviderSessionIdentityVerifiedUtc,
                        LaunchCorrelationId = launchCorrelationId,
                        LeaseVersion = 1,
                    };
                    var updated = session with
                    {
                        Version = session.Version + 1,
                        Status = InteractiveAgentSessionStatus.Starting,
                        UpdatedUtc = now,
                        LastActivityUtc = now,
                        ActiveAttachmentId = attachment.AttachmentId,
                        ActiveLease = new InteractiveAttachmentLease
                        {
                            InteractiveAgentSessionId = interactiveAgentSessionId,
                            AttachmentId = attachment.AttachmentId,
                            HolderKind = attachment.Kind.ToString(),
                            HolderClientInstanceId = attachment.ClientInstanceId,
                            AcquiredUtc = now,
                            LeaseExpiresUtc = now + leasePolicy.StartupLeaseDuration,
                            LastHeartbeatUtc = now,
                            Version = attachment.LeaseVersion,
                            TokenGeneration = 1,
                        },
                        AttachmentHistory = session.AttachmentHistory.Concat([attachment.AttachmentId]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        LastUpdatedByControllerSessionId = request.ControllerSessionId ?? string.Empty,
                        RecoveryEligibleAttachmentId = attachment.AttachmentId,
                        RecoveryEligibleUntilUtc = now + leasePolicy.RecoveryWindow,
                        RecoveryBlockedByCleanShutdown = false,
                    };
                    _sessions[interactiveAgentSessionId] = updated;
                    var runtime = AttachmentRuntimeState.CreateCurrent(interactiveAgentSessionId, attachmentId, request.ClientInstanceId, attachmentToken, attachmentRecoveryId, recoverySecret, launchCorrelationId, descriptors.ProcessLaunchDescriptor, descriptors.ProviderSessionProbeDescriptor) with
                    {
                        RecoveryEligibleUntilUtc = updated.RecoveryEligibleUntilUtc,
                    };
                    _attachmentRuntime[attachmentId] = runtime;
                    _completedAttachmentRuntime.TryRemove(attachmentId, out _);
                    await PersistAttachmentRuntimeAsync(interactiveAgentSessionId, runtime, cancellationToken);
                    RecordAttachmentSnapshotUnsafe(attachment);
                    await PersistAsync(updated, cancellationToken);
                    await PersistAttachmentAsync(interactiveAgentSessionId, attachment, cancellationToken);
                    return new InteractiveSessionAttachResult
                    {
                        Session = updated,
                        Attachment = attachment,
                        LaunchDescriptor = descriptors.TerminalLaunchDescriptor,
                    };
                }

                if (!request.RequestTransfer)
                {
                    throw new OpenCodeWorkspaceMcpException("already_attached", $"Interactive session '{interactiveAgentSessionId}' already has an active attachment.", "Use Take over to request an explicit transfer.");
                }

                var transferMarked = await MarkAttachmentDetachingUnsafeAsync(session, "transfer_requested", cancellationToken);
                if (!transferMarked)
                {
                    continue;
                }
            }
            finally
            {
                _sync.Release();
            }

            var deadline = DateTimeOffset.UtcNow + leasePolicy.TransferTimeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(leasePolicy.TransferPollInterval, cancellationToken);
                await _sync.WaitAsync(cancellationToken);
                try
                {
                    session = RequireSession(interactiveAgentSessionId);
                    session = await ExpireLeaseIfNeededUnsafeAsync(session, cancellationToken);
                    if (string.IsNullOrWhiteSpace(session.ActiveAttachmentId))
                    {
                        break;
                    }
                }
                finally
                {
                    _sync.Release();
                }
            }

            await _sync.WaitAsync(cancellationToken);
            try
            {
                session = RequireSession(interactiveAgentSessionId);
                session = await ExpireLeaseIfNeededUnsafeAsync(session, cancellationToken);
                if (!string.IsNullOrWhiteSpace(session.ActiveAttachmentId))
                {
                    throw new OpenCodeWorkspaceMcpException("transfer_rejected", $"Interactive session '{interactiveAgentSessionId}' is still attached to another client.", "Ask the current attachment owner to detach and retry Take over.");
                }
            }
            finally
            {
                _sync.Release();
            }
        }
    }

    public async Task<InteractiveSessionAttachmentActivationResult> ActivateAsync(string interactiveAgentSessionId, string attachmentId, ActivateInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var runtime = RequireCurrentRuntime(interactiveAgentSessionId, attachmentId, request.AttachmentToken);
            var session = await ExpireLeaseIfNeededUnsafeAsync(RequireSession(interactiveAgentSessionId), cancellationToken);
            if (!string.Equals(session.ActiveAttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase) || session.ActiveLease is null)
            {
                throw new OpenCodeWorkspaceMcpException("attachment_not_found", $"Attachment '{attachmentId}' is not active for interactive session '{interactiveAgentSessionId}'.", "Refresh session state and retry.");
            }

            var now = clock.UtcNow;
            var attachment = BuildCurrentAttachmentSnapshot(session, InteractiveAttachmentStatus.Starting) with
            {
                Version = BuildCurrentAttachmentSnapshot(session, InteractiveAttachmentStatus.Starting).Version + 1,
                ProcessId = request.HelperProcessId,
                LastActivityUtc = now,
                LastHeartbeatUtc = now,
                LeaseExpiresUtc = now + leasePolicy.StartupLeaseDuration,
                LeaseVersion = session.ActiveLease.Version + 1,
            };
            var lease = session.ActiveLease with
            {
                LastHeartbeatUtc = now,
                LeaseExpiresUtc = now + leasePolicy.StartupLeaseDuration,
                Version = session.ActiveLease.Version + 1,
            };
            var updated = session with
            {
                Version = session.Version + 1,
                Status = InteractiveAgentSessionStatus.Starting,
                UpdatedUtc = now,
                LastActivityUtc = now,
                ActiveLease = lease,
            };
            var updatedRuntime = runtime with { HelperProcessId = request.HelperProcessId, HelperStartedUtc = now };
            _attachmentRuntime[attachmentId] = updatedRuntime;
            _sessions[interactiveAgentSessionId] = updated;
            await PersistAttachmentRuntimeAsync(interactiveAgentSessionId, updatedRuntime, cancellationToken);
            RecordAttachmentSnapshotUnsafe(attachment);
            await PersistAsync(updated, cancellationToken);
            await PersistAttachmentAsync(interactiveAgentSessionId, attachment, cancellationToken);
            return new InteractiveSessionAttachmentActivationResult
            {
                Session = updated,
                Attachment = attachment,
                ProcessLaunchDescriptor = updatedRuntime.ProcessLaunchDescriptor,
                ProviderSessionProbeDescriptor = updatedRuntime.ProviderSessionProbeDescriptor,
                RequestedAction = updatedRuntime.RequestedAction,
                HeartbeatIntervalSeconds = (int)Math.Max(1, leasePolicy.HeartbeatInterval.TotalSeconds),
                TokenGeneration = lease.TokenGeneration,
            };
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<InteractiveSessionAttachmentRecoveryResult> RecoverAsync(string interactiveAgentSessionId, string attachmentId, RecoverInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var session = RequireSession(interactiveAgentSessionId);
            if (session.RecoveryBlockedByCleanShutdown)
            {
                throw new OpenCodeWorkspaceMcpException("recovery_not_allowed", $"Interactive session '{interactiveAgentSessionId}' was closed by intentional LocalHost shutdown.", "Start a new attachment instead of attempting recovery.");
            }

            if (!string.Equals(session.RecoveryEligibleAttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase)
                || session.RecoveryEligibleUntilUtc is null
                || session.RecoveryEligibleUntilUtc <= clock.UtcNow)
            {
                throw new OpenCodeWorkspaceMcpException("recovery_not_allowed", $"Attachment '{attachmentId}' is no longer recovery-eligible for interactive session '{interactiveAgentSessionId}'.", "Start a new attachment instead of attempting recovery.");
            }

            var runtime = RequireRecoveryRuntime(interactiveAgentSessionId, attachmentId, request.AttachmentRecoveryId, request.RecoverySecret);
            if (runtime.HelperStartedUtc != default && request.HelperStartedUtc != default && (runtime.HelperStartedUtc - request.HelperStartedUtc).Duration() > TimeSpan.FromSeconds(5))
            {
                throw new OpenCodeWorkspaceMcpException("invalid_recovery_proof", $"Recovery continuity for attachment '{attachmentId}' could not be verified.", "Start a new attachment instead of attempting recovery.");
            }

            if (runtime.ChildStartedUtc.HasValue && request.ChildStartedUtc.HasValue && (runtime.ChildStartedUtc.Value - request.ChildStartedUtc.Value).Duration() > TimeSpan.FromSeconds(5))
            {
                throw new OpenCodeWorkspaceMcpException("invalid_recovery_proof", $"Recovery continuity for attachment '{attachmentId}' could not be verified.", "Start a new attachment instead of attempting recovery.");
            }

            if (!string.IsNullOrWhiteSpace(request.ProviderSessionId)
                && !string.IsNullOrWhiteSpace(session.ProviderSessionId)
                && !string.Equals(request.ProviderSessionId, session.ProviderSessionId, StringComparison.OrdinalIgnoreCase))
            {
                throw new OpenCodeWorkspaceMcpException("provider_session_mismatch", $"Interactive session '{interactiveAgentSessionId}' already recorded provider session '{session.ProviderSessionId}'.", "Review provider session recovery before retrying.");
            }

            var now = clock.UtcNow;
            var newToken = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
            var latest = _attachmentHistory.TryGetValue(interactiveAgentSessionId, out var items)
                ? items.LastOrDefault(item => string.Equals(item.AttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase))
                : null;
            if (latest is null)
            {
                throw new OpenCodeWorkspaceMcpException("attachment_not_found", $"Attachment '{attachmentId}' history was not found for interactive session '{interactiveAgentSessionId}'.", "Start a new attachment instead of attempting recovery.");
            }

            var lease = new InteractiveAttachmentLease
            {
                InteractiveAgentSessionId = interactiveAgentSessionId,
                AttachmentId = attachmentId,
                HolderKind = latest.Kind.ToString(),
                HolderClientInstanceId = runtime.OwnerClientInstanceId,
                AcquiredUtc = latest.AttachedUtc == default ? now : latest.AttachedUtc,
                LeaseExpiresUtc = now + leasePolicy.ActiveLeaseDuration,
                LastHeartbeatUtc = now,
                Version = Math.Max(latest.LeaseVersion, session.Version) + 1,
                TokenGeneration = runtime.TokenGeneration + 1,
            };
            var attachment = latest with
            {
                Version = latest.Version + 1,
                Status = request.ChildProcessId.HasValue ? InteractiveAttachmentStatus.Active : InteractiveAttachmentStatus.Starting,
                ProcessId = request.ChildProcessId ?? latest.ProcessId,
                LastActivityUtc = now,
                LastHeartbeatUtc = now,
                LeaseExpiresUtc = lease.LeaseExpiresUtc,
                LeaseVersion = lease.Version,
                ProviderSessionId = string.IsNullOrWhiteSpace(request.ProviderSessionId) ? latest.ProviderSessionId : request.ProviderSessionId,
                ProviderSessionIdentitySource = string.IsNullOrWhiteSpace(request.ProviderSessionId) ? latest.ProviderSessionIdentitySource : ProviderSessionIdentitySource.ExistingCanonicalIdentity,
                ProviderSessionIdentityVerifiedUtc = string.IsNullOrWhiteSpace(request.ProviderSessionId) ? latest.ProviderSessionIdentityVerifiedUtc : now,
            };
            var updated = session with
            {
                Version = session.Version + 1,
                Status = attachment.Status == InteractiveAttachmentStatus.Active ? InteractiveAgentSessionStatus.Attached : InteractiveAgentSessionStatus.Starting,
                UpdatedUtc = now,
                LastActivityUtc = now,
                ActiveAttachmentId = attachmentId,
                ActiveLease = lease,
                RecoveryEligibleAttachmentId = attachmentId,
                RecoveryEligibleUntilUtc = now + leasePolicy.RecoveryWindow,
                RecoveryBlockedByCleanShutdown = false,
                ProviderSessionId = string.IsNullOrWhiteSpace(request.ProviderSessionId) ? session.ProviderSessionId : request.ProviderSessionId,
                ProviderSessionIdentitySource = string.IsNullOrWhiteSpace(request.ProviderSessionId) ? session.ProviderSessionIdentitySource : ProviderSessionIdentitySource.ExistingCanonicalIdentity,
                ProviderSessionIdentityVerifiedUtc = string.IsNullOrWhiteSpace(request.ProviderSessionId) ? session.ProviderSessionIdentityVerifiedUtc : now,
            };
            var updatedRuntime = runtime with
            {
                AttachmentTokenHash = AttachmentRuntimeState.CreateTokenHash(newToken),
                TokenGeneration = lease.TokenGeneration,
                RecoveryEligibleUntilUtc = updated.RecoveryEligibleUntilUtc,
                HelperProcessId = request.HelperProcessId,
                HelperStartedUtc = request.HelperStartedUtc,
                ChildProcessId = request.ChildProcessId,
                ChildStartedUtc = request.ChildStartedUtc,
                RequestedAction = session.Status == InteractiveAgentSessionStatus.Stopping ? InteractiveAttachmentControlAction.Detach : runtime.RequestedAction,
                CompletedAttachment = null,
                CompletedSession = null,
            };
            _sessions[interactiveAgentSessionId] = updated;
            _attachmentRuntime[attachmentId] = updatedRuntime;
            _completedAttachmentRuntime[attachmentId] = updatedRuntime with { CompletedAttachment = attachment, CompletedSession = updated };
            RecordAttachmentSnapshotUnsafe(attachment);
            await PersistAttachmentRuntimeAsync(interactiveAgentSessionId, updatedRuntime, cancellationToken);
            await PersistAsync(updated, cancellationToken);
            await PersistAttachmentAsync(interactiveAgentSessionId, attachment with { DetachReason = "recovery_granted" }, cancellationToken);
            return new InteractiveSessionAttachmentRecoveryResult
            {
                Session = updated,
                Attachment = attachment,
                AttachmentToken = newToken,
                RequestedAction = updatedRuntime.RequestedAction,
                HeartbeatIntervalSeconds = (int)Math.Max(1, leasePolicy.HeartbeatInterval.TotalSeconds),
                TokenGeneration = lease.TokenGeneration,
            };
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<InteractiveSessionAttachmentRecord> ReportProcessStartedAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProcessStartedRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        await _sync.WaitAsync(cancellationToken);
        try
        {
            _ = RequireCurrentRuntime(interactiveAgentSessionId, attachmentId, request.AttachmentToken);
            var session = await ExpireLeaseIfNeededUnsafeAsync(RequireSession(interactiveAgentSessionId), cancellationToken);
            if (!string.Equals(session.ActiveAttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase) || session.ActiveLease is null)
            {
                throw new OpenCodeWorkspaceMcpException("attachment_not_found", $"Attachment '{attachmentId}' is not active for interactive session '{interactiveAgentSessionId}'.", "Refresh session state and retry.");
            }

            var now = clock.UtcNow;
            var lease = session.ActiveLease with
            {
                LastHeartbeatUtc = now,
                LeaseExpiresUtc = now + leasePolicy.ActiveLeaseDuration,
                Version = session.ActiveLease.Version + 1,
                TokenGeneration = session.ActiveLease.TokenGeneration,
            };
            var attachment = BuildCurrentAttachmentSnapshot(session, InteractiveAttachmentStatus.Active) with
            {
                Version = BuildCurrentAttachmentSnapshot(session, InteractiveAttachmentStatus.Active).Version + 1,
                ProcessId = request.ChildProcessId,
                LastActivityUtc = now,
                LastHeartbeatUtc = now,
                LeaseExpiresUtc = lease.LeaseExpiresUtc,
                LeaseVersion = lease.Version,
            };
            var updated = session with
            {
                Version = session.Version + 1,
                Status = InteractiveAgentSessionStatus.Attached,
                UpdatedUtc = now,
                LastActivityUtc = now,
                ActiveLease = lease,
            };
            _sessions[interactiveAgentSessionId] = updated;
            if (_attachmentRuntime.TryGetValue(attachmentId, out var runtime))
            {
                var updatedRuntime = runtime with { ChildProcessId = request.ChildProcessId, ChildStartedUtc = now };
                _attachmentRuntime[attachmentId] = updatedRuntime;
                await PersistAttachmentRuntimeAsync(interactiveAgentSessionId, updatedRuntime, cancellationToken);
            }

            RecordAttachmentSnapshotUnsafe(attachment);
            await PersistAsync(updated, cancellationToken);
            await PersistAttachmentAsync(interactiveAgentSessionId, attachment, cancellationToken);
            return attachment;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<InteractiveSessionAttachmentHeartbeatResult> HeartbeatAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentHeartbeatRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (_completedAttachmentRuntime.TryGetValue(attachmentId, out var completedRuntime) && completedRuntime.TokenMatches(request.AttachmentToken))
            {
                return new InteractiveSessionAttachmentHeartbeatResult
                {
                    Session = completedRuntime.CompletedSession ?? new InteractiveAgentSessionRecord(),
                    Attachment = completedRuntime.CompletedAttachment ?? new InteractiveSessionAttachmentRecord(),
                    RequestedAction = InteractiveAttachmentControlAction.None,
                    HeartbeatIntervalSeconds = (int)Math.Max(1, leasePolicy.HeartbeatInterval.TotalSeconds),
                    TokenGeneration = completedRuntime.TokenGeneration,
                };
            }

            var runtime = RequireCurrentRuntime(interactiveAgentSessionId, attachmentId, request.AttachmentToken);
            var session = await ExpireLeaseIfNeededUnsafeAsync(RequireSession(interactiveAgentSessionId), cancellationToken);
            if (!string.Equals(session.ActiveAttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase) || session.ActiveLease is null)
            {
                throw new OpenCodeWorkspaceMcpException("attachment_not_found", $"Attachment '{attachmentId}' is not active for interactive session '{interactiveAgentSessionId}'.", "Refresh session state and retry.");
            }

            var latest = GetLatestAttachmentUnsafe(session);
            var duration = latest.Status is InteractiveAttachmentStatus.Pending or InteractiveAttachmentStatus.Starting
                ? leasePolicy.StartupLeaseDuration
                : leasePolicy.ActiveLeaseDuration;
            var now = clock.UtcNow;
            var lease = session.ActiveLease with
            {
                LastHeartbeatUtc = now,
                LeaseExpiresUtc = now + duration,
                Version = session.ActiveLease.Version + 1,
                TokenGeneration = session.ActiveLease.TokenGeneration,
            };
            var attachment = latest with
            {
                Version = latest.Version + 1,
                LastActivityUtc = now,
                LastHeartbeatUtc = now,
                LeaseExpiresUtc = lease.LeaseExpiresUtc,
                LeaseVersion = lease.Version,
            };
            var updated = session with
            {
                Version = session.Version + 1,
                UpdatedUtc = now,
                LastActivityUtc = now,
                ActiveLease = lease,
            };
            _sessions[interactiveAgentSessionId] = updated;
            if (_attachmentRuntime.TryGetValue(attachmentId, out runtime))
            {
                var updatedRuntime = runtime with { TokenGeneration = lease.TokenGeneration };
                _attachmentRuntime[attachmentId] = updatedRuntime;
                await PersistAttachmentRuntimeAsync(interactiveAgentSessionId, updatedRuntime, cancellationToken);
                runtime = updatedRuntime;
            }
            RecordAttachmentSnapshotUnsafe(attachment);
            await PersistAsync(updated, cancellationToken);
            await PersistAttachmentAsync(interactiveAgentSessionId, attachment, cancellationToken);
            return new InteractiveSessionAttachmentHeartbeatResult
            {
                Session = updated,
                Attachment = attachment,
                RequestedAction = runtime.RequestedAction,
                HeartbeatIntervalSeconds = (int)Math.Max(1, leasePolicy.HeartbeatInterval.TotalSeconds),
                TokenGeneration = lease.TokenGeneration,
            };
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<InteractiveAgentSessionRecord> ReportProviderSessionAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProviderSessionRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        await _sync.WaitAsync(cancellationToken);
        try
        {
            _ = RequireCurrentOrCompletedRuntime(interactiveAgentSessionId, attachmentId, request.AttachmentToken);
            var session = RequireSession(interactiveAgentSessionId);
            if (!string.IsNullOrWhiteSpace(session.ProviderSessionId) && !string.Equals(session.ProviderSessionId, request.ProviderSessionId, StringComparison.OrdinalIgnoreCase))
            {
                throw new OpenCodeWorkspaceMcpException("provider_session_mismatch", $"Interactive session '{interactiveAgentSessionId}' already recorded provider session '{session.ProviderSessionId}'.", "Review provider session recovery before retrying.");
            }

            if (string.Equals(session.ProviderSessionId, request.ProviderSessionId, StringComparison.OrdinalIgnoreCase))
            {
                return session;
            }

            var now = clock.UtcNow;
            var updated = session with
            {
                Version = session.Version + 1,
                ProviderSessionId = request.ProviderSessionId,
                ProviderSessionIdentitySource = request.IdentitySource,
                ProviderSessionIdentityVerifiedUtc = now,
                UpdatedUtc = now,
                LastActivityUtc = now,
            };
            _sessions[interactiveAgentSessionId] = updated;
            await PersistAsync(updated, cancellationToken);
            if (!string.IsNullOrWhiteSpace(updated.ActiveAttachmentId) && updated.ActiveLease is not null)
            {
                var attachment = GetLatestAttachmentUnsafe(updated) with
                {
                    Version = GetLatestAttachmentUnsafe(updated).Version + 1,
                    ProviderSessionId = request.ProviderSessionId,
                    ProviderSessionIdentitySource = request.IdentitySource,
                    ProviderSessionIdentityVerifiedUtc = now,
                };
                RecordAttachmentSnapshotUnsafe(attachment);
                await PersistAttachmentAsync(interactiveAgentSessionId, attachment, cancellationToken);
            }

            return updated;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<InteractiveAgentSessionRecord> ReportProcessExitAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProcessExitRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (_completedAttachmentRuntime.TryGetValue(attachmentId, out var completedRuntime) && completedRuntime.TokenMatches(request.AttachmentToken))
            {
                return completedRuntime.CompletedSession ?? RequireSession(interactiveAgentSessionId);
            }

            _ = RequireCurrentRuntime(interactiveAgentSessionId, attachmentId, request.AttachmentToken);
            var session = await ExpireLeaseIfNeededUnsafeAsync(RequireSession(interactiveAgentSessionId), cancellationToken);
            if (!string.Equals(session.ActiveAttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase) || session.ActiveLease is null)
            {
                throw new OpenCodeWorkspaceMcpException("attachment_not_found", $"Attachment '{attachmentId}' is not active for interactive session '{interactiveAgentSessionId}'.", "Refresh session state and retry.");
            }

            var status = string.Equals(request.Outcome, "detach_requested", StringComparison.OrdinalIgnoreCase)
                ? InteractiveAttachmentStatus.Detached
                : string.Equals(request.Outcome, "normal_exit", StringComparison.OrdinalIgnoreCase)
                    ? InteractiveAttachmentStatus.Detached
                    : InteractiveAttachmentStatus.Failed;
            return await ClearActiveAttachmentUnsafeAsync(session, request.Outcome, status, request.FailureMessage, request.ChildProcessId, request.ExitCode, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<InteractiveAgentSessionRecord> ReportLaunchFailureAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentLaunchFailureRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var session = await ExpireLeaseIfNeededUnsafeAsync(RequireSession(interactiveAgentSessionId), cancellationToken);
            if (!string.Equals(session.ActiveAttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase) || session.ActiveLease is null)
            {
                throw new OpenCodeWorkspaceMcpException("attachment_not_found", $"Attachment '{attachmentId}' is not active for interactive session '{interactiveAgentSessionId}'.", "Refresh session state and retry.");
            }

            if (!string.Equals(session.ActiveLease.HolderClientInstanceId, request.ClientInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                throw new OpenCodeWorkspaceMcpException("attachment_owner_mismatch", $"Attachment '{attachmentId}' is owned by another client.", "Only the current attachment owner may report launch failure.");
            }

            return await ClearActiveAttachmentUnsafeAsync(session, "launch_failed", InteractiveAttachmentStatus.Failed, request.FailureMessage, null, null, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<InteractiveAgentSessionRecord> RequestDetachAsync(string interactiveAgentSessionId, string attachmentId, DetachInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var session = await ExpireLeaseIfNeededUnsafeAsync(RequireSession(interactiveAgentSessionId), cancellationToken);
            if (!string.Equals(session.ActiveAttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase) || session.ActiveLease is null)
            {
                throw new OpenCodeWorkspaceMcpException("attachment_not_found", $"Attachment '{attachmentId}' is not active for interactive session '{interactiveAgentSessionId}'.", "Refresh session state and retry.");
            }

            if (!string.Equals(session.ActiveLease.HolderClientInstanceId, request.ClientInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                throw new OpenCodeWorkspaceMcpException("attachment_owner_mismatch", $"Attachment '{attachmentId}' is owned by another client.", "Only the active attachment owner may request detach.");
            }

            return await MarkAttachmentDetachingUnsafeAsync(session, string.IsNullOrWhiteSpace(request.Reason) ? "detach_requested" : request.Reason, cancellationToken)
                ? RequireSession(interactiveAgentSessionId)
                : session;
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _loaded, 1) == 1)
        {
            return;
        }

        foreach (var sessionDirectory in Directory.Exists(stateStore.InteractiveSessionsRoot)
                     ? Directory.GetDirectories(stateStore.InteractiveSessionsRoot)
                     : Array.Empty<string>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = stateStore.ReadJson<InteractiveAgentSessionRecord>(Path.Combine(sessionDirectory, "session.json"));
            if (session is null)
            {
                continue;
            }

            var normalized = NormalizeRecoveredSession(session);
            _sessions[normalized.InteractiveAgentSessionId] = normalized;
            if (!ReferenceEquals(normalized, session))
            {
                await PersistAsync(normalized, cancellationToken);
            }

            var attachmentsPath = Path.Combine(sessionDirectory, "attachments.jsonl");
            if (File.Exists(attachmentsPath))
            {
                var items = File.ReadAllLines(attachmentsPath)
                    .Select(TryDeserializeAttachment)
                    .Where(item => item is not null)
                    .Select(item => item!)
                    .OrderBy(item => item.AttachedUtc)
                    .ToList();
                _attachmentHistory[normalized.InteractiveAgentSessionId] = items;
            }

            var runtimePath = GetAttachmentRuntimePath(stateStore, normalized.InteractiveAgentSessionId);
            var persistedRuntime = stateStore.ReadJson<PersistedAttachmentRuntimeState>(runtimePath);
            if (persistedRuntime is not null)
            {
                var restored = persistedRuntime.ToRuntime() with { AttachmentTokenHash = string.Empty };
                if (!string.IsNullOrWhiteSpace(normalized.RecoveryEligibleAttachmentId))
                {
                    _completedAttachmentRuntime[normalized.RecoveryEligibleAttachmentId] = restored with
                    {
                        CompletedSession = normalized,
                        CompletedAttachment = _attachmentHistory.TryGetValue(normalized.InteractiveAgentSessionId, out var items)
                            ? items.LastOrDefault(item => string.Equals(item.AttachmentId, normalized.RecoveryEligibleAttachmentId, StringComparison.OrdinalIgnoreCase))
                            : null,
                    };
                }
            }
        }
    }

    private InteractiveAgentSessionRecord NormalizeRecoveredSession(InteractiveAgentSessionRecord session)
    {
        var hadActiveAttachment = !string.IsNullOrWhiteSpace(session.ActiveAttachmentId);
        var hadPendingDetach = session.Status == InteractiveAgentSessionStatus.Stopping;
        var status = session.Status switch
        {
            InteractiveAgentSessionStatus.Starting => InteractiveAgentSessionStatus.Detached,
            InteractiveAgentSessionStatus.Attaching => InteractiveAgentSessionStatus.Detached,
            InteractiveAgentSessionStatus.Attached => InteractiveAgentSessionStatus.Detached,
            InteractiveAgentSessionStatus.Stopping => InteractiveAgentSessionStatus.Detached,
            _ => session.Status,
        };
        if (status == session.Status && string.IsNullOrWhiteSpace(session.ActiveAttachmentId) && session.ActiveLease is null)
        {
            return session;
        }

        var recoveryEligible = hadActiveAttachment && !stateStore.WasPreviousShutdownClean;
        var recoveryAttachmentId = recoveryEligible ? session.ActiveAttachmentId : string.Empty;
        DateTimeOffset? recoveryEligibleUntilUtc = recoveryEligible ? clock.UtcNow + leasePolicy.RecoveryWindow : null;

        return session with
        {
            Version = session.Version + 1,
            Status = status,
            UpdatedUtc = clock.UtcNow,
            ActiveAttachmentId = string.Empty,
            ActiveLease = null,
            RecoveryEligibleAttachmentId = recoveryAttachmentId,
            RecoveryEligibleUntilUtc = recoveryEligibleUntilUtc,
            RecoveryBlockedByCleanShutdown = stateStore.WasPreviousShutdownClean,
            LastFailureSummary = hadPendingDetach ? session.LastFailureSummary : session.LastFailureSummary,
        };
    }

    private static InteractiveSessionAttachmentRecord? TryDeserializeAttachment(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<InteractiveSessionAttachmentRecord>(line, LocalHostContract.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task PersistAsync(InteractiveAgentSessionRecord session, CancellationToken cancellationToken)
        => await stateStore.WriteJsonAsync(Path.Combine(stateStore.InteractiveSessionsRoot, session.InteractiveAgentSessionId, "session.json"), session, cancellationToken);

    internal async Task PersistAttachmentAsync(string interactiveAgentSessionId, InteractiveSessionAttachmentRecord attachment, CancellationToken cancellationToken)
        => await stateStore.AppendJsonLineAsync(Path.Combine(stateStore.InteractiveSessionsRoot, interactiveAgentSessionId, "attachments.jsonl"), attachment, cancellationToken);

    private async Task PersistAttachmentRuntimeAsync(string interactiveAgentSessionId, AttachmentRuntimeState runtime, CancellationToken cancellationToken)
        => await stateStore.WriteJsonAsync(Path.Combine(stateStore.InteractiveSessionsRoot, interactiveAgentSessionId, "attachment-runtime.json"), PersistedAttachmentRuntimeState.FromRuntime(runtime), cancellationToken);

    private static string GetAttachmentRuntimePath(LocalHostStateStore stateStore, string interactiveAgentSessionId)
        => Path.Combine(stateStore.InteractiveSessionsRoot, interactiveAgentSessionId, "attachment-runtime.json");

    private InteractiveAgentSessionRecord RequireSession(string interactiveAgentSessionId)
        => _sessions.TryGetValue(interactiveAgentSessionId, out var session)
            ? session
            : throw new OpenCodeWorkspaceMcpException("interactive_session_not_found", $"Interactive session '{interactiveAgentSessionId}' was not found.", "Refresh interactive sessions and retry.");

    private AttachmentRuntimeState RequireCurrentRuntime(string interactiveAgentSessionId, string attachmentId, string attachmentToken)
    {
        if (!_attachmentRuntime.TryGetValue(attachmentId, out var runtime)
            || !string.Equals(runtime.InteractiveAgentSessionId, interactiveAgentSessionId, StringComparison.OrdinalIgnoreCase)
            || !runtime.TokenMatches(attachmentToken))
        {
            throw new OpenCodeWorkspaceMcpException("invalid_attachment_credential", $"Attachment '{attachmentId}' is no longer authorized for interactive session '{interactiveAgentSessionId}'.", "Request a new attachment and retry.");
        }

        return runtime;
    }

    private AttachmentRuntimeState RequireCurrentOrCompletedRuntime(string interactiveAgentSessionId, string attachmentId, string attachmentToken)
    {
        if (_attachmentRuntime.TryGetValue(attachmentId, out var current)
            && string.Equals(current.InteractiveAgentSessionId, interactiveAgentSessionId, StringComparison.OrdinalIgnoreCase)
            && current.TokenMatches(attachmentToken))
        {
            return current;
        }

        if (_completedAttachmentRuntime.TryGetValue(attachmentId, out var completed)
            && string.Equals(completed.InteractiveAgentSessionId, interactiveAgentSessionId, StringComparison.OrdinalIgnoreCase)
            && completed.TokenMatches(attachmentToken))
        {
            return completed;
        }

        throw new OpenCodeWorkspaceMcpException("invalid_attachment_credential", $"Attachment '{attachmentId}' is no longer authorized for interactive session '{interactiveAgentSessionId}'.", "Request a new attachment and retry.");
    }

    private AttachmentRuntimeState RequireRecoveryRuntime(string interactiveAgentSessionId, string attachmentId, string attachmentRecoveryId, string recoverySecret)
    {
        if (_attachmentRuntime.TryGetValue(attachmentId, out var current)
            && string.Equals(current.InteractiveAgentSessionId, interactiveAgentSessionId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.AttachmentRecoveryId, attachmentRecoveryId, StringComparison.OrdinalIgnoreCase)
            && current.RecoverySecretMatches(recoverySecret))
        {
            return current;
        }

        if (_completedAttachmentRuntime.TryGetValue(attachmentId, out var completed)
            && string.Equals(completed.InteractiveAgentSessionId, interactiveAgentSessionId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(completed.AttachmentRecoveryId, attachmentRecoveryId, StringComparison.OrdinalIgnoreCase)
            && completed.RecoverySecretMatches(recoverySecret))
        {
            return completed;
        }

        throw new OpenCodeWorkspaceMcpException("invalid_recovery_proof", $"Recovery proof for attachment '{attachmentId}' was rejected.", "Start a new attachment instead of attempting recovery.");
    }

    private void RecordAttachmentSnapshotUnsafe(InteractiveSessionAttachmentRecord attachment)
    {
        var history = _attachmentHistory.GetOrAdd(attachment.InteractiveAgentSessionId, _ => []);
        history.Add(attachment);
    }

    private InteractiveSessionAttachmentRecord GetLatestAttachmentUnsafe(InteractiveAgentSessionRecord session)
    {
        var latest = _attachmentHistory.TryGetValue(session.InteractiveAgentSessionId, out var items)
            ? items.LastOrDefault(item => string.Equals(item.AttachmentId, session.ActiveAttachmentId, StringComparison.OrdinalIgnoreCase))
            : null;
        if (latest is null || session.ActiveLease is null)
        {
            throw new OpenCodeWorkspaceMcpException("attachment_not_found", $"Attachment '{session.ActiveAttachmentId}' is not active for interactive session '{session.InteractiveAgentSessionId}'.", "Refresh session state and retry.");
        }

        return latest;
    }

    private InteractiveSessionAttachmentRecord BuildCurrentAttachmentSnapshot(InteractiveAgentSessionRecord session, InteractiveAttachmentStatus status)
    {
        var latest = GetLatestAttachmentUnsafe(session);

        return latest with
        {
            Status = status,
            LastActivityUtc = clock.UtcNow,
            LastHeartbeatUtc = session.ActiveLease.LastHeartbeatUtc,
            LeaseExpiresUtc = session.ActiveLease.LeaseExpiresUtc,
            LeaseVersion = session.ActiveLease.Version,
            ProviderSessionId = session.ProviderSessionId ?? string.Empty,
            ProviderSessionIdentitySource = session.ProviderSessionIdentitySource,
            ProviderSessionIdentityVerifiedUtc = session.ProviderSessionIdentityVerifiedUtc,
        };
    }

    private async Task<bool> MarkAttachmentDetachingUnsafeAsync(InteractiveAgentSessionRecord session, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.ActiveAttachmentId) || session.ActiveLease is null)
        {
            return false;
        }

        var latest = BuildCurrentAttachmentSnapshot(session, InteractiveAttachmentStatus.Detaching) with
        {
            Version = BuildCurrentAttachmentSnapshot(session, InteractiveAttachmentStatus.Detaching).Version + 1,
            DetachReason = reason,
        };
        if (_attachmentRuntime.TryGetValue(session.ActiveAttachmentId, out var runtime))
        {
            var updatedRuntime = runtime with { RequestedAction = InteractiveAttachmentControlAction.Detach };
            _attachmentRuntime[session.ActiveAttachmentId] = updatedRuntime;
            await PersistAttachmentRuntimeAsync(session.InteractiveAgentSessionId, updatedRuntime, cancellationToken);
        }

        var updated = session with
        {
            Version = session.Version + 1,
            Status = InteractiveAgentSessionStatus.Stopping,
            UpdatedUtc = clock.UtcNow,
            LastActivityUtc = clock.UtcNow,
        };
        _sessions[session.InteractiveAgentSessionId] = updated;
        RecordAttachmentSnapshotUnsafe(latest);
        await PersistAsync(updated, cancellationToken);
        await PersistAttachmentAsync(session.InteractiveAgentSessionId, latest, cancellationToken);
        return true;
    }

    private async Task<InteractiveAgentSessionRecord> ExpireLeaseIfNeededUnsafeAsync(InteractiveAgentSessionRecord session, CancellationToken cancellationToken)
    {
        if (session.ActiveLease is null || session.ActiveLease.LeaseExpiresUtc > clock.UtcNow || string.IsNullOrWhiteSpace(session.ActiveAttachmentId))
        {
            return session;
        }

        return await ClearActiveAttachmentUnsafeAsync(session, "lease_expired", InteractiveAttachmentStatus.Expired, string.Empty, null, null, cancellationToken);
    }

    private async Task<InteractiveAgentSessionRecord> ClearActiveAttachmentUnsafeAsync(InteractiveAgentSessionRecord session, string reason, InteractiveAttachmentStatus attachmentStatus, string failureMessage, int? processId, int? exitCode, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var latest = BuildCurrentAttachmentSnapshot(session, attachmentStatus) with
        {
            Version = BuildCurrentAttachmentSnapshot(session, attachmentStatus).Version + 1,
            ProcessId = processId ?? BuildCurrentAttachmentSnapshot(session, attachmentStatus).ProcessId,
            DetachedUtc = now,
            DetachReason = reason,
            LeaseExpiresUtc = session.ActiveLease?.LeaseExpiresUtc,
            Failure = string.IsNullOrWhiteSpace(failureMessage)
                ? (attachmentStatus == InteractiveAttachmentStatus.Failed ? new WorkspaceOperationFailure { Classification = reason, Message = string.IsNullOrWhiteSpace(reason) ? "Attachment failed." : reason } : null)
                : new WorkspaceOperationFailure { Classification = reason, Message = failureMessage },
        };
        var updated = session with
        {
            Version = session.Version + 1,
            Status = attachmentStatus == InteractiveAttachmentStatus.Failed ? InteractiveAgentSessionStatus.Failed : InteractiveAgentSessionStatus.Detached,
            UpdatedUtc = now,
            LastActivityUtc = now,
            ActiveAttachmentId = string.Empty,
            ActiveLease = null,
            LastFailureSummary = string.IsNullOrWhiteSpace(failureMessage) ? (attachmentStatus == InteractiveAttachmentStatus.Failed ? reason : session.LastFailureSummary) : failureMessage,
            RecoveryEligibleAttachmentId = string.Empty,
            RecoveryEligibleUntilUtc = null,
        };
        _sessions[session.InteractiveAgentSessionId] = updated;
        if (!string.IsNullOrWhiteSpace(latest.AttachmentId) && _attachmentRuntime.TryRemove(latest.AttachmentId, out var runtime))
        {
            var completedRuntime = runtime with
            {
                CompletedAttachment = latest with { ProcessId = processId ?? latest.ProcessId },
                CompletedSession = updated,
                RecoveryEligibleUntilUtc = updated.RecoveryEligibleUntilUtc,
            };
            _completedAttachmentRuntime[latest.AttachmentId] = completedRuntime;
            await PersistAttachmentRuntimeAsync(session.InteractiveAgentSessionId, completedRuntime, cancellationToken);
        }

        RecordAttachmentSnapshotUnsafe(latest);
        await PersistAsync(updated, cancellationToken);
        await PersistAttachmentAsync(session.InteractiveAgentSessionId, latest, cancellationToken);
        return updated;
    }
}

public sealed class InteractiveSessionAttachmentService(
    InteractiveAgentSessionService interactiveSessions)
{
    public Task<InteractiveSessionAttachResult> AttachAsync(string interactiveAgentSessionId, AttachInteractiveSessionRequest request, CancellationToken cancellationToken = default)
        => interactiveSessions.AttachAsync(interactiveAgentSessionId, request, cancellationToken);

    public Task<InteractiveSessionAttachmentActivationResult> ActivateAsync(string interactiveAgentSessionId, string attachmentId, ActivateInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken = default)
        => interactiveSessions.ActivateAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);

    public Task<InteractiveSessionAttachmentRecoveryResult> RecoverAsync(string interactiveAgentSessionId, string attachmentId, RecoverInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken = default)
        => interactiveSessions.RecoverAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);

    public Task<InteractiveSessionAttachmentRecord> ReportProcessStartedAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProcessStartedRequest request, CancellationToken cancellationToken = default)
        => interactiveSessions.ReportProcessStartedAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);

    public Task<InteractiveSessionAttachmentHeartbeatResult> HeartbeatAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentHeartbeatRequest request, CancellationToken cancellationToken = default)
        => interactiveSessions.HeartbeatAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);

    public Task<InteractiveAgentSessionRecord> ReportProviderSessionAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProviderSessionRequest request, CancellationToken cancellationToken = default)
        => interactiveSessions.ReportProviderSessionAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);

    public Task<InteractiveAgentSessionRecord> ReportProcessExitAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProcessExitRequest request, CancellationToken cancellationToken = default)
        => interactiveSessions.ReportProcessExitAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);

    public Task<InteractiveAgentSessionRecord> ReportLaunchFailureAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentLaunchFailureRequest request, CancellationToken cancellationToken = default)
        => interactiveSessions.ReportLaunchFailureAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);

    public Task<InteractiveAgentSessionRecord> DetachAsync(string interactiveAgentSessionId, string attachmentId, DetachInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken = default)
        => interactiveSessions.RequestDetachAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);
}

public sealed class InteractiveSessionLaunchDescriptorFactory
{
    private static readonly string[] CliCandidateNames = OperatingSystem.IsWindows()
        ? ["opencode-workspace-cli.exe", "opencode-workspace-cli.dll"]
        : ["opencode-workspace-cli", "opencode-workspace-cli.dll"];

    public InteractiveSessionLaunchDescriptorSet Build(WorkspaceSnapshot snapshot, string stateRoot, string interactiveAgentSessionId, string attachmentId, string attachmentToken, string attachmentRecoveryId, string recoverySecret, bool resumeKnownProviderSession)
    {
        var title = $"OpenCode Stuff - {snapshot.Definition.Workspace.Name}";
        var helperHost = ResolveCliHostCommand();
        return new InteractiveSessionLaunchDescriptorSet
        {
            TerminalLaunchDescriptor = new ApprovedTerminalLaunchDescriptor
            {
                LaunchKind = "windows-terminal",
                FileName = "wt.exe",
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Title = title,
                CommandText = $"wt.exe new-tab --title \"{title}\" -- {helperHost.RedactedCommandText} interactive-session attach --state-root \"{stateRoot}\" --session-id \"{interactiveAgentSessionId}\" --attachment-id \"{attachmentId}\" --attachment-token <redacted> --attachment-recovery-id \"{attachmentRecoveryId}\" --recovery-secret <redacted>",
                FallbackCommandText = $"{helperHost.RedactedCommandText} interactive-session attach --state-root \"{stateRoot}\" --session-id \"{interactiveAgentSessionId}\" --attachment-id \"{attachmentId}\" --attachment-token <redacted> --attachment-recovery-id \"{attachmentRecoveryId}\" --recovery-secret <redacted>",
                Arguments =
                [
                    "new-tab",
                    "--title",
                    title,
                    "--",
                    .. helperHost.FileAndArguments,
                    "interactive-session",
                    "attach",
                    "--state-root",
                    stateRoot,
                    "--session-id",
                    interactiveAgentSessionId,
                    "--attachment-id",
                    attachmentId,
                    "--attachment-token",
                    attachmentToken,
                    "--attachment-recovery-id",
                    attachmentRecoveryId,
                    "--recovery-secret",
                    recoverySecret,
                ],
            },
            ProcessLaunchDescriptor = new ApprovedProcessLaunchDescriptor
            {
                LaunchKind = "workspace-attach-wrapper",
                FileName = "powershell.exe",
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                CommandText = $"powershell.exe -ExecutionPolicy Bypass -File \"{snapshot.Paths.AttachWrapperScriptPath}\"",
                FallbackCommandText = $"powershell.exe -ExecutionPolicy Bypass -File \"{snapshot.Paths.AttachWrapperScriptPath}\"",
                Arguments = ["-ExecutionPolicy", "Bypass", "-File", snapshot.Paths.AttachWrapperScriptPath],
            },
            ProviderSessionProbeDescriptor = resumeKnownProviderSession
                ? null
                : new ApprovedProcessLaunchDescriptor
                {
                    LaunchKind = "workspace-provider-session-probe",
                    FileName = "powershell.exe",
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    CommandText = "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command <approved-provider-session-probe>",
                    FallbackCommandText = "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command <approved-provider-session-probe>",
                    Arguments = ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", BuildProviderProbeCommand(snapshot)],
                },
        };
    }

    private static ResolvedCliHostCommand ResolveCliHostCommand()
    {
        var roots = EnumerateCandidateRoots(AppContext.BaseDirectory)
            .Concat(EnumerateCandidateRoots(Environment.CurrentDirectory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var candidateDirectories = roots.SelectMany(root => new[]
        {
            Path.Combine(root, "bin", "cli"),
            Path.Combine(root, "src", "OpenCode.Workspace.Cli", "bin", "Debug", "net10.0"),
            Path.Combine(root, "src", "OpenCode.Workspace.Cli", "bin", "Release", "net10.0"),
        }).ToArray();

        foreach (var directory in candidateDirectories.Where(Directory.Exists))
        {
            foreach (var candidateName in CliCandidateNames)
            {
                var candidatePath = Path.Combine(directory, candidateName);
                if (!File.Exists(candidatePath))
                {
                    continue;
                }

                if (candidatePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    return new ResolvedCliHostCommand(["dotnet", candidatePath], $"dotnet \"{candidatePath}\"");
                }

                return new ResolvedCliHostCommand([candidatePath], $"\"{candidatePath}\"");
            }
        }

        throw new InvalidOperationException("Could not locate the packaged CLI helper executable.");
    }

    private static IEnumerable<string> EnumerateCandidateRoots(string startPath)
    {
        var current = Path.GetFullPath(startPath);
        while (!string.IsNullOrWhiteSpace(current))
        {
            yield return current;
            current = Path.GetDirectoryName(current) ?? string.Empty;
        }
    }

    private static string BuildProviderProbeCommand(WorkspaceSnapshot snapshot)
    {
        var containerName = $"{WorkspacePathBuilder.Slugify(snapshot.Definition.Workspace.Name)}-workspace";
        return string.Join(' ',
            "$ErrorActionPreference='Stop';",
            "$sessionOutput = & docker.exe exec --user opencode -w /workspace",
            containerName,
            "bash -lc 'export HOME=/home/opencode; opencode session list 2>/dev/null || true';",
            "$lines = @($sessionOutput) | Select-Object -Skip 2;",
            "$ids = $lines | ForEach-Object { ($_ -split '\\s+')[0] } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) };",
            "foreach ($id in $ids) {",
            "$json = & docker.exe exec --user opencode -w /workspace",
            containerName,
            "bash -lc \"export HOME=/home/opencode; opencode export '$id' 2>/dev/null\";",
            "if (-not [string]::IsNullOrWhiteSpace($json)) { try { $obj = $json | ConvertFrom-Json; if ($obj.info.directory -eq '/workspace') { [Console]::Out.WriteLine($id) } } catch { } }",
            "}");
    }

    private sealed record ResolvedCliHostCommand(IReadOnlyList<string> FileAndArguments, string RedactedCommandText);
}

public sealed record InteractiveSessionLaunchDescriptorSet
{
    public ApprovedTerminalLaunchDescriptor TerminalLaunchDescriptor { get; init; } = new();
    public ApprovedProcessLaunchDescriptor ProcessLaunchDescriptor { get; init; } = new();
    public ApprovedProcessLaunchDescriptor? ProviderSessionProbeDescriptor { get; init; }
}

internal sealed record AttachmentRuntimeState
{
    public string InteractiveAgentSessionId { get; init; } = string.Empty;
    public string AttachmentId { get; init; } = string.Empty;
    public string OwnerClientInstanceId { get; init; } = string.Empty;
    public string AttachmentTokenHash { get; init; } = string.Empty;
    public int TokenGeneration { get; init; } = 1;
    public string AttachmentRecoveryId { get; init; } = string.Empty;
    public string RecoverySecretHash { get; init; } = string.Empty;
    public string LaunchCorrelationId { get; init; } = string.Empty;
    public DateTimeOffset HelperStartedUtc { get; init; }
    public DateTimeOffset? ChildStartedUtc { get; init; }
    public DateTimeOffset? RecoveryEligibleUntilUtc { get; init; }
    public bool RecoveryBlockedByCleanShutdown { get; init; }
    public ApprovedProcessLaunchDescriptor ProcessLaunchDescriptor { get; init; } = new();
    public ApprovedProcessLaunchDescriptor ProviderSessionProbeDescriptor { get; init; } = new();
    public InteractiveAttachmentControlAction RequestedAction { get; init; }
    public int? HelperProcessId { get; init; }
    public int? ChildProcessId { get; init; }
    public InteractiveSessionAttachmentRecord? CompletedAttachment { get; init; }
    public InteractiveAgentSessionRecord? CompletedSession { get; init; }

    public bool TokenMatches(string attachmentToken)
        => string.Equals(AttachmentTokenHash, HashToken(attachmentToken), StringComparison.Ordinal);

    public bool RecoverySecretMatches(string recoverySecret)
        => string.Equals(RecoverySecretHash, HashToken(recoverySecret), StringComparison.Ordinal);

    public static string CreateTokenHash(string token)
        => HashToken(token);

    public static AttachmentRuntimeState CreateCurrent(string interactiveAgentSessionId, string attachmentId, string ownerClientInstanceId, string attachmentToken, string attachmentRecoveryId, string recoverySecret, string launchCorrelationId, ApprovedProcessLaunchDescriptor processLaunchDescriptor, ApprovedProcessLaunchDescriptor providerSessionProbeDescriptor)
        => new()
        {
            InteractiveAgentSessionId = interactiveAgentSessionId,
            AttachmentId = attachmentId,
            OwnerClientInstanceId = ownerClientInstanceId,
            AttachmentTokenHash = HashToken(attachmentToken),
            AttachmentRecoveryId = attachmentRecoveryId,
            RecoverySecretHash = HashToken(recoverySecret),
            LaunchCorrelationId = launchCorrelationId,
            ProcessLaunchDescriptor = processLaunchDescriptor,
            ProviderSessionProbeDescriptor = providerSessionProbeDescriptor,
        };

    private static string HashToken(string attachmentToken)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(attachmentToken ?? string.Empty))).ToLowerInvariant();
}

internal sealed record PersistedAttachmentRuntimeState
{
    public string InteractiveAgentSessionId { get; init; } = string.Empty;
    public string AttachmentId { get; init; } = string.Empty;
    public string OwnerClientInstanceId { get; init; } = string.Empty;
    public string AttachmentTokenHash { get; init; } = string.Empty;
    public int TokenGeneration { get; init; }
    public string AttachmentRecoveryId { get; init; } = string.Empty;
    public string RecoverySecretHash { get; init; } = string.Empty;
    public string LaunchCorrelationId { get; init; } = string.Empty;
    public DateTimeOffset HelperStartedUtc { get; init; }
    public DateTimeOffset? ChildStartedUtc { get; init; }
    public DateTimeOffset? RecoveryEligibleUntilUtc { get; init; }
    public bool RecoveryBlockedByCleanShutdown { get; init; }
    public InteractiveAttachmentControlAction RequestedAction { get; init; }
    public int? HelperProcessId { get; init; }
    public int? ChildProcessId { get; init; }

    public static PersistedAttachmentRuntimeState FromRuntime(AttachmentRuntimeState runtime)
        => new()
        {
            InteractiveAgentSessionId = runtime.InteractiveAgentSessionId,
            AttachmentId = runtime.AttachmentId,
            OwnerClientInstanceId = runtime.OwnerClientInstanceId,
            AttachmentTokenHash = runtime.AttachmentTokenHash,
            TokenGeneration = runtime.TokenGeneration,
            AttachmentRecoveryId = runtime.AttachmentRecoveryId,
            RecoverySecretHash = runtime.RecoverySecretHash,
            LaunchCorrelationId = runtime.LaunchCorrelationId,
            HelperStartedUtc = runtime.HelperStartedUtc,
            ChildStartedUtc = runtime.ChildStartedUtc,
            RecoveryEligibleUntilUtc = runtime.RecoveryEligibleUntilUtc,
            RecoveryBlockedByCleanShutdown = runtime.RecoveryBlockedByCleanShutdown,
            RequestedAction = runtime.RequestedAction,
            HelperProcessId = runtime.HelperProcessId,
            ChildProcessId = runtime.ChildProcessId,
        };

    public AttachmentRuntimeState ToRuntime()
        => new()
        {
            InteractiveAgentSessionId = InteractiveAgentSessionId,
            AttachmentId = AttachmentId,
            OwnerClientInstanceId = OwnerClientInstanceId,
            AttachmentTokenHash = AttachmentTokenHash,
            TokenGeneration = TokenGeneration,
            AttachmentRecoveryId = AttachmentRecoveryId,
            RecoverySecretHash = RecoverySecretHash,
            LaunchCorrelationId = LaunchCorrelationId,
            HelperStartedUtc = HelperStartedUtc,
            ChildStartedUtc = ChildStartedUtc,
            RecoveryEligibleUntilUtc = RecoveryEligibleUntilUtc,
            RecoveryBlockedByCleanShutdown = RecoveryBlockedByCleanShutdown,
            RequestedAction = RequestedAction,
            HelperProcessId = HelperProcessId,
            ChildProcessId = ChildProcessId,
        };
}

public sealed class LocalHostApplicationService(
    IOpenCodeWorkspaceMcpService service,
    IWorkspaceOperationService operations,
    WorkspaceInstanceService workspaceInstances,
    ControllerSessionService controllerSessions,
    InteractiveAgentSessionService interactiveSessions,
    InteractiveSessionAttachmentService interactiveAttachments)
{
    public ServerHealthModel GetServerHealth() => service.GetServerHealth();
    public async Task<IReadOnlyList<LocalTemplateSummaryModel>> ListWorkspaceTemplatesAsync(CancellationToken cancellationToken = default)
        => LocalHostModelMapper.MapToLocal<List<LocalTemplateSummaryModel>>(await service.ListWorkspaceTemplatesAsync(cancellationToken));
    public async Task<LocalTemplateDetailModel> GetWorkspaceTemplateAsync(string templateId, CancellationToken cancellationToken = default)
        => LocalHostModelMapper.MapToLocal<LocalTemplateDetailModel>(await service.GetWorkspaceTemplateAsync(templateId, cancellationToken));
    public Task<WorkspaceSmokeDefinitionCatalogResult> ListSmokeDefinitionsAsync(CancellationToken cancellationToken = default) => service.ListSmokeDefinitionsAsync(cancellationToken);
    public async Task<IReadOnlyList<LocalWorkspaceRecordModel>> ListWorkspacesAsync(CancellationToken cancellationToken = default) => LocalHostModelMapper.MapToLocal<List<LocalWorkspaceRecordModel>>(await service.ListWorkspacesAsync(cancellationToken));
    public async Task<LocalWorkspaceRecordModel> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => LocalHostModelMapper.MapToLocal<LocalWorkspaceRecordModel>(await service.GetWorkspaceAsync(workspaceId, cancellationToken));
    public async Task<LocalWorkspaceRecordModel> CreateWorkspaceAsync(string templateId, string workspaceName, string destinationRoot, CancellationToken cancellationToken = default) => LocalHostModelMapper.MapToLocal<LocalWorkspaceRecordModel>(await service.CreateWorkspaceAsync(templateId, workspaceName, destinationRoot, cancellationToken));
    public async Task<LocalWorkspaceRecordModel> CreateWorkspaceAsync(WorkspaceCreateRequest request, CancellationToken cancellationToken = default)
        => LocalHostModelMapper.MapToLocal<LocalWorkspaceRecordModel>(await service.CreateWorkspaceAsync(request.TemplateId, request.WorkspaceName, Path.GetDirectoryName(request.WorkspaceRootPath) ?? request.WorkspaceRootPath, cancellationToken));
    public Task<ExistingGitCheckoutPlan> InspectExistingGitCheckoutAsync(ExistingGitCheckoutInspectionRequest request, CancellationToken cancellationToken = default) => service.InspectExistingGitCheckoutAsync(request.RepositoryPath, request.WorkspaceName, cancellationToken);
    public Task<GitBranchValidationResult> ValidateExistingGitCheckoutBranchAsync(ExistingGitCheckoutBranchValidationRequest request, CancellationToken cancellationToken = default) => service.ValidateExistingGitCheckoutBranchAsync(request.RepositoryPath, request.BranchName, cancellationToken);
    public async Task<LocalWorkspaceRecordModel> ImportExistingGitCheckoutAsync(ExistingGitCheckoutImportRequest request, CancellationToken cancellationToken = default)
        => LocalHostModelMapper.MapToLocal<LocalWorkspaceRecordModel>(await service.ImportExistingGitCheckoutAsync(request, cancellationToken));
    public Task<string> SuggestSavePointMessageAsync(string workspaceId, CancellationToken cancellationToken = default) => service.SuggestSavePointMessageAsync(workspaceId, cancellationToken);
    public async Task<WorkspacePublishAssessmentRecord> GetWorkspacePublishAssessmentAsync(string workspaceId, CancellationToken cancellationToken = default)
        => LocalHostModelMapper.MapToLocal<WorkspacePublishAssessmentRecord>(await service.AssessWorkspacePublishAsync(workspaceId, cancellationToken));
    public async Task<WorkspaceRecoveryAssessmentRecord> GetWorkspaceRecoveryAssessmentAsync(string workspaceId, CancellationToken cancellationToken = default)
        => LocalHostModelMapper.MapToLocal<WorkspaceRecoveryAssessmentRecord>(await service.AssessWorkspaceRecoveryAsync(workspaceId, cancellationToken));
    public Task<WorkspaceSynchronizationStatusResult> GetSynchronizationStatusAsync(string workspaceId, string? environmentName = null, CancellationToken cancellationToken = default)
        => service.GetSynchronizationStatusAsync(workspaceId, environmentName, cancellationToken);
    public Task<WorkspaceTimeline> GetWorkspaceTimelineAsync(string workspaceId, CancellationToken cancellationToken = default) => service.GetWorkspaceTimelineAsync(workspaceId, cancellationToken);
    public Task<WorkspaceCheckpointIndex> GetWorkspaceCheckpointIndexAsync(string workspaceId, CancellationToken cancellationToken = default) => service.GetWorkspaceCheckpointIndexAsync(workspaceId, cancellationToken);
    public async Task<LocalWorkspaceRecordModel> ValidateWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => LocalHostModelMapper.MapToLocal<LocalWorkspaceRecordModel>(await service.ValidateWorkspaceAsync(workspaceId, cancellationToken));
    public async Task<LocalWorkspaceRecordModel> StopWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => LocalHostModelMapper.MapToLocal<LocalWorkspaceRecordModel>(await service.StopWorkspaceAsync(workspaceId, cancellationToken));
    public async Task<LocalWorkspaceRecordModel> RemoveWorkspaceRuntimeAsync(string workspaceId, CancellationToken cancellationToken = default) => LocalHostModelMapper.MapToLocal<LocalWorkspaceRecordModel>(await service.RemoveWorkspaceRuntimeAsync(workspaceId, cancellationToken));
    public Task<IReadOnlyList<WorkspaceSmokeDefinition>> SelectSmokeDefinitionsAsync(WorkspaceSmokeDefinitionSelectionRequest request, CancellationToken cancellationToken = default) => service.SelectSmokeDefinitionsAsync(request, cancellationToken);
    public Task<RuntimeResourceInventory> ListRuntimeResourcesAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken = default) => service.ListRuntimeResourcesAsync(query, cancellationToken);
    public Task<RuntimeResourceInventory> RunRuntimeDoctorAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken = default) => service.RunRuntimeDoctorAsync(query, cancellationToken);
    public Task<SmokeCleanupResult> CleanupSmokeResourcesAsync(SmokeCleanupOptions options, CancellationToken cancellationToken = default) => service.CleanupSmokeResourcesAsync(options, cancellationToken);

    public Task<IReadOnlyList<WorkspaceInstanceRecord>> ListWorkspaceInstancesAsync(CancellationToken cancellationToken = default) => workspaceInstances.ListAsync(cancellationToken);
    public Task<WorkspaceInstanceRecord> GetWorkspaceInstanceAsync(string workspaceInstanceId, CancellationToken cancellationToken = default) => workspaceInstances.GetAsync(workspaceInstanceId, cancellationToken);
    public IReadOnlyList<WorkspaceOperationRecord> ListOperations() => operations.List();
    public WorkspaceOperationRecord GetOperation(string operationId, long? afterSequence = null, int? maxEvents = null) => operations.Get(operationId, afterSequence, maxEvents);
    public Task<WorkspaceOperationRecord> CancelOperationAsync(string operationId, CancellationToken cancellationToken = default) => operations.CancelAsync(operationId, cancellationToken);
    public Task<IReadOnlyList<ControllerSessionRecord>> ListControllerSessionsAsync() => Task.FromResult(controllerSessions.List());
    public Task<ControllerSessionRecord> UpsertControllerSessionAsync(ControllerSessionUpsertRequest request, CancellationToken cancellationToken = default) => controllerSessions.UpsertAsync(request, cancellationToken);
    public Task<ControllerSessionRecord> DisconnectControllerSessionAsync(string controllerSessionId, CancellationToken cancellationToken = default) => controllerSessions.DisconnectAsync(controllerSessionId, cancellationToken);
    public Task<IReadOnlyList<InteractiveAgentSessionRecord>> ListInteractiveAgentSessionsAsync(string? workspaceId = null, CancellationToken cancellationToken = default) => interactiveSessions.ListAsync(workspaceId, cancellationToken);
    public Task<InteractiveAgentSessionRecord> GetInteractiveAgentSessionAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default) => interactiveSessions.GetAsync(interactiveAgentSessionId, cancellationToken);
    public Task<InteractiveAgentSessionRecord> CreateInteractiveAgentSessionAsync(CreateInteractiveAgentSessionRequest request, CancellationToken cancellationToken = default) => interactiveSessions.CreateAsync(request, cancellationToken);
    public Task<IReadOnlyList<InteractiveSessionAttachmentRecord>> GetInteractiveAttachmentsAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default) => interactiveSessions.GetAttachmentsAsync(interactiveAgentSessionId, cancellationToken);
    public Task<InteractiveSessionAttachResult> AttachInteractiveSessionAsync(string interactiveAgentSessionId, AttachInteractiveSessionRequest request, CancellationToken cancellationToken = default) => interactiveAttachments.AttachAsync(interactiveAgentSessionId, request, cancellationToken);
    public Task<InteractiveSessionAttachmentActivationResult> ActivateInteractiveSessionAttachmentAsync(string interactiveAgentSessionId, string attachmentId, ActivateInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken = default) => interactiveAttachments.ActivateAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);
    public Task<InteractiveSessionAttachmentRecoveryResult> RecoverInteractiveSessionAttachmentAsync(string interactiveAgentSessionId, string attachmentId, RecoverInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken = default) => interactiveAttachments.RecoverAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);
    public Task<InteractiveSessionAttachmentRecord> ReportInteractiveSessionAttachmentProcessStartedAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProcessStartedRequest request, CancellationToken cancellationToken = default) => interactiveAttachments.ReportProcessStartedAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);
    public Task<InteractiveSessionAttachmentHeartbeatResult> HeartbeatInteractiveSessionAttachmentAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentHeartbeatRequest request, CancellationToken cancellationToken = default) => interactiveAttachments.HeartbeatAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);
    public Task<InteractiveAgentSessionRecord> ReportInteractiveSessionProviderSessionAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProviderSessionRequest request, CancellationToken cancellationToken = default) => interactiveAttachments.ReportProviderSessionAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);
    public Task<InteractiveAgentSessionRecord> ReportInteractiveSessionAttachmentProcessExitAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProcessExitRequest request, CancellationToken cancellationToken = default) => interactiveAttachments.ReportProcessExitAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);
    public Task<InteractiveAgentSessionRecord> ReportInteractiveSessionAttachmentLaunchFailureAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentLaunchFailureRequest request, CancellationToken cancellationToken = default) => interactiveAttachments.ReportLaunchFailureAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);
    public Task<InteractiveAgentSessionRecord> DetachInteractiveSessionAsync(string interactiveAgentSessionId, string attachmentId, DetachInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken = default) => interactiveAttachments.DetachAsync(interactiveAgentSessionId, attachmentId, request, cancellationToken);

    public async Task<OracleApexApplicationDiscoveryResult> DiscoverOracleApexApplicationsAsync(OracleApexApplicationDiscoveryQuery request, CancellationToken cancellationToken = default)
        => await service.DiscoverOracleApexApplicationsAsync(request.WorkspaceId, request.EnvironmentName, request.WorkspaceName, request.ParsingSchema, request.SqlclProfile, request.SourcePath, cancellationToken);

    private WorkspaceOperationRecord FindAssistantPlanOperation(string workspaceId, string planId)
        => operations.List()
            .Where(item => string.Equals(item.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.Equals(item.OperationKind, "plan_oracle_assistant", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(item => string.Equals(item.Result?.Deserialize<OracleAssistantPlanOperationRecord>(LocalHostContract.JsonOptions)?.PlanId, planId, StringComparison.OrdinalIgnoreCase))
            ?? throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant plan '{planId}' was not found for workspace '{workspaceId}'.", "Refresh the Assistant plan and retry.");

    private WorkspaceOperationRecord FindAssistantApplyOperation(string workspaceId, string executionId)
        => operations.List()
            .Where(item => string.Equals(item.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.Equals(item.OperationKind, "apply_oracle_assistant", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(item => string.Equals(item.Result?.Deserialize<OracleAssistantApplyOperationRecord>(LocalHostContract.JsonOptions)?.ExecutionId, executionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant execution '{executionId}' was not found for workspace '{workspaceId}'.", "Refresh the Assistant execution state and retry.");

    private WorkspaceOperationRecord FindAssistantRepairPlanOperation(string workspaceId, string repairPlanId)
        => operations.List()
            .Where(item => string.Equals(item.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.Equals(item.OperationKind, "plan_oracle_assistant_repair", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(item => string.Equals(item.Result?.Deserialize<OracleAssistantRepairPlanOperationRecord>(LocalHostContract.JsonOptions)?.RepairPlanId, repairPlanId, StringComparison.OrdinalIgnoreCase))
            ?? throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant repair plan '{repairPlanId}' was not found for workspace '{workspaceId}'.", "Rebuild the repair plan and retry.");

    private static string BuildAssistantContextRevision(WorkspaceSnapshot snapshot, string? environmentName)
    {
        var resolvedEnvironmentName = string.IsNullOrWhiteSpace(environmentName)
            ? snapshot.Synchronization.DefaultEnvironment?.EnvironmentName ?? "dev"
            : environmentName;
        var environment = snapshot.Synchronization.DefaultEnvironment;
        var gitRevision = string.IsNullOrWhiteSpace(snapshot.Safety.AdvancedGit.LatestCommitSha) ? "nogit" : snapshot.Safety.AdvancedGit.LatestCommitSha;
        var sourceSignature = environment?.WorkspaceSourceSignature ?? string.Empty;
        return $"{resolvedEnvironmentName}|{gitRevision}|{sourceSignature}";
    }

    public async Task<WorkspaceOperationRecord> StartProvisionWorkspaceAsync(WorkspaceProvisionRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "provision_workspace",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("preparing", "Preparing workspace provisioning.");
                return await service.ProvisionWorkspaceAsync(request.WorkspaceId, reporter.ReportProgress, token);
            },
            $"provision_workspace::{request.WorkspaceId}",
            cancellationToken);
    }

    public Task<WorkspaceOperationRecord> StartPrepareWorkspaceAsync(WorkspaceLifecycleRequest request, CancellationToken cancellationToken = default)
        => StartWorkspaceLifecycleAsync("prepare_workspace", "preparing", "Preparing workspace.", request, (service, workspaceId, reporter, token) => service.PrepareWorkspaceAsync(workspaceId, reporter.ReportProgress, token), cancellationToken);

    public Task<WorkspaceOperationRecord> StartStartWorkspaceAsync(WorkspaceLifecycleRequest request, CancellationToken cancellationToken = default)
        => StartWorkspaceLifecycleAsync("start_workspace", "starting", "Starting workspace.", request, (service, workspaceId, reporter, token) => service.StartWorkspaceAsync(workspaceId, reporter.ReportProgress, token), cancellationToken);

    public Task<WorkspaceOperationRecord> StartStopWorkspaceAsync(WorkspaceLifecycleRequest request, CancellationToken cancellationToken = default)
        => StartWorkspaceLifecycleAsync("stop_workspace", "stopping", "Stopping workspace.", request, async (service, workspaceId, reporter, token) => await service.StopWorkspaceAsync(workspaceId, token), cancellationToken);

    public Task<WorkspaceOperationRecord> StartRecoverWorkspaceAsync(WorkspaceLifecycleRequest request, CancellationToken cancellationToken = default)
        => StartWorkspaceLifecycleAsync("recover_workspace", "recovering", "Recovering workspace.", request, (service, workspaceId, reporter, token) => service.RecoverWorkspaceAsync(workspaceId, reporter.ReportProgress, token), cancellationToken);

    public Task<WorkspaceOperationRecord> StartResetRuntimeAsync(WorkspaceLifecycleRequest request, CancellationToken cancellationToken = default)
        => StartWorkspaceLifecycleAsync("reset_workspace_runtime", "resettingRuntime", "Resetting runtime.", request, (service, workspaceId, reporter, token) => service.ResetWorkspaceRuntimeAsync(workspaceId, reporter.ReportProgress, token), cancellationToken);

    public async Task<WorkspaceOperationRecord> StartValidateSynchronizationAsync(WorkspaceSynchronizationValidationRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.DeploymentProfileOverride))
        {
            var status = await service.GetSynchronizationStatusAsync(request.WorkspaceId, string.IsNullOrWhiteSpace(request.EnvironmentName) ? null : request.EnvironmentName, cancellationToken);
            var availableProfiles = status.Snapshot.DefaultEnvironment?.AvailableDeploymentProfiles ?? Array.Empty<string>();
            if (!availableProfiles.Contains(request.DeploymentProfileOverride, StringComparer.OrdinalIgnoreCase))
            {
                throw new OpenCodeWorkspaceMcpException("invalid_request", $"Deployment profile '{request.DeploymentProfileOverride}' was not found for workspace '{workspace.Name}'.", "Retry with a known deployment profile.");
            }
        }

        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "validate_synchronization",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("validatingSynchronization", "Validating Oracle APEX source.");
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "loadingWorkspace", Message = "Loading current workspace state..." });
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "loadingWorkspace", Message = $"Selected workspace '{workspace.Name}'." });
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "validatingSynchronization", Message = "Validate in progress..." });
                var result = await service.ValidateSynchronizationAsync(request.WorkspaceId, string.IsNullOrWhiteSpace(request.EnvironmentName) ? null : request.EnvironmentName, string.IsNullOrWhiteSpace(request.DeploymentProfileOverride) ? null : request.DeploymentProfileOverride, token);
                foreach (var line in result.Message.Split([Environment.NewLine], StringSplitOptions.None))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "validatedSynchronization", Message = line });
                    }
                }

                return result;
            },
            $"validate_synchronization::{request.WorkspaceId}::{request.EnvironmentName}::{request.DeploymentProfileOverride}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartExportSynchronizationAsync(WorkspaceSynchronizationExportRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "export_synchronization",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("exportingSynchronization", "Exporting Oracle APEX changes.");
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "loadingWorkspace", Message = "Loading current workspace state..." });
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "loadingWorkspace", Message = $"Selected workspace '{workspace.Name}'." });
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "exportingSynchronization", Message = "Export in progress..." });
                var result = await service.ExportSynchronizationAsync(request.WorkspaceId, string.IsNullOrWhiteSpace(request.EnvironmentName) ? null : request.EnvironmentName, token);
                foreach (var line in result.Message.Split([Environment.NewLine], StringSplitOptions.None))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "exportedSynchronization", Message = line });
                    }
                }

                return result;
            },
            $"export_synchronization::{request.WorkspaceId}::{request.EnvironmentName}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartPullSynchronizationAsync(WorkspaceSynchronizationExportRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "pull_synchronization",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("pullingSynchronization", "Pulling Oracle APEX changes into Git.");
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "loadingWorkspace", Message = "Loading current workspace state..." });
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "loadingWorkspace", Message = $"Selected workspace '{workspace.Name}'." });
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "pullingSynchronization", Message = "Pull in progress..." });
                var result = await service.PullSynchronizationAsync(request.WorkspaceId, string.IsNullOrWhiteSpace(request.EnvironmentName) ? null : request.EnvironmentName, token);
                foreach (var line in result.Message.Split([Environment.NewLine], StringSplitOptions.None))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "pulledSynchronization", Message = line });
                    }
                }

                return result;
            },
            $"pull_synchronization::{request.WorkspaceId}::{request.EnvironmentName}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartPushSynchronizationAsync(WorkspaceSynchronizationImportRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.DeploymentProfileOverride))
        {
            var status = await service.GetSynchronizationStatusAsync(request.WorkspaceId, string.IsNullOrWhiteSpace(request.EnvironmentName) ? null : request.EnvironmentName, cancellationToken);
            var availableProfiles = status.Snapshot.DefaultEnvironment?.AvailableDeploymentProfiles ?? Array.Empty<string>();
            if (!availableProfiles.Contains(request.DeploymentProfileOverride, StringComparer.OrdinalIgnoreCase))
            {
                throw new OpenCodeWorkspaceMcpException("invalid_request", $"Deployment profile '{request.DeploymentProfileOverride}' was not found for workspace '{workspace.Name}'.", "Retry with a known deployment profile.");
            }
        }

        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "push_synchronization",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("pushingSynchronization", "Pushing Git changes into Oracle APEX.");
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "loadingWorkspace", Message = "Loading current workspace state..." });
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "loadingWorkspace", Message = $"Selected workspace '{workspace.Name}'." });
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "pushingSynchronization", Message = "Push in progress..." });
                var result = await service.PushSynchronizationAsync(request.WorkspaceId, string.IsNullOrWhiteSpace(request.EnvironmentName) ? null : request.EnvironmentName, string.IsNullOrWhiteSpace(request.DeploymentProfileOverride) ? null : request.DeploymentProfileOverride, token);
                foreach (var line in result.Message.Split([Environment.NewLine], StringSplitOptions.None))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "pushedSynchronization", Message = line });
                    }
                }

                return result;
            },
            $"push_synchronization::{request.WorkspaceId}::{request.EnvironmentName}::{request.DeploymentProfileOverride}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartValidateOracleAssistantAsync(OracleAssistantValidationRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "validate_oracle_assistant",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("validatingAssistantSource", "Running SQLcl validation.");
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "loadingAssistantPlan", Message = $"Using Oracle Assistant execution '{request.ExecutionId}'." });
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "loadingWorkspace", Message = $"Selected workspace '{workspace.Name}'." });
                var result = await service.ValidateOracleAssistantGeneratedApplicationAsync(request.WorkspaceId, string.IsNullOrWhiteSpace(request.ExecutionId) ? null : request.ExecutionId, string.IsNullOrWhiteSpace(request.EnvironmentName) ? null : request.EnvironmentName, token);
                foreach (var line in result.Response.Message.Split([Environment.NewLine], StringSplitOptions.None))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "completedAssistantValidation", Message = line });
                    }
                }

                return result;
            },
            $"validate_oracle_assistant::{request.WorkspaceId}::{request.ExecutionId}::{request.EnvironmentName}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartImportOracleAssistantAsync(OracleAssistantImportRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.DeploymentProfileOverride))
        {
            var status = await service.GetSynchronizationStatusAsync(request.WorkspaceId, string.IsNullOrWhiteSpace(request.EnvironmentName) ? null : request.EnvironmentName, cancellationToken);
            var availableProfiles = status.Snapshot.DefaultEnvironment?.AvailableDeploymentProfiles ?? Array.Empty<string>();
            if (!availableProfiles.Contains(request.DeploymentProfileOverride, StringComparer.OrdinalIgnoreCase))
            {
                throw new OpenCodeWorkspaceMcpException("invalid_request", $"Deployment profile '{request.DeploymentProfileOverride}' was not found for workspace '{workspace.Name}'.", "Retry with a known deployment profile.");
            }
        }

        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "import_oracle_assistant",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("importingAssistantChanges", "Importing validated APEXlang source.");
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "loadingAssistantPlan", Message = $"Using Oracle Assistant execution '{request.ExecutionId}'." });
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "loadingWorkspace", Message = $"Selected workspace '{workspace.Name}'." });
                var result = await service.ImportOracleAssistantGeneratedApplicationAsync(request.WorkspaceId, string.IsNullOrWhiteSpace(request.ExecutionId) ? null : request.ExecutionId, string.IsNullOrWhiteSpace(request.EnvironmentName) ? null : request.EnvironmentName, request.AllowNonDevelopmentDeployment, token);
                foreach (var line in result.Response.Message.Split([Environment.NewLine], StringSplitOptions.None))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "completedAssistantImport", Message = line });
                    }
                }

                return result;
            },
            $"import_oracle_assistant::{request.WorkspaceId}::{request.ExecutionId}::{request.EnvironmentName}:{request.AllowNonDevelopmentDeployment}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartImportSynchronizationAsync(WorkspaceSynchronizationImportRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.DeploymentProfileOverride))
        {
            var status = await service.GetSynchronizationStatusAsync(request.WorkspaceId, string.IsNullOrWhiteSpace(request.EnvironmentName) ? null : request.EnvironmentName, cancellationToken);
            var availableProfiles = status.Snapshot.DefaultEnvironment?.AvailableDeploymentProfiles ?? Array.Empty<string>();
            if (!availableProfiles.Contains(request.DeploymentProfileOverride, StringComparer.OrdinalIgnoreCase))
            {
                throw new OpenCodeWorkspaceMcpException("invalid_request", $"Deployment profile '{request.DeploymentProfileOverride}' was not found for workspace '{workspace.Name}'.", "Retry with a known deployment profile.");
            }
        }

        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "import_synchronization",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("importingSynchronization", "Importing workspace source into Oracle APEX.");
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "loadingWorkspace", Message = "Loading current workspace state..." });
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "loadingWorkspace", Message = $"Selected workspace '{workspace.Name}'." });
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "importingSynchronization", Message = "Import in progress..." });
                var result = await service.ImportSynchronizationAsync(request.WorkspaceId, string.IsNullOrWhiteSpace(request.EnvironmentName) ? null : request.EnvironmentName, string.IsNullOrWhiteSpace(request.DeploymentProfileOverride) ? null : request.DeploymentProfileOverride, token);
                foreach (var line in result.Message.Split([Environment.NewLine], StringSplitOptions.None))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "importedSynchronization", Message = line });
                    }
                }

                return result;
            },
            $"import_synchronization::{request.WorkspaceId}::{request.EnvironmentName}::{request.DeploymentProfileOverride}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartSynchronizeWorkspaceAsync(WorkspaceSynchronizeRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.DeploymentProfileOverride))
        {
            var status = await service.GetSynchronizationStatusAsync(request.WorkspaceId, string.IsNullOrWhiteSpace(request.EnvironmentName) ? null : request.EnvironmentName, cancellationToken);
            var availableProfiles = status.Snapshot.DefaultEnvironment?.AvailableDeploymentProfiles ?? Array.Empty<string>();
            if (!availableProfiles.Contains(request.DeploymentProfileOverride, StringComparer.OrdinalIgnoreCase))
            {
                throw new OpenCodeWorkspaceMcpException("invalid_request", $"Deployment profile '{request.DeploymentProfileOverride}' was not found for workspace '{workspace.Name}'.", "Retry with a known deployment profile.");
            }
        }

        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "synchronize_workspace",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("assessingSynchronization", "Synchronizing Oracle APEX workspace state.");
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "loadingWorkspace", Message = "Loading current workspace state..." });
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "loadingWorkspace", Message = $"Selected workspace '{workspace.Name}'." });

                var result = await service.SynchronizeWorkspaceAsync(request.WorkspaceId, string.IsNullOrWhiteSpace(request.EnvironmentName) ? null : request.EnvironmentName, string.IsNullOrWhiteSpace(request.DeploymentProfileOverride) ? null : request.DeploymentProfileOverride, token);
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "completedSynchronization", Message = $"Action: {result.ActionPerformed}" });

                var message = result.OperationResult?.Message
                    ?? result.DiffResult?.Summary
                    ?? "Synchronization assessment completed.";
                foreach (var line in string.IsNullOrWhiteSpace(result.DiffResult?.DiffText) ? message.Split([Environment.NewLine], StringSplitOptions.None) : $"{message}{Environment.NewLine}{result.DiffResult!.DiffText}".Split([Environment.NewLine], StringSplitOptions.None))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "completedSynchronization", Message = line });
                    }
                }

                return result;
            },
            $"synchronize_workspace::{request.WorkspaceId}::{request.EnvironmentName}::{request.DeploymentProfileOverride}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartPlanOracleAssistantAsync(OracleAssistantPlanRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "plan_oracle_assistant",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("buildingAssistantPlan", "Planning semantic APEXlang changes.");
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "loadingWorkspace", Message = $"Selected workspace '{workspace.Name}'." });
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "loadingAssistantContext", Message = "Building workspace index" });
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "analyzingIntent", Message = "Planning semantic changes" });
                var assistantRequest = new OracleApexAssistantRequest { Prompt = request.Intent, EnvironmentName = request.EnvironmentName };
                var result = await service.PlanOracleApexChangeAsync(request.WorkspaceId, assistantRequest, token);
                if (!string.IsNullOrWhiteSpace(result.Response.Review))
                {
                    reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "completedAssistantPlan", Message = result.Response.Review });
                }

                return result;
            },
            $"plan_oracle_assistant::{request.WorkspaceId}::{request.Intent}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartApplyOracleAssistantAsync(OracleAssistantApplyRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var plannedOperation = FindAssistantPlanOperation(request.WorkspaceId, request.PlanId);
        var planPayload = plannedOperation.Result?.Deserialize<OracleAssistantPlanOperationRecord>(LocalHostContract.JsonOptions)
            ?? throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant plan '{request.PlanId}' was not found.", "Refresh the Assistant plan and retry.");
        if (!string.Equals(planPayload.ContextRevision, request.ContextRevision, StringComparison.Ordinal))
        {
            throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant plan '{request.PlanId}' is stale for this workspace context.", "Create a new plan and retry.");
        }

        var currentContextRevision = BuildAssistantContextRevision(workspace.Snapshot, request.EnvironmentName);
        if (!string.Equals(currentContextRevision, request.ContextRevision, StringComparison.Ordinal))
        {
            throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant plan '{request.PlanId}' was created for '{request.ContextRevision}' but the current workspace context is '{currentContextRevision}'.", "Create a new plan and retry.");
        }

        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "apply_oracle_assistant",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("applyingAssistantChanges", "Applying semantic changes.");
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "loadingAssistantPlan", Message = $"Using Oracle Assistant plan '{request.PlanId}'." });
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "validatingPlanContext", Message = $"Context revision '{request.ContextRevision}'." });
                var assistantRequest = new OracleApexAssistantRequest
                {
                    Prompt = planPayload.Response.Request.Prompt,
                    ConfirmPlan = request.ConfirmPlan,
                    PostEditBehavior = request.PostEditBehavior,
                    EnvironmentName = request.EnvironmentName,
                    EnableSafeAutomaticRepair = request.EnableSafeAutomaticRepair,
                    AllowNonDevelopmentDeployment = request.AllowNonDevelopmentDeployment,
                };
                var result = await service.ExecuteOracleApexPlanAsync(request.WorkspaceId, assistantRequest, planPayload.Response.Plan, request.PlanId, request.ContextRevision, token);
                if (!string.IsNullOrWhiteSpace(result.Response.Summary))
                {
                    reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "completedAssistantApply", Message = result.Response.Summary });
                }

                return result;
            },
            $"apply_oracle_assistant::{request.WorkspaceId}::{request.PlanId}::{request.ContextRevision}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartPlanOracleAssistantRepairAsync(OracleAssistantRepairPlanRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var plannedOperation = FindAssistantPlanOperation(request.WorkspaceId, request.PlanId);
        var planPayload = plannedOperation.Result?.Deserialize<OracleAssistantPlanOperationRecord>(LocalHostContract.JsonOptions)
            ?? throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant plan '{request.PlanId}' was not found.", "Refresh the Assistant plan and retry.");
        var executionOperation = FindAssistantApplyOperation(request.WorkspaceId, request.ExecutionId);
        var executionPayload = executionOperation.Result?.Deserialize<OracleAssistantApplyOperationRecord>(LocalHostContract.JsonOptions)
            ?? throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant execution '{request.ExecutionId}' was not found.", "Refresh the Assistant execution state and retry.");
        if (!string.Equals(executionPayload.PlanId, request.PlanId, StringComparison.OrdinalIgnoreCase))
        {
            throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant execution '{request.ExecutionId}' does not belong to plan '{request.PlanId}'.", "Refresh the Assistant state and retry.");
        }

        var currentContextRevision = BuildAssistantContextRevision(workspace.Snapshot, executionPayload.Response.RollbackManifest?.EnvironmentName);
        if (!string.Equals(currentContextRevision, executionPayload.ContextRevision, StringComparison.Ordinal))
        {
            throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant execution '{request.ExecutionId}' is stale for the current workspace context.", "Revalidate the Assistant execution and retry.");
        }

        var compilerValidation = executionPayload.Response.CompilerValidation
            ?? throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant execution '{request.ExecutionId}' does not contain compiler validation diagnostics.", "Run Assistant validation before building a repair plan.");

        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "plan_oracle_assistant_repair",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("buildingRepairPlan", "Building semantic repair plan.");
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "loadingAssistantExecution", Message = $"Using Assistant execution '{request.ExecutionId}'." });
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "loadingValidationDiagnostics", Message = "Mapping compiler diagnostics" });
                var assistantRequest = executionPayload.Response.RollbackManifest is null
                    ? planPayload.Response.Request
                    : new OracleApexAssistantRequest
                    {
                        Prompt = planPayload.Response.Request.Prompt,
                        ConfirmPlan = true,
                        PostEditBehavior = OracleApexAssistantPostEditBehavior.ValidateOnly,
                        EnvironmentName = executionPayload.Response.RollbackManifest.EnvironmentName,
                    };
                var result = await service.CreateOracleApexRepairPlanAsync(request.WorkspaceId, assistantRequest, planPayload.Response.Plan, compilerValidation, request.PlanId, request.ExecutionId, executionPayload.ContextRevision, token);
                if (!string.IsNullOrWhiteSpace(result.Response.Review))
                {
                    reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "completedRepairPlan", Message = result.Response.Review });
                }

                return result;
            },
            $"plan_oracle_assistant_repair::{request.WorkspaceId}::{request.PlanId}::{request.ExecutionId}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartExecuteOracleAssistantRepairAsync(OracleAssistantRepairExecutionRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var plannedOperation = FindAssistantPlanOperation(request.WorkspaceId, request.PlanId);
        var planPayload = plannedOperation.Result?.Deserialize<OracleAssistantPlanOperationRecord>(LocalHostContract.JsonOptions)
            ?? throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant plan '{request.PlanId}' was not found.", "Refresh the Assistant plan and retry.");
        var executionOperation = FindAssistantApplyOperation(request.WorkspaceId, request.ExecutionId);
        var executionPayload = executionOperation.Result?.Deserialize<OracleAssistantApplyOperationRecord>(LocalHostContract.JsonOptions)
            ?? throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant execution '{request.ExecutionId}' was not found.", "Refresh the Assistant execution state and retry.");
        var repairPlanOperation = FindAssistantRepairPlanOperation(request.WorkspaceId, request.RepairPlanId);
        var repairPlanPayload = repairPlanOperation.Result?.Deserialize<OracleAssistantRepairPlanOperationRecord>(LocalHostContract.JsonOptions)
            ?? throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant repair plan '{request.RepairPlanId}' was not found.", "Rebuild the repair plan and retry.");
        if (!string.Equals(executionPayload.PlanId, request.PlanId, StringComparison.OrdinalIgnoreCase))
        {
            throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant execution '{request.ExecutionId}' does not belong to plan '{request.PlanId}'.", "Refresh the Assistant state and retry.");
        }

        if (!string.Equals(repairPlanPayload.PlanId, request.PlanId, StringComparison.OrdinalIgnoreCase))
        {
            throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant repair plan '{request.RepairPlanId}' does not belong to plan '{request.PlanId}'.", "Rebuild the repair plan and retry.");
        }

        if (!string.Equals(repairPlanPayload.ExecutionId, request.ExecutionId, StringComparison.OrdinalIgnoreCase))
        {
            throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant repair plan '{request.RepairPlanId}' does not belong to execution '{request.ExecutionId}'.", "Refresh the Assistant execution and rebuild the repair plan.");
        }

        var currentContextRevision = BuildAssistantContextRevision(workspace.Snapshot, executionPayload.Response.RollbackManifest?.EnvironmentName);
        if (!string.Equals(currentContextRevision, executionPayload.ContextRevision, StringComparison.Ordinal)
            || !string.Equals(currentContextRevision, repairPlanPayload.ContextRevision, StringComparison.Ordinal))
        {
            throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant repair plan '{request.RepairPlanId}' is stale for the current workspace context.", "Revalidate the Assistant execution, rebuild the repair plan, and retry.");
        }

        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "execute_oracle_assistant_repair",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("applyingRepairPlan", "Applying semantic repair operations.");
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "loadingRepairPlan", Message = $"Using Assistant repair plan '{request.RepairPlanId}'." });
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "validatingRepairContext", Message = $"Execution '{request.ExecutionId}' and plan '{request.PlanId}' validated." });
                var assistantRequest = new OracleApexAssistantRequest
                {
                    Prompt = planPayload.Response.Request.Prompt,
                    ConfirmPlan = true,
                    PostEditBehavior = OracleApexAssistantPostEditBehavior.ValidateOnly,
                    EnvironmentName = executionPayload.Response.RollbackManifest?.EnvironmentName ?? planPayload.Response.Request.EnvironmentName,
                    EnableSafeAutomaticRepair = planPayload.Response.Request.EnableSafeAutomaticRepair,
                    AllowNonDevelopmentDeployment = planPayload.Response.Request.AllowNonDevelopmentDeployment,
                };
                var result = await service.ExecuteOracleApexRepairPlanAsync(request.WorkspaceId, assistantRequest, repairPlanPayload.Response.Plan, request.PlanId, request.ExecutionId, request.RepairPlanId, repairPlanPayload.ContextRevision, token);
                if (!string.IsNullOrWhiteSpace(result.Response.Summary))
                {
                    reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "completedAssistantRepair", Message = result.Response.Summary });
                }

                return result;
            },
            $"execute_oracle_assistant_repair::{request.WorkspaceId}::{request.PlanId}::{request.ExecutionId}::{request.RepairPlanId}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartRollbackOracleAssistantAsync(OracleAssistantRollbackRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        _ = FindAssistantApplyOperation(request.WorkspaceId, request.ExecutionId);
        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "rollback_oracle_assistant",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("validatingRollbackManifest", "Validating rollback manifest.");
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "loadingAssistantExecution", Message = $"Using Assistant execution '{request.ExecutionId}'." });
                var result = await service.RollBackOracleApexGeneratedChangeAsync(request.WorkspaceId, request.ExecutionId, token);
                if (!string.IsNullOrWhiteSpace(result.Response.Summary))
                {
                    reporter.ReportProgress(new CommandLogEntry { Source = "oracleAssistant", Phase = "completedAssistantRollback", Message = result.Response.Summary });
                }

                return result;
            },
            $"rollback_oracle_assistant::{request.WorkspaceId}::{request.ExecutionId}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartConnectExistingOracleApexApplicationAsync(ConnectExistingOracleApexApplicationRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "connect_existing_oracle_apex_application",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("loadingWorkspace", "Loading current workspace state.");
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleApex", Phase = "resolvingOracleEnvironment", Message = $"Resolving Oracle APEX environment '{request.EnvironmentName}'." });
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleApex", Phase = "validatingSelectedApplication", Message = $"Validating Oracle APEX application '{request.ApplicationId}'." });
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleApex", Phase = "connectingApplication", Message = "Connecting Oracle APEX application and exporting source." });
                var result = await service.ConnectExistingOracleApexApplicationAsync(request.WorkspaceId, request.EnvironmentName, request.WorkspaceName, request.ParsingSchema, request.ApplicationId, request.SqlclProfile, request.SourcePath, token);
                reporter.ReportProgress(new CommandLogEntry { Source = "oracleApex", Phase = "completedApplicationConnection", Message = result.Message });
                return new ConnectExistingOracleApexApplicationOperationRecord
                {
                    EnvironmentName = request.EnvironmentName,
                    WorkspaceName = request.WorkspaceName,
                    ParsingSchema = request.ParsingSchema,
                    SqlclProfile = request.SqlclProfile,
                    SourcePath = request.SourcePath,
                    ApplicationId = request.ApplicationId,
                    ApplicationName = string.Empty,
                    Alias = string.Empty,
                    Message = result.Message,
                    ProcessResults = result.ProcessResults,
                };
            },
            $"connect_existing_oracle_apex_application::{request.WorkspaceId}::{request.EnvironmentName}::{request.ApplicationId}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartDiffSynchronizationAsync(WorkspaceSynchronizationDiffRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "diff_synchronization",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("diffingSynchronization", "Comparing workspace source and Oracle APEX export.");
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "loadingWorkspace", Message = "Loading current workspace state..." });
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "loadingWorkspace", Message = $"Selected workspace '{workspace.Name}'." });
                reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "diffingSynchronization", Message = "Show Diff in progress..." });
                var result = await service.DiffSynchronizationAsync(request.WorkspaceId, string.IsNullOrWhiteSpace(request.EnvironmentName) ? null : request.EnvironmentName, token);
                foreach (var line in string.IsNullOrWhiteSpace(result.DiffText) ? [result.Summary] : $"{result.Summary}{Environment.NewLine}{result.DiffText}".Split([Environment.NewLine], StringSplitOptions.None))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        reporter.ReportProgress(new CommandLogEntry { Source = "synchronization", Phase = "diffedSynchronization", Message = line });
                    }
                }

                return result;
            },
            $"diff_synchronization::{request.WorkspaceId}::{request.EnvironmentName}",
            cancellationToken);
    }

    public Task<WorkspaceOperationRecord> StartAttachWorkspaceAsync(WorkspaceLifecycleRequest request, CancellationToken cancellationToken = default)
        => StartWorkspaceLifecycleAsync("attach_workspace", "attaching", "Attaching workspace.", request, (service, workspaceId, reporter, token) => service.AttachWorkspaceAsync(workspaceId, reporter.ReportProgress, token), cancellationToken);

    public Task<WorkspaceOperationRecord> StartReprovisionWorkspaceAsync(WorkspaceLifecycleRequest request, CancellationToken cancellationToken = default)
        => StartWorkspaceLifecycleAsync("reprovision_workspace", "reprovisioning", "Reprovisioning workspace.", request, (service, workspaceId, reporter, token) => service.ReprovisionWorkspaceAsync(workspaceId, reporter.ReportProgress, token), cancellationToken);

    public Task<WorkspaceOperationRecord> StartCreateSavePointAsync(WorkspaceSavePointCreateRequest request, CancellationToken cancellationToken = default)
        => StartWorkspaceLifecycleAsync("create_save_point", "creatingSavePoint", "Creating Save Point.", new WorkspaceLifecycleRequest { CommandId = request.CommandId, WorkspaceId = request.WorkspaceId, RequestedBy = request.RequestedBy }, (service, workspaceId, reporter, token) => service.CreateSavePointAsync(workspaceId, request.Message, reporter.ReportProgress, token), cancellationToken);

    public async Task<WorkspaceOperationRecord> StartCreateCheckpointAsync(WorkspaceCheckpointCreateRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "create_checkpoint",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("creatingCheckpoint", "Creating checkpoint.");
                return LocalHostModelMapper.MapToLocal<LocalWorkspaceRecordModel>(await service.CreateCheckpointAsync(request.WorkspaceId, reporter.ReportProgress, token));
            },
            $"create_checkpoint::{request.WorkspaceId}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartBackupWorkspaceAsync(WorkspaceBackupRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            throw new OpenCodeWorkspaceMcpException("invalid_request", "Backup destination is required.", "Choose a backup destination and retry.");
        }

        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "backup_workspace",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("creatingBackup", "Creating backup archive.");
                return await service.BackupWorkspaceAsync(request.WorkspaceId, request.DestinationPath, request.OverwriteExisting, reporter.ReportProgress, token);
            },
            $"backup_workspace::{request.WorkspaceId}::{request.DestinationPath}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartPublishWorkspaceAsync(WorkspacePublishRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "publish_workspace",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("publishing", "Publishing Working Copy.");
                return await service.PublishWorkspaceAsync(request.WorkspaceId, reporter.ReportProgress, token);
            },
            $"publish_workspace::{request.WorkspaceId}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartRemoveWorkspaceAsync(OpenCode.Workspace.LocalClient.WorkspaceRemovalRequest request, CancellationToken cancellationToken = default)
    {
        if (request.DeleteWorkspaceFiles)
        {
            throw new OpenCodeWorkspaceMcpException("invalid_request", "Delete workspace files is not available in this version. Use File Explorer or terminal after creating a backup.", "Retry with workspace file deletion disabled.");
        }

        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            "remove_workspace",
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted("removingWorkspace", request.RemoveOwnedRuntimeResources ? "Removing Docker resources." : "Removing workspace from list.");
                return await service.RemoveWorkspaceAsync(request.WorkspaceId, request.RemoveOwnedRuntimeResources, request.DeleteWorkspaceFiles, reporter.ReportProgress, token);
            },
            $"remove_workspace::{request.WorkspaceId}::{request.RemoveOwnedRuntimeResources}:{request.DeleteWorkspaceFiles}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartSmokeRunAsync(SmokeRunOperationRequest request, CancellationToken cancellationToken = default)
    {
        await service.GetWorkspaceTemplateAsync(request.TemplateId, cancellationToken);
        return await operations.StartAsync(
            "run_smoke",
            WorkspaceOperationScope.Host,
            string.Empty,
            string.Empty,
            request.RequestedBy,
            async (reporter, token) =>
            {
                var smokeRequest = new WorkspaceSmokeSingleRunRequest
                {
                    TemplateId = request.TemplateId,
                    Timeout = string.IsNullOrWhiteSpace(request.Timeout) ? null : TimeSpan.Parse(request.Timeout),
                    ArtifactsRoot = request.ArtifactsRoot ?? string.Empty,
                    Progress = new Progress<WorkspaceSmokeProgressUpdate>(reporter.ReportProgress),
                };
                return await service.RunSmokeAsync(smokeRequest, token);
            },
            $"run_smoke::{request.TemplateId}",
            cancellationToken);
    }

    public async Task<WorkspaceOperationRecord> StartSmokeMatrixAsync(SmokeMatrixOperationRequest request, CancellationToken cancellationToken = default)
    {
        var selected = await service.SelectSmokeDefinitionsAsync(new WorkspaceSmokeDefinitionSelectionRequest { TemplateIds = request.TemplateIds, Family = request.Family, All = request.All }, cancellationToken);
        return await operations.StartAsync(
            "run_smoke_matrix",
            WorkspaceOperationScope.Host,
            string.Empty,
            string.Empty,
            request.RequestedBy,
            async (reporter, token) =>
            {
                var matrixRequest = new WorkspaceSmokeMatrixRunRequest
                {
                    TemplateIds = selected.Select(item => item.TemplateId).ToArray(),
                    MatrixTimeout = string.IsNullOrWhiteSpace(request.Timeout) ? null : TimeSpan.Parse(request.Timeout),
                    Progress = new Progress<WorkspaceSmokeProgressUpdate>(reporter.ReportProgress),
                };
                return await service.RunSmokeMatrixAsync(matrixRequest, token);
            },
            $"run_smoke_matrix::{string.Join(",", selected.Select(item => item.TemplateId))}",
            cancellationToken);
    }

    private async Task<WorkspaceOperationRecord> StartWorkspaceLifecycleAsync(
        string operationKind,
        string phase,
        string message,
        WorkspaceLifecycleRequest request,
        Func<IOpenCodeWorkspaceMcpService, string, WorkspaceOperationReporter, CancellationToken, Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel>> action,
        CancellationToken cancellationToken)
    {
        var workspace = await service.GetWorkspaceAsync(request.WorkspaceId, cancellationToken);
        var workspaceInstanceId = WorkspaceInstanceService.BuildWorkspaceInstanceId(workspace.WorkspaceId);
        return await operations.StartAsync(
            operationKind,
            WorkspaceOperationScope.Workspace,
            workspace.WorkspaceId,
            workspaceInstanceId,
            request.RequestedBy,
            async (reporter, token) =>
            {
                reporter.MarkStarted(phase, message);
                return await action(service, request.WorkspaceId, reporter, token);
            },
            $"{operationKind}::{request.WorkspaceId}",
            cancellationToken);
    }
}

internal static class LocalHostModelMapper
{
    public static T MapToLocal<T>(object value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, LocalHostContract.JsonOptions), LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Could not map '{value.GetType().Name}' to '{typeof(T).Name}'.");
}

public sealed class LocalHostDescriptorHostedService : IHostedService, IDisposable
{
    private readonly LocalHostStateStore _stateStore;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<LocalHostDescriptorHostedService> _logger;
    private FileStream? _lockStream;
    private LocalHostDescriptor? _descriptor;

    public LocalHostDescriptorHostedService(LocalHostStateStore stateStore, IConfiguration configuration, IHostEnvironment hostEnvironment, ILogger<LocalHostDescriptorHostedService> logger)
    {
        _stateStore = stateStore;
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.Equals(_hostEnvironment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(_configuration["localHost:enableDescriptorInTests"]))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_stateStore.LockPath)!);
        if (File.Exists(_stateStore.ShutdownMarkerPath))
        {
            File.Delete(_stateStore.ShutdownMarkerPath);
        }

        _lockStream = new FileStream(_stateStore.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var baseUrl = _configuration["ASPNETCORE_URLS"] ?? _configuration["urls"] ?? "http://127.0.0.1:43127";
        _descriptor = new LocalHostDescriptor
        {
            InstanceId = Guid.NewGuid().ToString("n"),
            ProcessId = Environment.ProcessId,
            BaseUrl = baseUrl.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0],
            StartedUtc = DateTimeOffset.UtcNow,
            ExecutablePath = Environment.ProcessPath ?? string.Empty,
        };
        await _stateStore.WriteJsonAsync(_stateStore.DescriptorPath, _descriptor, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (string.Equals(_hostEnvironment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(_configuration["localHost:enableDescriptorInTests"]))
        {
            return Task.CompletedTask;
        }

        try
        {
            File.WriteAllText(_stateStore.ShutdownMarkerPath, $"{DateTimeOffset.UtcNow:O}");
            if (File.Exists(_stateStore.DescriptorPath))
            {
                File.Delete(_stateStore.DescriptorPath);
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not remove LocalHost descriptor during shutdown.");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _lockStream?.Dispose();
    }
}

internal sealed class WorkspaceOperationRuntimeState(WorkspaceOperationRecord record, string operationRoot, CancellationTokenSource tokenSource)
{
    public WorkspaceOperationRecord Record { get; set; } = record;
    public string OperationRoot { get; } = operationRoot;
    public CancellationTokenSource TokenSource { get; } = tokenSource;
    public object SyncRoot { get; } = new();
}
