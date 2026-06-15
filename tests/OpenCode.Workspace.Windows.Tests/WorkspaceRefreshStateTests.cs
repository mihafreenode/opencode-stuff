using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Manager.Services;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class WorkspaceRefreshStateTests
{
    [Fact]
    public async Task StartupLoad_UsesSharedLoadingStateUntilWorkspaceLoadCompletes()
    {
        using var fixture = new WorkspaceRefreshFixture();
        await fixture.CreateWorkspaceAsync("alpha-workspace");
        var viewModel = fixture.CreateViewModel();

        Assert.True(viewModel.ShowWorkspaceLoadingState);
        Assert.False(viewModel.ShowOnboardingState);
        Assert.False(viewModel.ShowWorkspaceSidePanels);

        await viewModel.InitializeBackgroundAsync(new StartupDiagnosticsService(fixture.AppDataRoot));

        Assert.False(viewModel.ShowWorkspaceLoadingState);
        Assert.True(viewModel.ShowWorkspaceListState);
        Assert.True(viewModel.ShowWorkspaceSidePanels);
        Assert.Single(viewModel.Workspaces);
    }

    [Fact]
    public async Task ManualRefresh_PreservesSelectedWorkspaceAfterSuccessfulReload()
    {
        using var fixture = new WorkspaceRefreshFixture();
        await fixture.CreateWorkspaceAsync("alpha-workspace");
        await fixture.CreateWorkspaceAsync("beta-workspace");
        var viewModel = fixture.CreateViewModel();

        await viewModel.InitializeAsync();
        viewModel.SelectedWorkspace = viewModel.Workspaces.Single(item => item.Name == "beta-workspace");

        await viewModel.InitializeAsync();

        Assert.Equal("beta-workspace", viewModel.SelectedWorkspace?.Name);
        Assert.Equal(2, viewModel.Workspaces.Count);
        Assert.False(viewModel.ShowWorkspaceLoadingState);
        Assert.False(viewModel.WorkspaceListLoadFailed);
    }

    [Fact]
    public async Task BackgroundRefreshFailure_KeepsPreviousListAndShowsRetryBanner()
    {
        using var fixture = new WorkspaceRefreshFixture();
        await fixture.CreateWorkspaceAsync("alpha-workspace");
        var viewModel = fixture.CreateViewModel();

        await viewModel.InitializeBackgroundAsync(new StartupDiagnosticsService(fixture.AppDataRoot));
        var selectedRootPath = viewModel.SelectedWorkspace?.RootPath;

        File.WriteAllText(Path.Combine(fixture.AppDataRoot, "workspaces.json"), "not valid json");
        await viewModel.InitializeBackgroundAsync(new StartupDiagnosticsService(fixture.AppDataRoot));

        Assert.Single(viewModel.Workspaces);
        Assert.Equal(selectedRootPath, viewModel.SelectedWorkspace?.RootPath);
        Assert.True(viewModel.WorkspaceListLoadFailed);
        Assert.True(viewModel.ShowWorkspaceReloadErrorBanner);
        Assert.False(viewModel.ShowWorkspaceErrorState);
        Assert.True(viewModel.ShowWorkspaceListState);
    }

    [Fact]
    public async Task FailedStartupLoad_ShowsRetryErrorStateWithoutEmptyOnboarding()
    {
        using var fixture = new WorkspaceRefreshFixture();
        File.WriteAllText(Path.Combine(fixture.AppDataRoot, "workspaces.json"), "not valid json");
        var viewModel = fixture.CreateViewModel();

        await viewModel.InitializeBackgroundAsync(new StartupDiagnosticsService(fixture.AppDataRoot));

        Assert.Empty(viewModel.Workspaces);
        Assert.True(viewModel.WorkspaceListLoadFailed);
        Assert.True(viewModel.ShowWorkspaceErrorState);
        Assert.False(viewModel.ShowOnboardingState);
        Assert.False(viewModel.ShowWorkspaceListState);
        Assert.False(viewModel.ShowWorkspaceReloadErrorBanner);
    }

    private sealed class WorkspaceRefreshFixture : IDisposable
    {
        public WorkspaceRefreshFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"ocwm-refresh-{Guid.NewGuid():N}");
            AppDataRoot = Path.Combine(Root, "appdata");
            WorkspacesRoot = Path.Combine(Root, "workspaces");
            Directory.CreateDirectory(AppDataRoot);
            Directory.CreateDirectory(WorkspacesRoot);
        }

        public string Root { get; }

        public string AppDataRoot { get; }

        public string WorkspacesRoot { get; }

        public MainWindowViewModel CreateViewModel()
        {
            var bootstrapper = new AppBootstrapper();
            return bootstrapper.CreateMainWindowViewModel(TestPaths.RepositoryRoot, AppDataRoot, "en");
        }

        public async Task CreateWorkspaceAsync(string workspaceName)
        {
            var orchestrator = CreateOrchestrator();
            var rootPath = Path.Combine(WorkspacesRoot, workspaceName);
            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata
                {
                    Name = workspaceName,
                    Image = "ubuntu:24.04",
                },
                Features = ["core"],
                Services = [],
                Skills = [],
                Mcp = [],
            };

            await orchestrator.CreateWorkspaceAsync(rootPath, definition, includeRuntimeInspection: false);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private WorkspaceOrchestrator CreateOrchestrator()
        {
            var processRunner = new ProcessRunner();
            var catalogProvider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
            var ignorePolicyService = new WorkspaceIgnorePolicyService();
            return new WorkspaceOrchestrator(
                new WorkspaceYamlService(),
                new WorkspaceRepository(AppDataRoot),
                new WorkspaceResolver(catalogProvider.LoadFeatures(), catalogProvider.LoadServices()),
                new ComposeGenerator(),
                new EnvironmentFileGenerator(),
                new ProvisioningScriptGenerator(),
                new TerminalArtifactsGenerator(),
                new AttachArtifactsGenerator(),
                new WorkspaceContentGenerator(),
                new WorkspaceAppliedStateService(),
                new WorkspaceCheckpointService(),
                new WorkspaceTimelineService(),
                new WorkspaceSafetyService(),
                ignorePolicyService,
                new GitWorkspaceProvider(processRunner, ignorePolicyService),
                new DockerService(processRunner),
                new WindowsTerminalLauncher(new AttachCommandBuilder()));
        }
    }
}
