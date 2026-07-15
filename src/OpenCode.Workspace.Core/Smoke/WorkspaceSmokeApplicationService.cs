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

    public Task<WorkspaceSmokeDefinitionCatalogResult> ListDefinitionsAsync(WorkspaceSmokeDefinitionQuery? query = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definitions = FilterDefinitions(DiscoverDefinitions(), query);
        return Task.FromResult(new WorkspaceSmokeDefinitionCatalogResult
        {
            Definitions = definitions,
        });
    }

    public Task<IReadOnlyList<WorkspaceSmokeDefinition>> SelectDefinitionsAsync(WorkspaceSmokeDefinitionSelectionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definitions = DiscoverDefinitions();
        if (request.All)
        {
            return Task.FromResult<IReadOnlyList<WorkspaceSmokeDefinition>>(definitions
                .OrderBy(item => item.TemplateId, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        }

        if (!string.IsNullOrWhiteSpace(request.Family))
        {
            var selected = definitions
                .Where(item => string.Equals(item.Family, request.Family, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.TemplateId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (selected.Length == 0)
            {
                throw new WorkspaceSmokeSelectionException($"Unknown smoke family '{request.Family}'.");
            }

            return Task.FromResult<IReadOnlyList<WorkspaceSmokeDefinition>>(selected);
        }

        return Task.FromResult(ResolveDefinitions(definitions, request.TemplateIds));
    }

    public async Task<WorkspaceSmokeResult> RunAsync(WorkspaceSmokeSingleRunRequest request, CancellationToken cancellationToken = default)
    {
        var definition = ResolveDefinition(request.TemplateId);
        return await CreateRunner().RunAsync(definition, new WorkspaceSmokeRunnerOptions
        {
            ArtifactsRoot = request.ArtifactsRoot,
            WorkspaceRoot = request.WorkspaceRoot,
            DryRun = request.DryRun,
            KeepWorkspace = request.KeepWorkspace,
            KeepRuntimeOnFailure = request.KeepRuntimeOnFailure,
            Timeout = request.Timeout,
        }, cancellationToken);
    }

    public async Task<WorkspaceSmokeMatrixResult> RunMatrixAsync(WorkspaceSmokeMatrixRunRequest request, CancellationToken cancellationToken = default)
    {
        var definitions = DiscoverDefinitions();
        var selected = ResolveDefinitions(definitions, request.TemplateIds);
        var smokeOwnershipService = new SmokeRuntimeOwnershipService(_containerRuntime);
        var runtimeOwnershipService = new RuntimeOwnershipService(_containerRuntime);
        var matrixRunner = new WorkspaceSmokeMatrixRunner(CreateRunner(), smokeOwnershipService, runtimeOwnershipService);
        return await matrixRunner.RunAsync(selected, new WorkspaceSmokeMatrixRunnerOptions
        {
            ArtifactsRoot = request.ArtifactsRoot,
            ParallelCount = request.ParallelCount,
            KeepWorkspace = request.KeepWorkspace,
            KeepRuntimeOnFailure = request.KeepRuntimeOnFailure,
            RunTimeoutOverride = request.RunTimeoutOverride,
            MatrixTimeout = request.MatrixTimeout,
        }, cancellationToken);
    }

    private WorkspaceSmokeDefinition ResolveDefinition(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new WorkspaceSmokeSelectionException("A smoke template id is required.");
        }

        return DiscoverDefinitions().SingleOrDefault(item => string.Equals(item.TemplateId, templateId, StringComparison.OrdinalIgnoreCase))
            ?? throw new WorkspaceSmokeSelectionException($"Unknown smoke template '{templateId}'.");
    }

    private static IReadOnlyList<WorkspaceSmokeDefinition> ResolveDefinitions(IReadOnlyList<WorkspaceSmokeDefinition> definitions, IReadOnlyList<string> templateIds)
    {
        if (templateIds.Count == 0)
        {
            throw new WorkspaceSmokeSelectionException("Smoke selection is empty.");
        }

        var selected = new List<WorkspaceSmokeDefinition>();
        var missing = new List<string>();
        foreach (var templateId in templateIds)
        {
            var definition = definitions.SingleOrDefault(item => string.Equals(item.TemplateId, templateId, StringComparison.OrdinalIgnoreCase));
            if (definition is null)
            {
                missing.Add(templateId);
                continue;
            }

            selected.Add(definition);
        }

        if (missing.Count > 0)
        {
            throw new WorkspaceSmokeSelectionException($"Unknown smoke template(s): {string.Join(", ", missing)}.");
        }

        if (selected.Count == 0)
        {
            throw new WorkspaceSmokeSelectionException("Smoke selection is empty.");
        }

        return selected;
    }

    private static IReadOnlyList<WorkspaceSmokeDefinition> FilterDefinitions(IReadOnlyList<WorkspaceSmokeDefinition> definitions, WorkspaceSmokeDefinitionQuery? query)
    {
        if (string.IsNullOrWhiteSpace(query?.Family))
        {
            return definitions;
        }

        return definitions
            .Where(item => string.Equals(item.Family, query.Family, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.TemplateId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private WorkspaceSmokeRunner CreateRunner()
        => new(
            new DefaultWorkspaceSmokeWorkspaceServiceFactory(_catalogRootPath, _stateRootPath, _containerRuntime),
            new DefaultWorkspaceSmokeValidatorProvider(),
            _containerRuntime,
            new RuntimeOwnershipService(_containerRuntime),
            new SmokeRuntimeOwnershipService(_containerRuntime));
}

public sealed class WorkspaceSmokeSelectionException : ArgumentException
{
    public WorkspaceSmokeSelectionException(string message)
        : base(message)
    {
    }
}
