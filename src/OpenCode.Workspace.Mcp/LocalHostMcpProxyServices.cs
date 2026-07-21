using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.LocalClient;
using System.Net.Http.Json;
using System.Text.Json;

namespace OpenCode.Workspace.Mcp;

public sealed class LocalHostClientAccessor
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LocalHostClient? _client;
    private readonly LocalHostClientOptions _options;

    public LocalHostClientAccessor(OpenCodeWorkspaceMcpOptions options)
    {
        var environmentOptions = LocalHostClientOptions.FromEnvironment();
        _options = new LocalHostClientOptions
        {
            StateRoot = string.IsNullOrWhiteSpace(environmentOptions.StateRoot) ? options.WorkspaceStateRoot : environmentOptions.StateRoot,
            DistributionRoot = environmentOptions.DistributionRoot,
            LocalHostExecutableDirectory = environmentOptions.LocalHostExecutableDirectory,
        };
    }

    public async Task<LocalHostClient> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
        {
            return _client;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _client ??= await LocalHostClient.ConnectAsync(_options, cancellationToken);
            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class McpControllerSessionContext
{
    public string ControllerSessionId { get; } = Guid.NewGuid().ToString("n");
    public string ClientInstanceId { get; } = Guid.NewGuid().ToString("n");

    public OperationInitiator ToInitiator()
        => new()
        {
            Kind = "controllerSession",
            ControllerSessionId = ControllerSessionId,
            ClientKind = "mcp",
            ClientInstanceId = ClientInstanceId,
        };
}

public sealed class McpControllerSessionHostedService(LocalHostClientAccessor clients, McpControllerSessionContext session, ILogger<McpControllerSessionHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = await clients.GetAsync(cancellationToken);
            await client.UpsertControllerSessionAsync(new ControllerSessionUpsertRequest
            {
                ControllerSessionId = session.ControllerSessionId,
                ClientKind = "mcp",
                ClientName = "OpenCode Workspace MCP",
                ClientVersion = "1",
                ClientInstanceId = session.ClientInstanceId,
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not register MCP controller session with LocalHost.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = await clients.GetAsync(cancellationToken);
            await client.DisconnectControllerSessionAsync(session.ControllerSessionId, new ControllerSessionUpsertRequest
            {
                ControllerSessionId = session.ControllerSessionId,
                ClientKind = "mcp",
                ClientName = "OpenCode Workspace MCP",
                ClientVersion = "1",
                ClientInstanceId = session.ClientInstanceId,
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not disconnect MCP controller session cleanly.");
        }
    }
}

public sealed class LocalHostOperationStore(LocalHostClientAccessor clients, McpControllerSessionContext session)
{
    public async Task<McpOperationModel> StartProvisionWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
        => McpCompatibilityMapper.ToMcpOperationModel(await (await clients.GetAsync(cancellationToken)).StartProvisionWorkspaceAsync(new WorkspaceProvisionRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspaceId,
            RequestedBy = session.ToInitiator(),
        }, cancellationToken));

    public async Task<McpOperationModel> StartSmokeRunAsync(string templateId, string? timeout, string? artifactsRoot, CancellationToken cancellationToken = default)
        => McpCompatibilityMapper.ToMcpOperationModel(await (await clients.GetAsync(cancellationToken)).StartSmokeRunAsync(new SmokeRunOperationRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            TemplateId = templateId,
            Timeout = timeout,
            ArtifactsRoot = artifactsRoot,
            RequestedBy = session.ToInitiator(),
        }, cancellationToken));

    public async Task<McpOperationModel> StartSmokeMatrixAsync(string[]? templateIds, string? family, bool all, string? timeout, CancellationToken cancellationToken = default)
        => McpCompatibilityMapper.ToMcpOperationModel(await (await clients.GetAsync(cancellationToken)).StartSmokeMatrixAsync(new SmokeMatrixOperationRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            TemplateIds = templateIds ?? Array.Empty<string>(),
            Family = family,
            All = all,
            Timeout = timeout,
            RequestedBy = session.ToInitiator(),
        }, cancellationToken));

    public async Task<IReadOnlyList<McpOperationModel>> ListAsync(CancellationToken cancellationToken = default)
        => (await (await clients.GetAsync(cancellationToken)).ListOperationsAsync(cancellationToken)).Select(McpCompatibilityMapper.ToMcpOperationModel).ToArray();

    public async Task<McpOperationModel> GetAsync(string operationId, long? afterSequence = null, int? maxEvents = null, CancellationToken cancellationToken = default)
        => McpCompatibilityMapper.ToMcpOperationModel(await (await clients.GetAsync(cancellationToken)).GetOperationAsync(operationId, afterSequence, maxEvents, cancellationToken));

    public async Task<McpOperationModel> CancelAsync(string operationId, CancellationToken cancellationToken = default)
        => McpCompatibilityMapper.ToMcpOperationModel(await (await clients.GetAsync(cancellationToken)).CancelOperationAsync(operationId, new OperationCommandRequest { CommandId = Guid.NewGuid().ToString("n"), RequestedBy = session.ToInitiator() }, cancellationToken));
}

public sealed class LocalHostMcpProxyService(LocalHostClientAccessor clients, OpenCodeWorkspaceMcpService fallbackService) : IOpenCodeWorkspaceMcpService
{
    public async Task<IReadOnlyList<WorkspaceTemplateSummaryModel>> ListWorkspaceTemplatesAsync(CancellationToken cancellationToken = default)
        => Map<IReadOnlyList<WorkspaceTemplateSummaryModel>>(await (await clients.GetAsync(cancellationToken)).ListWorkspaceTemplatesAsync(cancellationToken));

    public async Task<WorkspaceTemplateDetailModel> GetWorkspaceTemplateAsync(string templateId, CancellationToken cancellationToken = default)
        => Map<WorkspaceTemplateDetailModel>(await (await clients.GetAsync(cancellationToken)).GetWorkspaceTemplateAsync(templateId, cancellationToken));

    public async Task<WorkspaceSmokeDefinitionCatalogResult> ListSmokeDefinitionsAsync(CancellationToken cancellationToken = default)
        => await (await clients.GetAsync(cancellationToken)).ListSmokeDefinitionsAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkspaceRecordModel>> ListWorkspacesAsync(CancellationToken cancellationToken = default)
        => Map<IReadOnlyList<WorkspaceRecordModel>>(await (await clients.GetAsync(cancellationToken)).ListWorkspacesAsync(cancellationToken));

    public async Task<WorkspaceRecordModel> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
        => Map<WorkspaceRecordModel>(await (await clients.GetAsync(cancellationToken)).GetWorkspaceAsync(workspaceId, cancellationToken));

    public async Task<WorkspaceRecordModel> CreateWorkspaceAsync(string templateId, string workspaceName, string destinationRoot, CancellationToken cancellationToken = default)
        => Map<WorkspaceRecordModel>(await (await clients.GetAsync(cancellationToken)).CreateWorkspaceCanonicalAsync(new WorkspaceCreateRequest { TemplateId = templateId, WorkspaceName = workspaceName, WorkspaceRootPath = Path.Combine(Path.GetFullPath(destinationRoot), workspaceName.Trim()) }, cancellationToken));

    public async Task<ExistingGitCheckoutPlan> InspectExistingGitCheckoutAsync(string repositoryPath, string workspaceName, CancellationToken cancellationToken = default)
        => await (await clients.GetAsync(cancellationToken)).InspectExistingGitCheckoutAsync(new ExistingGitCheckoutInspectionRequest { RepositoryPath = repositoryPath, WorkspaceName = workspaceName }, cancellationToken);

    public async Task<GitBranchValidationResult> ValidateExistingGitCheckoutBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default)
        => await (await clients.GetAsync(cancellationToken)).ValidateExistingGitCheckoutBranchAsync(new ExistingGitCheckoutBranchValidationRequest { RepositoryPath = repositoryPath, BranchName = branchName }, cancellationToken);

    public async Task<WorkspaceRecordModel> ImportExistingGitCheckoutAsync(ExistingGitCheckoutImportRequest request, CancellationToken cancellationToken = default)
        => Map<WorkspaceRecordModel>(await (await clients.GetAsync(cancellationToken)).ImportExistingGitCheckoutAsync(request, cancellationToken));

    public async Task<string> SuggestSavePointMessageAsync(string workspaceId, CancellationToken cancellationToken = default)
        => await (await clients.GetAsync(cancellationToken)).SuggestSavePointMessageAsync(workspaceId, cancellationToken);

    public async Task<WorkspaceRecordModel> CreateSavePointAsync(string workspaceId, string message, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default)
        => Map<WorkspaceRecordModel>((await (await clients.GetAsync(cancellationToken)).StartCreateSavePointAsync(new WorkspaceSavePointCreateRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, Message = message, RequestedBy = BuildProxyInitiator() }, cancellationToken)).Result!.Value.Deserialize<OpenCode.Workspace.LocalClient.WorkspaceRecordModel>(LocalHostContract.JsonOptions)!);

    public async Task<WorkspaceRecordModel> CreateCheckpointAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default)
        => Map<WorkspaceRecordModel>((await (await clients.GetAsync(cancellationToken)).StartCreateCheckpointAsync(new WorkspaceCheckpointCreateRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = BuildProxyInitiator() }, cancellationToken)).Result!.Value.Deserialize<OpenCode.Workspace.LocalClient.WorkspaceRecordModel>(LocalHostContract.JsonOptions)!);

    public Task<WorkspaceBackupOperationResultModel> BackupWorkspaceAsync(string workspaceId, string destinationPath, bool overwriteExisting, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Backups are started through canonical LocalHost operations, not direct MCP execution.");

    public Task<WorkspacePublishAssessmentModel> AssessWorkspacePublishAsync(string workspaceId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Publish assessment is handled through canonical LocalHost routes, not direct MCP execution.");

    public Task<WorkspacePublishOperationResultModel> PublishWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Publish is started through canonical LocalHost operations, not direct MCP execution.");

    public Task<WorkspaceRemovalOperationResultModel> RemoveWorkspaceAsync(string workspaceId, bool removeOwnedRuntimeResources, bool deleteWorkspaceFiles, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Removal is started through canonical LocalHost operations, not direct MCP execution.");

    public Task<WorkspaceRecoveryAssessmentModel> AssessWorkspaceRecoveryAsync(string workspaceId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Recovery assessment is handled through canonical LocalHost routes, not direct MCP execution.");

    public Task<WorkspaceSynchronizationStatusResult> GetSynchronizationStatusAsync(string workspaceId, string? environmentName = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Synchronization status is handled through canonical LocalHost routes, not direct MCP execution.");

    public Task<WorkspaceSynchronizationOperationResult> ExportSynchronizationAsync(string workspaceId, string? environmentName = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Synchronization export is handled through canonical LocalHost operations, not direct MCP execution.");

    public Task<WorkspaceSynchronizationOperationResult> ImportSynchronizationAsync(string workspaceId, string? environmentName = null, string? deploymentProfileOverride = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Synchronization import is handled through canonical LocalHost operations, not direct MCP execution.");

    public Task<WorkspaceSynchronizationOperationResult> PullSynchronizationAsync(string workspaceId, string? environmentName = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Synchronization pull is handled through canonical LocalHost operations, not direct MCP execution.");

    public Task<WorkspaceSynchronizationOperationResult> PushSynchronizationAsync(string workspaceId, string? environmentName = null, string? deploymentProfileOverride = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Synchronization push is handled through canonical LocalHost operations, not direct MCP execution.");

    public Task<WorkspaceSynchronizationExecutionResult> SynchronizeWorkspaceAsync(string workspaceId, string? environmentName = null, string? deploymentProfileOverride = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Synchronization orchestration is handled through canonical LocalHost operations, not direct MCP execution.");

    public Task<OracleAssistantPlanOperationRecord> PlanOracleApexChangeAsync(string workspaceId, OracleApexAssistantRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Oracle Assistant planning is handled through canonical LocalHost operations, not direct MCP execution.");

    public Task<OracleAssistantApplyOperationRecord> ExecuteOracleApexPlanAsync(string workspaceId, OracleApexAssistantRequest request, OracleApexEditPlan plan, string planId, string contextRevision, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Oracle Assistant apply is handled through canonical LocalHost operations, not direct MCP execution.");

    public Task<OracleAssistantRepairPlanOperationRecord> CreateOracleApexRepairPlanAsync(string workspaceId, OracleApexAssistantRequest request, OracleApexEditPlan sourcePlan, OracleApexValidationResult validation, string planId, string executionId, string contextRevision, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Oracle Assistant repair planning is handled through canonical LocalHost operations, not direct MCP execution.");

    public Task<OracleAssistantRepairOperationRecord> ExecuteOracleApexRepairPlanAsync(string workspaceId, OracleApexAssistantRequest request, OracleApexEditPlan repairPlan, string planId, string executionId, string repairPlanId, string contextRevision, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Oracle Assistant repair execution is handled through canonical LocalHost operations, not direct MCP execution.");

    public Task<OracleAssistantRollbackOperationRecord> RollBackOracleApexGeneratedChangeAsync(string workspaceId, string executionId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Oracle Assistant rollback is handled through canonical LocalHost operations, not direct MCP execution.");

    public Task<OracleApexApplicationDiscoveryResult> DiscoverOracleApexApplicationsAsync(string workspaceId, string environmentName, string workspaceName, string parsingSchema, string sqlclProfile, string sourcePath, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Oracle APEX application discovery is handled through canonical LocalHost workflows, not direct MCP execution.");

    public Task<OracleApexConnectExistingApplicationResult> ConnectExistingOracleApexApplicationAsync(string workspaceId, string environmentName, string workspaceName, string parsingSchema, int applicationId, string sqlclProfile, string sourcePath, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Oracle APEX application connection is handled through canonical LocalHost workflows, not direct MCP execution.");

    public Task<OracleAssistantSynchronizationOperationRecord> ValidateOracleAssistantGeneratedApplicationAsync(string workspaceId, string? executionId = null, string? environmentName = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Oracle Assistant validation is handled through canonical LocalHost operations, not direct MCP execution.");

    public Task<OracleAssistantSynchronizationOperationRecord> ImportOracleAssistantGeneratedApplicationAsync(string workspaceId, string? executionId = null, string? environmentName = null, bool allowNonDevelopmentDeployment = false, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Oracle Assistant import is handled through canonical LocalHost operations, not direct MCP execution.");

    public Task<WorkspaceSynchronizationOperationResult> ValidateSynchronizationAsync(string workspaceId, string? environmentName = null, string? deploymentProfileOverride = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Synchronization validation is handled through canonical LocalHost operations, not direct MCP execution.");

    public Task<WorkspaceSynchronizationDiffResult> DiffSynchronizationAsync(string workspaceId, string? environmentName = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Synchronization diff is handled through canonical LocalHost operations, not direct MCP execution.");

    public async Task<WorkspaceTimeline> GetWorkspaceTimelineAsync(string workspaceId, CancellationToken cancellationToken = default)
        => await (await clients.GetAsync(cancellationToken)).GetWorkspaceTimelineAsync(workspaceId, cancellationToken);

    public async Task<WorkspaceCheckpointIndex> GetWorkspaceCheckpointIndexAsync(string workspaceId, CancellationToken cancellationToken = default)
        => await (await clients.GetAsync(cancellationToken)).GetWorkspaceCheckpointsAsync(workspaceId, cancellationToken);

    public Task<WorkspaceRecordModel> ProvisionWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Provisioning is started through canonical LocalHost operations, not direct MCP execution.");

    public async Task<WorkspaceRecordModel> PrepareWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default)
        => Map<WorkspaceRecordModel>((await (await clients.GetAsync(cancellationToken)).StartPrepareWorkspaceAsync(new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = BuildProxyInitiator() }, cancellationToken)).Result!.Value.Deserialize<OpenCode.Workspace.LocalClient.WorkspaceRecordModel>(LocalHostContract.JsonOptions)!);

    public async Task<WorkspaceRecordModel> StartWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default)
        => Map<WorkspaceRecordModel>((await (await clients.GetAsync(cancellationToken)).StartWorkspaceLifecycleAsync("start", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = BuildProxyInitiator() }, cancellationToken)).Result!.Value.Deserialize<OpenCode.Workspace.LocalClient.WorkspaceRecordModel>(LocalHostContract.JsonOptions)!);

    public async Task<WorkspaceRecordModel> ValidateWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
        => Map<WorkspaceRecordModel>(await (await clients.GetAsync(cancellationToken)).ValidateWorkspaceAsync(workspaceId, cancellationToken));

    public async Task<WorkspaceRecordModel> StopWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
        => Map<WorkspaceRecordModel>(await (await clients.GetAsync(cancellationToken)).StopWorkspaceAsync(workspaceId, cancellationToken));

    public async Task<WorkspaceRecordModel> RemoveWorkspaceRuntimeAsync(string workspaceId, CancellationToken cancellationToken = default)
        => Map<WorkspaceRecordModel>(await (await clients.GetAsync(cancellationToken)).RemoveWorkspaceRuntimeAsync(workspaceId, cancellationToken));

    public async Task<WorkspaceRecordModel> RecoverWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default)
        => Map<WorkspaceRecordModel>((await (await clients.GetAsync(cancellationToken)).StartWorkspaceLifecycleAsync("recover", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = BuildProxyInitiator() }, cancellationToken)).Result!.Value.Deserialize<OpenCode.Workspace.LocalClient.WorkspaceRecordModel>(LocalHostContract.JsonOptions)!);

    public async Task<WorkspaceRecordModel> ResetWorkspaceRuntimeAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default)
        => Map<WorkspaceRecordModel>((await (await clients.GetAsync(cancellationToken)).StartWorkspaceLifecycleAsync("reset-runtime", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = BuildProxyInitiator() }, cancellationToken)).Result!.Value.Deserialize<OpenCode.Workspace.LocalClient.WorkspaceRecordModel>(LocalHostContract.JsonOptions)!);

    public async Task<WorkspaceRecordModel> AttachWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default)
        => Map<WorkspaceRecordModel>((await (await clients.GetAsync(cancellationToken)).StartWorkspaceLifecycleAsync("attach", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = BuildProxyInitiator() }, cancellationToken)).Result!.Value.Deserialize<OpenCode.Workspace.LocalClient.WorkspaceRecordModel>(LocalHostContract.JsonOptions)!);

    public async Task<WorkspaceRecordModel> ReprovisionWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default)
        => Map<WorkspaceRecordModel>((await (await clients.GetAsync(cancellationToken)).StartWorkspaceLifecycleAsync("reprovision", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = BuildProxyInitiator() }, cancellationToken)).Result!.Value.Deserialize<OpenCode.Workspace.LocalClient.WorkspaceRecordModel>(LocalHostContract.JsonOptions)!);

    public ServerHealthModel GetServerHealth()
    {
        try
        {
            var client = clients.GetAsync().GetAwaiter().GetResult();
            using var http = new HttpClient { BaseAddress = new Uri(client.BaseUrl, UriKind.Absolute) };
            var envelope = http.GetFromJsonAsync<CompatibilityEnvelope<ServerHealthModel>>("/api/v1/server/health", LocalHostContract.JsonOptions).GetAwaiter().GetResult();
            var health = envelope?.Data ?? fallbackService.GetServerHealth();
            var catalogRoot = string.IsNullOrWhiteSpace(health.CatalogRoot)
                ? OpenCodeWorkspaceInstallationLayout.Resolve(AppContext.BaseDirectory).CatalogRoot
                : health.CatalogRoot;
            return new ServerHealthModel
            {
                Transport = "stdio",
                CatalogRoot = catalogRoot,
                WorkspaceStateRoot = health.WorkspaceStateRoot,
                SmokeArtifactsRoot = health.SmokeArtifactsRoot,
                HttpEnabled = true,
                HttpBinding = "loopback-proxy",
            };
        }
        catch
        {
            var health = fallbackService.GetServerHealth();
            return new ServerHealthModel
            {
                Transport = "stdio",
                CatalogRoot = string.IsNullOrWhiteSpace(health.CatalogRoot) ? OpenCodeWorkspaceInstallationLayout.Resolve(AppContext.BaseDirectory).CatalogRoot : health.CatalogRoot,
                WorkspaceStateRoot = health.WorkspaceStateRoot,
                SmokeArtifactsRoot = health.SmokeArtifactsRoot,
                HttpEnabled = true,
                HttpBinding = "loopback-proxy",
            };
        }
    }

    public Task<IReadOnlyList<WorkspaceSmokeDefinition>> SelectSmokeDefinitionsAsync(WorkspaceSmokeDefinitionSelectionRequest request, CancellationToken cancellationToken = default)
        => fallbackService.SelectSmokeDefinitionsAsync(request, cancellationToken);

    public Task<WorkspaceSmokeResult> RunSmokeAsync(WorkspaceSmokeSingleRunRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Smoke runs are started through canonical LocalHost operations, not direct MCP execution.");

    public Task<WorkspaceSmokeMatrixResult> RunSmokeMatrixAsync(WorkspaceSmokeMatrixRunRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Smoke matrix runs are started through canonical LocalHost operations, not direct MCP execution.");

    public async Task<RuntimeResourceInventory> ListRuntimeResourcesAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken = default)
        => await (await clients.GetAsync(cancellationToken)).ListRuntimeResourcesAsync(query.OwnerKind, query.RunId, query.Project, query.WorkspaceRoot, cancellationToken);

    public async Task<RuntimeResourceInventory> RunRuntimeDoctorAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken = default)
        => await (await clients.GetAsync(cancellationToken)).RunRuntimeDoctorAsync(query.OwnerKind, query.RunId, query.Project, query.WorkspaceRoot, cancellationToken);

    public Task<SmokeCleanupResult> CleanupSmokeResourcesAsync(SmokeCleanupOptions options, CancellationToken cancellationToken = default)
        => fallbackService.CleanupSmokeResourcesAsync(options, cancellationToken);

    public Task<IReadOnlyList<ArtifactListItem>> ListWorkspaceArtifactsAsync(string workspaceId, string? relativePath, bool recursive, CancellationToken cancellationToken = default)
        => fallbackService.ListWorkspaceArtifactsAsync(workspaceId, relativePath, recursive, cancellationToken);

    public Task<ArtifactReadModel> GetWorkspaceArtifactAsync(string workspaceId, string relativePath, CancellationToken cancellationToken = default)
        => fallbackService.GetWorkspaceArtifactAsync(workspaceId, relativePath, cancellationToken);

    public Task<IReadOnlyList<ArtifactListItem>> ListSmokeArtifactsAsync(string runId, string? relativePath, bool recursive, CancellationToken cancellationToken = default)
        => fallbackService.ListSmokeArtifactsAsync(runId, relativePath, recursive, cancellationToken);

    public Task<ArtifactReadModel> GetSmokeArtifactAsync(string runId, string relativePath, CancellationToken cancellationToken = default)
        => fallbackService.GetSmokeArtifactAsync(runId, relativePath, cancellationToken);

    public Task<ArtifactReadModel> ReadArtifactByResourceUriAsync(string resourceUri, CancellationToken cancellationToken = default)
        => fallbackService.ReadArtifactByResourceUriAsync(resourceUri, cancellationToken);

    public Task<ArtifactResourceReadModel> ReadArtifactResourceAsync(string resourceUri, CancellationToken cancellationToken = default)
        => fallbackService.ReadArtifactResourceAsync(resourceUri, cancellationToken);

    public Task<ExcelProcessResultModel> ProcessExcelArtifactAsync(string sourcePath, string? destinationWorkspaceId, string? processingTemplateId, string? outputLogicalName, CancellationToken cancellationToken = default)
        => fallbackService.ProcessExcelArtifactAsync(sourcePath, destinationWorkspaceId, processingTemplateId, outputLogicalName, cancellationToken);

    private static T Map<T>(object source)
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source, LocalHostContract.JsonOptions), LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Could not map '{source.GetType().Name}' to '{typeof(T).Name}'.");

    private static OperationInitiator BuildProxyInitiator()
        => new() { Kind = "controllerSession", ClientKind = "mcp-proxy" };
}

internal sealed class CompatibilityEnvelope<T>
{
    public T Data { get; init; } = default!;
}
