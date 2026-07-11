using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public enum WorkspacePresentationStatusKind
{
    Checking,
    Provisioning,
    ProvisioningFailed,
    Ready,
    Stopped,
    NeedsRebuild,
    NeedsRecovery,
    Invalid,
    Unavailable,
}

public enum WorkspacePresentationTone
{
    Ready,
    Warning,
    Unavailable,
}

public enum WorkspacePresentedActionKind
{
    None,
    Refresh,
    ViewProgress,
    OpenWorkspace,
    RetryProvisioning,
    RebuildRuntime,
    RunDiagnostics,
    OpenFolder,
    StartOnly,
    AttachOnly,
    Retry,
    Validate,
    Export,
    Import,
    Synchronize,
    ShowDiff,
    PullChanges,
    PushChanges,
    PlanApexlangChange,
    CreateApplication,
    ConnectExistingApplication,
    SavePoint,
    Checkpoint,
    Backup,
    Publish,
    Remove,
    OpenOracleDownloadPage,
    OpenDownloadFolder,
}

public enum WorkspacePresentedServiceActionKind
{
    Open,
    CopyUrl,
    CopyCredentials,
    CopyCommand,
    OpenDocumentation,
}

public sealed record WorkspacePresentedAction(
    WorkspacePresentedActionKind Kind,
    string Label,
    string Description,
    bool IsVisible,
    bool IsEnabled,
    string DisabledReason,
    bool IsPrimary,
    bool ConfirmationRequired = false);

public sealed record WorkspacePresentedServiceAction(
    WorkspacePresentedServiceActionKind Kind,
    string Label,
    bool IsVisible,
    bool IsEnabled,
    string DisabledReason);

public sealed record WorkspacePresentedService(
    string Service,
    string Category,
    string Description,
    string Status,
    WorkspacePresentationTone Tone,
    bool IsAvailable,
    string UnavailableReason,
    string OpenOrCommand,
    string Credentials,
    string DocsPath,
    IReadOnlyList<WorkspacePresentedServiceAction> AvailableActions);

public sealed record WorkspacePresentationState(
    WorkspacePresentationStatusKind Status,
    WorkspacePresentationTone Tone,
    string StatusLabel,
    string Summary,
    string Recommendation,
    bool IsOperationRunning,
    string OperationKind,
    string OperationStage,
    WorkspacePresentedAction? PrimaryAction,
    IReadOnlyList<WorkspacePresentedAction> SecondaryActions,
    IReadOnlyList<WorkspacePresentedAction> AdvancedActions,
    IReadOnlyList<WorkspacePresentedService> AvailableServices,
    WorkspaceReadinessSnapshot? Readiness,
    string CurrentStatus,
    string CurrentActivity,
    string ActivitySummary,
    string CapabilitiesSummary,
    string ApplicationsSummary,
    string DevelopmentEnvironmentSummary,
    string ServicesSummary,
    string RecentHistoryNote)
{
    public bool HasPrimaryAction => PrimaryAction is not null && PrimaryAction.IsVisible;
}
