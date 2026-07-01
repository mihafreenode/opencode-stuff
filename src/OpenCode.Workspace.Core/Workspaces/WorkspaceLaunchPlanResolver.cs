using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceLaunchPlanResolver
{
    public WorkspaceLaunchPlan Resolve(WorkspaceSnapshot snapshot, bool safeRepairAttempted = false)
    {
        var hasCompose = File.Exists(snapshot.Paths.ComposePath);
        var hasAttachWrapper = File.Exists(snapshot.Paths.AttachWrapperScriptPath);
        var hasRuntimeStateFile = File.Exists(snapshot.Paths.RuntimeStatePath);
        var hasShellScript = File.Exists(snapshot.Paths.OpencodeWorkspaceShellPath);
        var primaryServiceName = "workspace";

        if (!hasCompose || !hasAttachWrapper || !hasShellScript)
        {
            return new WorkspaceLaunchPlan
            {
                State = safeRepairAttempted ? WorkspaceLaunchState.NeedsReset : WorkspaceLaunchState.NeedsRecover,
                PrimaryServiceName = primaryServiceName,
                Summary = "Managed runtime files are missing or stale.",
                BlockReason = "Managed runtime artifacts are missing.",
            };
        }

        if (snapshot.AppliedState is null)
        {
            return new WorkspaceLaunchPlan
            {
                State = WorkspaceLaunchState.NeedsProvision,
                PrimaryServiceName = primaryServiceName,
                Summary = "Workspace needs initial runtime provisioning before it can open.",
                BlockReason = "Initial runtime provisioning has not completed yet.",
            };
        }

        if (!hasRuntimeStateFile || snapshot.LocalRuntimeState is null)
        {
            return new WorkspaceLaunchPlan
            {
                State = safeRepairAttempted ? WorkspaceLaunchState.NeedsReset : WorkspaceLaunchState.NeedsRecover,
                PrimaryServiceName = primaryServiceName,
                Summary = "Managed runtime files are missing or stale.",
                BlockReason = "runtime-state.yaml is missing or unreadable.",
            };
        }

        if (snapshot.UpdateRequired)
        {
            return new WorkspaceLaunchPlan
            {
                State = safeRepairAttempted ? WorkspaceLaunchState.NeedsReset : WorkspaceLaunchState.NeedsProvision,
                PrimaryServiceName = primaryServiceName,
                Summary = "Workspace runtime needs safe reprovisioning before it can open.",
                BlockReason = "Applied runtime state no longer matches the desired generated state.",
            };
        }

        if (snapshot.RuntimeState == WorkspaceRuntimeState.Stopped)
        {
            return new WorkspaceLaunchPlan
            {
                State = WorkspaceLaunchState.NeedsStart,
                PrimaryServiceName = primaryServiceName,
                Summary = "Workspace runtime is ready but not running.",
                BlockReason = "Managed containers are stopped.",
            };
        }

        if (snapshot.RuntimeState == WorkspaceRuntimeState.Unknown)
        {
            return new WorkspaceLaunchPlan
            {
                State = WorkspaceLaunchState.NeedsManual,
                PrimaryServiceName = primaryServiceName,
                Summary = "Workspace runtime could not be validated automatically.",
                BlockReason = "Runtime state is unknown.",
            };
        }

        return new WorkspaceLaunchPlan
        {
            State = snapshot.RuntimeState == WorkspaceRuntimeState.Running ? WorkspaceLaunchState.NeedsAttach : WorkspaceLaunchState.NeedsManual,
            PrimaryServiceName = primaryServiceName,
            Summary = snapshot.RuntimeState == WorkspaceRuntimeState.Running
                ? "Workspace is ready to open."
                : "Workspace runtime could not be validated automatically.",
            BlockReason = snapshot.RuntimeState == WorkspaceRuntimeState.Running
                ? string.Empty
                : "Runtime state is not running.",
        };
    }
}
