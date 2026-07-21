using System.Collections.ObjectModel;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class RuntimeResourcesPageViewModel : PageViewModel
{
    private readonly IDesktopWorkspaceService _desktopWorkspaceService;
    private WorkspaceRuntimeWorkspaceEntry? _selectedWorkspace;
    private WorkspaceRuntimeResourceEntry? _selectedResource;
    private WorkspaceRuntimeConflictEntry? _selectedConflict;
    private WorkspaceRuntimeResourceEntry? _selectedOrphanedResource;
    private WorkspaceRuntimeHealthEntry? _selectedHealth;
    private string _statusMessage;

    public RuntimeResourcesPageViewModel(IDesktopWorkspaceService desktopWorkspaceService)
        : base("Runtime Resources", "Workspace-centric runtime resources, ownership, conflicts, and cleanup guidance.")
    {
        _desktopWorkspaceService = desktopWorkspaceService;
        _statusMessage = "Refresh runtime resources to inspect ownership, conflicts, and orphaned Docker resources.";
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenOwningWorkspaceCommand = new AsyncRelayCommand(OpenOwningWorkspaceAsync, CanOpenOwningWorkspace);
        OpenServiceCommand = new AsyncRelayCommand(OpenServiceAsync, CanOpenService);
        StartRuntimeCommand = new AsyncRelayCommand(StartRuntimeAsync, CanRunWorkspaceAction);
        StopRuntimeCommand = new AsyncRelayCommand(StopRuntimeAsync, CanRunWorkspaceAction);
        ReleaseResourcesCommand = new AsyncRelayCommand(ReleaseResourcesAsync, CanRunWorkspaceAction);
        ResetRuntimeCommand = new AsyncRelayCommand(ResetRuntimeAsync, CanRunWorkspaceAction);
        InspectResourceCommand = new AsyncRelayCommand(InspectSelectedResourceAsync, CanInspectResource);
        CleanOrphanedResourcesCommand = new AsyncRelayCommand(CleanOrphanedResourcesAsync, () => OrphanedResources.Any(item => item.CanCleanUpSafely));
        DetailTitle = Title;
        DetailSummary = _statusMessage;
        UpdateActions();
    }

    public Func<string, Task>? NavigateToWorkspaceAsync { get; set; }

    public ObservableCollection<WorkspaceRuntimeWorkspaceEntry> Workspaces { get; } = [];
    public ObservableCollection<WorkspaceRuntimeResourceEntry> Resources { get; } = [];
    public ObservableCollection<WorkspaceRuntimeConflictEntry> Conflicts { get; } = [];
    public ObservableCollection<WorkspaceRuntimeResourceEntry> UnusedResources { get; } = [];
    public ObservableCollection<WorkspaceRuntimeResourceEntry> OrphanedResources { get; } = [];
    public ObservableCollection<WorkspaceRuntimeHealthEntry> HealthItems { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand OpenOwningWorkspaceCommand { get; }
    public AsyncRelayCommand OpenServiceCommand { get; }
    public AsyncRelayCommand StartRuntimeCommand { get; }
    public AsyncRelayCommand StopRuntimeCommand { get; }
    public AsyncRelayCommand ReleaseResourcesCommand { get; }
    public AsyncRelayCommand ResetRuntimeCommand { get; }
    public AsyncRelayCommand InspectResourceCommand { get; }
    public AsyncRelayCommand CleanOrphanedResourcesCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public WorkspaceRuntimeWorkspaceEntry? SelectedWorkspace
    {
        get => _selectedWorkspace;
        set
        {
            if (SetProperty(ref _selectedWorkspace, value) && value is not null)
            {
                _selectedResource = null;
                _selectedConflict = null;
                _selectedOrphanedResource = null;
                _selectedHealth = null;
                RaisePropertyChanged(nameof(SelectedResource));
                RaisePropertyChanged(nameof(SelectedConflict));
                RaisePropertyChanged(nameof(SelectedOrphanedResource));
                RaisePropertyChanged(nameof(SelectedHealth));
                ShowWorkspaceDetail(value);
            }
        }
    }

    public WorkspaceRuntimeResourceEntry? SelectedResource
    {
        get => _selectedResource;
        set
        {
            if (SetProperty(ref _selectedResource, value) && value is not null)
            {
                _selectedWorkspace = null;
                _selectedConflict = null;
                _selectedOrphanedResource = null;
                _selectedHealth = null;
                RaisePropertyChanged(nameof(SelectedWorkspace));
                RaisePropertyChanged(nameof(SelectedConflict));
                RaisePropertyChanged(nameof(SelectedOrphanedResource));
                RaisePropertyChanged(nameof(SelectedHealth));
                ShowResourceDetail(value, "Resource");
            }
        }
    }

    public WorkspaceRuntimeConflictEntry? SelectedConflict
    {
        get => _selectedConflict;
        set
        {
            if (SetProperty(ref _selectedConflict, value) && value is not null)
            {
                _selectedWorkspace = null;
                _selectedResource = null;
                _selectedOrphanedResource = null;
                _selectedHealth = null;
                RaisePropertyChanged(nameof(SelectedWorkspace));
                RaisePropertyChanged(nameof(SelectedResource));
                RaisePropertyChanged(nameof(SelectedOrphanedResource));
                RaisePropertyChanged(nameof(SelectedHealth));
                ShowConflictDetail(value);
            }
        }
    }

    public WorkspaceRuntimeResourceEntry? SelectedOrphanedResource
    {
        get => _selectedOrphanedResource;
        set
        {
            if (SetProperty(ref _selectedOrphanedResource, value) && value is not null)
            {
                _selectedWorkspace = null;
                _selectedResource = null;
                _selectedConflict = null;
                _selectedHealth = null;
                RaisePropertyChanged(nameof(SelectedWorkspace));
                RaisePropertyChanged(nameof(SelectedResource));
                RaisePropertyChanged(nameof(SelectedConflict));
                RaisePropertyChanged(nameof(SelectedHealth));
                ShowResourceDetail(value, "Orphaned Resource");
            }
        }
    }

    public WorkspaceRuntimeHealthEntry? SelectedHealth
    {
        get => _selectedHealth;
        set
        {
            if (SetProperty(ref _selectedHealth, value) && value is not null)
            {
                _selectedWorkspace = null;
                _selectedResource = null;
                _selectedConflict = null;
                _selectedOrphanedResource = null;
                RaisePropertyChanged(nameof(SelectedWorkspace));
                RaisePropertyChanged(nameof(SelectedResource));
                RaisePropertyChanged(nameof(SelectedConflict));
                RaisePropertyChanged(nameof(SelectedOrphanedResource));
                ShowHealthDetail(value);
            }
        }
    }

    public async Task RefreshAsync()
    {
        var report = await _desktopWorkspaceService.GetRuntimeResourceExplorerAsync();
        ReplaceCollection(Workspaces, report.Workspaces);
        ReplaceCollection(Resources, report.Resources);
        ReplaceCollection(Conflicts, report.Conflicts);
        ReplaceCollection(UnusedResources, report.UnusedResources);
        ReplaceCollection(OrphanedResources, report.OrphanedResources);
        ReplaceCollection(HealthItems, report.Health);
        StatusMessage = report.Summary;
        DetailSummary = report.Summary;
        CleanOrphanedResourcesCommand.RaiseCanExecuteChanged();
        if (SelectedWorkspace is null && SelectedResource is null && SelectedConflict is null && SelectedOrphanedResource is null && SelectedHealth is null)
        {
            SelectedWorkspace = Workspaces.FirstOrDefault();
        }
    }

    private void ShowWorkspaceDetail(WorkspaceRuntimeWorkspaceEntry workspace)
    {
        DetailTitle = workspace.WorkspaceName;
        DetailSummary = workspace.Status;
        DetailItems.Clear();
        DetailItems.Add(new DetailItemViewModel("Workspace", workspace.WorkspaceName));
        DetailItems.Add(new DetailItemViewModel("Status", workspace.Status));
        DetailItems.Add(new DetailItemViewModel("Health", workspace.Health));
        DetailItems.Add(new DetailItemViewModel("Ports", workspace.Ports.Count == 0 ? "None" : string.Join(Environment.NewLine, workspace.Ports)));
        DetailItems.Add(new DetailItemViewModel("Containers", workspace.Containers.Count == 0 ? "None" : string.Join(Environment.NewLine, workspace.Containers)));
        DetailItems.Add(new DetailItemViewModel("Volumes", workspace.Volumes.Count == 0 ? "None" : string.Join(Environment.NewLine, workspace.Volumes)));
        DetailItems.Add(new DetailItemViewModel("Network", workspace.Networks.Count == 0 ? "None" : string.Join(Environment.NewLine, workspace.Networks)));
        DetailItems.Add(new DetailItemViewModel("Services", workspace.Services.Count == 0 ? "None" : string.Join(Environment.NewLine, workspace.Services)));
        DetailItems.Add(new DetailItemViewModel("Runtime", workspace.OwningRuntime));
        DetailItems.Add(new DetailItemViewModel("Template", workspace.Template));
        UpdateActions();
    }

    private void ShowResourceDetail(WorkspaceRuntimeResourceEntry resource, string category)
    {
        DetailTitle = resource.DisplayName;
        DetailSummary = resource.Status;
        DetailItems.Clear();
        DetailItems.Add(new DetailItemViewModel("Category", category));
        DetailItems.Add(new DetailItemViewModel("Workspace", string.IsNullOrWhiteSpace(resource.WorkspaceName) ? "Unowned" : resource.WorkspaceName));
        DetailItems.Add(new DetailItemViewModel("Status", resource.Status));
        DetailItems.Add(new DetailItemViewModel("Health", resource.Health));
        if (resource.PreferredPort is not null)
        {
            DetailItems.Add(new DetailItemViewModel("Preferred", resource.PreferredPort.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (resource.CurrentPort is not null)
        {
            DetailItems.Add(new DetailItemViewModel("Current", resource.CurrentPort.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrWhiteSpace(resource.ContainerName))
        {
            DetailItems.Add(new DetailItemViewModel("Container", resource.ContainerName));
        }

        if (!string.IsNullOrWhiteSpace(resource.Endpoint))
        {
            DetailItems.Add(new DetailItemViewModel("Endpoint", resource.Endpoint));
        }

        if (!string.IsNullOrWhiteSpace(resource.Reason))
        {
            DetailItems.Add(new DetailItemViewModel("Reason", resource.Reason));
        }

        DetailItems.Add(new DetailItemViewModel("Cleanup", resource.CleanupSummary));
        UpdateActions();
    }

    private void ShowConflictDetail(WorkspaceRuntimeConflictEntry conflict)
    {
        DetailTitle = conflict.DisplayName;
        DetailSummary = conflict.ConflictType;
        DetailItems.Clear();
        DetailItems.Add(new DetailItemViewModel("Conflict", conflict.ConflictType));
        DetailItems.Add(new DetailItemViewModel("Current owner", conflict.CurrentOwner));
        DetailItems.Add(new DetailItemViewModel("Requested owner", conflict.RequestedOwner));
        DetailItems.Add(new DetailItemViewModel("Recommended action", conflict.RecommendedAction));
        DetailItems.Add(new DetailItemViewModel("Details", conflict.Details));
        UpdateActions();
    }

    private void ShowHealthDetail(WorkspaceRuntimeHealthEntry health)
    {
        DetailTitle = health.Category;
        DetailSummary = health.Summary;
        DetailItems.Clear();
        DetailItems.Add(new DetailItemViewModel("Status", health.Status));
        DetailItems.Add(new DetailItemViewModel("Summary", health.Summary));
        UpdateActions();
    }

    private void UpdateActions()
    {
        OpenOwningWorkspaceCommand.RaiseCanExecuteChanged();
        OpenServiceCommand.RaiseCanExecuteChanged();
        StartRuntimeCommand.RaiseCanExecuteChanged();
        StopRuntimeCommand.RaiseCanExecuteChanged();
        ReleaseResourcesCommand.RaiseCanExecuteChanged();
        ResetRuntimeCommand.RaiseCanExecuteChanged();
        InspectResourceCommand.RaiseCanExecuteChanged();

        DetailActions.Clear();
        DetailActions.Add(new ActionItemViewModel("Refresh", "Refresh cached runtime resources and Docker ownership state.", true, string.Empty, RefreshCommand));
        DetailActions.Add(new ActionItemViewModel("Open Owning Workspace", "Open the workspace that owns the selected resource.", CanOpenOwningWorkspace(), string.Empty, OpenOwningWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Open Service", "Open the selected service endpoint if one is available.", CanOpenService(), string.Empty, OpenServiceCommand));
        DetailActions.Add(new ActionItemViewModel("Inspect", "Inspect the selected runtime resource or runtime-state entry.", CanInspectResource(), string.Empty, InspectResourceCommand));

        DetailAdvancedActions.Clear();
        DetailAdvancedActions.Add(new ActionItemViewModel("Start Runtime", "Start the owning workspace runtime.", CanRunWorkspaceAction(), string.Empty, StartRuntimeCommand));
        DetailAdvancedActions.Add(new ActionItemViewModel("Stop Runtime", "Stop the owning workspace runtime.", CanRunWorkspaceAction(), string.Empty, StopRuntimeCommand));
        DetailAdvancedActions.Add(new ActionItemViewModel("Release Resources", "Release managed Docker resources without unregistering the workspace.", CanRunWorkspaceAction(), string.Empty, ReleaseResourcesCommand));
        DetailAdvancedActions.Add(new ActionItemViewModel("Rebuild Runtime", "Recreate managed runtime resources for the owning workspace from a clean state.", CanRunWorkspaceAction(), string.Empty, ResetRuntimeCommand));
        DetailAdvancedActions.Add(new ActionItemViewModel("Clean Orphaned Resources", "Delete orphaned managed Docker resources that are safe to clean up.", OrphanedResources.Any(item => item.CanCleanUpSafely), string.Empty, CleanOrphanedResourcesCommand));
        ShowAdvancedActions = true;
    }

    private bool CanOpenOwningWorkspace()
        => NavigateToWorkspaceAsync is not null && !string.IsNullOrWhiteSpace(GetSelectedWorkspaceRootPath());

    private bool CanOpenService()
        => !string.IsNullOrWhiteSpace((_selectedResource ?? _selectedOrphanedResource)?.OpenUrl);

    private bool CanRunWorkspaceAction()
        => !string.IsNullOrWhiteSpace(GetSelectedWorkspaceRootPath());

    private bool CanInspectResource()
        => _selectedResource is not null || _selectedOrphanedResource is not null;

    private async Task OpenOwningWorkspaceAsync()
    {
        var rootPath = GetSelectedWorkspaceRootPath();
        if (NavigateToWorkspaceAsync is not null && !string.IsNullOrWhiteSpace(rootPath))
        {
            await NavigateToWorkspaceAsync(rootPath);
        }
    }

    private async Task OpenServiceAsync()
    {
        var url = (_selectedResource ?? _selectedOrphanedResource)?.OpenUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            await _desktopWorkspaceService.OpenPathAsync(url);
        }
    }

    private async Task StartRuntimeAsync()
    {
        var rootPath = GetSelectedWorkspaceRootPath();
        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            await _desktopWorkspaceService.StartWorkspaceAsync(rootPath);
            await RefreshAsync();
        }
    }

    private async Task StopRuntimeAsync()
    {
        var rootPath = GetSelectedWorkspaceRootPath();
        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            await _desktopWorkspaceService.StopWorkspaceAsync(rootPath);
            await RefreshAsync();
        }
    }

    private async Task ReleaseResourcesAsync()
    {
        var rootPath = GetSelectedWorkspaceRootPath();
        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            await _desktopWorkspaceService.ReleaseRuntimeResourcesAsync(rootPath);
            await RefreshAsync();
        }
    }

    private async Task ResetRuntimeAsync()
    {
        var rootPath = GetSelectedWorkspaceRootPath();
        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            await _desktopWorkspaceService.ResetRuntimeAsync(rootPath);
            await RefreshAsync();
        }
    }

    private async Task InspectSelectedResourceAsync()
    {
        var resource = _selectedResource ?? _selectedOrphanedResource;
        if (resource is null)
        {
            return;
        }

        var inspect = await _desktopWorkspaceService.InspectRuntimeResourceAsync(resource);
        DetailTitle = inspect.Title;
        DetailSummary = inspect.Summary;
        DetailItems.Clear();
        DetailItems.Add(new DetailItemViewModel("Inspect", inspect.Details));
        UpdateActions();
    }

    private async Task CleanOrphanedResourcesAsync()
    {
        await _desktopWorkspaceService.CleanOrphanedRuntimeResourcesAsync();
        await RefreshAsync();
    }

    private string GetSelectedWorkspaceRootPath()
        => _selectedWorkspace?.WorkspaceRootPath
            ?? _selectedResource?.WorkspaceRootPath
            ?? _selectedConflict?.WorkspaceRootPath
            ?? _selectedOrphanedResource?.WorkspaceRootPath
            ?? string.Empty;

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
