using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting.Server;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Api;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.LocalClient;
using OpenCode.Workspace.Mcp;

var builder = WebApplication.CreateBuilder(args);
TryAddPackagedConfiguration(builder.Configuration, "api");
builder.Logging.AddSimpleConsole();
builder.Services.AddOpenCodeWorkspaceLocalHostServices(builder.Configuration);

var app = builder.Build();
var server = app.Services.GetRequiredService<IServer>();
var shutdownOnStdinEof = args.Any(argument => string.Equals(argument, "--shutdown-on-stdin-eof", StringComparison.OrdinalIgnoreCase));
if (shutdownOnStdinEof && !string.Equals(server.GetType().FullName, "Microsoft.AspNetCore.TestHost.TestServer", StringComparison.Ordinal))
{
    StartStandardInputShutdownMonitor(app.Lifetime, app.Services, app.Logger);
}

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "API request failed for {Method} {Path}.", context.Request.Method, context.Request.Path);
        var (statusCode, envelope) = MapError(exception);
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(envelope);
    }
});

var api = app.MapGroup("/api/v1");

api.MapGet("/health/live", () => Results.Ok(new ApiHealthResponse { Status = "live", Message = "LocalHost is running." }));
api.MapGet("/local-host/health", ([FromServices] LocalHostDescriptorHostedService descriptor) => Results.Ok(new LocalHostEnvelope<LocalHostHealthResponse> { Data = new LocalHostHealthResponse { Status = "live", Message = "LocalHost is running.", HostInstanceId = descriptor.InstanceId } }));
api.MapGet("/server/health", ([FromServices] LocalHostApplicationService service) => Results.Ok(new ApiEnvelope<ServerHealthModel> { Data = service.GetServerHealth() }));
api.MapGet("/health/ready", async ([FromServices] LocalHostApplicationService service) => Results.Ok(new ApiHealthResponse { Status = "ready", Message = "Runtime diagnostics completed.", RuntimeInventory = await service.RunRuntimeDoctorAsync(new RuntimeOwnershipQuery()) }));
api.MapGet("/local-host/readiness", async ([FromServices] LocalHostApplicationService service) => Results.Ok(new LocalHostEnvelope<LocalHostReadinessResponse> { Data = new LocalHostReadinessResponse { Status = "ready", Message = "Runtime diagnostics completed.", RuntimeInventory = await service.RunRuntimeDoctorAsync(new RuntimeOwnershipQuery()) } }));
api.MapPost("/local-host/workspaces/create", async (WorkspaceCreateRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceRecordModel> { Data = await service.CreateWorkspaceAsync(request) }));
api.MapPost("/local-host/workspaces/import/inspect-git-checkout", async (ExistingGitCheckoutInspectionRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<ExistingGitCheckoutPlan> { Data = await service.InspectExistingGitCheckoutAsync(request) }));
api.MapPost("/local-host/workspaces/import/validate-branch", async (ExistingGitCheckoutBranchValidationRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<GitBranchValidationResult> { Data = await service.ValidateExistingGitCheckoutBranchAsync(request) }));
api.MapPost("/local-host/workspaces/import", async (ExistingGitCheckoutImportRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceRecordModel> { Data = await service.ImportExistingGitCheckoutAsync(request) }));
api.MapPost("/local-host/workspaces/{workspaceId}/save-points/suggest-message", async (string workspaceId, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<string> { Data = await service.SuggestSavePointMessageAsync(workspaceId) }));
api.MapGet("/local-host/workspaces/{workspaceId}/publish-assessment", async (string workspaceId, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspacePublishAssessmentRecord> { Data = await service.GetWorkspacePublishAssessmentAsync(workspaceId) }));
api.MapGet("/local-host/workspaces/{workspaceId}/recovery-assessment", async (string workspaceId, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceRecoveryAssessmentRecord> { Data = await service.GetWorkspaceRecoveryAssessmentAsync(workspaceId) }));
api.MapGet("/local-host/workspaces/{workspaceId}/synchronization/status", async (string workspaceId, string? environmentName, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceSynchronizationStatusResult> { Data = await service.GetSynchronizationStatusAsync(workspaceId, environmentName) }));
api.MapPost("/local-host/workspaces/{workspaceId}/save-points", async (string workspaceId, WorkspaceSavePointCreateRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartCreateSavePointAsync(request with { WorkspaceId = workspaceId }) }));
api.MapPost("/local-host/workspaces/{workspaceId}/checkpoints", async (string workspaceId, WorkspaceCheckpointCreateRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartCreateCheckpointAsync(request with { WorkspaceId = workspaceId }) }));
api.MapPost("/local-host/workspaces/{workspaceId}/backups", async (string workspaceId, WorkspaceBackupRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartBackupWorkspaceAsync(request with { WorkspaceId = workspaceId }) }));
api.MapPost("/local-host/workspaces/{workspaceId}/publish", async (string workspaceId, WorkspacePublishRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartPublishWorkspaceAsync(ValidateWorkspaceScopedRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/remove", async (string workspaceId, OpenCode.Workspace.LocalClient.WorkspaceRemovalRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartRemoveWorkspaceAsync(ValidateWorkspaceRemovalRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/synchronization/validate", async (string workspaceId, WorkspaceSynchronizationValidationRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartValidateSynchronizationAsync(ValidateWorkspaceSynchronizationValidationRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/synchronization/export", async (string workspaceId, WorkspaceSynchronizationExportRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartExportSynchronizationAsync(ValidateWorkspaceSynchronizationExportRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/synchronization/pull", async (string workspaceId, WorkspaceSynchronizationExportRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartPullSynchronizationAsync(ValidateWorkspaceSynchronizationExportRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/synchronization/push", async (string workspaceId, WorkspaceSynchronizationImportRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartPushSynchronizationAsync(ValidateWorkspaceSynchronizationImportRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/synchronization/import", async (string workspaceId, WorkspaceSynchronizationImportRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartImportSynchronizationAsync(ValidateWorkspaceSynchronizationImportRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/synchronization/synchronize", async (string workspaceId, WorkspaceSynchronizeRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartSynchronizeWorkspaceAsync(ValidateWorkspaceSynchronizeRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/oracle-assistant/validate", async (string workspaceId, OracleAssistantValidationRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartValidateOracleAssistantAsync(ValidateOracleAssistantValidationRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/oracle-assistant/plan", async (string workspaceId, OracleAssistantPlanRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartPlanOracleAssistantAsync(ValidateOracleAssistantPlanRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/oracle-assistant/apply", async (string workspaceId, OracleAssistantApplyRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartApplyOracleAssistantAsync(ValidateOracleAssistantApplyRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/oracle-assistant/repair-plan", async (string workspaceId, OracleAssistantRepairPlanRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartPlanOracleAssistantRepairAsync(ValidateOracleAssistantRepairPlanRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/oracle-assistant/repair", async (string workspaceId, OracleAssistantRepairExecutionRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartExecuteOracleAssistantRepairAsync(ValidateOracleAssistantRepairExecutionRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/oracle-assistant/rollback", async (string workspaceId, OracleAssistantRollbackRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartRollbackOracleAssistantAsync(ValidateOracleAssistantRollbackRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/oracle-apex/discover-applications", async (string workspaceId, OracleApexApplicationDiscoveryQuery request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<OracleApexApplicationDiscoveryResult> { Data = await service.DiscoverOracleApexApplicationsAsync(ValidateOracleApexApplicationDiscoveryQuery(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/oracle-apex/connect-existing-application", async (string workspaceId, ConnectExistingOracleApexApplicationRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartConnectExistingOracleApexApplicationAsync(ValidateConnectExistingOracleApexApplicationRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/oracle-assistant/import", async (string workspaceId, OracleAssistantImportRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartImportOracleAssistantAsync(ValidateOracleAssistantImportRequest(workspaceId, request)) }));
api.MapPost("/local-host/workspaces/{workspaceId}/synchronization/diff", async (string workspaceId, WorkspaceSynchronizationDiffRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartDiffSynchronizationAsync(ValidateWorkspaceSynchronizationDiffRequest(workspaceId, request)) }));
api.MapGet("/local-host/workspaces/{workspaceId}/save-points", async (string workspaceId, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceTimeline> { Data = await service.GetWorkspaceTimelineAsync(workspaceId) }));
api.MapGet("/local-host/workspaces/{workspaceId}/checkpoints", async (string workspaceId, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceCheckpointIndex> { Data = await service.GetWorkspaceCheckpointIndexAsync(workspaceId) }));
api.MapGet("/local-host/operations", ([FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<IReadOnlyList<WorkspaceOperationRecord>> { Data = service.ListOperations() }));
api.MapGet("/local-host/operations/{operationId}", (string operationId, long? afterSequence, int? maxEvents, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = service.GetOperation(operationId, afterSequence, maxEvents) }));
api.MapPost("/local-host/operations/{operationId}/cancel", async (string operationId, OperationCommandRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.CancelOperationAsync(operationId) }));
api.MapPost("/local-host/workspaces/{workspaceId}/provision", async (string workspaceId, WorkspaceProvisionRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartProvisionWorkspaceAsync(request with { WorkspaceId = workspaceId }) }));
api.MapPost("/local-host/workspaces/{workspaceId}/prepare", async (string workspaceId, WorkspaceLifecycleRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartPrepareWorkspaceAsync(request with { WorkspaceId = workspaceId }) }));
api.MapPost("/local-host/workspaces/{workspaceId}/start", async (string workspaceId, WorkspaceLifecycleRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartStartWorkspaceAsync(request with { WorkspaceId = workspaceId }) }));
api.MapPost("/local-host/workspaces/{workspaceId}/stop", async (string workspaceId, WorkspaceLifecycleRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartStopWorkspaceAsync(request with { WorkspaceId = workspaceId }) }));
api.MapPost("/local-host/workspaces/{workspaceId}/recover", async (string workspaceId, WorkspaceLifecycleRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartRecoverWorkspaceAsync(request with { WorkspaceId = workspaceId }) }));
api.MapPost("/local-host/workspaces/{workspaceId}/reset-runtime", async (string workspaceId, WorkspaceLifecycleRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartResetRuntimeAsync(request with { WorkspaceId = workspaceId }) }));
api.MapPost("/local-host/workspaces/{workspaceId}/attach", async (string workspaceId, WorkspaceLifecycleRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartAttachWorkspaceAsync(request with { WorkspaceId = workspaceId }) }));
api.MapPost("/local-host/workspaces/{workspaceId}/reprovision", async (string workspaceId, WorkspaceLifecycleRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartReprovisionWorkspaceAsync(request with { WorkspaceId = workspaceId }) }));
api.MapPost("/local-host/smoke/runs", async (SmokeRunOperationRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartSmokeRunAsync(request) }));
api.MapPost("/local-host/smoke/matrices", async (SmokeMatrixOperationRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<WorkspaceOperationRecord> { Data = await service.StartSmokeMatrixAsync(request) }));

api.MapGet("/templates", async ([FromServices] LocalHostApplicationService service) => Results.Ok(new ApiEnvelope<IReadOnlyList<OpenCode.Workspace.LocalClient.WorkspaceTemplateSummaryModel>> { Data = await service.ListWorkspaceTemplatesAsync() }));
api.MapGet("/templates/{templateId}", async (string templateId, [FromServices] LocalHostApplicationService service) => Results.Ok(new ApiEnvelope<OpenCode.Workspace.LocalClient.WorkspaceTemplateDetailModel> { Data = await service.GetWorkspaceTemplateAsync(templateId) }));

api.MapGet("/smoke/definitions", async ([FromServices] LocalHostApplicationService service) => Results.Ok(new ApiEnvelope<WorkspaceSmokeDefinitionCatalogResult> { Data = await service.ListSmokeDefinitionsAsync() }));
api.MapPost("/smoke/runs", async (StartSmokeRunRequest request, [FromServices] LocalHostApplicationService service) =>
{
    var operation = await service.StartSmokeRunAsync(new SmokeRunOperationRequest { CommandId = Guid.NewGuid().ToString("n"), TemplateId = request.TemplateId, Timeout = request.Timeout, ArtifactsRoot = request.ArtifactsRoot, RequestedBy = new OperationInitiator { Kind = "api" } });
    return Results.Accepted($"/api/v1/operations/{operation.OperationId}", McpCompatibilityMapper.ToMcpOperationModel(operation));
});
api.MapPost("/smoke/matrices", async (StartSmokeMatrixRequest request, [FromServices] LocalHostApplicationService service) =>
{
    var operation = await service.StartSmokeMatrixAsync(new SmokeMatrixOperationRequest { CommandId = Guid.NewGuid().ToString("n"), TemplateIds = request.TemplateIds, Family = request.Family, All = request.All, Timeout = request.Timeout, RequestedBy = new OperationInitiator { Kind = "api" } });
    return Results.Accepted($"/api/v1/operations/{operation.OperationId}", McpCompatibilityMapper.ToMcpOperationModel(operation));
});
api.MapPost("/smoke/cleanup", async (CleanupSmokeRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new ApiEnvelope<SmokeCleanupResult> { Data = await service.CleanupSmokeResourcesAsync(new SmokeCleanupOptions(request.DryRun, request.IncludeAll, request.RunId, "json")) }));

api.MapGet("/workspaces", async ([FromServices] LocalHostApplicationService service)
    => Results.Ok(new ApiEnvelope<IReadOnlyList<OpenCode.Workspace.LocalClient.WorkspaceRecordModel>> { Data = await service.ListWorkspacesAsync() }));
api.MapGet("/workspaces/{workspaceId}", async (string workspaceId, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new ApiEnvelope<OpenCode.Workspace.LocalClient.WorkspaceRecordModel> { Data = await service.GetWorkspaceAsync(workspaceId) }));
api.MapPost("/workspaces", async (CreateWorkspaceRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Created($"/api/v1/workspaces/{request.WorkspaceName}", new ApiEnvelope<OpenCode.Workspace.LocalClient.WorkspaceRecordModel> { Data = await service.CreateWorkspaceAsync(request.TemplateId, request.WorkspaceName, request.DestinationRoot) }));
api.MapPost("/workspaces/{workspaceId}/validate", async (string workspaceId, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new ApiEnvelope<OpenCode.Workspace.LocalClient.WorkspaceRecordModel> { Data = await service.ValidateWorkspaceAsync(workspaceId) }));
api.MapPost("/workspaces/{workspaceId}/provision", async (string workspaceId, [FromServices] LocalHostApplicationService service) =>
{
    var operation = await service.StartProvisionWorkspaceAsync(new WorkspaceProvisionRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = new OperationInitiator { Kind = "api" } });
    return Results.Accepted($"/api/v1/operations/{operation.OperationId}", McpCompatibilityMapper.ToMcpOperationModel(operation));
});
api.MapPost("/workspaces/{workspaceId}/stop", async (string workspaceId, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new ApiEnvelope<OpenCode.Workspace.LocalClient.WorkspaceRecordModel> { Data = await service.StopWorkspaceAsync(workspaceId) }));
api.MapPost("/workspaces/{workspaceId}/remove-runtime", async (string workspaceId, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new ApiEnvelope<OpenCode.Workspace.LocalClient.WorkspaceRecordModel> { Data = await service.RemoveWorkspaceRuntimeAsync(workspaceId) }));

api.MapGet("/workspace-instances", async ([FromServices] LocalHostApplicationService service) => Results.Ok(new LocalHostEnvelope<IReadOnlyList<WorkspaceInstanceRecord>> { Data = await service.ListWorkspaceInstancesAsync() }));
api.MapGet("/workspace-instances/{workspaceInstanceId}", async (string workspaceInstanceId, [FromServices] LocalHostApplicationService service) => Results.Ok(new LocalHostEnvelope<WorkspaceInstanceRecord> { Data = await service.GetWorkspaceInstanceAsync(workspaceInstanceId) }));

api.MapGet("/runtime/resources", async (string? owner, string? runId, string? project, string? workspaceRoot, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new ApiEnvelope<RuntimeResourceInventory> { Data = await service.ListRuntimeResourcesAsync(new RuntimeOwnershipQuery { OwnerKind = owner, RunId = runId, Project = project, WorkspaceRoot = workspaceRoot }) }));
api.MapGet("/runtime/doctor", async (string? owner, string? runId, string? project, string? workspaceRoot, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new ApiEnvelope<RuntimeResourceInventory> { Data = await service.RunRuntimeDoctorAsync(new RuntimeOwnershipQuery { OwnerKind = owner, RunId = runId, Project = project, WorkspaceRoot = workspaceRoot }) }));

api.MapGet("/operations", ([FromServices] LocalHostApplicationService service)
    => Results.Ok(new ApiEnvelope<IReadOnlyList<McpOperationModel>> { Data = service.ListOperations().Select(McpCompatibilityMapper.ToMcpOperationModel).ToArray() }));
api.MapGet("/operations/{operationId}", (string operationId, long? afterSequence, int? maxEvents, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new ApiEnvelope<McpOperationModel> { Data = McpCompatibilityMapper.ToMcpOperationModel(service.GetOperation(operationId, afterSequence, maxEvents)) }));
api.MapGet("/operations/{operationId}/events", (string operationId, long? afterSequence, int? maxEvents, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<IReadOnlyList<OpenCode.Workspace.LocalClient.WorkspaceOperationProgressEvent>> { Data = service.GetOperation(operationId, afterSequence, maxEvents).RecentEvents }));
api.MapPost("/operations/{operationId}/cancel", async (string operationId, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new ApiEnvelope<McpOperationModel> { Data = McpCompatibilityMapper.ToMcpOperationModel(await service.CancelOperationAsync(operationId)) }));

api.MapGet("/controller-sessions", async ([FromServices] LocalHostApplicationService service) => Results.Ok(new LocalHostEnvelope<IReadOnlyList<ControllerSessionRecord>> { Data = await service.ListControllerSessionsAsync() }));
api.MapPost("/controller-sessions", async (ControllerSessionUpsertRequest request, [FromServices] LocalHostApplicationService service) => Results.Ok(new LocalHostEnvelope<ControllerSessionRecord> { Data = await service.UpsertControllerSessionAsync(request) }));
api.MapPost("/controller-sessions/{controllerSessionId}/disconnect", async (string controllerSessionId, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<ControllerSessionRecord> { Data = await service.DisconnectControllerSessionAsync(controllerSessionId) }));
api.MapGet("/interactive-agent-sessions", async (string? workspaceId, [FromServices] LocalHostApplicationService service) => Results.Ok(new LocalHostEnvelope<IReadOnlyList<InteractiveAgentSessionRecord>> { Data = await service.ListInteractiveAgentSessionsAsync(workspaceId: workspaceId) }));
api.MapGet("/interactive-agent-sessions/{interactiveAgentSessionId}", async (string interactiveAgentSessionId, [FromServices] LocalHostApplicationService service) => Results.Ok(new LocalHostEnvelope<InteractiveAgentSessionRecord> { Data = await service.GetInteractiveAgentSessionAsync(interactiveAgentSessionId) }));
api.MapGet("/interactive-agent-sessions/{interactiveAgentSessionId}/attachments", async (string interactiveAgentSessionId, [FromServices] LocalHostApplicationService service) => Results.Ok(new LocalHostEnvelope<IReadOnlyList<InteractiveSessionAttachmentRecord>> { Data = await service.GetInteractiveAttachmentsAsync(interactiveAgentSessionId) }));
api.MapPost("/local-host/workspaces/{workspaceId}/interactive-sessions", async (string workspaceId, CreateInteractiveAgentSessionRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<InteractiveAgentSessionRecord> { Data = await service.CreateInteractiveAgentSessionAsync(ValidateCreateInteractiveAgentSessionRequest(workspaceId, request)) }));
api.MapPost("/local-host/interactive-agent-sessions/{interactiveAgentSessionId}/attachments", async (string interactiveAgentSessionId, AttachInteractiveSessionRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<InteractiveSessionAttachResult> { Data = await service.AttachInteractiveSessionAsync(interactiveAgentSessionId, ValidateAttachInteractiveSessionRequest(interactiveAgentSessionId, request)) }));
api.MapPost("/local-host/interactive-agent-sessions/{interactiveAgentSessionId}/attachments/{attachmentId}/activate", async (string interactiveAgentSessionId, string attachmentId, ActivateInteractiveSessionAttachmentRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<InteractiveSessionAttachmentActivationResult> { Data = await service.ActivateInteractiveSessionAttachmentAsync(interactiveAgentSessionId, attachmentId, request) }));
api.MapPost("/local-host/interactive-agent-sessions/{interactiveAgentSessionId}/attachments/{attachmentId}/recover", async (string interactiveAgentSessionId, string attachmentId, RecoverInteractiveSessionAttachmentRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<InteractiveSessionAttachmentRecoveryResult> { Data = await service.RecoverInteractiveSessionAttachmentAsync(interactiveAgentSessionId, attachmentId, request) }));
api.MapPost("/local-host/interactive-agent-sessions/{interactiveAgentSessionId}/attachments/{attachmentId}/process-started", async (string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProcessStartedRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<InteractiveSessionAttachmentRecord> { Data = await service.ReportInteractiveSessionAttachmentProcessStartedAsync(interactiveAgentSessionId, attachmentId, request) }));
api.MapPost("/local-host/interactive-agent-sessions/{interactiveAgentSessionId}/attachments/{attachmentId}/heartbeat", async (string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentHeartbeatRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<InteractiveSessionAttachmentHeartbeatResult> { Data = await service.HeartbeatInteractiveSessionAttachmentAsync(interactiveAgentSessionId, attachmentId, request) }));
api.MapPost("/local-host/interactive-agent-sessions/{interactiveAgentSessionId}/attachments/{attachmentId}/provider-session", async (string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProviderSessionRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<InteractiveAgentSessionRecord> { Data = await service.ReportInteractiveSessionProviderSessionAsync(interactiveAgentSessionId, attachmentId, request) }));
api.MapPost("/local-host/interactive-agent-sessions/{interactiveAgentSessionId}/attachments/{attachmentId}/process-exit", async (string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProcessExitRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<InteractiveAgentSessionRecord> { Data = await service.ReportInteractiveSessionAttachmentProcessExitAsync(interactiveAgentSessionId, attachmentId, request) }));
api.MapPost("/local-host/interactive-agent-sessions/{interactiveAgentSessionId}/attachments/{attachmentId}/launch-failed", async (string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentLaunchFailureRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<InteractiveAgentSessionRecord> { Data = await service.ReportInteractiveSessionAttachmentLaunchFailureAsync(interactiveAgentSessionId, attachmentId, request) }));
api.MapPost("/local-host/interactive-agent-sessions/{interactiveAgentSessionId}/attachments/{attachmentId}/detach", async (string interactiveAgentSessionId, string attachmentId, DetachInteractiveSessionAttachmentRequest request, [FromServices] LocalHostApplicationService service)
    => Results.Ok(new LocalHostEnvelope<InteractiveAgentSessionRecord> { Data = await service.DetachInteractiveSessionAsync(interactiveAgentSessionId, attachmentId, request) }));

api.MapHub<LocalHostEventHub>("/hubs/events");

app.Run();

static void StartStandardInputShutdownMonitor(IHostApplicationLifetime lifetime, IServiceProvider services, ILogger logger)
{
    if (!Console.IsInputRedirected)
    {
        return;
    }

    _ = Task.Run(async () =>
    {
        try
        {
            using var input = Console.OpenStandardInput();
            var buffer = new byte[4096];
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), CancellationToken.None);
                if (read == 0)
                {
                    logger.LogInformation("Standard input reached EOF. Waiting for controllers and durable operations before shutdown.");
                    var service = services.GetRequiredService<LocalHostApplicationService>();
                    while (!lifetime.ApplicationStopping.IsCancellationRequested)
                    {
                        var controllers = await service.ListControllerSessionsAsync();
                        var hasActiveOperation = service.ListOperations().Any(item => item.Status is WorkspaceOperationStatus.Pending or WorkspaceOperationStatus.Running);
                        if (controllers.All(item => item.Status != ControllerSessionStatus.Connected) && !hasActiveOperation)
                        {
                            lifetime.StopApplication();
                            return;
                        }

                        await Task.Delay(100, CancellationToken.None);
                    }
                    return;
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException exception)
        {
            logger.LogDebug(exception, "Standard input shutdown monitor stopped after I/O exception.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Standard input shutdown monitor failed.");
        }
    });
}

static void TryAddPackagedConfiguration(ConfigurationManager configuration, string hostName)
{
    try
    {
        var installationLayout = OpenCodeWorkspaceInstallationLayout.Resolve(AppContext.BaseDirectory);
        var configPath = installationLayout.GetConfigFilePath(hostName);
        if (File.Exists(configPath))
        {
            configuration.AddJsonFile(configPath, optional: true, reloadOnChange: false);
        }
    }
    catch (InvalidOperationException)
    {
    }
}

static WorkspacePublishRequest ValidateWorkspaceScopedRequest(string workspaceId, WorkspacePublishRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static OpenCode.Workspace.LocalClient.WorkspaceRemovalRequest ValidateWorkspaceRemovalRequest(string workspaceId, OpenCode.Workspace.LocalClient.WorkspaceRemovalRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static WorkspaceSynchronizationValidationRequest ValidateWorkspaceSynchronizationValidationRequest(string workspaceId, WorkspaceSynchronizationValidationRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static WorkspaceSynchronizationExportRequest ValidateWorkspaceSynchronizationExportRequest(string workspaceId, WorkspaceSynchronizationExportRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static WorkspaceSynchronizationImportRequest ValidateWorkspaceSynchronizationImportRequest(string workspaceId, WorkspaceSynchronizationImportRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static WorkspaceSynchronizeRequest ValidateWorkspaceSynchronizeRequest(string workspaceId, WorkspaceSynchronizeRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static OracleAssistantValidationRequest ValidateOracleAssistantValidationRequest(string workspaceId, OracleAssistantValidationRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static OracleAssistantPlanRequest ValidateOracleAssistantPlanRequest(string workspaceId, OracleAssistantPlanRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static OracleAssistantApplyRequest ValidateOracleAssistantApplyRequest(string workspaceId, OracleAssistantApplyRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static OracleAssistantRepairPlanRequest ValidateOracleAssistantRepairPlanRequest(string workspaceId, OracleAssistantRepairPlanRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static OracleAssistantRepairExecutionRequest ValidateOracleAssistantRepairExecutionRequest(string workspaceId, OracleAssistantRepairExecutionRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static OracleAssistantRollbackRequest ValidateOracleAssistantRollbackRequest(string workspaceId, OracleAssistantRollbackRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static OracleApexApplicationDiscoveryQuery ValidateOracleApexApplicationDiscoveryQuery(string workspaceId, OracleApexApplicationDiscoveryQuery request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static ConnectExistingOracleApexApplicationRequest ValidateConnectExistingOracleApexApplicationRequest(string workspaceId, ConnectExistingOracleApexApplicationRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static CreateInteractiveAgentSessionRequest ValidateCreateInteractiveAgentSessionRequest(string workspaceId, CreateInteractiveAgentSessionRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static AttachInteractiveSessionRequest ValidateAttachInteractiveSessionRequest(string interactiveAgentSessionId, AttachInteractiveSessionRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.SessionId) && !string.Equals(request.SessionId, interactiveAgentSessionId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Session id '{request.SessionId}' does not match route session id '{interactiveAgentSessionId}'.", "Retry with matching session ids.");
    }

    return request with { SessionId = interactiveAgentSessionId };
}

static OracleAssistantImportRequest ValidateOracleAssistantImportRequest(string workspaceId, OracleAssistantImportRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static WorkspaceSynchronizationDiffRequest ValidateWorkspaceSynchronizationDiffRequest(string workspaceId, WorkspaceSynchronizationDiffRequest request)
{
    if (!string.IsNullOrWhiteSpace(request.WorkspaceId) && !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
    {
        throw new OpenCodeWorkspaceMcpException("invalid_request", $"Workspace id '{request.WorkspaceId}' does not match route workspace id '{workspaceId}'.", "Retry with matching workspace ids.");
    }

    return request with { WorkspaceId = workspaceId };
}

static (int StatusCode, ApiErrorEnvelope Envelope) MapError(Exception exception)
{
    return exception switch
    {
        OpenCodeWorkspaceMcpException mcpException => (MapStatusCode(mcpException.Code), new ApiErrorEnvelope
        {
            Code = mcpException.Code,
            Message = mcpException.Message,
            Recommendation = mcpException.Recommendation,
        }),
        WorkspaceSmokeSelectionException smokeSelection => (400, new ApiErrorEnvelope
        {
            Code = "invalid_smoke_selection",
            Message = smokeSelection.Message,
            Recommendation = "Use list smoke definitions to select stable template ids or families.",
        }),
        ArgumentException argumentException => (400, new ApiErrorEnvelope
        {
            Code = "invalid_request",
            Message = argumentException.Message,
            Recommendation = "Review the request payload and retry.",
        }),
        _ => (500, new ApiErrorEnvelope
        {
            Code = "internal_error",
            Message = "The API request failed.",
            Recommendation = "Inspect server diagnostics and retry.",
        }),
    };
}

static int MapStatusCode(string code)
    => code switch
    {
        "unknown_template" => 404,
        "workspace_not_found" => 404,
        "interactive_session_not_found" => 404,
        "attachment_not_found" => 404,
        "operation_not_found" => 404,
        "artifact_not_found" => 404,
        "operation_not_cancellable" => 409,
        "already_attached" => 409,
        "transfer_rejected" => 409,
        "attachment_owner_mismatch" => 409,
        "provider_session_mismatch" => 409,
        "invalid_attachment_credential" => 401,
        "invalid_recovery_proof" => 401,
        "recovery_not_allowed" => 409,
        "artifact_outside_allowed_root" => 400,
        "invalid_artifact_resource_id" => 400,
        "invalid_workbook" => 400,
        "invalid_smoke_selection" => 400,
        _ => 400,
    };

public partial class Program;
