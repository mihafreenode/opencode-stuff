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
    public string RootPath => Snapshot.Paths.RootPath;
    public string RepositoryPath => string.IsNullOrWhiteSpace(Snapshot.Record.RepositoryPath) ? Snapshot.Paths.RootPath : Snapshot.Record.RepositoryPath;
    public string RuntimeStatusLabel => Snapshot.Record.LastOperationSucceeded == false
        ? "Error"
        : Snapshot.UpdateRequired
            ? "Update available"
            : Snapshot.RuntimeState == WorkspaceRuntimeState.Running
                ? "Running"
                : Snapshot.RuntimeState == WorkspaceRuntimeState.Stopped
                    ? "Stopped"
                    : "Ready";
    public string ProtectionLabel => Snapshot.Safety.OverallStatus switch
    {
        WorkspaceSafetyLevel.Protected => "Protected",
        WorkspaceSafetyLevel.PartiallyProtected => "Partially Protected",
        WorkspaceSafetyLevel.AtRisk => "At Risk",
        WorkspaceSafetyLevel.NeedsReview => "Needs Review",
        _ => Snapshot.Safety.Headline,
    };
    public string RuntimeSummary => string.IsNullOrWhiteSpace(Snapshot.ResolvedRuntimePlan?.TargetPlatform)
        ? $"Runtime {Snapshot.RuntimeState}"
        : $"Runtime {Snapshot.ResolvedRuntimePlan.TargetPlatform}";
    public string CurrentBranch => string.IsNullOrWhiteSpace(Snapshot.Safety.AdvancedGit.CurrentBranch) ? "Unknown" : Snapshot.Safety.AdvancedGit.CurrentBranch;
    public string Services => Snapshot.Definition.Services.Count == 0 ? "No services" : string.Join(", ", Snapshot.Definition.Services);
    public string Features => Snapshot.Definition.Features.Count == 0 ? "No features" : string.Join(", ", Snapshot.Definition.Features);
    public string LastActivity => string.IsNullOrWhiteSpace(Snapshot.Record.LastOperationResult) ? "No recent activity" : Snapshot.Record.LastOperationResult!;
    public string SafetyState => Snapshot.Safety.Headline;
    public string RepositoryStatus => string.IsNullOrWhiteSpace(Snapshot.Safety.AdvancedGit.StatusSummary)
        ? CurrentBranch
        : Snapshot.Safety.AdvancedGit.StatusSummary;
    public string LocalRuntimeStateStatus => Snapshot.LocalRuntimeState is null
        ? "Missing"
        : string.IsNullOrWhiteSpace(Snapshot.LocalRuntimeState.ResolvedPlatform)
            ? "Loaded"
            : $"Loaded ({Snapshot.LocalRuntimeState.ResolvedPlatform})";
}
