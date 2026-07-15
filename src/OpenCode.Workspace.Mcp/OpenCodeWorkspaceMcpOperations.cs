using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace OpenCode.Workspace.Mcp;

public sealed class McpOperationStore
{
    private readonly ConcurrentDictionary<string, McpOperationState> _operations = new(StringComparer.OrdinalIgnoreCase);
    private readonly OpenCodeWorkspaceMcpOptions _options;
    private readonly ILogger<McpOperationStore> _logger;

    public McpOperationStore(OpenCodeWorkspaceMcpOptions options, ILogger<McpOperationStore> logger)
    {
        _options = options;
        _logger = logger;
    }

    public McpOperationModel Start(string kind, string workspaceId, string initialMessage, Func<CancellationToken, Task<object>> work)
    {
        TrimExpired();
        var state = new McpOperationState(kind, workspaceId, initialMessage);
        if (!_operations.TryAdd(state.OperationId, state))
        {
            throw new InvalidOperationException("Operation registration failed.");
        }

        state.Task = Task.Run(async () =>
        {
            state.StartedUtc = DateTimeOffset.UtcNow;
            state.Status = McpOperationStatus.Running;
            try
            {
                var result = await work(state.CancellationSource.Token);
                state.ApplyResult(result, McpOperationStatus.Succeeded);
            }
            catch (OperationCanceledException)
            {
                state.Status = McpOperationStatus.Cancelled;
                state.FailureClassification = "cancelled";
                state.FailureMessage = "Operation was cancelled.";
            }
            catch (Exception exception)
            {
                state.Status = McpOperationStatus.Failed;
                state.FailureClassification = exception.GetType().Name;
                state.FailureMessage = exception.Message;
                _logger.LogWarning(exception, "MCP operation {OperationId} failed", state.OperationId);
            }
            finally
            {
                state.CompletedUtc = DateTimeOffset.UtcNow;
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
        state.CancellationRequested = true;
        state.CancellationSource.Cancel();
        return state.ToModel();
    }

    private McpOperationState Resolve(string operationId)
        => _operations.TryGetValue(operationId, out var state)
            ? state
            : throw new InvalidOperationException($"Operation '{operationId}' was not found.");

    private void TrimExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - _options.Operations.Retention;
        foreach (var pair in _operations)
        {
            if (pair.Value.CompletedUtc is { } completed && completed < cutoff)
            {
                _operations.TryRemove(pair.Key, out _);
            }
        }
    }
}

internal sealed class McpOperationState
{
    public McpOperationState(string kind, string workspaceId, string initialMessage)
    {
        OperationId = Guid.NewGuid().ToString("n");
        Kind = kind;
        WorkspaceId = workspaceId;
        ProgressMessage = initialMessage;
    }

    public string OperationId { get; }
    public string Kind { get; }
    public string WorkspaceId { get; }
    public DateTimeOffset CreatedUtc { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public string CurrentPhase { get; set; } = "queued";
    public string ProgressMessage { get; set; }
    public McpOperationStatus Status { get; set; } = McpOperationStatus.Pending;
    public string SmokeRunId { get; private set; } = string.Empty;
    public string SmokeMatrixRunId { get; private set; } = string.Empty;
    public string ArtifactDirectory { get; private set; } = string.Empty;
    public string FailureClassification { get; set; } = string.Empty;
    public string FailureMessage { get; set; } = string.Empty;
    public string CleanupFailureClassification { get; private set; } = string.Empty;
    public string CleanupFailureMessage { get; private set; } = string.Empty;
    public bool CancellationRequested { get; set; }
    public JsonElement? Result { get; private set; }
    public CancellationTokenSource CancellationSource { get; } = new();
    public Task? Task { get; set; }

    public void ApplyResult(object result, McpOperationStatus status)
    {
        Status = status;
        CurrentPhase = "completed";
        ProgressMessage = "Operation completed.";
        Result = JsonSerializer.SerializeToElement(result);
        switch (result)
        {
            case WorkspaceSmokeResult smoke:
                SmokeRunId = smoke.RunId;
                ArtifactDirectory = smoke.ArtifactDirectory;
                FailureClassification = smoke.FailureClassification.ToString();
                FailureMessage = smoke.FailureMessage;
                CleanupFailureClassification = smoke.CleanupFailureClassification.ToString();
                CleanupFailureMessage = smoke.CleanupFailureMessage;
                break;
            case WorkspaceSmokeMatrixResult matrix:
                SmokeMatrixRunId = matrix.MatrixRunId;
                ArtifactDirectory = matrix.ArtifactDirectory;
                FailureClassification = matrix.FailureClassification.ToString();
                FailureMessage = matrix.FailureMessage;
                break;
            case RuntimeResourceInventory:
                break;
        }
    }

    public McpOperationModel ToModel()
        => new()
        {
            OperationId = OperationId,
            Kind = Kind,
            Status = Status,
            CreatedUtc = CreatedUtc,
            StartedUtc = StartedUtc,
            CompletedUtc = CompletedUtc,
            CurrentPhase = CurrentPhase,
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
