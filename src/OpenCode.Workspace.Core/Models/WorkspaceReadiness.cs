namespace OpenCode.Workspace.Core.Models;

public sealed class WorkspaceReadinessSnapshot
{
    public WorkspaceReadinessStatus Status { get; init; } = WorkspaceReadinessStatus.Unavailable;
    public WorkspaceActivity CurrentActivity { get; init; } = WorkspaceActivity.None;
    public WorkspacePrimaryAction PrimaryAction { get; init; } = WorkspacePrimaryAction.OpenWorkspace;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<WorkspaceCapabilitySnapshot> Capabilities { get; init; } = Array.Empty<WorkspaceCapabilitySnapshot>();
    public IReadOnlyList<WorkspaceAttentionItem> AttentionItems { get; init; } = Array.Empty<WorkspaceAttentionItem>();
    public IReadOnlyList<WorkspaceEvidenceSection> Evidence { get; init; } = Array.Empty<WorkspaceEvidenceSection>();
    public bool CanOpenWorkspace { get; init; }
    public bool CanRebuildRuntime { get; init; }
    public bool IsOperationInProgress { get; init; }
}

public enum WorkspaceReadinessStatus
{
    Ready,
    Preparing,
    NeedsRebuild,
    Unavailable,
}

public enum WorkspaceActivity
{
    None,
    Discovering,
    Preparing,
    OpeningTerminal,
    RepairingRuntime,
    Investigating,
}

public enum WorkspacePrimaryAction
{
    OpenWorkspace,
    ViewProgress,
    RebuildRuntime,
    OpenFolder,
    RunDiagnostics,
}

public sealed class WorkspaceCapabilitySnapshot
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public WorkspaceCapabilityState State { get; init; } = WorkspaceCapabilityState.Unavailable;
    public string Summary { get; init; } = string.Empty;
    public bool IsPrimaryWorkSurface { get; init; }
}

public enum WorkspaceCapabilityState
{
    Available,
    Preparing,
    Unavailable,
}

public sealed class WorkspaceAttentionItem
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public WorkspaceAttentionSeverity Severity { get; init; } = WorkspaceAttentionSeverity.Info;
    public string Summary { get; init; } = string.Empty;
    public string RecommendedActionLabel { get; init; } = string.Empty;
    public WorkspaceAttentionScope Scope { get; init; } = WorkspaceAttentionScope.Workspace;
}

public enum WorkspaceAttentionSeverity
{
    Info,
    Attention,
    Blocking,
}

public enum WorkspaceAttentionScope
{
    Workspace,
    Capability,
    DevelopmentEnvironment,
    Runtime,
    Host,
}

public sealed class WorkspaceEvidenceSection
{
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<WorkspaceEvidenceItem> Items { get; init; } = Array.Empty<WorkspaceEvidenceItem>();
}

public sealed class WorkspaceEvidenceItem
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public sealed class WorkspaceReadinessInput
{
    public WorkspaceSnapshot? Snapshot { get; init; }
    public WorkspaceHealthSnapshot? Health { get; init; }
    public WorkspaceOperationState Operation { get; init; } = new();
}

public sealed class WorkspaceOperationState
{
    public bool IsInProgress { get; init; }
    public string OperationName { get; init; } = string.Empty;
    public string StatusMessage { get; init; } = string.Empty;
}
