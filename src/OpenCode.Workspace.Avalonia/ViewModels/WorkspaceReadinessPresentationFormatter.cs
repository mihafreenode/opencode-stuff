using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.ViewModels;

internal static class WorkspaceReadinessPresentationFormatter
{
    public static string FormatStatusLabel(WorkspaceReadinessSnapshot readiness, WorkspaceSummaryViewModel workspace)
    {
        if (readiness.Status == WorkspaceReadinessStatus.Unavailable
            && readiness.Capabilities.Any(item => !item.IsPrimaryWorkSurface && item.State == WorkspaceCapabilityState.Available))
        {
            return "Workspace Partially Ready";
        }

        if (readiness.Status == WorkspaceReadinessStatus.Unavailable
            && workspace.Record.LastPreparedUtc is null
            && workspace.Record.LastOperationSucceeded == true
            && string.Equals(workspace.Record.LastOperationName, "Create Workspace", StringComparison.Ordinal))
        {
            return "Not Prepared";
        }

        if (readiness.Status == WorkspaceReadinessStatus.Unavailable
            && (workspace.Snapshot?.LocalRuntimeState is null || workspace.Snapshot?.AppliedState is null || workspace.Snapshot?.UpdateRequired == true))
        {
            return "Needs Preparation";
        }

        return readiness.Status switch
        {
            WorkspaceReadinessStatus.Ready => "Workspace Ready",
            WorkspaceReadinessStatus.Preparing => "Preparing",
            WorkspaceReadinessStatus.NeedsRebuild => "Needs Rebuild",
            _ => "Unavailable",
        };
    }

    public static string FormatHeadline(WorkspaceReadinessSnapshot readiness, WorkspaceSummaryViewModel workspace)
        => readiness.Status == WorkspaceReadinessStatus.Preparing
            ? FormatActivityLabel(readiness.CurrentActivity)
            : FormatStatusLabel(readiness, workspace);

    public static string FormatActivityLabel(WorkspaceActivity activity)
        => activity switch
        {
            WorkspaceActivity.Preparing => "Provisioning",
            WorkspaceActivity.OpeningTerminal => "Opening terminal",
            WorkspaceActivity.RepairingRuntime => "Repairing runtime",
            WorkspaceActivity.Investigating => "Investigating",
            WorkspaceActivity.Discovering => "Discovering",
            _ => "None",
        };

    public static string FormatPrimaryActionLabel(WorkspaceReadinessSnapshot readiness)
        => FormatPrimaryActionLabel(readiness.PrimaryAction, readiness.Status);

    public static string FormatPrimaryActionLabel(WorkspacePrimaryAction action, WorkspaceReadinessStatus status = WorkspaceReadinessStatus.Unavailable)
        => action switch
        {
            WorkspacePrimaryAction.ViewProgress => "Open Workspace",
            WorkspacePrimaryAction.RebuildRuntime => "Rebuild Runtime",
            WorkspacePrimaryAction.RunDiagnostics => "Run Diagnostics",
            WorkspacePrimaryAction.OpenFolder => "Open Folder",
            _ => "Open Workspace",
        };
}
