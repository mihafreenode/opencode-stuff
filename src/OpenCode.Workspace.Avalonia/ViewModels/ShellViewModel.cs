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
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = [];

    public PageViewModel CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                RaisePropertyChanged(nameof(StatusBarState));
                RaisePropertyChanged(nameof(StatusBarWorkspace));
                RaisePropertyChanged(nameof(StatusBarRuntime));
            }
        }
    }

    public string StatusBarBuild { get; }
    public string StatusBarState => $"Current page: {CurrentPage.Title}";
    public string StatusBarWorkspace => _workspacesPage.SelectedWorkspace is null ? "No workspace selected" : $"Workspace: {_workspacesPage.SelectedWorkspace.Name} ({_workspacesPage.SelectedWorkspace.StatusLabel})";
    public string StatusBarRuntime => _workspacesPage.SelectedWorkspace?.Snapshot.ResolvedRuntimePlan?.TargetPlatform is null ? "Runtime target unknown" : $"Runtime: {_workspacesPage.SelectedWorkspace.Snapshot.ResolvedRuntimePlan.TargetPlatform}";

    public static ShellViewModel Create(
        IDesktopShellService desktopShellService,
        IDiagnosticsShellService diagnosticsShellService,
        ITemplateCatalogShellService templateCatalogShellService,
        IDocumentationShellService documentationShellService,
        IThemeCoordinator themeCoordinator,
        AppBuildInfo appBuildInfo,
        string languageCode)
    {
        var workspacesPage = new WorkspacesPageViewModel(desktopShellService);
        workspacesPage.LoadAsync().GetAwaiter().GetResult();
        var diagnosticsPage = new DiagnosticsPageViewModel(diagnosticsShellService, desktopShellService.LoadWorkspaceReferences());
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

    private NavigationItemViewModel CreateNavigationItem(PageViewModel page)
        => new(page.Title, page, new RelayCommand(() => CurrentPage = page));

    private async Task ValidateWorkspaceFromOverviewAsync(string workspacePath)
    {
        CurrentPage = _diagnosticsPage;
        _diagnosticsPage.SelectedWorkspaceTarget = _diagnosticsPage.WorkspaceTargets.FirstOrDefault(item => string.Equals(item.RootPath, workspacePath, StringComparison.OrdinalIgnoreCase))
            ?? _diagnosticsPage.SelectedWorkspaceTarget;
        await _diagnosticsPage.RunDoctorAsync();
    }
}
