namespace OpenCode.Workspace.Core.Models;

/// <summary>
/// The repository keeps a small Windows-local index of known workspaces so the
/// app can reopen them quickly. The durable workspace behavior still lives in the
/// user-owned workspace configuration file inside each workspace folder.
/// </summary>
public sealed class WorkspaceRecord
{
    public string Name { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string ConfigurationPath { get; init; } = "workspace.yaml";
    public WorkspaceSourceType SourceType { get; init; } = WorkspaceSourceType.NewWorkspace;
    public bool ImportedFromExistingCheckout { get; init; }
    public string OriginalDefaultBranch { get; init; } = string.Empty;
    public string SelectedWorkspaceBranch { get; init; } = string.Empty;
    public string RemoteOriginUrl { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset LastOpenedUtc { get; init; }
    public DateTimeOffset? LastPreparedUtc { get; init; }
    public bool OracleSoftwareNoticeShown { get; init; }
    public string? LastOperationName { get; init; }
    public string? LastOperationResult { get; init; }
    public bool? LastOperationSucceeded { get; init; }
    public DateTimeOffset? LastOperationUtc { get; init; }
    public WorkspaceProvisioningHealthRecord? LastProvisioningHealth { get; init; }
}

public sealed class WorkspaceProvisioningHealthRecord
{
    public bool Succeeded { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public string ProblemScope { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
    public string PreviousRecommendedAction { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public TimeSpan Duration { get; init; }
    public string RawLogReference { get; init; } = string.Empty;
    public string OracleVersion { get; init; } = string.Empty;
    public string ApexVersion { get; init; } = string.Empty;
    public string OrdsVersion { get; init; } = string.Empty;
    public string WorkspaceRuntimeVersion { get; init; } = string.Empty;
    public string Repairability { get; init; } = string.Empty;
    public string EstimatedEffort { get; init; } = string.Empty;
    public string EstimatedDuration { get; init; } = string.Empty;
    public DateTimeOffset? LastDiagnosticsTimestamp { get; init; }
    public IReadOnlyList<WorkspaceRepairAttemptRecord> RepairHistory { get; init; } = Array.Empty<WorkspaceRepairAttemptRecord>();
    public IReadOnlyList<WorkspaceInvestigationRecord> InvestigationHistory { get; init; } = Array.Empty<WorkspaceInvestigationRecord>();
}

public sealed class WorkspaceRepairAttemptRecord
{
    public string RepairType { get; init; } = string.Empty;
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public TimeSpan Duration { get; init; }
    public string Result { get; init; } = string.Empty;
    public string EvidenceBefore { get; init; } = string.Empty;
    public string EvidenceAfter { get; init; } = string.Empty;
    public string RootCauseBefore { get; init; } = string.Empty;
    public string RootCauseAfter { get; init; } = string.Empty;
    public bool RootCauseChanged { get; init; }
    public string WorkspaceStateBefore { get; init; } = string.Empty;
    public string WorkspaceStateAfter { get; init; } = string.Empty;
    public bool WorkspaceStateChanged { get; init; }
    public string Confidence { get; init; } = string.Empty;
    public string PreviousRecommendation { get; init; } = string.Empty;
    public string UpdatedRecommendation { get; init; } = string.Empty;
}

public sealed class WorkspaceInvestigationRecord
{
    public string InvestigationId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public string EstimatedDuration { get; init; } = string.Empty;
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public TimeSpan Duration { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public string RelevantLogReference { get; init; } = string.Empty;
}

public static class WorkspaceRepairOutcome
{
    public const string RepairSucceeded = nameof(RepairSucceeded);
    public const string RepairImproved = nameof(RepairImproved);
    public const string RepairNoEffect = nameof(RepairNoEffect);
    public const string RepairPartiallySucceeded = nameof(RepairPartiallySucceeded);
    public const string RepairFailed = nameof(RepairFailed);
}

public enum WorkspaceSourceType
{
    NewWorkspace,
    ExistingGitCheckout,
}

public sealed class WorkspacePaths
{
    public required string RootPath { get; init; }
    public required string GitIgnorePath { get; init; }
    public required string OpencodePath { get; init; }
    public required string OpencodeLocalPath { get; init; }
    public string ApexMetadataPath { get; init; } = string.Empty;
    public required string WorkspaceYamlRelativePath { get; init; }
    public required string WorkspaceYamlPath { get; init; }
    public required string ComposePath { get; init; }
    public required string EnvironmentFilePath { get; init; }
    public required string MountsRootPath { get; init; }
    public required string InboxPath { get; init; }
    public required string WorkspacePath { get; init; }
    public required string UserPath { get; init; }
    public required string HomePath { get; init; }
    public required string ConfigPath { get; init; }
    public required string ProvisionScriptPath { get; init; }
    public required string StarshipConfigPath { get; init; }
    public required string ShellInitScriptPath { get; init; }
    public required string OpencodeWorkspaceShellPath { get; init; }
    public required string ScreenConfigPath { get; init; }
    public required string AttachWrapperScriptPath { get; init; }
    public required string AttachDiagnosticsLogPath { get; init; }
    public required string TerminalDiagnosticsScriptPath { get; init; }
    public required string RuntimeStatePath { get; init; }
    public required string AppliedStatePath { get; init; }
    public required string HistoryPath { get; init; }
    public required string CheckpointsPath { get; init; }
    public required string CheckpointIndexPath { get; init; }
    public required string TimelinePath { get; init; }
    public required string RuntimesPath { get; init; }
    public required string DefaultRuntimePath { get; init; }
    public required string ArtifactsPath { get; init; }
    public required string ArtifactRunsPath { get; init; }
    public required string ArtifactIndexPath { get; init; }
}

public sealed class WorkspaceAppliedState
{
    public string DesiredStateHash { get; init; } = string.Empty;
    public string WorkspaceDefinitionHash { get; init; } = string.Empty;
    public DateTimeOffset AppliedUtc { get; init; }
    public string? AppVersion { get; init; }
}

public enum WorkspaceRuntimeState
{
    Unknown,
    Stopped,
    Running,
}

public sealed class WorkspaceSnapshot
{
    public required WorkspaceRecord Record { get; init; }
    public required WorkspaceDefinition Definition { get; init; }
    public required WorkspacePaths Paths { get; init; }
    public required string ConfigurationPath { get; init; }
    public required WorkspaceRuntimeState RuntimeState { get; init; }
    public required WorkspaceSafetySnapshot Safety { get; init; }
    public required WorkspaceSessionSnapshot Session { get; init; }
    public WorkspaceAppliedState? AppliedState { get; init; }
    public WorkspaceRuntimeStateRecord? LocalRuntimeState { get; init; }
    public ResolvedRuntimePlan? ResolvedRuntimePlan { get; init; }
    public bool UpdateRequired { get; init; }
    public WorkspaceSynchronizationSnapshot Synchronization { get; init; } = new();
    public WorkspaceApexAssistantSnapshot Assistant { get; init; } = new();
    public WorkspaceHealthSnapshot Health { get; init; } = new();
    public WorkspaceReadinessSnapshot Readiness { get; init; } = new();
    public IReadOnlyList<WorkspaceServiceInfo> AvailableServices { get; init; } = Array.Empty<WorkspaceServiceInfo>();
}

public sealed class WorkspaceApexAssistantSnapshot
{
    public WorkspaceApexAssistantState State { get; init; } = WorkspaceApexAssistantState.NoPendingPlan;
    public string Summary { get; init; } = string.Empty;
    public bool WasRolledBack { get; init; }
    public bool CanOpenApplication { get; init; }
    public bool CanOpenBuilder { get; init; }
    public IReadOnlyList<string> ChangedFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

public enum WorkspaceApexAssistantState
{
    NoPendingPlan,
    PlanReadyForReview,
    AwaitingConfirmation,
    Applying,
    Validating,
    Importing,
    Completed,
    Failed,
    RolledBack,
}

public sealed class WorkspaceSessionSnapshot
{
    public string SessionName { get; init; } = string.Empty;
    public WorkspaceSessionState State { get; init; } = WorkspaceSessionState.Unknown;
}

public enum WorkspaceSessionState
{
    Unknown,
    NotRunning,
    Resumable,
}
