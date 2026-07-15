using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Smoke;

public sealed class WorkspaceSmokeApplicationService
{
    private readonly string _catalogRootPath;
    private readonly string _stateRootPath;
    private readonly IContainerRuntime _containerRuntime;

    public WorkspaceSmokeApplicationService(string catalogRootPath, string stateRootPath, IContainerRuntime containerRuntime)
    {
        _catalogRootPath = catalogRootPath;
        _stateRootPath = stateRootPath;
        _containerRuntime = containerRuntime;
    }

    public IReadOnlyList<WorkspaceSmokeDefinition> DiscoverDefinitions()
    {
        var provider = new BuiltInCatalogProvider(_catalogRootPath);
        return WorkspaceSmokeCatalog.BuildDefinitions(provider.LoadTemplates());
    }

    public async Task<WorkspaceSmokeResult> RunAsync(WorkspaceSmokeSingleRunRequest request, CancellationToken cancellationToken = default)
    {
        var definition = DiscoverDefinitions().SingleOrDefault(item => string.Equals(item.TemplateId, request.TemplateId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown smoke template '{request.TemplateId}'.");
        return await CreateRunner().RunAsync(definition, new WorkspaceSmokeRunnerOptions
        {
            ArtifactsRoot = request.ArtifactsRoot,
            WorkspaceRoot = request.WorkspaceRoot,
            DryRun = request.DryRun,
            KeepWorkspace = request.KeepWorkspace,
            KeepRuntimeOnFailure = request.KeepRuntimeOnFailure,
        }, cancellationToken);
    }

    public async Task<WorkspaceSmokeMatrixResult> RunMatrixAsync(WorkspaceSmokeMatrixRunRequest request, CancellationToken cancellationToken = default)
    {
        var definitions = DiscoverDefinitions();
        var selected = request.TemplateIds.Count == 0
            ? definitions
            : definitions.Where(item => request.TemplateIds.Contains(item.TemplateId, StringComparer.OrdinalIgnoreCase)).ToArray();
        var smokeOwnershipService = new SmokeRuntimeOwnershipService(_containerRuntime);
        var runtimeOwnershipService = new RuntimeOwnershipService(_containerRuntime);
        var matrixRunner = new WorkspaceSmokeMatrixRunner(CreateRunner(), smokeOwnershipService, runtimeOwnershipService);
        return await matrixRunner.RunAsync(selected, new WorkspaceSmokeMatrixRunnerOptions
        {
            ArtifactsRoot = request.ArtifactsRoot,
            ParallelCount = request.ParallelCount,
            KeepWorkspace = request.KeepWorkspace,
            KeepRuntimeOnFailure = request.KeepRuntimeOnFailure,
        }, cancellationToken);
    }

    private WorkspaceSmokeRunner CreateRunner()
        => new(
            new DefaultWorkspaceSmokeWorkspaceServiceFactory(_catalogRootPath, _stateRootPath, _containerRuntime),
            new DefaultWorkspaceSmokeValidatorProvider(),
            _containerRuntime,
            new RuntimeOwnershipService(_containerRuntime),
            new SmokeRuntimeOwnershipService(_containerRuntime));
}
