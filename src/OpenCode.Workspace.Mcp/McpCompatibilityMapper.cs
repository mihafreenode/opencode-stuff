using OpenCode.Workspace.LocalClient;
using OpenCode.Workspace.Core.Smoke;
using System.Text.Json;

namespace OpenCode.Workspace.Mcp;

public static class McpCompatibilityMapper
{
    public static McpOperationModel ToMcpOperationModel(WorkspaceOperationRecord record)
    {
        var progressMessage = string.IsNullOrWhiteSpace(record.ProgressMessage)
            ? record.RecentEvents.LastOrDefault()?.Message ?? string.Empty
            : record.ProgressMessage;
        var artifactReferences = record.ArtifactReferences.Select(item => item.SafeLocalReference).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        var normalized = NormalizeResult(record);

        return new McpOperationModel
        {
            ContractVersion = record.ContractVersion,
            OperationId = record.OperationId,
            OperationResourceUri = $"opencode://operations/{record.OperationId}",
            Kind = record.OperationKind,
            Status = MapStatus(record.Status),
            CreatedUtc = record.CreatedUtc,
            UpdatedUtc = record.LastUpdatedUtc,
            StartedUtc = record.StartedUtc,
            CompletedUtc = record.CompletedUtc,
            CurrentPhase = string.IsNullOrWhiteSpace(record.CurrentPhase) ? "queued" : record.CurrentPhase,
            PhaseHistory = record.PhaseHistory,
            ProgressMessage = progressMessage,
            WorkspaceId = record.WorkspaceId,
            ControllerSessionId = record.InitiatedBy.ControllerSessionId,
            SmokeRunId = normalized.SmokeRunId,
            SmokeMatrixRunId = normalized.SmokeMatrixRunId,
            ArtifactDirectory = normalized.ArtifactDirectory,
            FailureClassification = ResolveFailureClassification(record),
            FailureMessage = ResolveFailureMessage(record),
            CleanupFailureClassification = record.CleanupFailure?.Classification ?? string.Empty,
            CleanupFailureMessage = record.CleanupFailure?.Message ?? string.Empty,
            CancellationRequested = record.CancellationState is WorkspaceOperationCancellationState.Requested or WorkspaceOperationCancellationState.Cancelled,
            LastEventSequence = record.LastEventSequence,
            EventsTruncated = record.EventsTruncated,
            RecentEvents = record.RecentEvents.Select(ToLegacyProgressEvent).ToArray(),
            ArtifactReferences = artifactReferences,
            Result = normalized.Result,
        };
    }

    public static WorkspaceOperationProgressEvent ToLegacyProgressEvent(OpenCode.Workspace.LocalClient.WorkspaceOperationProgressEvent record)
        => new()
        {
            Sequence = record.Sequence,
            TimestampUtc = record.TimestampUtc,
            Level = record.Level switch
            {
                OpenCode.Workspace.LocalClient.WorkspaceOperationProgressLevel.Debug => WorkspaceOperationProgressLevel.Debug,
                OpenCode.Workspace.LocalClient.WorkspaceOperationProgressLevel.Warning => WorkspaceOperationProgressLevel.Warning,
                OpenCode.Workspace.LocalClient.WorkspaceOperationProgressLevel.Error => WorkspaceOperationProgressLevel.Error,
                _ => WorkspaceOperationProgressLevel.Information,
            },
            Phase = record.Phase,
            Message = record.Message,
            Percent = record.Percent,
            CurrentStep = record.CurrentStep,
            TotalSteps = record.TotalSteps,
            Source = record.Source,
            ArtifactReference = record.ArtifactReference,
        };

    private static McpOperationStatus MapStatus(WorkspaceOperationStatus status)
        => status switch
        {
            WorkspaceOperationStatus.Running => McpOperationStatus.Running,
            WorkspaceOperationStatus.Succeeded => McpOperationStatus.Succeeded,
            WorkspaceOperationStatus.Failed => McpOperationStatus.Failed,
            WorkspaceOperationStatus.Cancelled => McpOperationStatus.Cancelled,
            WorkspaceOperationStatus.Interrupted => McpOperationStatus.Failed,
            _ => McpOperationStatus.Pending,
        };

    private static NormalizedOperationResult NormalizeResult(WorkspaceOperationRecord record)
    {
        if (!record.Result.HasValue)
        {
            return new NormalizedOperationResult(null, string.Empty, string.Empty, string.Empty);
        }

        try
        {
            return record.OperationKind switch
            {
                "run_smoke" => NormalizeSmokeResult(record.Result.Value.Deserialize<WorkspaceSmokeResult>(LocalHostContract.JsonOptions)!),
                "run_smoke_matrix" => NormalizeSmokeMatrixResult(record.Result.Value.Deserialize<WorkspaceSmokeMatrixResult>(LocalHostContract.JsonOptions)!),
                _ => new NormalizedOperationResult(record.Result, string.Empty, string.Empty, string.Empty),
            };
        }
        catch
        {
            return new NormalizedOperationResult(record.Result, string.Empty, string.Empty, string.Empty);
        }
    }

    private static NormalizedOperationResult NormalizeSmokeResult(WorkspaceSmokeResult result)
        => new(JsonSerializer.SerializeToElement(result), result.RunId, string.Empty, result.ArtifactDirectory);

    private static NormalizedOperationResult NormalizeSmokeMatrixResult(WorkspaceSmokeMatrixResult result)
        => new(JsonSerializer.SerializeToElement(result), string.Empty, result.MatrixRunId, result.ArtifactDirectory);

    private static string ResolveFailureClassification(WorkspaceOperationRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.OriginalFailure?.Classification))
        {
            return record.OriginalFailure.Classification;
        }

        return record.Status == WorkspaceOperationStatus.Cancelled ? "cancelled" : string.Empty;
    }

    private static string ResolveFailureMessage(WorkspaceOperationRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.OriginalFailure?.Message))
        {
            return record.OriginalFailure.Message;
        }

        return record.Status == WorkspaceOperationStatus.Cancelled ? "Operation was cancelled." : string.Empty;
    }

    private sealed record NormalizedOperationResult(JsonElement? Result, string SmokeRunId, string SmokeMatrixRunId, string ArtifactDirectory);
}
