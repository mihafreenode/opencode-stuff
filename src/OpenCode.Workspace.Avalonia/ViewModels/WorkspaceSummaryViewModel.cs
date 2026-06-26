using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspaceSummaryViewModel : ObservableObject
{
    private string? _runtimeStatusLabelOverride;
    private string? _lastActivityOverride;
    private bool _isSelected;

    public WorkspaceSummaryViewModel(WorkspaceShellItem item)
    {
        Item = item;
    }

    public WorkspaceShellItem Item { get; }
    public WorkspaceSnapshot? Snapshot => Item.Snapshot;
    public WorkspaceRecord Record => Item.Record;
    public bool IsLoading => Item.IsLoading;
    public bool HasSnapshot => Snapshot is not null;
    public bool HasError => !HasSnapshot && !IsLoading;
    public string ErrorMessage => string.IsNullOrWhiteSpace(Item.ErrorMessage) ? "Workspace could not be loaded." : Item.ErrorMessage;
    public string Name => HasSnapshot ? Snapshot!.Definition.Workspace.Name : DisplayNameFromRecord();
    public string RootPath => HasSnapshot ? Snapshot!.Paths.RootPath : Record.RootPath;
    public string RepositoryPath => HasSnapshot
        ? string.IsNullOrWhiteSpace(Snapshot!.Record.RepositoryPath) ? Snapshot.Paths.RootPath : Snapshot.Record.RepositoryPath
        : string.IsNullOrWhiteSpace(Record.RepositoryPath) ? Record.RootPath : Record.RepositoryPath;
    public string RuntimeStatusLabel => IsLoading
        ? "Loading..."
        : HasError
        ? _runtimeStatusLabelOverride ?? "Error"
        : !string.IsNullOrWhiteSpace(_runtimeStatusLabelOverride)
        ? _runtimeStatusLabelOverride!
        : Snapshot!.Record.LastOperationSucceeded == false
        ? "Error"
        : Snapshot.UpdateRequired
            ? "Update available"
            : Snapshot.RuntimeState == WorkspaceRuntimeState.Running
                ? "Running"
                : Snapshot.RuntimeState == WorkspaceRuntimeState.Stopped
                    ? "Stopped"
                    : "Ready";
    public string ProtectionLabel => IsLoading
        ? "Loading..."
        : HasError
        ? "Needs Review"
        : Snapshot!.Safety.OverallStatus switch
        {
            WorkspaceSafetyLevel.Protected => "Protected",
            WorkspaceSafetyLevel.PartiallyProtected => "Partially Protected",
            WorkspaceSafetyLevel.AtRisk => "At Risk",
            WorkspaceSafetyLevel.NeedsReview => "Needs Review",
            _ => Snapshot.Safety.Headline,
        };
    public string RuntimeSummary => IsLoading
        ? Item.LoadingStatusMessage
        : !HasSnapshot
        ? "Runtime unavailable"
        : string.IsNullOrWhiteSpace(Snapshot!.ResolvedRuntimePlan?.TargetPlatform)
            ? $"Runtime {Snapshot.RuntimeState}"
            : $"Runtime {Snapshot.ResolvedRuntimePlan.TargetPlatform}";
    public string CurrentBranch => IsLoading
        ? "Loading..."
        : !HasSnapshot || string.IsNullOrWhiteSpace(Snapshot!.Safety.AdvancedGit.CurrentBranch)
            ? Record.SelectedWorkspaceBranch is { Length: > 0 } ? Record.SelectedWorkspaceBranch : "Unknown"
            : Snapshot.Safety.AdvancedGit.CurrentBranch;
    public string Services => IsLoading ? "Loading details..." : !HasSnapshot ? "Unavailable" : Snapshot!.Definition.Services.Count == 0 ? "No services" : string.Join(", ", Snapshot.Definition.Services);
    public string Features => IsLoading ? "Loading details..." : !HasSnapshot ? "Unavailable" : Snapshot!.Definition.Features.Count == 0 ? "No features" : string.Join(", ", Snapshot.Definition.Features);
    public string LastActivity => IsLoading
        ? string.IsNullOrWhiteSpace(Item.LoadingStatusMessage) ? "Loading details..." : Item.LoadingStatusMessage
        : HasError
        ? _lastActivityOverride ?? ErrorMessage
        : !string.IsNullOrWhiteSpace(_lastActivityOverride)
        ? _lastActivityOverride!
        : string.IsNullOrWhiteSpace(Snapshot!.Record.LastOperationResult) ? "No recent activity" : Snapshot.Record.LastOperationResult!;
    public string SafetyState => IsLoading ? "Workspace details are still loading." : HasError ? "Workspace record exists but the workspace could not be loaded." : Snapshot!.Safety.Headline;
    public string RepositoryStatus => IsLoading
        ? string.IsNullOrWhiteSpace(Item.LoadingStatusMessage) ? "Loading details..." : Item.LoadingStatusMessage
        : HasError
        ? "Workspace needs review before it can be opened safely."
        : string.IsNullOrWhiteSpace(Snapshot!.Safety.AdvancedGit.StatusSummary)
            ? CurrentBranch
            : Snapshot.Safety.AdvancedGit.StatusSummary;
    public string LocalRuntimeStateStatus => IsLoading
        ? "Loading..."
        : !HasSnapshot
        ? "Unavailable"
        : Snapshot!.LocalRuntimeState is null
        ? "Missing"
        : string.IsNullOrWhiteSpace(Snapshot.LocalRuntimeState.ResolvedPlatform)
            ? "Loaded"
            : $"Loaded ({Snapshot.LocalRuntimeState.ResolvedPlatform})";
    public string RuntimeTarget => IsLoading ? "Loading..." : HasSnapshot ? Snapshot!.ResolvedRuntimePlan?.TargetPlatform ?? "Unknown" : "Unavailable";
    public string SafeWorkspaceName => BuildSafeWorkspaceToken(Name);
    public string RowAutomationId => $"WorkspaceRow_{SafeWorkspaceName}";
    public string RowAutomationName => $"WorkspaceRow_{SafeWorkspaceName}";
    public string TitleAutomationId => $"WorkspaceTitle_{SafeWorkspaceName}";
    public string TitleAutomationName => $"WorkspaceTitle_{SafeWorkspaceName}";
    public string SelectedMarkerAutomationId => "WorkspaceRow_Selected";
    public string SelectedMarkerAutomationName => "WorkspaceRow_Selected";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void SetReprovisioningState(string message)
    {
        _runtimeStatusLabelOverride = "Reprovisioning";
        _lastActivityOverride = message;
        RaiseWorkspaceDisplayChanged();
    }

    public void SetOperationFailureState(string message)
    {
        _runtimeStatusLabelOverride = "Error";
        _lastActivityOverride = message;
        RaiseWorkspaceDisplayChanged();
    }

    public void ClearTransientOperationState()
    {
        if (_runtimeStatusLabelOverride is null && _lastActivityOverride is null)
        {
            return;
        }

        _runtimeStatusLabelOverride = null;
        _lastActivityOverride = null;
        RaiseWorkspaceDisplayChanged();
    }

    private string DisplayNameFromRecord()
        => string.IsNullOrWhiteSpace(Record.Name) ? Record.RootPath : Record.Name;

    private static string BuildSafeWorkspaceToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unnamed";
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    private void RaiseWorkspaceDisplayChanged()
    {
        RaisePropertyChanged(nameof(RuntimeStatusLabel));
        RaisePropertyChanged(nameof(LastActivity));
    }
}
