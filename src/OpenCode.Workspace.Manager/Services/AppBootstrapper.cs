using System.IO;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Manager.Services;

/// <summary>
/// The app bootstrap graph is kept explicit so tests can validate Windows-host
/// startup behavior without having to instantiate the full WPF Application type.
/// </summary>
public sealed class AppBootstrapper
{
    public MainWindowViewModel CreateMainWindowViewModel(string applicationBasePath, string applicationDataRoot, string languageCode)
    {
        var processRunner = new ProcessRunner();
        var catalogRoot = Path.Combine(applicationBasePath, "catalog");
        var localizationRoot = Path.Combine(applicationBasePath, "Localization");

        var catalogProvider = new BuiltInCatalogProvider(catalogRoot);
        var yamlService = new WorkspaceYamlService();
        var repository = new WorkspaceRepository(applicationDataRoot);
        var resolver = new WorkspaceResolver(catalogProvider.LoadFeatures(), catalogProvider.LoadServices(), catalogProvider.LoadCapabilities(), catalogProvider.LoadKnowledgePacks());
        var environmentFileGenerator = new EnvironmentFileGenerator();
        var composeGenerator = new ComposeGenerator();
        var provisioningScriptGenerator = new ProvisioningScriptGenerator();
        var terminalArtifactsGenerator = new TerminalArtifactsGenerator();
        var attachArtifactsGenerator = new AttachArtifactsGenerator();
        var workspaceContentGenerator = new WorkspaceContentGenerator();
        var appliedStateService = new WorkspaceAppliedStateService();
        var checkpointService = new WorkspaceCheckpointService();
        var timelineService = new WorkspaceTimelineService();
        var safetyService = new WorkspaceSafetyService();
        var ignorePolicyService = new WorkspaceIgnorePolicyService();
        var workspaceProvider = new GitWorkspaceProvider(processRunner, ignorePolicyService);
        var dockerService = new DockerService(processRunner);
        var containerRuntime = new DockerContainerRuntime(dockerService);
        var platformDetector = new PlatformDetector(processRunner);
        var runtimeResolver = new RuntimeResolver();
        var terminalLauncher = new WindowsTerminalLauncher(new AttachCommandBuilder());
        var profileManager = new WindowsTerminalProfileManager();
        var hostCapabilities = new WindowsHostCapabilities(processRunner);
        var nerdFontInstaller = new NerdFontInstaller(processRunner);
        var savePointMessageService = new WorkspaceSavePointMessageService(processRunner);
        var tutorialService = new QuickTutorialService(applicationBasePath, applicationDataRoot);
        var tmpReprovisionWorkflowService = new TmpReprovisionWorkflowService(applicationBasePath, processRunner);
        var appBuildInfoService = new AppBuildInfoService(applicationBasePath);
        var orchestrator = new WorkspaceOrchestrator(
            yamlService,
            new WorkspaceDiscoveryService(),
            repository,
            resolver,
            composeGenerator,
            environmentFileGenerator,
            provisioningScriptGenerator,
            terminalArtifactsGenerator,
            attachArtifactsGenerator,
            workspaceContentGenerator,
            appliedStateService,
            checkpointService,
            timelineService,
            safetyService,
            ignorePolicyService,
            new WorkspaceRuntimeStateService(),
            workspaceProvider,
            containerRuntime,
            platformDetector,
            runtimeResolver,
            terminalLauncher);

        var localization = new PoLocalizationService(localizationRoot, languageCode);
        var diagnostics = new EnvironmentDiagnostics(processRunner);
        return new MainWindowViewModel(orchestrator, catalogProvider, diagnostics, localization, hostCapabilities, profileManager, containerRuntime, nerdFontInstaller, savePointMessageService, tutorialService, tmpReprovisionWorkflowService, appBuildInfoService);
    }
}
