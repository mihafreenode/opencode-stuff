using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace OpenCode.Workspace.Mcp;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class McpOperationStore : IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<string, McpOperationState> _operations = new(StringComparer.OrdinalIgnoreCase);
    private readonly OpenCodeWorkspaceMcpOptions _options;
    private readonly ILogger<McpOperationStore> _logger;
    private readonly ISystemClock _clock;
    private int _stopping;

    public McpOperationStore(OpenCodeWorkspaceMcpOptions options, ILogger<McpOperationStore> logger, ISystemClock? clock = null)
    {
        _options = options;
        _logger = logger;
        _clock = clock ?? new SystemClock();
    }

    public McpOperationModel Start(string kind, string workspaceId, string initialMessage, Func<McpOperationReporter, CancellationToken, Task<object>> work)
    {
        if (Volatile.Read(ref _stopping) != 0)
        {
            throw new OpenCodeWorkspaceMcpException("server_stopping", "The MCP server is shutting down.", "Retry after the local MCP host restarts.");
        }

        TrimExpired();
        var operationsRoot = Path.Combine(string.IsNullOrWhiteSpace(_options.WorkspaceStateRoot) ? WorkspaceAppDataPaths.GetWorkspaceManagerDataRoot() : Path.GetFullPath(_options.WorkspaceStateRoot), "operations");
        Directory.CreateDirectory(operationsRoot);
        var state = new McpOperationState(kind, workspaceId, initialMessage, _clock, _options.Operations.MaxRecentEvents, Path.Combine(operationsRoot, Guid.NewGuid().ToString("n")));
        if (!_operations.TryAdd(state.OperationId, state))
        {
            throw new OpenCodeWorkspaceMcpException("operation_registration_failed", "Operation registration failed.", "Retry the request.");
        }

        var reporter = new McpOperationReporter(state);
        state.Task = Task.Run(async () =>
        {
            reporter.MarkStarted();
            try
            {
                var result = await work(reporter, state.TokenSource.Token);
                reporter.ApplyResult(result, McpOperationStatus.Succeeded);
            }
            catch (OperationCanceledException)
            {
                reporter.MarkCancelled();
            }
            catch (Exception exception)
            {
                reporter.MarkFailed(exception);
                _logger.LogWarning(exception, "MCP operation {OperationId} failed", state.OperationId);
            }
            finally
            {
                reporter.MarkCompleted();
            }
        });

        return state.ToModel();
    }

    public IReadOnlyList<McpOperationModel> List()
    {
        TrimExpired();
        return _operations.Values.Select(item => item.ToModel()).OrderByDescending(item => item.CreatedUtc).ToArray();
    }

    public McpOperationModel Get(string operationId, long? afterSequence = null, int? maxEvents = null)
    {
        TrimExpired();
        return Resolve(operationId).ToModel(afterSequence, maxEvents);
    }

    public McpOperationModel Cancel(string operationId)
    {
        var state = Resolve(operationId);
        if (state.IsTerminal)
        {
            throw new OpenCodeWorkspaceMcpException("operation_not_cancellable", $"Operation '{operationId}' is already complete.", "Start a new operation if you need to repeat the work.");
        }

        state.SetCancellationRequested();
        state.TokenSource.Cancel();
        return state.ToModel();
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _stopping, 1);
        _logger.LogInformation("Stopping MCP operation store and cancelling active operations.");
        var active = _operations.Values.Where(item => !item.IsTerminal).ToArray();
        foreach (var state in active)
        {
            state.SetCancellationRequested();
            state.TokenSource.Cancel();
        }

        var activeTasks = active.Select(item => item.Task).Where(item => item is not null).Cast<Task>().ToArray();
        if (activeTasks.Length == 0)
        {
            return;
        }

        using var timeoutSource = new CancellationTokenSource(_options.Operations.CleanupTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await Task.WhenAll(activeTasks).WaitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timed out waiting for active MCP operations to finish during shutdown.");
        }
    }

    public void Dispose()
    {
        foreach (var state in _operations.Values)
        {
            state.Dispose();
        }
    }

    private McpOperationState Resolve(string operationId)
        => _operations.TryGetValue(operationId, out var state)
            ? state
            : throw new OpenCodeWorkspaceMcpException("operation_not_found", $"Operation '{operationId}' was not found.", "Refresh the operation list and retry.");

    public void TrimExpired()
    {
        var cutoff = _clock.UtcNow - _options.Operations.Retention;
        foreach (var pair in _operations)
        {
            if (pair.Value.CanExpire(cutoff) && _operations.TryRemove(pair.Key, out var removed))
            {
                removed.Dispose();
            }
        }
    }
}

public sealed class McpOperationReporter
{
    private readonly McpOperationState _state;

    internal McpOperationReporter(McpOperationState state)
    {
        _state = state;
    }

    public void MarkStarted(string phase = "queued", string message = "Operation started.")
        => _state.Update(item =>
        {
            item.StartedUtc ??= item.Clock.UtcNow;
            item.Status = McpOperationStatus.Running;
            item.CurrentPhase = phase;
            item.ProgressMessage = message;
        }, CreateEvent(phase, message, source: "app"));

    public void ReportProgress(string phase, string message, WorkspaceOperationProgressLevel level = WorkspaceOperationProgressLevel.Information, string source = "app", double? percent = null, int? currentStep = null, int? totalSteps = null, string? artifactReference = null)
        => _state.Update(item =>
        {
            item.Status = McpOperationStatus.Running;
            item.CurrentPhase = phase;
            item.ProgressMessage = message;
        }, CreateEvent(phase, message, level, source, percent, currentStep, totalSteps, artifactReference));

    public void ReportProgress(CommandLogEntry entry)
        => ReportProgress(
            string.IsNullOrWhiteSpace(entry.Phase) ? InferPhase(entry) : entry.Phase,
            entry.Message,
            MapLevel(entry.Severity),
            entry.Source,
            entry.Percent,
            entry.CurrentStep,
            entry.TotalSteps,
            entry.ArtifactReference);

    public void ReportProgress(WorkspaceSmokeProgressUpdate update)
        => ReportProgress(update.Phase, update.Message, WorkspaceOperationProgressLevel.Information, string.IsNullOrWhiteSpace(update.TemplateId) ? "smoke" : update.TemplateId);

    public void ApplyResult(object result, McpOperationStatus status)
        => _state.Update(item =>
        {
            item.Status = ResolveStatus(result, status);
            item.CurrentPhase = "completed";
            item.ProgressMessage = "Operation completed.";
            item.Result = JsonSerializer.SerializeToElement(result);
            switch (result)
            {
                case WorkspaceSmokeResult smoke:
                    item.SmokeRunId = smoke.RunId;
                    item.ArtifactDirectory = smoke.ArtifactDirectory;
                    item.ArtifactReferences = BuildArtifactReferences(item.OperationArtifactDirectory, smoke.ArtifactDirectory);
                    item.FailureClassification = smoke.FailureClassification.ToString();
                    item.FailureMessage = smoke.FailureMessage;
                    item.CleanupFailureClassification = smoke.CleanupFailureClassification.ToString();
                    item.CleanupFailureMessage = smoke.CleanupFailureMessage;
                    break;
                case WorkspaceSmokeMatrixResult matrix:
                    item.SmokeMatrixRunId = matrix.MatrixRunId;
                    item.ArtifactDirectory = matrix.ArtifactDirectory;
                    item.ArtifactReferences = BuildArtifactReferences(item.OperationArtifactDirectory, matrix.ArtifactDirectory);
                    item.FailureClassification = matrix.FailureClassification.ToString();
                    item.FailureMessage = matrix.FailureMessage;
                    break;
                case WorkspaceRecordModel:
                    item.ArtifactReferences = BuildArtifactReferences(item.OperationArtifactDirectory, item.ArtifactDirectory);
                    break;
            }
        }, CreateEvent("completed", "Operation completed.", source: "app", artifactReference: _state.OperationArtifactDirectory));

    private static McpOperationStatus ResolveStatus(object result, McpOperationStatus fallback)
        => result switch
        {
            WorkspaceSmokeResult smoke => smoke.Status switch
            {
                WorkspaceSmokeStatus.Passed or WorkspaceSmokeStatus.Skipped => McpOperationStatus.Succeeded,
                WorkspaceSmokeStatus.Cancelled => McpOperationStatus.Cancelled,
                _ => McpOperationStatus.Failed,
            },
            WorkspaceSmokeMatrixResult matrix => matrix.Status switch
            {
                WorkspaceSmokeStatus.Passed or WorkspaceSmokeStatus.Skipped => McpOperationStatus.Succeeded,
                WorkspaceSmokeStatus.Cancelled => McpOperationStatus.Cancelled,
                _ => McpOperationStatus.Failed,
            },
            _ => fallback,
        };

    public void MarkCancelled()
        => _state.Update(item =>
        {
            item.Status = McpOperationStatus.Cancelled;
            item.CurrentPhase = item.CurrentPhase == "verifyingCleanup" ? item.CurrentPhase : "cleaningUp";
            item.FailureClassification = "cancelled";
            item.FailureMessage = "Operation was cancelled.";
            item.ProgressMessage = "Cancellation requested. Cleaning up owned resources.";
        }, CreateEvent("cleaningUp", "Cancellation requested. Cleaning up owned resources.", WorkspaceOperationProgressLevel.Warning, "app"));

    public void MarkFailed(Exception exception)
        => _state.Update(item =>
        {
            item.Status = McpOperationStatus.Failed;
            item.CurrentPhase = "collectingDiagnostics";
            item.FailureClassification = exception.GetType().Name;
            item.FailureMessage = exception.Message;
            item.ProgressMessage = "Operation failed. Collecting diagnostics.";
        }, CreateEvent("collectingDiagnostics", "Operation failed. Collecting diagnostics.", WorkspaceOperationProgressLevel.Error, "app"));

    public void MarkCompleted()
        => _state.Update(item => item.CompletedUtc ??= item.Clock.UtcNow);

    private WorkspaceOperationProgressEvent CreateEvent(string phase, string message, WorkspaceOperationProgressLevel level = WorkspaceOperationProgressLevel.Information, string source = "app", double? percent = null, int? currentStep = null, int? totalSteps = null, string? artifactReference = null)
        => new()
        {
            TimestampUtc = _state.Clock.UtcNow,
            Level = level,
            Phase = phase,
            Message = message,
            Percent = percent,
            CurrentStep = currentStep,
            TotalSteps = totalSteps,
            Source = source,
            ArtifactReference = artifactReference ?? string.Empty,
        };

    private static WorkspaceOperationProgressLevel MapLevel(DiagnosticSeverity severity)
        => severity switch
        {
            DiagnosticSeverity.Warning => WorkspaceOperationProgressLevel.Warning,
            DiagnosticSeverity.Error => WorkspaceOperationProgressLevel.Error,
            _ => WorkspaceOperationProgressLevel.Information,
        };

    private static string InferPhase(CommandLogEntry entry)
    {
        var message = entry.Message;
        return message switch
        {
            var value when value.Contains("Building Workspace Image", StringComparison.OrdinalIgnoreCase) => "buildingWorkspaceImage",
            var value when value.Contains("Preparing workspace", StringComparison.OrdinalIgnoreCase) => "preparingWorkspaceFiles",
            var value when value.Contains("Starting workspace", StringComparison.OrdinalIgnoreCase) => "startingRuntime",
            var value when value.Contains("Validating Docker Compose service status", StringComparison.OrdinalIgnoreCase) => "waitingForRuntimeHealth",
            var value when value.Contains("Provisioning Oracle", StringComparison.OrdinalIgnoreCase) => "startingOracleProvisioning",
            var value when value.Contains("XDB", StringComparison.OrdinalIgnoreCase) => "validatingXdb",
            var value when value.Contains("APEX", StringComparison.OrdinalIgnoreCase) => "installingApex",
            var value when value.Contains("ORDS", StringComparison.OrdinalIgnoreCase) => "configuringOrds",
            var value when value.Contains("Cleaning up", StringComparison.OrdinalIgnoreCase) => "cleaningUp",
            _ => "provisioning",
        };
    }

    private static IReadOnlyList<string> BuildArtifactReferences(string operationArtifactDirectory, string artifactDirectory)
    {
        var results = new List<string>
        {
            Path.Combine(operationArtifactDirectory, "operation-progress.jsonl"),
            Path.Combine(operationArtifactDirectory, "operation-progress.txt"),
        };

        if (!string.IsNullOrWhiteSpace(artifactDirectory))
        {
            results.Add(artifactDirectory);
        }

        return results;
    }
}

internal sealed class McpOperationState : IDisposable
{
    private readonly object _sync = new();
    private readonly List<WorkspaceOperationProgressEvent> _recentEvents = [];
    private readonly int _maxRecentEvents;
    private long _lastEventSequence;
    private long _truncatedEventCount;

    public McpOperationState(string kind, string workspaceId, string initialMessage, ISystemClock clock, int maxRecentEvents, string operationArtifactDirectory)
    {
        OperationId = Guid.NewGuid().ToString("n");
        Kind = kind;
        WorkspaceId = workspaceId;
        ProgressMessage = initialMessage;
        Clock = clock;
        CreatedUtc = clock.UtcNow;
        _maxRecentEvents = Math.Max(1, maxRecentEvents);
        OperationArtifactDirectory = operationArtifactDirectory;
        Directory.CreateDirectory(OperationArtifactDirectory);
        ArtifactReferences = [Path.Combine(OperationArtifactDirectory, "operation-progress.jsonl"), Path.Combine(OperationArtifactDirectory, "operation-progress.txt")];
    }

    public string OperationId { get; }
    public string Kind { get; }
    public string WorkspaceId { get; }
    public DateTimeOffset CreatedUtc { get; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public string CurrentPhase { get; set; } = "queued";
    public List<string> PhaseHistory { get; } = ["queued"];
    public string ProgressMessage { get; set; }
    public McpOperationStatus Status { get; set; } = McpOperationStatus.Pending;
    public string SmokeRunId { get; set; } = string.Empty;
    public string SmokeMatrixRunId { get; set; } = string.Empty;
    public string ArtifactDirectory { get; set; } = string.Empty;
    public string OperationArtifactDirectory { get; }
    public IReadOnlyList<string> ArtifactReferences { get; set; }
    public string FailureClassification { get; set; } = string.Empty;
    public string FailureMessage { get; set; } = string.Empty;
    public string CleanupFailureClassification { get; set; } = string.Empty;
    public string CleanupFailureMessage { get; set; } = string.Empty;
    public bool CancellationRequested { get; set; }
    public JsonElement? Result { get; set; }
    public CancellationTokenSource TokenSource { get; } = new();
    public Task? Task { get; set; }
    public ISystemClock Clock { get; }

    public bool IsTerminal => Status is McpOperationStatus.Succeeded or McpOperationStatus.Failed or McpOperationStatus.Cancelled;

    public void Update(Action<McpOperationState> update, WorkspaceOperationProgressEvent? progressEvent = null)
    {
        lock (_sync)
        {
            update(this);
            if (!string.IsNullOrWhiteSpace(CurrentPhase)
                && (PhaseHistory.Count == 0 || !string.Equals(PhaseHistory[^1], CurrentPhase, StringComparison.OrdinalIgnoreCase)))
            {
                PhaseHistory.Add(CurrentPhase);
            }

            if (progressEvent is not null)
            {
                var nextEvent = new WorkspaceOperationProgressEvent
                {
                    Sequence = ++_lastEventSequence,
                    TimestampUtc = progressEvent.TimestampUtc,
                    Level = progressEvent.Level,
                    Phase = Sanitize(CurrentPhaseOr(progressEvent.Phase)),
                    Message = Sanitize(progressEvent.Message),
                    Percent = progressEvent.Percent,
                    CurrentStep = progressEvent.CurrentStep,
                    TotalSteps = progressEvent.TotalSteps,
                    Source = Sanitize(progressEvent.Source),
                    ArtifactReference = Sanitize(progressEvent.ArtifactReference),
                };
                _recentEvents.Add(nextEvent);
                while (_recentEvents.Count > _maxRecentEvents)
                {
                    _recentEvents.RemoveAt(0);
                    _truncatedEventCount++;
                }

                AppendProgressArtifacts(nextEvent);
            }
        }
    }

    private string CurrentPhaseOr(string fallback)
        => string.IsNullOrWhiteSpace(CurrentPhase) ? fallback : CurrentPhase;

    public void SetCancellationRequested()
        => Update(item => item.CancellationRequested = true);

    public bool CanExpire(DateTimeOffset cutoff)
    {
        lock (_sync)
        {
            return IsTerminal && CompletedUtc is { } completed && completed < cutoff;
        }
    }

    public McpOperationModel ToModel(long? afterSequence = null, int? maxEvents = null)
    {
        lock (_sync)
        {
            var effectiveAfterSequence = afterSequence ?? 0;
            var filtered = _recentEvents.Where(item => item.Sequence > effectiveAfterSequence);
            if (maxEvents is > 0)
            {
                filtered = filtered.Take(maxEvents.Value);
            }

            return new McpOperationModel
            {
                OperationId = OperationId,
                OperationResourceUri = $"opencode://operations/{OperationId}",
                Kind = Kind,
                Status = Status,
                CreatedUtc = CreatedUtc,
                StartedUtc = StartedUtc,
                CompletedUtc = CompletedUtc,
                CurrentPhase = CurrentPhase,
                PhaseHistory = PhaseHistory.ToArray(),
                ProgressMessage = ProgressMessage,
                WorkspaceId = WorkspaceId,
                SmokeRunId = SmokeRunId,
                SmokeMatrixRunId = SmokeMatrixRunId,
                ArtifactDirectory = ArtifactDirectory,
                FailureClassification = FailureClassification,
                FailureMessage = FailureMessage,
                CleanupFailureClassification = CleanupFailureClassification,
                CleanupFailureMessage = CleanupFailureMessage,
                CancellationRequested = CancellationRequested,
                LastEventSequence = _lastEventSequence,
                EventsTruncated = _truncatedEventCount > 0 && (!afterSequence.HasValue || afterSequence.Value < (_recentEvents.FirstOrDefault()?.Sequence ?? (_lastEventSequence + 1))),
                RecentEvents = filtered.ToArray(),
                ArtifactReferences = ArtifactReferences,
                Result = Result,
            };
        }
    }

    private void AppendProgressArtifacts(WorkspaceOperationProgressEvent progressEvent)
    {
        var jsonlPath = Path.Combine(OperationArtifactDirectory, "operation-progress.jsonl");
        var textPath = Path.Combine(OperationArtifactDirectory, "operation-progress.txt");
        var envelope = new WorkspaceOperationProgressEnvelope
        {
            Sequence = progressEvent.Sequence,
            TimestampUtc = progressEvent.TimestampUtc,
            Level = progressEvent.Level,
            Phase = progressEvent.Phase,
            Message = progressEvent.Message,
            Percent = progressEvent.Percent,
            CurrentStep = progressEvent.CurrentStep,
            TotalSteps = progressEvent.TotalSteps,
            Source = progressEvent.Source,
            ArtifactReference = progressEvent.ArtifactReference,
        };
        File.AppendAllText(jsonlPath, JsonSerializer.Serialize(envelope) + Environment.NewLine, new UTF8Encoding(false));
        File.AppendAllText(textPath, $"[{progressEvent.TimestampUtc:O}] {progressEvent.Level} {progressEvent.Phase}: {progressEvent.Message}{Environment.NewLine}", new UTF8Encoding(false));
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = value;
        foreach (var marker in new[] { "password=", "token=", "apikey=", "api_key=", "secret=", "authorization:" })
        {
            var index = sanitized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var tailIndex = sanitized.IndexOfAny([' ', '\t', '\r', '\n'], index + marker.Length);
                var endIndex = tailIndex >= 0 ? tailIndex : sanitized.Length;
                sanitized = sanitized[..(index + marker.Length)] + "[redacted]" + sanitized[endIndex..];
            }
        }

        return sanitized;
    }

    public void Dispose()
    {
        TokenSource.Dispose();
    }
}

public sealed class OpenCodeWorkspaceMcpException : InvalidOperationException
{
    public OpenCodeWorkspaceMcpException(string code, string message, string recommendation = "", string failureClassification = "")
        : base(message)
    {
        Code = code;
        Recommendation = recommendation;
        FailureClassification = string.IsNullOrWhiteSpace(failureClassification) ? code : failureClassification;
    }

    public string Code { get; }
    public string Recommendation { get; }
    public string FailureClassification { get; }
}
