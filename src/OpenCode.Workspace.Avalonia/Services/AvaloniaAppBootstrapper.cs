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
        var services = new WorkspaceDesktopServiceFactory().Create(applicationBasePath, applicationDataRoot);
        var commandProbe = new ProcessRunnerCommandProbe(services.ProcessRunner);
        var hostCapabilities = new HostCapabilitiesFactory(
            () => new WindowsHostCapabilities(commandProbe),
            () => new LinuxHostCapabilities(commandProbe),
            () => new MacHostCapabilities(commandProbe))
            .CreateForCurrentPlatform();
        var windowsHostCapabilities = new WindowsHostCapabilities(commandProbe);
        var windowsTerminalProfileSetupService = new WindowsTerminalProfileSetupService(new WindowsTerminalProfileManager(), windowsHostCapabilities);
        var desktopShellService = new DesktopShellService(
            services.WorkspaceOrchestrator,
            services.Repository,
            services.TimelineService,
            services.CheckpointService,
            services.SavePointMessageService,
            services.BackupExportService,
            services.BackupManifestService,
            services.PublishAssessmentService,
            services.RemovalService,
            services.OracleSoftwareNoticeService,
            windowsTerminalProfileSetupService);
        var doctorService = new WorkspaceDoctorService(services.PlatformDetector, services.RuntimeResolver, new WorkspaceDiscoveryService(), new WorkspaceYamlService(), new WorkspaceRuntimeStateService());
        var validationService = new PlatformValidationService(new WorkspaceDiscoveryService(), new WorkspaceYamlService(), services.PlatformDetector, services.RuntimeResolver, new WorkspaceResolver(services.CatalogProvider.LoadFeatures(), services.CatalogProvider.LoadServices(), services.CatalogProvider.LoadCapabilities(), services.CatalogProvider.LoadKnowledgePacks()), new ComposeGenerator(), new ProvisioningScriptGenerator());
        var diagnosticsShellService = new DiagnosticsShellService(doctorService, validationService, hostCapabilities, services.CatalogProvider);
        var templateShellService = new TemplateCatalogShellService(services.CatalogProvider);
        var documentationShellService = new DocumentationShellService(services.InstallationLayout.DistributionRoot, desktopShellService);
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
