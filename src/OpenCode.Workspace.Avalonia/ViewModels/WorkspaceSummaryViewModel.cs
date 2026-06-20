using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspaceSummaryViewModel
{
    public WorkspaceSummaryViewModel(WorkspaceShellItem item)
    {
        Item = item;
    }

    public WorkspaceShellItem Item { get; }
    public WorkspaceSnapshot? Snapshot => Item.Snapshot;
    public WorkspaceRecord Record => Item.Record;
    public bool HasSnapshot => Snapshot is not null;
    public bool HasError => !HasSnapshot;
    public string ErrorMessage => string.IsNullOrWhiteSpace(Item.ErrorMessage) ? "Workspace could not be loaded." : Item.ErrorMessage;
    public string Name => HasSnapshot ? Snapshot!.Definition.Workspace.Name : DisplayNameFromRecord();
    public string RootPath => HasSnapshot ? Snapshot!.Paths.RootPath : Record.RootPath;
    public string RepositoryPath => HasSnapshot
        ? string.IsNullOrWhiteSpace(Snapshot!.Record.RepositoryPath) ? Snapshot.Paths.RootPath : Snapshot.Record.RepositoryPath
        : string.IsNullOrWhiteSpace(Record.RepositoryPath) ? Record.RootPath : Record.RepositoryPath;
    public string RuntimeStatusLabel => HasError
        ? "Error"
        : Snapshot!.Record.LastOperationSucceeded == false
        ? "Error"
        : Snapshot.UpdateRequired
            ? "Update available"
            : Snapshot.RuntimeState == WorkspaceRuntimeState.Running
                ? "Running"
                : Snapshot.RuntimeState == WorkspaceRuntimeState.Stopped
                    ? "Stopped"
                    : "Ready";
    public string ProtectionLabel => HasError
        ? "Needs Review"
        : Snapshot!.Safety.OverallStatus switch
        {
            WorkspaceSafetyLevel.Protected => "Protected",
            WorkspaceSafetyLevel.PartiallyProtected => "Partially Protected",
            WorkspaceSafetyLevel.AtRisk => "At Risk",
            WorkspaceSafetyLevel.NeedsReview => "Needs Review",
            _ => Snapshot.Safety.Headline,
        };
    public string RuntimeSummary => !HasSnapshot
        ? "Runtime unavailable"
        : string.IsNullOrWhiteSpace(Snapshot!.ResolvedRuntimePlan?.TargetPlatform)
            ? $"Runtime {Snapshot.RuntimeState}"
            : $"Runtime {Snapshot.ResolvedRuntimePlan.TargetPlatform}";
    public string CurrentBranch => !HasSnapshot || string.IsNullOrWhiteSpace(Snapshot!.Safety.AdvancedGit.CurrentBranch) ? Record.SelectedWorkspaceBranch is { Length: > 0 } ? Record.SelectedWorkspaceBranch : "Unknown" : Snapshot.Safety.AdvancedGit.CurrentBranch;
    public string Services => !HasSnapshot ? "Unavailable" : Snapshot!.Definition.Services.Count == 0 ? "No services" : string.Join(", ", Snapshot.Definition.Services);
    public string Features => !HasSnapshot ? "Unavailable" : Snapshot!.Definition.Features.Count == 0 ? "No features" : string.Join(", ", Snapshot.Definition.Features);
    public string LastActivity => HasError
        ? ErrorMessage
        : string.IsNullOrWhiteSpace(Snapshot!.Record.LastOperationResult) ? "No recent activity" : Snapshot.Record.LastOperationResult!;
    public string SafetyState => HasError ? "Workspace record exists but the workspace could not be loaded." : Snapshot!.Safety.Headline;
    public string RepositoryStatus => HasError
        ? "Workspace needs review before it can be opened safely."
        : string.IsNullOrWhiteSpace(Snapshot!.Safety.AdvancedGit.StatusSummary)
            ? CurrentBranch
            : Snapshot.Safety.AdvancedGit.StatusSummary;
    public string LocalRuntimeStateStatus => !HasSnapshot
        ? "Unavailable"
        : Snapshot!.LocalRuntimeState is null
        ? "Missing"
        : string.IsNullOrWhiteSpace(Snapshot.LocalRuntimeState.ResolvedPlatform)
            ? "Loaded"
            : $"Loaded ({Snapshot.LocalRuntimeState.ResolvedPlatform})";
    public string RuntimeTarget => HasSnapshot ? Snapshot!.ResolvedRuntimePlan?.TargetPlatform ?? "Unknown" : "Unavailable";

    private string DisplayNameFromRecord()
        => string.IsNullOrWhiteSpace(Record.Name) ? Record.RootPath : Record.Name;
}
