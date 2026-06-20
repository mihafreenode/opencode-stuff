using System.Collections.ObjectModel;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspacesPageViewModel : PageViewModel
{
    private readonly IDesktopShellService _desktopShellService;
    private WorkspaceSummaryViewModel? _selectedWorkspace;

    public WorkspacesPageViewModel(IDesktopShellService desktopShellService)
        : base("Workspaces", "Inspect local workspaces, repository state, and runtime readiness.")
    {
        _desktopShellService = desktopShellService;
        OpenSelectedWorkspaceCommand = new AsyncRelayCommand(OpenSelectedWorkspaceAsync, () => SelectedWorkspace is not null);
        ValidateSelectedWorkspaceCommand = new AsyncRelayCommand(ValidateSelectedWorkspaceInternalAsync, () => SelectedWorkspace is not null);
        DisabledActionCommand = new RelayCommand(() => { });
    }

    public ObservableCollection<WorkspaceSummaryViewModel> Workspaces { get; } = [];
    public AsyncRelayCommand OpenSelectedWorkspaceCommand { get; }
    public AsyncRelayCommand ValidateSelectedWorkspaceCommand { get; }
    public RelayCommand DisabledActionCommand { get; }
    public Func<string, Task>? ValidateWorkspaceAsync { get; set; }

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
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Workspaces.Clear();
        foreach (var snapshot in await _desktopShellService.LoadWorkspaceSnapshotsAsync(includeRuntimeInspection: true, cancellationToken))
        {
            Workspaces.Add(new WorkspaceSummaryViewModel(snapshot));
        }

        SelectedWorkspace = Workspaces.FirstOrDefault();
        if (SelectedWorkspace is null)
        {
            DetailTitle = "No workspaces";
            DetailSummary = "No local workspaces were discovered in the current index.";
            DetailItems.Clear();
            DetailActions.Clear();
        }
    }

    private async Task OpenSelectedWorkspaceAsync()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        await _desktopShellService.OpenPathAsync(SelectedWorkspace.Snapshot.Paths.RootPath);
    }

    private async Task ValidateSelectedWorkspaceInternalAsync()
    {
        if (SelectedWorkspace is null || ValidateWorkspaceAsync is null)
        {
            return;
        }

        await ValidateWorkspaceAsync(SelectedWorkspace.Snapshot.Paths.RootPath);
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
        DetailSummary = SelectedWorkspace.LastActivity;
        DetailItems.Add(new DetailItemViewModel("Repository path", SelectedWorkspace.RepositoryPath));
        DetailItems.Add(new DetailItemViewModel("Current branch", SelectedWorkspace.CurrentBranch));
        DetailItems.Add(new DetailItemViewModel("Services", SelectedWorkspace.Services));
        DetailItems.Add(new DetailItemViewModel("Runtime target", SelectedWorkspace.Snapshot.ResolvedRuntimePlan?.TargetPlatform ?? "Unknown"));
        DetailItems.Add(new DetailItemViewModel("Safety state", SelectedWorkspace.SafetyState));

        DetailActions.Add(new ActionItemViewModel("Open", "Open the workspace folder with the host shell.", true, string.Empty, OpenSelectedWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Validate", "Run portable doctor and platform validation from the Diagnostics page.", true, string.Empty, ValidateSelectedWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Attach", string.Empty, false, "Unavailable in Avalonia preview. Use WPF or CLI for now.", DisabledActionCommand));
        DetailActions.Add(new ActionItemViewModel("Recover", string.Empty, false, "Recovery actions are not ported yet. Use WPF or CLI for now.", DisabledActionCommand));
        DetailActions.Add(new ActionItemViewModel("Save Point", string.Empty, false, "Save Point creation is not implemented in Avalonia preview yet.", DisabledActionCommand));
    }
}
