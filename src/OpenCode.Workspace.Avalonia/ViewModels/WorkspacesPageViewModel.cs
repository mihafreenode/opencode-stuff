using System.Collections.ObjectModel;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspacesPageViewModel : PageViewModel
{
    private readonly IDesktopShellService _desktopShellService;
    private WorkspaceSummaryViewModel? _selectedWorkspace;
    private string _emptyStateTitle = string.Empty;
    private string _emptyStateMessage = string.Empty;
    private WorkspaceLoadReport _workspaceLoadReport = new();
    private bool _isLoading;
    private bool _hasLoadError;
    private string _loadErrorMessage = string.Empty;
    private bool _isReprovisioning;
    private string _reprovisionStatusMessage = string.Empty;

    public WorkspacesPageViewModel(IDesktopShellService desktopShellService)
        : base("Workspaces", "Inspect local workspaces, repository state, and runtime readiness.")
    {
        _desktopShellService = desktopShellService;
        OpenSelectedWorkspaceCommand = new AsyncRelayCommand(OpenSelectedWorkspaceAsync, () => SelectedWorkspace is not null);
        ValidateSelectedWorkspaceCommand = new AsyncRelayCommand(ValidateSelectedWorkspaceInternalAsync, () => SelectedWorkspace is not null);
        ReprovisionWorkspaceCommand = new AsyncRelayCommand(ReprovisionSelectedWorkspaceAsync, CanReprovisionSelectedWorkspace);
        DisabledActionCommand = new RelayCommand(() => { });
        SetLoadingState();
    }

    public ObservableCollection<WorkspaceSummaryViewModel> Workspaces { get; } = [];
    public AsyncRelayCommand OpenSelectedWorkspaceCommand { get; }
    public AsyncRelayCommand ValidateSelectedWorkspaceCommand { get; }
    public AsyncRelayCommand ReprovisionWorkspaceCommand { get; }
    public RelayCommand DisabledActionCommand { get; }
    public Func<string, Task>? ValidateWorkspaceAsync { get; set; }
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasLoadError
    {
        get => _hasLoadError;
        private set => SetProperty(ref _hasLoadError, value);
    }

    public string LoadErrorMessage
    {
        get => _loadErrorMessage;
        private set => SetProperty(ref _loadErrorMessage, value);
    }

    public bool IsReprovisioning
    {
        get => _isReprovisioning;
        private set => SetProperty(ref _isReprovisioning, value);
    }

    public string ReprovisionStatusMessage
    {
        get => _reprovisionStatusMessage;
        private set => SetProperty(ref _reprovisionStatusMessage, value);
    }

    public bool HasWorkspaces => Workspaces.Count > 0;
    public bool ShowEmptyState => !IsLoading && !HasLoadError && !HasWorkspaces;
    public bool ShowLoadingState => IsLoading;
    public bool ShowErrorState => HasLoadError && !HasWorkspaces;
    public WorkspaceLoadReport WorkspaceLoadReport
    {
        get => _workspaceLoadReport;
        private set => SetProperty(ref _workspaceLoadReport, value);
    }

    public string EmptyStateTitle
    {
        get => _emptyStateTitle;
        private set => SetProperty(ref _emptyStateTitle, value);
    }

    public string EmptyStateMessage
    {
        get => _emptyStateMessage;
        private set => SetProperty(ref _emptyStateMessage, value);
    }

    public WorkspaceSummaryViewModel? SelectedWorkspace
    {
        get => _selectedWorkspace;
        set
        {
            if (SetProperty(ref _selectedWorkspace, value))
            {
                UpdateDetailPanel();
                OpenSelectedWorkspaceCommand.RaiseCanExecuteChanged();
                ValidateSelectedWorkspaceCommand.RaiseCanExecuteChanged();
                ReprovisionWorkspaceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        SetLoadingState();
        Workspaces.Clear();
        try
        {
            var loadResult = await _desktopShellService.LoadWorkspaceItemsAsync(includeRuntimeInspection: true, cancellationToken);
            WorkspaceLoadReport = loadResult.Report;
            foreach (var item in loadResult.Items.OrderBy(item => string.IsNullOrWhiteSpace(item.Record.Name) ? item.Record.RootPath : item.Record.Name, StringComparer.OrdinalIgnoreCase))
            {
                Workspaces.Add(new WorkspaceSummaryViewModel(item));
            }

            HasLoadError = false;
            LoadErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            HasLoadError = true;
            LoadErrorMessage = exception.Message;
            DetailTitle = "Workspace discovery failed";
            DetailSummary = "The window is available, but workspace discovery did not complete.";
            DetailItems.Clear();
            DetailItems.Add(new DetailItemViewModel("Error", exception.Message));
            DetailActions.Clear();
            DetailActions.Add(new ActionItemViewModel("Open", string.Empty, false, "Workspace discovery failed.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Attach", string.Empty, false, "Workspace discovery failed.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Validate", string.Empty, false, "Workspace discovery failed.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Recover", string.Empty, false, "Workspace discovery failed.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Save Point", string.Empty, false, "Workspace discovery failed.", DisabledActionCommand));
            SelectedWorkspace = null;
        }
        finally
        {
            IsLoading = false;
        }

        RaisePropertyChanged(nameof(HasWorkspaces));
        RaisePropertyChanged(nameof(ShowEmptyState));
        RaisePropertyChanged(nameof(ShowLoadingState));
        RaisePropertyChanged(nameof(ShowErrorState));

        if (!HasLoadError)
        {
            SelectedWorkspace = Workspaces.FirstOrDefault();
        }
        if (SelectedWorkspace is null)
        {
            if (HasLoadError)
            {
                EmptyStateTitle = string.Empty;
                EmptyStateMessage = string.Empty;
                return;
            }

            EmptyStateTitle = "No workspaces discovered.";
            EmptyStateMessage = "OpenCode looks for workspace.yaml,\nworkspace.yml,\n.opencode/profile.yaml,\n.opencode/profile.yml\n\nUse Create Workspace or Open Existing Repository.";
            DetailTitle = EmptyStateTitle;
            DetailSummary = EmptyStateMessage;
            DetailItems.Clear();
            DetailActions.Clear();
            DetailActions.Add(new ActionItemViewModel("Open", string.Empty, false, "No workspace selected.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Attach", string.Empty, false, "Unavailable in Avalonia preview. Use WPF or CLI for now.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Validate", string.Empty, false, "No workspace selected.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Recover", string.Empty, false, "No workspace selected. Use WPF or CLI for now.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Save Point", string.Empty, false, "No workspace selected. Use WPF or CLI for now.", DisabledActionCommand));
            return;
        }

        EmptyStateTitle = string.Empty;
        EmptyStateMessage = string.Empty;
    }

    private void SetLoadingState()
    {
        IsLoading = true;
        HasLoadError = false;
        LoadErrorMessage = string.Empty;
        EmptyStateTitle = "Loading workspaces...";
        EmptyStateMessage = "Reading the shared workspace index and snapshot state.";
        DetailTitle = "Workspaces";
        DetailSummary = "Loading workspace index and snapshot state.";
        RaisePropertyChanged(nameof(ShowLoadingState));
        RaisePropertyChanged(nameof(ShowErrorState));
        RaisePropertyChanged(nameof(ShowEmptyState));
    }

    private async Task OpenSelectedWorkspaceAsync()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        await _desktopShellService.OpenPathAsync(SelectedWorkspace.RootPath);
    }

    private async Task ValidateSelectedWorkspaceInternalAsync()
    {
        if (SelectedWorkspace is null || ValidateWorkspaceAsync is null)
        {
            return;
        }

        await ValidateWorkspaceAsync(SelectedWorkspace.RootPath);
    }

    private async Task ReprovisionSelectedWorkspaceAsync()
    {
        if (!CanReprovisionSelectedWorkspace() || SelectedWorkspace is null)
        {
            return;
        }

        try
        {
            IsReprovisioning = true;
            ReprovisionStatusMessage = "Preparing workspace";
            UpdateDetailPanel();

            var result = await _desktopShellService.ReprovisionWorkspaceAsync(
                SelectedWorkspace.RootPath,
                message =>
                {
                    ReprovisionStatusMessage = message;
                    DetailSummary = message;
                });

            ReplaceSelectedWorkspace(result.Snapshot);
            ReprovisionStatusMessage = result.Message;
            DetailSummary = result.Message;
        }
        catch (Exception exception)
        {
            ReprovisionStatusMessage = GetActionableReprovisionFailure(exception.Message);
            DetailSummary = ReprovisionStatusMessage;
            DetailItems.Clear();
            DetailItems.Add(new DetailItemViewModel("Root path", SelectedWorkspace.RootPath));
            DetailItems.Add(new DetailItemViewModel("Failure", ReprovisionStatusMessage));
            DetailActions.Clear();
            DetailActions.Add(new ActionItemViewModel("Reprovision", "Retry workspace regeneration and runtime provisioning.", CanReprovisionSelectedWorkspace(), string.Empty, ReprovisionWorkspaceCommand));
            DetailActions.Add(new ActionItemViewModel("Attach", string.Empty, false, "Unavailable in Avalonia preview. Use WPF or CLI for now.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Validate", "Run portable doctor and platform validation from the Diagnostics page.", true, string.Empty, ValidateSelectedWorkspaceCommand));
        }
        finally
        {
            IsReprovisioning = false;
            ReprovisionWorkspaceCommand.RaiseCanExecuteChanged();
        }
    }

    private void UpdateDetailPanel()
    {
        DetailItems.Clear();
        DetailActions.Clear();

        if (SelectedWorkspace is null)
        {
            DetailTitle = "No workspace selected";
            DetailSummary = "Select a workspace to inspect repository and runtime details.";
            return;
        }

        DetailTitle = SelectedWorkspace.Name;
        DetailSummary = BuildWorkspaceSummary(SelectedWorkspace);
        DetailItems.Add(new DetailItemViewModel("Root path", SelectedWorkspace.RootPath));
        DetailItems.Add(new DetailItemViewModel("Repository path", SelectedWorkspace.RepositoryPath));
        DetailItems.Add(new DetailItemViewModel("Current branch", SelectedWorkspace.CurrentBranch));
        DetailItems.Add(new DetailItemViewModel("Protection state", SelectedWorkspace.ProtectionLabel));
        DetailItems.Add(new DetailItemViewModel("Repository status", SelectedWorkspace.RepositoryStatus));
        DetailItems.Add(new DetailItemViewModel("Runtime-state status", SelectedWorkspace.LocalRuntimeStateStatus));
        DetailItems.Add(new DetailItemViewModel("Last activity", SelectedWorkspace.LastActivity));
        DetailItems.Add(new DetailItemViewModel("Services", SelectedWorkspace.Services));
        DetailItems.Add(new DetailItemViewModel("Features", SelectedWorkspace.Features));
        DetailItems.Add(new DetailItemViewModel("Runtime target", SelectedWorkspace.RuntimeTarget));
        if (SelectedWorkspace.HasError)
        {
            DetailItems.Add(new DetailItemViewModel("Load failure", SelectedWorkspace.ErrorMessage));
        }

        DetailActions.Add(new ActionItemViewModel("Open", "Open the workspace folder with the host shell.", true, string.Empty, OpenSelectedWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Validate", "Run portable doctor and platform validation from the Diagnostics page.", true, string.Empty, ValidateSelectedWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Reprovision", BuildReprovisionDescription(SelectedWorkspace), CanReprovisionSelectedWorkspace(), GetReprovisionDisabledReason(SelectedWorkspace), ReprovisionWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Attach", string.Empty, false, "Unavailable in Avalonia preview. Use WPF or CLI for now.", DisabledActionCommand));
        DetailActions.Add(new ActionItemViewModel("Recover", string.Empty, false, SelectedWorkspace.HasError ? "Workspace must load successfully before recovery UI can be offered in Avalonia. Use WPF or CLI for now." : "Recovery actions are not ported yet. Use WPF or CLI for now.", DisabledActionCommand));
        DetailActions.Add(new ActionItemViewModel("Save Point", string.Empty, false, SelectedWorkspace.HasError ? "Workspace must load successfully before Save Point operations can run. Use WPF or CLI for now." : "Save Point creation is not implemented in Avalonia preview yet.", DisabledActionCommand));

        RaisePropertyChanged(nameof(SelectedWorkspace));
    }

    private bool CanReprovisionSelectedWorkspace()
        => SelectedWorkspace is { HasSnapshot: true } && !IsReprovisioning;

    private string GetReprovisionDisabledReason(WorkspaceSummaryViewModel workspace)
    {
        if (IsReprovisioning)
        {
            return string.IsNullOrWhiteSpace(ReprovisionStatusMessage) ? "Reprovision is already running." : ReprovisionStatusMessage;
        }

        return workspace.HasSnapshot
            ? string.Empty
            : "Workspace configuration must load successfully before reprovision can run.";
    }

    private string BuildReprovisionDescription(WorkspaceSummaryViewModel workspace)
    {
        if (IsReprovisioning)
        {
            return ReprovisionStatusMessage;
        }

        if (workspace.Snapshot?.LocalRuntimeState is null)
        {
            return "Runtime state is missing. Reprovision will regenerate local runtime state.";
        }

        if (workspace.Snapshot?.UpdateRequired == true || workspace.Snapshot?.AppliedState is null)
        {
            return "Workspace files are out of date. Reprovision to regenerate runtime files.";
        }

        return "Regenerate runtime files, validate compose, and reprovision the workspace runtime.";
    }

    private string BuildWorkspaceSummary(WorkspaceSummaryViewModel workspace)
    {
        if (IsReprovisioning)
        {
            return string.IsNullOrWhiteSpace(ReprovisionStatusMessage) ? "Reprovision in progress." : ReprovisionStatusMessage;
        }

        if (workspace.Snapshot?.LocalRuntimeState is null)
        {
            return "Runtime state is missing. Reprovision will regenerate local runtime state.";
        }

        if (workspace.Snapshot?.UpdateRequired == true || workspace.Snapshot?.AppliedState is null)
        {
            return "Workspace files are out of date. Reprovision to regenerate runtime files.";
        }

        return workspace.SafetyState;
    }

    private void ReplaceSelectedWorkspace(OpenCode.Workspace.Core.Models.WorkspaceSnapshot snapshot)
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        var replacement = new WorkspaceSummaryViewModel(new WorkspaceShellItem { Record = snapshot.Record, Snapshot = snapshot });
        var index = Workspaces.IndexOf(SelectedWorkspace);
        if (index >= 0)
        {
            Workspaces[index] = replacement;
            SelectedWorkspace = replacement;
        }
    }

    private static string GetActionableReprovisionFailure(string error)
        => string.IsNullOrWhiteSpace(error)
            ? "Workspace reprovision failed. Check the workspace activity and try again."
            : $"Workspace reprovision failed. {error}";
}
