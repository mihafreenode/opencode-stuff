using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.Mcp;
using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Mcp.Tests;

public sealed class McpToolAdapterTests
{
    [Fact]
    public async Task ToolRegistration_ExposesStableToolNames_OverStdio()
    {
        var transport = CreateStdioTransport();
        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();
        var toolNames = tools.Select(item => item.Name).ToArray();

        Assert.Contains("list_workspace_templates", toolNames);
        Assert.Contains("get_workspace_template", toolNames);
        Assert.Contains("list_workspaces", toolNames);
        Assert.Contains("create_workspace", toolNames);
        Assert.Contains("run_smoke", toolNames);
        Assert.Contains("run_smoke_matrix", toolNames);
        Assert.Contains("list_runtime_resources", toolNames);
        Assert.Contains("process_excel_artifact", toolNames);
        Assert.Contains("get_operation", toolNames);
        Assert.Contains("cancel_operation", toolNames);

        var templatesResult = await client.CallToolAsync("list_workspace_templates", cancellationToken: CancellationToken.None);
        Assert.Null(templatesResult.IsError);
        Assert.NotNull(templatesResult.StructuredContent);
    }

    [Fact]
    public async Task CreateWorkspace_MapsRequestIntoFacade()
    {
        var fake = new FakeMcpService
        {
            CreateWorkspaceHandler = (templateId, name, root) => Task.FromResult(new OpenCode.Workspace.Mcp.WorkspaceRecordModel { WorkspaceId = $"{templateId}:{name}:{root}" }),
        };

        var result = await OpenCodeWorkspaceMcpTools.CreateWorkspace("empty-workspace", "demo", "/tmp/workspaces", fake);
        var payload = ReadEnvelope<OpenCode.Workspace.Mcp.WorkspaceRecordModel>(result);

        Assert.Equal("empty-workspace:demo:/tmp/workspaces", payload.Data.WorkspaceId);
        Assert.Equal("1", payload.ContractVersion);
    }

    [Fact]
    public async Task RunSmokeMatrix_UsesCoreSelectionAndStartsOperation()
    {
        var fake = new FakeMcpService
        {
            SelectSmokeDefinitionsHandler = request => Task.FromResult<IReadOnlyList<WorkspaceSmokeDefinition>>([new WorkspaceSmokeDefinition { TemplateId = "empty-workspace", DisplayName = "Empty", Family = request.Family ?? "lightweight", Supported = true }]),
            RunSmokeMatrixHandler = request => Task.FromResult(new WorkspaceSmokeMatrixResult { MatrixRunId = "matrix-1", SelectedTemplates = request.TemplateIds, Status = WorkspaceSmokeStatus.Passed, ArtifactDirectory = "/tmp/matrix" }),
        };
        var operations = new McpOperationStore(new OpenCodeWorkspaceMcpOptions(), NullLogger<McpOperationStore>.Instance);

        var result = await OpenCodeWorkspaceMcpTools.RunSmokeMatrix(fake, operations, null, "lightweight", false, "00:10:00");
        var payload = ReadEnvelope<McpOperationModel>(result);

        Assert.Equal("run_smoke_matrix", payload.Data.Kind);
        Assert.Single(operations.List());
    }

    [Fact]
    public async Task RuntimeInventory_MapsFilters()
    {
        RuntimeOwnershipQuery? captured = null;
        var fake = new FakeMcpService
        {
            ListRuntimeResourcesHandler = query =>
            {
                captured = query;
                return Task.FromResult(new RuntimeResourceInventory());
            },
        };

        await OpenCodeWorkspaceMcpTools.ListRuntimeResources(fake, "smoke", "run-1", "proj", "/tmp/ws");

        Assert.NotNull(captured);
        Assert.Equal("smoke", captured!.OwnerKind);
        Assert.Equal("run-1", captured.RunId);
        Assert.Equal("proj", captured.Project);
        Assert.Equal("/tmp/ws", captured.WorkspaceRoot);
    }

    [Fact]
    public async Task CleanupSmokeResources_MapsDryRunRequest()
    {
        SmokeCleanupOptions? captured = null;
        var fake = new FakeMcpService
        {
            CleanupSmokeResourcesHandler = options =>
            {
                captured = options;
                return Task.FromResult(new SmokeCleanupResult { Succeeded = true, VerificationSucceeded = true });
            },
        };

        await OpenCodeWorkspaceMcpTools.CleanupSmokeResources(true, true, fake, "run-1");

        Assert.NotNull(captured);
        Assert.True(captured!.DryRun);
        Assert.True(captured.IncludeAll);
        Assert.Equal("run-1", captured.RunId);
    }

    [Fact]
    public void OperationStore_SupportsCancellation()
    {
        var store = new McpOperationStore(new OpenCodeWorkspaceMcpOptions(), NullLogger<McpOperationStore>.Instance);
        var operation = store.Start("run_smoke", string.Empty, "running", async token =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new RuntimeResourceInventory();
        });

        var cancelled = store.Cancel(operation.OperationId);

        Assert.True(cancelled.CancellationRequested);
    }

    [Fact]
    public void McpSource_HasNoDirectConsoleWrites()
    {
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Mcp"), "*.cs", SearchOption.AllDirectories);
        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Console.Write", text, StringComparison.Ordinal);
        }
    }

    private static McpToolEnvelope<T> ReadEnvelope<T>(CallToolResult result)
    {
        if (result.StructuredContent is null)
        {
            throw new InvalidOperationException(string.Join(" | ", result.Content.OfType<TextContentBlock>().Select(item => item.Text)));
        }

        return JsonSerializer.Deserialize<McpToolEnvelope<T>>(result.StructuredContent.Value.GetRawText())!;
    }

    private static StdioClientTransport CreateStdioTransport()
        => new(new StdioClientTransportOptions
        {
            Name = "OpenCode Workspace MCP",
            Command = "dotnet",
            Arguments = [Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Mcp", "bin", "Debug", "net10.0", "OpenCode.Workspace.Mcp.dll")],
            WorkingDirectory = TestPaths.RepositoryRoot,
        });

}

internal sealed class FakeMcpService : IOpenCodeWorkspaceMcpService
{
    public Func<string, string, string, Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel>>? CreateWorkspaceHandler { get; init; }
    public Func<WorkspaceSmokeDefinitionSelectionRequest, Task<IReadOnlyList<WorkspaceSmokeDefinition>>>? SelectSmokeDefinitionsHandler { get; init; }
    public Func<WorkspaceSmokeMatrixRunRequest, Task<WorkspaceSmokeMatrixResult>>? RunSmokeMatrixHandler { get; init; }
    public Func<RuntimeOwnershipQuery, Task<RuntimeResourceInventory>>? ListRuntimeResourcesHandler { get; init; }
    public Func<SmokeCleanupOptions, Task<SmokeCleanupResult>>? CleanupSmokeResourcesHandler { get; init; }

    public ServerHealthModel GetServerHealth() => new();
    public Task<IReadOnlyList<OpenCode.Workspace.Mcp.WorkspaceTemplateSummaryModel>> ListWorkspaceTemplatesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceTemplateDetailModel> GetWorkspaceTemplateAsync(string templateId, CancellationToken cancellationToken = default) => Task.FromResult(new OpenCode.Workspace.Mcp.WorkspaceTemplateDetailModel());
    public Task<WorkspaceSmokeDefinitionCatalogResult> ListSmokeDefinitionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<OpenCode.Workspace.Mcp.WorkspaceRecordModel>> ListWorkspacesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> CreateWorkspaceAsync(string templateId, string workspaceName, string destinationRoot, CancellationToken cancellationToken = default) => CreateWorkspaceHandler!(templateId, workspaceName, destinationRoot);
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> ProvisionWorkspaceAsync(string workspaceId, Action<string>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> ValidateWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> StopWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> RemoveWorkspaceRuntimeAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<WorkspaceSmokeDefinition>> SelectSmokeDefinitionsAsync(WorkspaceSmokeDefinitionSelectionRequest request, CancellationToken cancellationToken = default) => SelectSmokeDefinitionsHandler!(request);
    public Task<WorkspaceSmokeResult> RunSmokeAsync(WorkspaceSmokeSingleRunRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceSmokeMatrixResult> RunSmokeMatrixAsync(WorkspaceSmokeMatrixRunRequest request, CancellationToken cancellationToken = default) => RunSmokeMatrixHandler!(request);
    public Task<RuntimeResourceInventory> ListRuntimeResourcesAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken = default) => ListRuntimeResourcesHandler!(query);
    public Task<RuntimeResourceInventory> RunRuntimeDoctorAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<SmokeCleanupResult> CleanupSmokeResourcesAsync(SmokeCleanupOptions options, CancellationToken cancellationToken = default) => CleanupSmokeResourcesHandler!(options);
    public Task<IReadOnlyList<ArtifactListItem>> ListWorkspaceArtifactsAsync(string workspaceId, string? relativePath, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ArtifactReadModel> GetWorkspaceArtifactAsync(string workspaceId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ArtifactListItem>> ListSmokeArtifactsAsync(string runId, string? relativePath, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ArtifactReadModel> GetSmokeArtifactAsync(string runId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ArtifactReadModel> ReadArtifactByResourceUriAsync(string resourceUri, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ArtifactResourceReadModel> ReadArtifactResourceAsync(string resourceUri, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ExcelProcessResultModel> ProcessExcelArtifactAsync(string sourcePath, string? destinationWorkspaceId, string? processingTemplateId, string? outputLogicalName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
