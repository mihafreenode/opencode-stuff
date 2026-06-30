using System.Collections.ObjectModel;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Platform;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private PageViewModel _currentPage;
    private readonly IDesktopShellService _desktopShellService;
    private readonly WorkspacesPageViewModel _workspacesPage;
    private readonly SavePointsPageViewModel _savePointsPage;
    private readonly DiagnosticsPageViewModel _diagnosticsPage;
    private readonly WorkspaceTroubleshootingPageViewModel _workspaceTroubleshootingPage;
    private readonly SettingsPageViewModel _settingsPage;

    private ShellViewModel(
        WorkspacesPageViewModel workspacesPage,
        IDesktopShellService desktopShellService,
        WorkspaceTroubleshootingPageViewModel workspaceTroubleshootingPage,
        DiagnosticsPageViewModel diagnosticsPage,
        TemplatesPageViewModel templatesPage,
        SavePointsPageViewModel savePointsPage,
        TranscriptsPageViewModel transcriptsPage,
        RemoteTargetsPageViewModel remoteTargetsPage,
        DocumentationPageViewModel documentationPage,
        SettingsPageViewModel settingsPage,
        AppBuildInfo appBuildInfo)
    {
        _desktopShellService = desktopShellService;
        _workspacesPage = workspacesPage;
        _savePointsPage = savePointsPage;
        _diagnosticsPage = diagnosticsPage;
        _workspaceTroubleshootingPage = workspaceTroubleshootingPage;
        _settingsPage = settingsPage;
        _currentPage = workspacesPage;
        StatusBarBuild = $"{appBuildInfo.BuildConfiguration} {appBuildInfo.AssemblyVersion}";

        workspacesPage.TroubleshootWorkspaceAsync = TroubleshootWorkspaceFromOverviewAsync;
        workspacesPage.PropertyChanged += (_, eventArgs) =>
        {
            RefreshStatusBar();
            if (eventArgs.PropertyName == nameof(WorkspacesPageViewModel.SelectedWorkspace))
            {
                _ = _savePointsPage.RefreshAsync(workspacesPage.SelectedWorkspace);
                _settingsPage.RefreshWorkspaceContext();
            }
        };
        diagnosticsPage.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName is nameof(DiagnosticsPageViewModel.StatusMessage)
                or nameof(DiagnosticsPageViewModel.SelectedWorkspaceTarget)
                or nameof(DiagnosticsPageViewModel.SelectedDoctorItem)
                or nameof(DiagnosticsPageViewModel.SelectedValidationItem))
            {
                RefreshStatusBar();
            }
        };

        NavigationItems =
        [
            CreateNavigationItem(workspacesPage),
            CreateNavigationItem(remoteTargetsPage),
            CreateNavigationItem(templatesPage),
            CreateNavigationItem(savePointsPage),
            CreateNavigationItem(transcriptsPage),
            CreateNavigationItem(diagnosticsPage),
            CreateNavigationItem(documentationPage),
            CreateNavigationItem(settingsPage),
        ];

        RefreshStatusBar();
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = [];

    public PageViewModel CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                RefreshStatusBar();
            }
        }
    }

    public string StatusBarBuild { get; }
    public WorkspaceLoadReport WorkspaceLoadReport => _workspacesPage.WorkspaceLoadReport;
    public string HeaderWorkspaceCount => WorkspaceLoadReport.RawRecordCount.ToString();
    public string HeaderRuntimeSummary => _workspacesPage.SelectedWorkspace is null || string.Equals(_workspacesPage.SelectedWorkspace.RuntimeTarget, "Unavailable", StringComparison.Ordinal)
        ? "Unknown"
        : _workspacesPage.SelectedWorkspace.RuntimeTarget;
    public string HeaderStatusSummary => _workspacesPage.IsLoading
        ? "Loading"
        : _workspacesPage.IsReprovisioning
            ? "Busy"
            : _workspacesPage.HasLoadError || WorkspaceLoadReport.FailureCount > 0
                ? "Issues"
                : "Ready";
    public string StatusBarState => CurrentPage == _diagnosticsPage && !string.IsNullOrWhiteSpace(_diagnosticsPage.StatusMessage)
        ? $"Diagnostics: {_diagnosticsPage.StatusMessage}"
        : $"Current page: {CurrentPage.Title}";
    public string StatusBarWorkspace => _workspacesPage.SelectedWorkspace is null ? "No workspace selected" : $"Workspace: {_workspacesPage.SelectedWorkspace.Name}";
    public string StatusBarBranch => _workspacesPage.SelectedWorkspace is null ? "Branch unknown" : $"Branch: {_workspacesPage.SelectedWorkspace.CurrentBranch}";
    public string StatusBarRuntime => _workspacesPage.SelectedWorkspace is null || string.Equals(_workspacesPage.SelectedWorkspace.RuntimeTarget, "Unavailable", StringComparison.Ordinal) ? "Runtime target unknown" : $"Runtime: {_workspacesPage.SelectedWorkspace.RuntimeTarget}";
    public string StatusBarProtection => _workspacesPage.SelectedWorkspace is null ? "Protection unknown" : $"Protection: {_workspacesPage.SelectedWorkspace.ProtectionLabel}";

    public static ShellViewModel Create(
        IDesktopShellService desktopShellService,
        IDiagnosticsShellService diagnosticsShellService,
        IHostCapabilities hostCapabilities,
        ITemplateCatalogShellService templateCatalogShellService,
        IDocumentationShellService documentationShellService,
        IThemeCoordinator themeCoordinator,
        AppBuildInfo appBuildInfo,
        string languageCode)
    {
        var templates = templateCatalogShellService.LoadTemplates();
        var workspacesPage = new WorkspacesPageViewModel(desktopShellService, templates);
        var diagnosticsPage = new DiagnosticsPageViewModel(diagnosticsShellService, desktopShellService.LoadWorkspaceReferences(), () => workspacesPage.WorkspaceLoadReport);
        var shell = new ShellViewModel(
            workspacesPage,
            desktopShellService,
            new WorkspaceTroubleshootingPageViewModel(),
            diagnosticsPage,
            new TemplatesPageViewModel(templateCatalogShellService),
            new SavePointsPageViewModel(desktopShellService),
            new TranscriptsPageViewModel(desktopShellService),
            new RemoteTargetsPageViewModel(),
            new DocumentationPageViewModel(documentationShellService),
            new SettingsPageViewModel(themeCoordinator, appBuildInfo, desktopShellService, hostCapabilities, () => workspacesPage.SelectedWorkspace),
            appBuildInfo);

        return shell;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _workspacesPage.LoadAsync(cancellationToken);
        await _savePointsPage.RefreshAsync(_workspacesPage.SelectedWorkspace, cancellationToken);
        await _settingsPage.LoadHostCapabilitiesAsync(cancellationToken);
        _diagnosticsPage.RefreshWorkspaceLoadSummary();
        RefreshStatusBar();
    }

    public void SetClipboardService(IClipboardService clipboardService)
    {
        _workspacesPage.SetClipboardService(clipboardService);
        _savePointsPage.SetClipboardService(clipboardService);
        _diagnosticsPage.SetClipboardService(clipboardService);
    }

    public void SetInteractionService(IWorkspaceInteractionService interactionService)
    {
        _workspacesPage.SetInteractionService(interactionService);
    }

    private NavigationItemViewModel CreateNavigationItem(PageViewModel page)
        => new(page.Title, page, new RelayCommand(() => CurrentPage = page));

    private async Task TroubleshootWorkspaceFromOverviewAsync(string workspacePath)
    {
        var request = BuildWorkspaceTroubleshootingRequest(workspacePath);
        var report = await _desktopShellService.GetWorkspaceTroubleshootingReportAsync(request);
        ShowWorkspaceTroubleshootingReport(report, request);
    }

    private WorkspaceTroubleshootingRequest BuildWorkspaceTroubleshootingRequest(string workspacePath)
    {
        var selectedWorkspace = _workspacesPage.SelectedWorkspace;
        return new WorkspaceTroubleshootingRequest
        {
            RootPath = workspacePath,
            Snapshot = selectedWorkspace?.Snapshot,
            WorkspaceName = selectedWorkspace?.Name ?? string.Empty,
            IsOperationInProgress = _workspacesPage.HasActiveWorkspaceOperation,
            CurrentOperationName = _workspacesPage.CurrentWorkspaceOperationName,
            CurrentStatusMessage = _workspacesPage.CurrentWorkspaceOperationStatus,
            TranscriptFilePath = _workspacesPage.CurrentOperationTranscriptFilePath ?? string.Empty,
        };
    }

    private void ShowWorkspaceTroubleshootingReport(WorkspaceTroubleshootingReport report, WorkspaceTroubleshootingRequest request)
    {
        _workspaceTroubleshootingPage.ShowReport(
            report,
            CreateTroubleshootingPrimaryAction(report),
            CreateTroubleshootingVisibleActions(report),
            CreateTroubleshootingAdvancedActions(report),
            CreateTroubleshootingInvestigationActions(report, request));
        CurrentPage = _workspaceTroubleshootingPage;
        RefreshStatusBar();
    }

    private ActionItemViewModel? CreateTroubleshootingPrimaryAction(WorkspaceTroubleshootingReport report)
    {
        if (report.IsProvisioningInProgress && report.CanKeepWaiting)
        {
            return CreateWorkspaceTroubleshootingAction("Keep Waiting", "Return to the workspace and keep streaming the active operation log.", true, string.Empty, KeepWaitingForWorkspaceAsync);
        }

        if (report.CanOpenWorkspace)
        {
            return CreateWorkspaceTroubleshootingAction("Open Workspace", "Retry the intent-based workspace flow with safe repair steps.", true, string.Empty, OpenWorkspaceFromTroubleshootingAsync);
        }

        if (report.RecommendHostDiagnostics)
        {
            return CreateWorkspaceTroubleshootingAction("Run Host Diagnostics", "Open the generic Diagnostics page for host-level checks.", true, string.Empty, OpenHostDiagnosticsAsync);
        }

        return null;
    }

    private IReadOnlyList<ActionItemViewModel> CreateTroubleshootingVisibleActions(WorkspaceTroubleshootingReport report)
    {
        var actions = new List<ActionItemViewModel>();

        if (report.CanViewLog)
        {
            actions.Add(CreateWorkspaceTroubleshootingAction("View Log", "Return to the workspace and focus the streamed operation log.", true, string.Empty, ViewTroubleshootingLogAsync));
        }

        actions.Add(CreateWorkspaceTroubleshootingAction("Open Folder", "Open the workspace folder with the host shell.", true, string.Empty, OpenTroubleshootingWorkspaceFolderAsync));
        return actions;
    }

    private IReadOnlyList<ActionItemViewModel> CreateTroubleshootingAdvancedActions(WorkspaceTroubleshootingReport report)
    {
        var actions = new List<ActionItemViewModel>();
        if (report.CanRecoverWorkspace)
        {
            actions.Add(CreateWorkspaceTroubleshootingAction("Recover Workspace", "Repair generated runtime files and validate the runtime without deleting user work.", true, string.Empty, RecoverWorkspaceFromTroubleshootingAsync));
        }

        if (report.CanResetRuntime)
        {
            actions.Add(CreateWorkspaceTroubleshootingAction("Reset Runtime", "Delete managed runtime resources and reprovision from a clean state after confirmation.", true, string.Empty, ResetRuntimeFromTroubleshootingAsync));
        }

        if (report.RecommendHostDiagnostics)
        {
            actions.Add(CreateWorkspaceTroubleshootingAction("Run Host Diagnostics", "Open the generic Diagnostics page for host-level checks.", true, string.Empty, OpenHostDiagnosticsAsync));
        }

        return actions;
    }

    private IReadOnlyList<ActionItemViewModel> CreateTroubleshootingInvestigationActions(WorkspaceTroubleshootingReport report, WorkspaceTroubleshootingRequest request)
        => report.InvestigationActions
            .Select(action => CreateWorkspaceTroubleshootingAction(
                action.Label,
                string.IsNullOrWhiteSpace(action.EstimatedDuration)
                    ? action.Description
                    : $"{action.Description} Estimated time: {action.EstimatedDuration}.",
                true,
                string.Empty,
                () => RunWorkspaceInvestigationAsync(request, action.Id)))
            .ToList();

    private ActionItemViewModel CreateWorkspaceTroubleshootingAction(string label, string description, bool isEnabled, string disabledReason, Func<Task> executeAsync)
        => new(label, description, isEnabled, disabledReason, new AsyncRelayCommand(executeAsync));

    private Task KeepWaitingForWorkspaceAsync()
    {
        CurrentPage = _workspacesPage;
        return Task.CompletedTask;
    }

    private async Task ViewTroubleshootingLogAsync()
    {
        CurrentPage = _workspacesPage;
        if (!_workspacesPage.IsOperationLogVisible)
        {
            _workspacesPage.ToggleOperationLogVisibilityCommand.Execute(null);
        }

        await Task.CompletedTask;
    }

    private Task OpenTroubleshootingWorkspaceFolderAsync()
    {
        CurrentPage = _workspacesPage;
        _workspacesPage.OpenWorkspaceFolderCommand.Execute(null);
        return Task.CompletedTask;
    }

    private async Task OpenWorkspaceFromTroubleshootingAsync()
    {
        CurrentPage = _workspacesPage;
        await _workspacesPage.OpenSelectedWorkspaceCommand.ExecuteAsync();
    }

    private async Task RecoverWorkspaceFromTroubleshootingAsync()
    {
        CurrentPage = _workspacesPage;
        await _workspacesPage.RecoverWorkspaceCommand.ExecuteAsync();
    }

    private async Task ResetRuntimeFromTroubleshootingAsync()
    {
        CurrentPage = _workspacesPage;
        await _workspacesPage.ResetRuntimeCommand.ExecuteAsync();
    }

    private async Task OpenHostDiagnosticsAsync()
    {
        CurrentPage = _diagnosticsPage;
        _diagnosticsPage.SelectedWorkspaceTarget = _diagnosticsPage.WorkspaceTargets.FirstOrDefault(item => string.Equals(item.RootPath, _workspaceTroubleshootingPage.WorkspaceRootPath, StringComparison.OrdinalIgnoreCase))
            ?? _diagnosticsPage.SelectedWorkspaceTarget;
        await _diagnosticsPage.RunDoctorAsync();
    }

    private async Task RunWorkspaceInvestigationAsync(WorkspaceTroubleshootingRequest request, string actionId)
    {
        var updatedRequest = BuildWorkspaceTroubleshootingRequest(request.RootPath);
        var report = await _desktopShellService.ExecuteWorkspaceTroubleshootingActionAsync(updatedRequest, actionId);
        ShowWorkspaceTroubleshootingReport(report, updatedRequest);
    }

    private void RefreshStatusBar()
    {
        foreach (var item in NavigationItems)
        {
            item.IsSelected = ReferenceEquals(item.Page, CurrentPage);
        }

        RaisePropertyChanged(nameof(StatusBarState));
        RaisePropertyChanged(nameof(StatusBarWorkspace));
        RaisePropertyChanged(nameof(StatusBarBranch));
        RaisePropertyChanged(nameof(StatusBarRuntime));
        RaisePropertyChanged(nameof(StatusBarProtection));
        RaisePropertyChanged(nameof(HeaderWorkspaceCount));
        RaisePropertyChanged(nameof(HeaderRuntimeSummary));
        RaisePropertyChanged(nameof(HeaderStatusSummary));
    }
}
