using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.Mcp;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Api.IntegrationTests;

public sealed class ApiContractTests : IDisposable
{
    private readonly ApiIntegrationEnvironment _environment = new();

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task Health_And_Template_Routes_Work_Through_Http_Pipeline()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var live = await client.GetFromJsonAsync<ApiHealthResponse>("/api/v1/health/live");
        Assert.NotNull(live);
        Assert.Equal("live", live!.Status);

        var templates = await client.GetFromJsonAsync<ApiEnvelope<IReadOnlyList<WorkspaceTemplateSummaryModel>>>("/api/v1/templates");
        Assert.NotNull(templates);
        Assert.Contains(templates!.Data, item => item.TemplateId == "empty-workspace");

        var template = await client.GetFromJsonAsync<ApiEnvelope<WorkspaceTemplateDetailModel>>("/api/v1/templates/empty-workspace");
        Assert.NotNull(template);
        Assert.Equal("empty-workspace", template!.Data.Summary.TemplateId);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task Unknown_Template_Returns_404_Error_Envelope()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/templates/does-not-exist");
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("unknown_template", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task Workspace_Routes_Create_And_List_Workspaces_With_Real_Side_Effects()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/workspaces", new CreateWorkspaceRequest
        {
            TemplateId = "empty-workspace",
            WorkspaceName = "api-demo",
            DestinationRoot = _environment.WorkspaceParentRoot,
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ApiEnvelope<WorkspaceRecordModel>>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        Assert.True(Directory.Exists(created!.Data.WorkspaceRoot));
        Assert.True(File.Exists(Path.Combine(created.Data.WorkspaceRoot, "workspace.yaml")));

        var list = await client.GetFromJsonAsync<ApiEnvelope<IReadOnlyList<WorkspaceRecordModel>>>("/api/v1/workspaces");
        Assert.Contains(list!.Data, item => item.WorkspaceId == created.Data.WorkspaceId);

        var validate = await client.PostAsync($"/api/v1/workspaces/{created.Data.WorkspaceId}/validate", null);
        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task Runtime_Endpoints_Map_Filters_And_Do_Not_Expose_Broad_Operations()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                ListRuntimeResourcesHandler = query => Task.FromResult(new RuntimeResourceInventory
                {
                    Resources = [new RuntimeOwnedResource { ResourceId = query.Project ?? string.Empty, Name = query.OwnerKind ?? string.Empty, RunId = query.RunId ?? string.Empty, Type = RuntimeResourceType.Container }],
                }),
                RunRuntimeDoctorHandler = _ => Task.FromResult(new RuntimeResourceInventory()),
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<ApiEnvelope<RuntimeResourceInventory>>("/api/v1/runtime/resources?owner=smoke&runId=run-1&project=proj&workspaceRoot=/tmp/ws");
        Assert.Equal("smoke", response!.Data.Resources[0].Name);
        Assert.Equal("run-1", response.Data.Resources[0].RunId);

        var doctor = await client.GetFromJsonAsync<ApiEnvelope<RuntimeResourceInventory>>("/api/v1/runtime/doctor?owner=smoke");
        Assert.NotNull(doctor);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task Smoke_Operation_Endpoints_Start_Poll_And_Cancel_Using_Real_Http_Pipeline()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceTemplateHandler = _ => Task.FromResult(new WorkspaceTemplateDetailModel { Summary = new WorkspaceTemplateSummaryModel { TemplateId = "empty-workspace" } }),
                RunSmokeHandler = async (request, cancellationToken) =>
                {
                    await gate.Task.WaitAsync(cancellationToken);
                    return new WorkspaceSmokeResult
                    {
                        TemplateId = request.TemplateId,
                        RunId = "run-1",
                        Status = WorkspaceSmokeStatus.Passed,
                        Phase = WorkspaceSmokePhase.Completed,
                        FailureClassification = WorkspaceSmokeFailureClassification.None,
                        CleanupVerificationSucceeded = true,
                        ArtifactDirectory = _environment.SmokeArtifactsRoot,
                    };
                },
            });
        });
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/smoke/runs", new StartSmokeRunRequest { TemplateId = "empty-workspace", Timeout = "00:05:00" });
        var operation = await start.Content.ReadFromJsonAsync<McpOperationModel>();
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal("queued", operation!.CurrentPhase);

        var queued = await client.GetFromJsonAsync<ApiEnvelope<McpOperationModel>>($"/api/v1/operations/{operation.OperationId}");
        Assert.Equal(McpOperationStatus.Running, queued!.Data.Status);

        var cancel = await client.PostAsync($"/api/v1/operations/{operation.OperationId}/cancel", null);
        var cancelled = await cancel.Content.ReadFromJsonAsync<ApiEnvelope<McpOperationModel>>();
        Assert.True(cancelled!.Data.CancellationRequested);

        gate.SetCanceled();
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task Smoke_Definitions_And_Cleanup_DryRun_Return_Stable_Contracts()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var definitions = await client.GetFromJsonAsync<ApiEnvelope<WorkspaceSmokeDefinitionCatalogResult>>("/api/v1/smoke/definitions");
        Assert.Equal("1", definitions!.Data.SchemaVersion);

        var cleanup = await client.PostAsJsonAsync("/api/v1/smoke/cleanup", new CleanupSmokeRequest { DryRun = true, IncludeAll = true });
        var cleanupResult = await cleanup.Content.ReadFromJsonAsync<ApiEnvelope<SmokeCleanupResult>>();
        Assert.True(cleanupResult!.Data.DryRun);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "LiveDockerIntegration")]
    public async Task Live_Api_Smoke_Run_Completes_And_Leaves_No_Smoke_Resources()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/smoke/runs", new StartSmokeRunRequest { TemplateId = "empty-workspace", Timeout = "00:05:00" });
        var operation = await start.Content.ReadFromJsonAsync<McpOperationModel>();
        Assert.NotNull(operation);

        var completed = await WaitForOperationAsync(client, operation!.OperationId, TimeSpan.FromMinutes(4));
        Assert.Equal(McpOperationStatus.Succeeded, completed.Status);

        var doctor = await client.GetFromJsonAsync<ApiEnvelope<RuntimeResourceInventory>>("/api/v1/runtime/doctor?owner=smoke");
        Assert.Empty(doctor!.Data.Resources);
        Assert.Empty(doctor.Data.Orphans);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "LiveDockerIntegration")]
    public async Task Live_Api_Smoke_Cancellation_Cleans_Up_And_Reports_Cancelled()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/smoke/runs", new StartSmokeRunRequest { TemplateId = "web-testing", Timeout = "00:05:00" });
        var operation = await start.Content.ReadFromJsonAsync<McpOperationModel>();
        Assert.NotNull(operation);

        var active = await WaitForOperationStateAsync(client, operation!.OperationId, TimeSpan.FromMinutes(2), item => item.Status == McpOperationStatus.Running && item.StartedUtc is not null);
        Assert.False(active.CancellationRequested);

        var cancel = await client.PostAsync($"/api/v1/operations/{operation.OperationId}/cancel", null);
        var cancelled = await cancel.Content.ReadFromJsonAsync<ApiEnvelope<McpOperationModel>>();
        Assert.True(cancelled!.Data.CancellationRequested);

        var completed = await WaitForOperationAsync(client, operation.OperationId, TimeSpan.FromMinutes(3));
        Assert.Equal(McpOperationStatus.Cancelled, completed.Status);
        Assert.Equal("cancelled", completed.FailureClassification, ignoreCase: true);

        var doctor = await client.GetFromJsonAsync<ApiEnvelope<RuntimeResourceInventory>>("/api/v1/runtime/doctor?owner=smoke");
        Assert.Empty(doctor!.Data.Resources);
        Assert.Empty(doctor.Data.Orphans);
    }

    public void Dispose() => _environment.Dispose();

    private static async Task<McpOperationModel> WaitForOperationAsync(HttpClient client, string operationId, TimeSpan timeout)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout)
        {
            var operation = await client.GetFromJsonAsync<ApiEnvelope<McpOperationModel>>($"/api/v1/operations/{operationId}");
            if (operation!.Data.Status is McpOperationStatus.Succeeded or McpOperationStatus.Failed or McpOperationStatus.Cancelled)
            {
                return operation.Data;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Operation '{operationId}' did not complete in time.");
    }

    private static async Task<McpOperationModel> WaitForOperationStateAsync(HttpClient client, string operationId, TimeSpan timeout, Func<McpOperationModel, bool> predicate)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout)
        {
            var operation = await client.GetFromJsonAsync<ApiEnvelope<McpOperationModel>>($"/api/v1/operations/{operationId}");
            if (predicate(operation!.Data))
            {
                return operation.Data;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Operation '{operationId}' did not reach the expected state in time.");
    }
}

internal sealed class FakeApiService : IOpenCodeWorkspaceMcpService
{
    public Func<string, Task<WorkspaceTemplateDetailModel>>? GetWorkspaceTemplateHandler { get; init; }
    public Func<RuntimeOwnershipQuery, Task<RuntimeResourceInventory>>? ListRuntimeResourcesHandler { get; init; }
    public Func<RuntimeOwnershipQuery, Task<RuntimeResourceInventory>>? RunRuntimeDoctorHandler { get; init; }
    public Func<WorkspaceSmokeSingleRunRequest, CancellationToken, Task<WorkspaceSmokeResult>>? RunSmokeHandler { get; init; }

    public ServerHealthModel GetServerHealth() => new();
    public Task<IReadOnlyList<WorkspaceTemplateSummaryModel>> ListWorkspaceTemplatesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkspaceTemplateSummaryModel>>([new WorkspaceTemplateSummaryModel { TemplateId = "empty-workspace" }]);
    public Task<WorkspaceTemplateDetailModel> GetWorkspaceTemplateAsync(string templateId, CancellationToken cancellationToken = default) => GetWorkspaceTemplateHandler?.Invoke(templateId) ?? Task.FromResult(new WorkspaceTemplateDetailModel { Summary = new WorkspaceTemplateSummaryModel { TemplateId = templateId } });
    public Task<WorkspaceSmokeDefinitionCatalogResult> ListSmokeDefinitionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceSmokeDefinitionCatalogResult());
    public Task<IReadOnlyList<WorkspaceRecordModel>> ListWorkspacesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkspaceRecordModel>>([]);
    public Task<WorkspaceRecordModel> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new OpenCodeWorkspaceMcpException("workspace_not_found", $"Workspace '{workspaceId}' was not found.");
    public Task<WorkspaceRecordModel> CreateWorkspaceAsync(string templateId, string workspaceName, string destinationRoot, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceRecordModel> ProvisionWorkspaceAsync(string workspaceId, Action<string>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceRecordModel> ValidateWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceRecordModel> StopWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceRecordModel> RemoveWorkspaceRuntimeAsync(string workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<IReadOnlyList<WorkspaceSmokeDefinition>> SelectSmokeDefinitionsAsync(WorkspaceSmokeDefinitionSelectionRequest request, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkspaceSmokeDefinition>>([new WorkspaceSmokeDefinition { TemplateId = "empty-workspace", DisplayName = "Empty Workspace", Family = "lightweight", Supported = true }]);
    public Task<WorkspaceSmokeResult> RunSmokeAsync(WorkspaceSmokeSingleRunRequest request, CancellationToken cancellationToken = default) => RunSmokeHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new WorkspaceSmokeResult { TemplateId = request.TemplateId, RunId = "run-1", Status = WorkspaceSmokeStatus.Passed, Phase = WorkspaceSmokePhase.Completed, FailureClassification = WorkspaceSmokeFailureClassification.None, CleanupVerificationSucceeded = true });
    public Task<WorkspaceSmokeMatrixResult> RunSmokeMatrixAsync(WorkspaceSmokeMatrixRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceSmokeMatrixResult { MatrixRunId = "matrix-1", SelectedTemplates = request.TemplateIds, Status = WorkspaceSmokeStatus.Passed });
    public Task<RuntimeResourceInventory> ListRuntimeResourcesAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken = default) => ListRuntimeResourcesHandler?.Invoke(query) ?? Task.FromResult(new RuntimeResourceInventory());
    public Task<RuntimeResourceInventory> RunRuntimeDoctorAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken = default) => RunRuntimeDoctorHandler?.Invoke(query) ?? Task.FromResult(new RuntimeResourceInventory());
    public Task<SmokeCleanupResult> CleanupSmokeResourcesAsync(SmokeCleanupOptions options, CancellationToken cancellationToken = default) => Task.FromResult(new SmokeCleanupResult { Succeeded = true, DryRun = options.DryRun, VerificationSucceeded = true });
    public Task<IReadOnlyList<ArtifactListItem>> ListWorkspaceArtifactsAsync(string workspaceId, string? relativePath, bool recursive, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ArtifactListItem>>([]);
    public Task<ArtifactReadModel> GetWorkspaceArtifactAsync(string workspaceId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ArtifactListItem>> ListSmokeArtifactsAsync(string runId, string? relativePath, bool recursive, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ArtifactListItem>>([]);
    public Task<ArtifactReadModel> GetSmokeArtifactAsync(string runId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ArtifactReadModel> ReadArtifactByResourceUriAsync(string resourceUri, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ArtifactResourceReadModel> ReadArtifactResourceAsync(string resourceUri, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ExcelProcessResultModel> ProcessExcelArtifactAsync(string sourcePath, string? destinationWorkspaceId, string? processingTemplateId, string? outputLogicalName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
