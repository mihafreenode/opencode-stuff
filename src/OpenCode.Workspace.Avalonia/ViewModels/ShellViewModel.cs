using System.Collections.ObjectModel;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private PageViewModel _currentPage;
    private readonly WorkspacesPageViewModel _workspacesPage;
    private readonly DiagnosticsPageViewModel _diagnosticsPage;
    private readonly SettingsPageViewModel _settingsPage;

    private ShellViewModel(
        WorkspacesPageViewModel workspacesPage,
        DiagnosticsPageViewModel diagnosticsPage,
        TemplatesPageViewModel templatesPage,
        SavePointsPageViewModel savePointsPage,
        TranscriptsPageViewModel transcriptsPage,
        RemoteTargetsPageViewModel remoteTargetsPage,
        DocumentationPageViewModel documentationPage,
        SettingsPageViewModel settingsPage,
        AppBuildInfo appBuildInfo)
    {
        _workspacesPage = workspacesPage;
        _diagnosticsPage = diagnosticsPage;
        _settingsPage = settingsPage;
        _currentPage = workspacesPage;
        StatusBarBuild = $"{appBuildInfo.BuildConfiguration} {appBuildInfo.AssemblyVersion}";

        workspacesPage.ValidateWorkspaceAsync = ValidateWorkspaceFromOverviewAsync;
        workspacesPage.PropertyChanged += (_, _) => RefreshStatusBar();
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
            diagnosticsPage,
            new TemplatesPageViewModel(templateCatalogShellService),
            new SavePointsPageViewModel(desktopShellService),
            new TranscriptsPageViewModel(desktopShellService),
            new RemoteTargetsPageViewModel(),
            new DocumentationPageViewModel(documentationShellService),
            new SettingsPageViewModel(themeCoordinator, appBuildInfo),
            appBuildInfo);

        return shell;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _workspacesPage.LoadAsync(cancellationToken);
        _diagnosticsPage.RefreshWorkspaceLoadSummary();
        RefreshStatusBar();
    }

    public void SetClipboardService(IClipboardService clipboardService)
    {
        _workspacesPage.SetClipboardService(clipboardService);
    }

    public void SetInteractionService(IWorkspaceInteractionService interactionService)
    {
        _workspacesPage.SetInteractionService(interactionService);
    }

    private NavigationItemViewModel CreateNavigationItem(PageViewModel page)
        => new(page.Title, page, new RelayCommand(() => CurrentPage = page));

    private async Task ValidateWorkspaceFromOverviewAsync(string workspacePath)
    {
        CurrentPage = _diagnosticsPage;
        _diagnosticsPage.SelectedWorkspaceTarget = _diagnosticsPage.WorkspaceTargets.FirstOrDefault(item => string.Equals(item.RootPath, workspacePath, StringComparison.OrdinalIgnoreCase))
            ?? _diagnosticsPage.SelectedWorkspaceTarget;
        await _diagnosticsPage.RunDoctorAsync();
        RefreshStatusBar();
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
