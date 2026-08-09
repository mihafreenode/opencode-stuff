using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Mcp;

public sealed class WorkspaceBackupOperationResultModel
{
    public required WorkspaceRecordModel Workspace { get; init; }
    public required string Message { get; init; }
    public required WorkspaceBackupExportResult Export { get; init; }
    public required WorkspaceBackupManifestResult Manifest { get; init; }
}

public sealed class WorkspacePublishAssessmentModel
{
    public string WorkspaceId { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string CurrentBranch { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string ConfirmationMessage { get; init; } = string.Empty;
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool CanPublish { get; init; }
    public bool IsBlocked { get; init; }
    public bool RequiresConfirmation { get; init; }
    public bool RequiresSavePoint { get; init; }
    public bool HasRemoteConfigured { get; init; }
    public string RemoteName { get; init; } = string.Empty;
    public string RemoteBranch { get; init; } = string.Empty;
    public int AheadCount { get; init; }
    public int BehindCount { get; init; }
}

public sealed class WorkspacePublishOperationResultModel
{
    public required WorkspaceRecordModel Workspace { get; init; }
    public required string Message { get; init; }
    public required WorkspacePublishReview Review { get; init; }
}

public sealed class WorkspaceRemovalOperationResultModel
{
    public required string Message { get; init; }
    public required WorkspaceRemovalResultRecordModel Removal { get; init; }
}

public sealed class WorkspaceRemovalResultRecordModel
{
    public string WorkspaceId { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string WorkspaceRoot { get; init; } = string.Empty;
    public bool RegistrationRemoved { get; init; }
    public bool RuntimeResourcesRemoved { get; init; }
    public bool WorkspaceFilesDeleted { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool Succeeded { get; init; }
    public string FailureReason { get; init; } = string.Empty;
}

public sealed class WorkspaceRecoveryAssessmentModel
{
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();
    public string ConfirmationMessage { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string StatusSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> RecoverActions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CurrentProblems { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PreviousFailureContext { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> WillNotChange { get; init; } = Array.Empty<string>();
    public string ManualActionSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> ManualActions { get; init; } = Array.Empty<string>();
    public string AdvancedDetails { get; init; } = string.Empty;
    public DateTimeOffset? LastCheckedAt { get; init; }
}
