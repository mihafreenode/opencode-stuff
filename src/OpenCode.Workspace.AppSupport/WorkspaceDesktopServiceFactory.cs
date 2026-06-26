using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.AppSupport;

public sealed class WorkspaceDesktopServiceFactory
{
    public WorkspaceDesktopServices Create(string applicationBasePath, string applicationDataRoot)
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
        var safetyService = new WorkspaceSafetyService();
        var ignorePolicyService = new WorkspaceIgnorePolicyService();
        var workspaceProvider = new GitWorkspaceProvider(processRunner, ignorePolicyService);
        var dockerService = new DockerService(processRunner);
        var containerRuntime = new DockerContainerRuntime(dockerService);
        var platformDetector = new PlatformDetector(processRunner);
        var runtimeResolver = new RuntimeResolver();

        return new WorkspaceDesktopServices
        {
            ProcessRunner = processRunner,
            CatalogProvider = catalogProvider,
            Repository = repository,
            CheckpointService = checkpointService,
            TimelineService = timelineService,
            SavePointMessageService = new WorkspaceSavePointMessageService(processRunner),
            BackupExportService = new WorkspaceBackupExportService(ignorePolicyService),
            BackupManifestService = new WorkspaceBackupManifestService(),
            PublishAssessmentService = new WorkspacePublishAssessmentService(processRunner),
            RemovalService = new WorkspaceRemovalService(repository),
            OracleSoftwareNoticeService = new OracleSoftwareNoticeService(repository),
            ContainerRuntime = containerRuntime,
            PlatformDetector = platformDetector,
            RuntimeResolver = runtimeResolver,
            WorkspaceOrchestrator = new WorkspaceOrchestrator(
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
                new WindowsTerminalLauncher(new AttachCommandBuilder())),
        };
    }
}

public sealed class WorkspaceDesktopServices
{
    public required ProcessRunner ProcessRunner { get; init; }
    public required BuiltInCatalogProvider CatalogProvider { get; init; }
    public required WorkspaceRepository Repository { get; init; }
    public required WorkspaceOrchestrator WorkspaceOrchestrator { get; init; }
    public required WorkspaceCheckpointService CheckpointService { get; init; }
    public required WorkspaceTimelineService TimelineService { get; init; }
    public required WorkspaceSavePointMessageService SavePointMessageService { get; init; }
    public required WorkspaceBackupExportService BackupExportService { get; init; }
    public required WorkspaceBackupManifestService BackupManifestService { get; init; }
    public required WorkspacePublishAssessmentService PublishAssessmentService { get; init; }
    public required WorkspaceRemovalService RemovalService { get; init; }
    public required OracleSoftwareNoticeService OracleSoftwareNoticeService { get; init; }
    public required IContainerRuntime ContainerRuntime { get; init; }
    public required PlatformDetector PlatformDetector { get; init; }
    public required RuntimeResolver RuntimeResolver { get; init; }
}
