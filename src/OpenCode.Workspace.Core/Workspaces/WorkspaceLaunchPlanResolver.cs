using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceLaunchPlanResolver
{
    public WorkspaceLaunchPlan Resolve(WorkspaceSnapshot snapshot)
    {
        var hasCompose = File.Exists(snapshot.Paths.ComposePath);
        var hasAttachWrapper = File.Exists(snapshot.Paths.AttachWrapperScriptPath);
        var hasRuntimeStateFile = File.Exists(snapshot.Paths.RuntimeStatePath);
        var hasShellScript = File.Exists(snapshot.Paths.OpencodeWorkspaceShellPath);

        if (!hasCompose || !hasAttachWrapper || !hasShellScript)
        {
            return new WorkspaceLaunchPlan
            {
                NeedsRecover = true,
                Summary = "Runtime files need repair. Run Recover Workspace.",
            };
        }

        if (snapshot.AppliedState is null)
        {
            return new WorkspaceLaunchPlan
            {
                NeedsProvision = true,
                Summary = "Workspace needs initial runtime provisioning before it can open.",
            };
        }

        if (!hasRuntimeStateFile || snapshot.LocalRuntimeState is null || snapshot.UpdateRequired)
        {
            return new WorkspaceLaunchPlan
            {
                NeedsRecover = true,
                Summary = "Runtime files need repair. Run Recover Workspace.",
            };
        }

        if (snapshot.RuntimeState == WorkspaceRuntimeState.Stopped)
        {
            return new WorkspaceLaunchPlan
            {
                NeedsStart = true,
                Summary = "Workspace runtime is ready but not running.",
            };
        }

        if (snapshot.RuntimeState == WorkspaceRuntimeState.Unknown)
        {
            return new WorkspaceLaunchPlan
            {
                NeedsDiagnostics = true,
                Summary = "Workspace runtime could not be validated. Run Diagnostics.",
            };
        }

        return new WorkspaceLaunchPlan
        {
            CanAttach = snapshot.RuntimeState == WorkspaceRuntimeState.Running,
            NeedsDiagnostics = snapshot.RuntimeState != WorkspaceRuntimeState.Running,
            Summary = snapshot.RuntimeState == WorkspaceRuntimeState.Running
                ? "Workspace is ready to open."
                : "Workspace runtime could not be validated. Run Diagnostics.",
        };
    }
}
