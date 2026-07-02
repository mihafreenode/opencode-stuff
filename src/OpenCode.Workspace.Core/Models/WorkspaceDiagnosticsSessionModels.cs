using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Models;

public sealed class WorkspaceDiagnosticsSession
{
    public string WorkspaceName { get; init; } = string.Empty;
    public string WorkspaceRootPath { get; init; } = string.Empty;
    public string OperationName { get; init; } = string.Empty;
    public WorkspaceDiagnosticsMode Mode { get; init; } = WorkspaceDiagnosticsMode.Diagnostics;
    public WorkspaceDiagnosticsStatus Status { get; init; } = WorkspaceDiagnosticsStatus.Succeeded;
    public string Summary { get; init; } = string.Empty;
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public IReadOnlyList<WorkspaceAttemptResult> AttemptedSteps { get; init; } = Array.Empty<WorkspaceAttemptResult>();
    public IReadOnlyList<WorkspaceDiagnosticsEntry> Entries { get; init; } = Array.Empty<WorkspaceDiagnosticsEntry>();
    public WorkspaceReadinessSnapshot? Readiness { get; init; }
    public WorkspaceProvisioningHealthRecord? ProvisioningHealth { get; init; }
    public WorkspaceFailureSummary? FailureSummary { get; init; }
    public WorkspaceNextActionRecommendation Recommendation { get; init; } = WorkspaceNextActionRecommendation.None;
    public WorkspaceDiagnosticsBundleInfo BundleInfo { get; init; } = new();
}

public enum WorkspaceDiagnosticsMode
{
    Progress,
    Diagnostics,
}

public enum WorkspaceDiagnosticsStatus
{
    Running,
    Succeeded,
    Failed,
    Blocked,
}

public enum WorkspaceAttemptStep
{
    Unknown,
    SafeRepair,
    Provision,
    Start,
    Attach,
    Rebuild,
}

public sealed class WorkspaceAttemptResult
{
    public WorkspaceAttemptStep Step { get; init; } = WorkspaceAttemptStep.Unknown;
    public bool? Succeeded { get; init; }
    public bool IsInProgress { get; init; }
    public string Summary { get; init; } = string.Empty;
    public DateTimeOffset? Timestamp { get; init; }
}

public sealed class WorkspaceDiagnosticsEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public WorkspaceDiagnosticsEntryKind Kind { get; init; } = WorkspaceDiagnosticsEntryKind.Status;
    public string Message { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public bool IsFailureEvidence { get; init; }
}

public enum WorkspaceDiagnosticsEntryKind
{
    Comment,
    Command,
    Status,
    Output,
    Error,
    Result,
    Evidence,
    Summary,
}

public sealed class WorkspaceFailureSummary
{
    public string Summary { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
}

public enum WorkspaceNextActionRecommendation
{
    None,
    OpenWorkspace,
    RebuildRuntime,
    RunDiagnostics,
    OpenFolder,
}

public sealed class WorkspaceDiagnosticsBundleInfo
{
    public string SuggestedFileName { get; init; } = string.Empty;
    public bool CanCopyToClipboard { get; init; }
    public bool CanExportToFile { get; init; }
}

public sealed class WorkspaceDiagnosticsSessionBuildInput
{
    public object? Transcript { get; init; }
    public WorkspaceProvisioningHealthRecord? ProvisioningHealth { get; init; }
    public WorkspaceReadinessSnapshot? Readiness { get; init; }
    public WorkspaceTroubleshootingContext? TroubleshootingContext { get; init; }
    public string WorkspaceName { get; init; } = string.Empty;
    public string WorkspaceRootPath { get; init; } = string.Empty;
    public string OperationName { get; init; } = string.Empty;
}
