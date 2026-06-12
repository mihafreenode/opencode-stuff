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
        var resolver = new WorkspaceResolver(catalogProvider.LoadFeatures(), catalogProvider.LoadServices());
        var environmentFileGenerator = new EnvironmentFileGenerator();
        var composeGenerator = new ComposeGenerator();
        var provisioningScriptGenerator = new ProvisioningScriptGenerator();
        var terminalArtifactsGenerator = new TerminalArtifactsGenerator();
        var attachArtifactsGenerator = new AttachArtifactsGenerator();
        var appliedStateService = new WorkspaceAppliedStateService();
        var dockerService = new DockerService(processRunner);
        var terminalLauncher = new WindowsTerminalLauncher(new AttachCommandBuilder());
        var profileManager = new WindowsTerminalProfileManager();
        var hostCapabilities = new WindowsHostCapabilities(processRunner);
        var nerdFontInstaller = new NerdFontInstaller(processRunner);
        var orchestrator = new WorkspaceOrchestrator(
            yamlService,
            repository,
            resolver,
            composeGenerator,
            environmentFileGenerator,
            provisioningScriptGenerator,
            terminalArtifactsGenerator,
            attachArtifactsGenerator,
            appliedStateService,
            dockerService,
            terminalLauncher);

        var localization = new PoLocalizationService(localizationRoot, languageCode);
        var diagnostics = new EnvironmentDiagnostics(processRunner);
        return new MainWindowViewModel(orchestrator, catalogProvider, diagnostics, localization, hostCapabilities, profileManager, dockerService, nerdFontInstaller);
    }
}
