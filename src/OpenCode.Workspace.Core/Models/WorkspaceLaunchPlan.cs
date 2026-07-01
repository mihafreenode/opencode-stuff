namespace OpenCode.Workspace.Core.Models;

public enum WorkspaceLaunchState
{
    Ready,
    NeedsProvision,
    NeedsRecover,
    NeedsStart,
    NeedsAttach,
    NeedsReset,
    NeedsManual,
}

public sealed class WorkspaceLaunchPlan
{
    public WorkspaceLaunchState State { get; init; } = WorkspaceLaunchState.Ready;
    public string PrimaryServiceName { get; init; } = "workspace";
    public string Summary { get; init; } = string.Empty;
    public string BlockReason { get; init; } = string.Empty;
}
