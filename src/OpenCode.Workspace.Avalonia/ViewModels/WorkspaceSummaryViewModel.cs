using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspaceSummaryViewModel
{
    public WorkspaceSummaryViewModel(WorkspaceSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public WorkspaceSnapshot Snapshot { get; }
    public string Name => Snapshot.Definition.Workspace.Name;
    public string RepositoryPath => string.IsNullOrWhiteSpace(Snapshot.Record.RepositoryPath) ? Snapshot.Paths.RootPath : Snapshot.Record.RepositoryPath;
    public string StatusLabel => Snapshot.Record.LastOperationSucceeded == false
        ? "Error"
        : Snapshot.UpdateRequired
            ? "Update available"
            : Snapshot.RuntimeState == WorkspaceRuntimeState.Running
                ? "Running"
                : Snapshot.RuntimeState == WorkspaceRuntimeState.Stopped
                    ? "Stopped"
                    : "Ready";
    public string RuntimeSummary => string.IsNullOrWhiteSpace(Snapshot.ResolvedRuntimePlan?.TargetPlatform)
        ? $"Runtime {Snapshot.RuntimeState}"
        : $"{Snapshot.ResolvedRuntimePlan.Runtime} on {Snapshot.ResolvedRuntimePlan.TargetPlatform}";
    public string CurrentBranch => string.IsNullOrWhiteSpace(Snapshot.Safety.AdvancedGit.CurrentBranch) ? "Unknown" : Snapshot.Safety.AdvancedGit.CurrentBranch;
    public string Services => Snapshot.Definition.Services.Count == 0 ? "No services" : string.Join(", ", Snapshot.Definition.Services);
    public string LastActivity => string.IsNullOrWhiteSpace(Snapshot.Record.LastOperationResult) ? "No recent activity" : Snapshot.Record.LastOperationResult!;
    public string SafetyState => Snapshot.Safety.Headline;
}
