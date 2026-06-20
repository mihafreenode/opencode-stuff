using System.Collections.ObjectModel;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspacesPageViewModel : PageViewModel
{
    private readonly IDesktopShellService _desktopShellService;
    private WorkspaceSummaryViewModel? _selectedWorkspace;
    private string _emptyStateTitle = string.Empty;
    private string _emptyStateMessage = string.Empty;

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
    public bool HasWorkspaces => Workspaces.Count > 0;
    public bool ShowEmptyState => !HasWorkspaces;

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
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Workspaces.Clear();
        foreach (var snapshot in (await _desktopShellService.LoadWorkspaceSnapshotsAsync(includeRuntimeInspection: true, cancellationToken))
            .OrderBy(item => item.Definition.Workspace.Name, StringComparer.OrdinalIgnoreCase))
        {
            Workspaces.Add(new WorkspaceSummaryViewModel(snapshot));
        }

        RaisePropertyChanged(nameof(HasWorkspaces));
        RaisePropertyChanged(nameof(ShowEmptyState));

        SelectedWorkspace = Workspaces.FirstOrDefault();
        if (SelectedWorkspace is null)
        {
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
        DetailSummary = SelectedWorkspace.SafetyState;
        DetailItems.Add(new DetailItemViewModel("Root path", SelectedWorkspace.RootPath));
        DetailItems.Add(new DetailItemViewModel("Repository path", SelectedWorkspace.RepositoryPath));
        DetailItems.Add(new DetailItemViewModel("Current branch", SelectedWorkspace.CurrentBranch));
        DetailItems.Add(new DetailItemViewModel("Protection state", SelectedWorkspace.ProtectionLabel));
        DetailItems.Add(new DetailItemViewModel("Repository status", SelectedWorkspace.RepositoryStatus));
        DetailItems.Add(new DetailItemViewModel("Runtime-state status", SelectedWorkspace.LocalRuntimeStateStatus));
        DetailItems.Add(new DetailItemViewModel("Last activity", SelectedWorkspace.LastActivity));
        DetailItems.Add(new DetailItemViewModel("Services", SelectedWorkspace.Services));
        DetailItems.Add(new DetailItemViewModel("Features", SelectedWorkspace.Features));
        DetailItems.Add(new DetailItemViewModel("Runtime target", SelectedWorkspace.Snapshot.ResolvedRuntimePlan?.TargetPlatform ?? "Unknown"));

        DetailActions.Add(new ActionItemViewModel("Open", "Open the workspace folder with the host shell.", true, string.Empty, OpenSelectedWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Validate", "Run portable doctor and platform validation from the Diagnostics page.", true, string.Empty, ValidateSelectedWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Attach", string.Empty, false, "Unavailable in Avalonia preview. Use WPF or CLI for now.", DisabledActionCommand));
        DetailActions.Add(new ActionItemViewModel("Recover", string.Empty, false, "Recovery actions are not ported yet. Use WPF or CLI for now.", DisabledActionCommand));
        DetailActions.Add(new ActionItemViewModel("Save Point", string.Empty, false, "Save Point creation is not implemented in Avalonia preview yet.", DisabledActionCommand));

        RaisePropertyChanged(nameof(SelectedWorkspace));
    }
}
