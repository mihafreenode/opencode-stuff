using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using System.Collections.Concurrent;
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
        var state = new McpOperationState(kind, workspaceId, initialMessage, _clock);
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

    public McpOperationModel Get(string operationId)
    {
        TrimExpired();
        return Resolve(operationId).ToModel();
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
        });

    public void ReportProgress(string phase, string message)
        => _state.Update(item =>
        {
            item.Status = McpOperationStatus.Running;
            item.CurrentPhase = phase;
            item.ProgressMessage = message;
        });

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
                    item.FailureClassification = smoke.FailureClassification.ToString();
                    item.FailureMessage = smoke.FailureMessage;
                    item.CleanupFailureClassification = smoke.CleanupFailureClassification.ToString();
                    item.CleanupFailureMessage = smoke.CleanupFailureMessage;
                    break;
                case WorkspaceSmokeMatrixResult matrix:
                    item.SmokeMatrixRunId = matrix.MatrixRunId;
                    item.ArtifactDirectory = matrix.ArtifactDirectory;
                    item.FailureClassification = matrix.FailureClassification.ToString();
                    item.FailureMessage = matrix.FailureMessage;
                    break;
            }
        });

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
        });

    public void MarkFailed(Exception exception)
        => _state.Update(item =>
        {
            item.Status = McpOperationStatus.Failed;
            item.CurrentPhase = "failed";
            item.FailureClassification = exception.GetType().Name;
            item.FailureMessage = exception.Message;
            item.ProgressMessage = "Operation failed.";
        });

    public void MarkCompleted()
        => _state.Update(item => item.CompletedUtc ??= item.Clock.UtcNow);
}

internal sealed class McpOperationState : IDisposable
{
    private readonly object _sync = new();

    public McpOperationState(string kind, string workspaceId, string initialMessage, ISystemClock clock)
    {
        OperationId = Guid.NewGuid().ToString("n");
        Kind = kind;
        WorkspaceId = workspaceId;
        ProgressMessage = initialMessage;
        Clock = clock;
        CreatedUtc = clock.UtcNow;
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

    public void Update(Action<McpOperationState> update)
    {
        lock (_sync)
        {
            update(this);
            if (!string.IsNullOrWhiteSpace(CurrentPhase)
                && (PhaseHistory.Count == 0 || !string.Equals(PhaseHistory[^1], CurrentPhase, StringComparison.OrdinalIgnoreCase)))
            {
                PhaseHistory.Add(CurrentPhase);
            }
        }
    }

    public void SetCancellationRequested()
        => Update(item => item.CancellationRequested = true);

    public bool CanExpire(DateTimeOffset cutoff)
    {
        lock (_sync)
        {
            return IsTerminal && CompletedUtc is { } completed && completed < cutoff;
        }
    }

    public McpOperationModel ToModel()
    {
        lock (_sync)
        {
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
                Result = Result,
            };
        }
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
