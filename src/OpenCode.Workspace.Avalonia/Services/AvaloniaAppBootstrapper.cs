using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Diagnostics;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Platform;
using OpenCode.Workspace.Platform.Linux;
using OpenCode.Workspace.Platform.MacOS;
using OpenCode.Workspace.Platform.Windows;
using OpenCode.Workspace.Avalonia.ViewModels;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class AvaloniaAppBootstrapper
{
    public ShellViewModel CreateShellViewModel(string applicationBasePath, string applicationDataRoot, string languageCode, IThemeCoordinator themeCoordinator)
    {
        var processRunner = new ProcessRunner();
        var catalogRoot = Path.Combine(applicationBasePath, "catalog");
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
        var savePointMessageService = new WorkspaceSavePointMessageService(processRunner);
        var ignorePolicyService = new WorkspaceIgnorePolicyService();
        var backupExportService = new WorkspaceBackupExportService(ignorePolicyService);
        var publishAssessmentService = new WorkspacePublishAssessmentService(processRunner);
        var removalService = new WorkspaceRemovalService(repository);
        var oracleSoftwareNoticeService = new OracleSoftwareNoticeService(repository);
        var commandProbe = new ProcessRunnerCommandProbe(processRunner);
        var hostCapabilities = new HostCapabilitiesFactory(
            () => new WindowsHostCapabilities(commandProbe),
            () => new LinuxHostCapabilities(commandProbe),
            () => new MacHostCapabilities(commandProbe))
            .CreateForCurrentPlatform();
        var windowsHostCapabilities = new WindowsHostCapabilities(commandProbe);
        var windowsTerminalProfileSetupService = new WindowsTerminalProfileSetupService(new WindowsTerminalProfileManager(), windowsHostCapabilities);
        var safetyService = new WorkspaceSafetyService();
        var workspaceProvider = new GitWorkspaceProvider(processRunner, ignorePolicyService);
        var dockerService = new DockerService(processRunner);
        var containerRuntime = new DockerContainerRuntime(dockerService);
        var platformDetector = new PlatformDetector(processRunner);
        var runtimeResolver = new RuntimeResolver();
        ITerminalLauncher terminalLauncher = OperatingSystem.IsWindows()
            ? new WindowsTerminalLauncher(new AttachCommandBuilder())
            : new PreviewTerminalLauncher();
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

        var desktopShellService = new DesktopShellService(orchestrator, repository, timelineService, checkpointService, savePointMessageService, backupExportService, publishAssessmentService, removalService, oracleSoftwareNoticeService, windowsTerminalProfileSetupService);
        var doctorService = new WorkspaceDoctorService(platformDetector, runtimeResolver, new WorkspaceDiscoveryService(), yamlService, new WorkspaceRuntimeStateService());
        var validationService = new PlatformValidationService(new WorkspaceDiscoveryService(), yamlService, platformDetector, runtimeResolver, resolver, composeGenerator, provisioningScriptGenerator);
        var diagnosticsShellService = new DiagnosticsShellService(doctorService, validationService, hostCapabilities);
        var templateShellService = new TemplateCatalogShellService(catalogProvider);
        var documentationShellService = new DocumentationShellService(applicationBasePath, desktopShellService);
        var appBuildInfo = new AppBuildInfoService(applicationBasePath).GetCurrent();

        return ShellViewModel.Create(
            desktopShellService,
            diagnosticsShellService,
            hostCapabilities,
            templateShellService,
            documentationShellService,
            themeCoordinator,
            appBuildInfo,
            languageCode);
    }
}
