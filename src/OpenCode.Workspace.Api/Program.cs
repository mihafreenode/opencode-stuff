using Microsoft.AspNetCore.Mvc;
using OpenCode.Workspace.Api;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.Mcp;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSimpleConsole();
builder.Services.AddOpenCodeWorkspaceLocalServices(builder.Configuration);

var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception exception)
    {
        var (statusCode, envelope) = MapError(exception);
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(envelope);
    }
});

var api = app.MapGroup("/api/v1");

api.MapGet("/health/live", () => Results.Ok(new ApiHealthResponse { Status = "live", Message = "API host is running." }));
api.MapGet("/health/ready", async ([FromServices] IOpenCodeWorkspaceMcpService service) =>
{
    try
    {
        var inventory = await service.RunRuntimeDoctorAsync(new RuntimeOwnershipQuery());
        return Results.Ok(new ApiHealthResponse { Status = "ready", Message = "Runtime diagnostics completed.", RuntimeInventory = inventory });
    }
    catch (Exception exception)
    {
        return Results.Ok(new ApiHealthResponse { Status = "notReady", Message = exception.Message });
    }
});

api.MapGet("/templates", async ([FromServices] IOpenCodeWorkspaceMcpService service)
    => Results.Ok(new ApiEnvelope<IReadOnlyList<WorkspaceTemplateSummaryModel>> { Data = await service.ListWorkspaceTemplatesAsync() }));
api.MapGet("/templates/{templateId}", async (string templateId, [FromServices] IOpenCodeWorkspaceMcpService service)
    => Results.Ok(new ApiEnvelope<WorkspaceTemplateDetailModel> { Data = await service.GetWorkspaceTemplateAsync(templateId) }));

api.MapGet("/smoke/definitions", async ([FromServices] IOpenCodeWorkspaceMcpService service)
    => Results.Ok(new ApiEnvelope<WorkspaceSmokeDefinitionCatalogResult> { Data = await service.ListSmokeDefinitionsAsync() }));
api.MapPost("/smoke/runs", async (StartSmokeRunRequest request, [FromServices] IOpenCodeWorkspaceMcpService service, [FromServices] McpOperationStore operations) =>
{
    await service.GetWorkspaceTemplateAsync(request.TemplateId);
    var smokeRequest = new WorkspaceSmokeSingleRunRequest
    {
        TemplateId = request.TemplateId,
        Timeout = ParseTimeout(request.Timeout),
        ArtifactsRoot = request.ArtifactsRoot ?? string.Empty,
    };
    var operation = operations.Start("run_smoke", string.Empty, $"Running smoke for {request.TemplateId}.", async (reporter, cancellationToken) =>
    {
        reporter.MarkStarted("queued", $"Queued smoke run for '{request.TemplateId}'.");
        smokeRequest = new WorkspaceSmokeSingleRunRequest
        {
            TemplateId = smokeRequest.TemplateId,
            Timeout = smokeRequest.Timeout,
            ArtifactsRoot = smokeRequest.ArtifactsRoot,
            Progress = new Progress<WorkspaceSmokeProgressUpdate>(update => reporter.ReportProgress(update.Phase, update.Message)),
        };
        return await service.RunSmokeAsync(smokeRequest, cancellationToken);
    });
    return Results.Accepted($"/api/v1/operations/{operation.OperationId}", operation);
});
api.MapPost("/smoke/matrices", async (StartSmokeMatrixRequest request, [FromServices] IOpenCodeWorkspaceMcpService service, [FromServices] McpOperationStore operations) =>
{
    var selected = await service.SelectSmokeDefinitionsAsync(new WorkspaceSmokeDefinitionSelectionRequest
    {
        TemplateIds = request.TemplateIds,
        Family = request.Family,
        All = request.All,
    });
    var matrixRequest = new WorkspaceSmokeMatrixRunRequest
    {
        TemplateIds = selected.Select(item => item.TemplateId).ToArray(),
        MatrixTimeout = ParseTimeout(request.Timeout),
    };
    var operation = operations.Start("run_smoke_matrix", string.Empty, "Running smoke matrix.", async (reporter, cancellationToken) =>
    {
        reporter.MarkStarted("queued", "Queued smoke matrix.");
        matrixRequest = new WorkspaceSmokeMatrixRunRequest
        {
            TemplateIds = matrixRequest.TemplateIds,
            MatrixTimeout = matrixRequest.MatrixTimeout,
            Progress = new Progress<WorkspaceSmokeProgressUpdate>(update => reporter.ReportProgress(update.Phase, update.Message)),
        };
        return await service.RunSmokeMatrixAsync(matrixRequest, cancellationToken);
    });
    return Results.Accepted($"/api/v1/operations/{operation.OperationId}", operation);
});
api.MapPost("/smoke/cleanup", async (CleanupSmokeRequest request, [FromServices] IOpenCodeWorkspaceMcpService service)
    => Results.Ok(new ApiEnvelope<SmokeCleanupResult> { Data = await service.CleanupSmokeResourcesAsync(new SmokeCleanupOptions(request.DryRun, request.IncludeAll, request.RunId, "json")) }));

api.MapGet("/workspaces", async ([FromServices] IOpenCodeWorkspaceMcpService service)
    => Results.Ok(new ApiEnvelope<IReadOnlyList<WorkspaceRecordModel>> { Data = await service.ListWorkspacesAsync() }));
api.MapGet("/workspaces/{workspaceId}", async (string workspaceId, [FromServices] IOpenCodeWorkspaceMcpService service)
    => Results.Ok(new ApiEnvelope<WorkspaceRecordModel> { Data = await service.GetWorkspaceAsync(workspaceId) }));
api.MapPost("/workspaces", async (CreateWorkspaceRequest request, [FromServices] IOpenCodeWorkspaceMcpService service)
    => Results.Created($"/api/v1/workspaces/{request.WorkspaceName}", new ApiEnvelope<WorkspaceRecordModel> { Data = await service.CreateWorkspaceAsync(request.TemplateId, request.WorkspaceName, request.DestinationRoot) }));
api.MapPost("/workspaces/{workspaceId}/validate", async (string workspaceId, [FromServices] IOpenCodeWorkspaceMcpService service)
    => Results.Ok(new ApiEnvelope<WorkspaceRecordModel> { Data = await service.ValidateWorkspaceAsync(workspaceId) }));
api.MapPost("/workspaces/{workspaceId}/provision", (string workspaceId, [FromServices] IOpenCodeWorkspaceMcpService service, [FromServices] McpOperationStore operations) =>
{
    var operation = operations.Start("provision_workspace", workspaceId, "Provisioning workspace.", async (reporter, cancellationToken) =>
    {
        reporter.MarkStarted("preparing", "Preparing workspace provisioning.");
        return await service.ProvisionWorkspaceAsync(workspaceId, message => reporter.ReportProgress("provisioning", message), cancellationToken);
    });
    return Results.Accepted($"/api/v1/operations/{operation.OperationId}", operation);
});
api.MapPost("/workspaces/{workspaceId}/stop", async (string workspaceId, [FromServices] IOpenCodeWorkspaceMcpService service)
    => Results.Ok(new ApiEnvelope<WorkspaceRecordModel> { Data = await service.StopWorkspaceAsync(workspaceId) }));
api.MapPost("/workspaces/{workspaceId}/remove-runtime", async (string workspaceId, [FromServices] IOpenCodeWorkspaceMcpService service)
    => Results.Ok(new ApiEnvelope<WorkspaceRecordModel> { Data = await service.RemoveWorkspaceRuntimeAsync(workspaceId) }));

api.MapGet("/runtime/resources", async (string? owner, string? runId, string? project, string? workspaceRoot, [FromServices] IOpenCodeWorkspaceMcpService service)
    => Results.Ok(new ApiEnvelope<RuntimeResourceInventory> { Data = await service.ListRuntimeResourcesAsync(new RuntimeOwnershipQuery { OwnerKind = owner, RunId = runId, Project = project, WorkspaceRoot = workspaceRoot }) }));
api.MapGet("/runtime/doctor", async (string? owner, string? runId, string? project, string? workspaceRoot, [FromServices] IOpenCodeWorkspaceMcpService service)
    => Results.Ok(new ApiEnvelope<RuntimeResourceInventory> { Data = await service.RunRuntimeDoctorAsync(new RuntimeOwnershipQuery { OwnerKind = owner, RunId = runId, Project = project, WorkspaceRoot = workspaceRoot }) }));

api.MapGet("/operations", ([FromServices] McpOperationStore operations)
    => Results.Ok(new ApiEnvelope<IReadOnlyList<McpOperationModel>> { Data = operations.List() }));
api.MapGet("/operations/{operationId}", (string operationId, [FromServices] McpOperationStore operations)
    => Results.Ok(new ApiEnvelope<McpOperationModel> { Data = operations.Get(operationId) }));
api.MapPost("/operations/{operationId}/cancel", (string operationId, [FromServices] McpOperationStore operations)
    => Results.Ok(new ApiEnvelope<McpOperationModel> { Data = operations.Cancel(operationId) }));

app.Run();

static TimeSpan? ParseTimeout(string? value)
    => string.IsNullOrWhiteSpace(value) ? null : TimeSpan.Parse(value);

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
        "operation_not_found" => 404,
        "artifact_not_found" => 404,
        "operation_not_cancellable" => 409,
        "artifact_outside_allowed_root" => 400,
        "invalid_artifact_resource_id" => 400,
        "invalid_workbook" => 400,
        "invalid_smoke_selection" => 400,
        _ => 400,
    };

public partial class Program;
