using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Platform.Windows;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class WorkspaceOperationResult
{
    public required WorkspaceSnapshot Snapshot { get; init; }
    public required string Message { get; init; }
    public required OperationTranscript Transcript { get; init; }
}

public sealed class WorkspaceBackupResult
{
    public required WorkspaceSnapshot Snapshot { get; init; }
    public required string Message { get; init; }
    public required OperationTranscript Transcript { get; init; }
    public required WorkspaceBackupExportResult Export { get; init; }
    public required WorkspaceBackupManifestResult Manifest { get; init; }
}

public sealed class WorkspaceCheckpointOperationResult
{
    public required WorkspaceSnapshot Snapshot { get; init; }
    public required string Message { get; init; }
    public required OperationTranscript Transcript { get; init; }
    public required WorkspaceCheckpointRecord Checkpoint { get; init; }
}

public sealed class WorkspacePublishResult
{
    public required WorkspaceSnapshot Snapshot { get; init; }
    public required string Message { get; init; }
    public required OperationTranscript Transcript { get; init; }
    public required WorkspacePublishReview Review { get; init; }
}

public sealed class WorkspaceRemovalPrompt
{
    public required string WorkspaceName { get; init; }
    public required string WorkspaceRoot { get; init; }
    public bool DeleteWorkspaceFilesSupported { get; init; }
    public string DeleteWorkspaceFilesUnavailableReason { get; init; } = string.Empty;
}

public enum WorkspaceRemovalChoice
{
    RegistrationOnly,
    DockerResources,
    DeleteFiles,
}

public sealed class WorkspaceRemovalDecision
{
    public required WorkspaceRemovalChoice Choice { get; init; }
}

public sealed class WorkspaceRemovalOperationResult
{
    public required string Message { get; init; }
    public required OperationTranscript Transcript { get; init; }
    public required WorkspaceRemovalResult Removal { get; init; }
}

public sealed class WindowsTerminalProfileOperationResult
{
    public required string Message { get; init; }
    public required WindowsTerminalProfileSetupResult Setup { get; init; }
}

public sealed class WorkspaceRecoveryAssessment
{
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Findings { get; init; }
    public required string ConfirmationMessage { get; init; }
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

public sealed class WorkspaceRuntimeResetPrompt
{
    public required string WorkspaceName { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> Removes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Keeps { get; init; } = Array.Empty<string>();
    public string ConfirmationMessage { get; init; } = string.Empty;
}

public sealed class WorkspaceCheckpointPrompt
{
    public required string WorkspaceName { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required string Summary { get; init; }
    public required string ConfirmationMessage { get; init; }
}

public sealed class CreateWorkspaceDraft
{
    public required string WorkspaceName { get; init; }
    public required string WorkspaceRootPath { get; init; }
    public required TemplateManifest Template { get; init; }
}

public sealed class ExistingRepositoryImportDraft
{
    public required string RepositoryPath { get; init; }
    public required string WorkspaceName { get; init; }
    public required ExistingGitCheckoutBranchMode BranchMode { get; init; }
    public string NamedBranch { get; init; } = string.Empty;
    public bool ReuseExistingNamedBranch { get; init; }
}

public sealed class RuntimeResourceCleanupResult
{
    public required string Message { get; init; }
    public required OperationTranscript Transcript { get; init; }
}

public sealed class SavePointDraft
{
    public required string Message { get; init; }
}

public sealed class WorkspaceTroubleshootingRequest
{
    public required string RootPath { get; init; }
    public WorkspaceSnapshot? Snapshot { get; init; }
    public string WorkspaceName { get; init; } = string.Empty;
    public bool IsOperationInProgress { get; init; }
    public string CurrentOperationName { get; init; } = string.Empty;
    public string CurrentStatusMessage { get; init; } = string.Empty;
    public string TranscriptFilePath { get; init; } = string.Empty;
}

public sealed class WorkspaceTroubleshootingFact
{
    public required string Label { get; init; }
    public required string Value { get; init; }
}

public sealed class WorkspaceTroubleshootingAction
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Description { get; init; }
    public string EstimatedDuration { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
}

public sealed class WorkspaceTroubleshootingHistoryEntry
{
    public required string Title { get; init; }
    public required string Outcome { get; init; }
    public required string Summary { get; init; }
    public string Evidence { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public string EstimatedDuration { get; init; } = string.Empty;
    public DateTimeOffset OccurredUtc { get; init; }
    public TimeSpan Duration { get; init; }
    public string Source { get; init; } = string.Empty;
}

public sealed class WorkspaceTroubleshootingServiceEntry
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Applications { get; init; } = string.Empty;
    public string PrimaryUrl { get; init; } = string.Empty;
    public string Highlights { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string ActionLabel { get; init; } = string.Empty;
    public string OpenUrl { get; init; } = string.Empty;
}

public sealed class WorkspaceTroubleshootingReport
{
    public required string WorkspaceName { get; init; }
    public required string RootPath { get; init; }
    public required string Headline { get; init; }
    public required string Summary { get; init; }
    public required string Recommendation { get; init; }
    public string CurrentDiagnosis { get; init; } = string.Empty;
    public string CurrentEvidence { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public string RecommendedNextStep { get; init; } = string.Empty;
    public string RecommendedNextStepDescription { get; init; } = string.Empty;
    public string RecommendedNextStepDuration { get; init; } = string.Empty;
    public IReadOnlyList<WorkspaceTroubleshootingFact> Facts { get; init; } = Array.Empty<WorkspaceTroubleshootingFact>();
    public IReadOnlyList<string> SuggestedNextSteps { get; init; } = Array.Empty<string>();
    public IReadOnlyList<WorkspaceTroubleshootingServiceEntry> Services { get; init; } = Array.Empty<WorkspaceTroubleshootingServiceEntry>();
    public IReadOnlyList<WorkspaceTroubleshootingAction> InvestigationActions { get; init; } = Array.Empty<WorkspaceTroubleshootingAction>();
    public IReadOnlyList<WorkspaceTroubleshootingHistoryEntry> RepairHistory { get; init; } = Array.Empty<WorkspaceTroubleshootingHistoryEntry>();
    public IReadOnlyList<WorkspaceTroubleshootingHistoryEntry> InvestigationHistory { get; init; } = Array.Empty<WorkspaceTroubleshootingHistoryEntry>();
    public bool IsProvisioningInProgress { get; init; }
    public bool RecommendHostDiagnostics { get; init; }
    public bool CanKeepWaiting { get; init; }
    public bool CanViewLog { get; init; }
    public bool CanOpenWorkspace { get; init; }
    public bool CanRecoverWorkspace { get; init; }
    public bool CanResetRuntime { get; init; }
    public string TranscriptFilePath { get; init; } = string.Empty;
    public string TranscriptExcerpt { get; init; } = string.Empty;
}
