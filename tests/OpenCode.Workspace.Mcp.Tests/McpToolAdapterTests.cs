using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Mcp;
using System.Threading;
using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Mcp.Tests;

[Trait("Category", "FastProtocol")]
public sealed class McpToolAdapterTests
{
    [Fact]
    public async Task ToolRegistration_ExposesStableToolNames_OverStdio()
    {
        var stderrLines = new List<string>();
        var transport = CreateStdioTransport(stderrLines.Add);
        await using var client = await CreateClientAsync(transport, stderrLines);
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
        Assert.True(templatesResult.StructuredContent is not null || templatesResult.Content.Count > 0);
    }

    [Fact]
    public async Task ToolRegistration_DoesNotExposeTerminalControl()
    {
        var stderrLines = new List<string>();
        var transport = CreateStdioTransport(stderrLines.Add);
        await using var client = await CreateClientAsync(transport, stderrLines);
        var names = (await client.ListToolsAsync()).Select(item => item.Name).ToArray();
        var forbidden = new[] { "terminal_start", "terminal_stop", "terminal_input", "terminal_output", "terminal_resize", "terminal_attach", "terminal_takeover", "pty" };

        Assert.DoesNotContain(names, name => forbidden.Any(item => name.Contains(item, StringComparison.OrdinalIgnoreCase)));
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

        await OpenCodeWorkspaceMcpTools.ListRuntimeResources("smoke", "run-1", "proj", "/tmp/ws", fake);

        Assert.NotNull(captured);
        Assert.Equal("smoke", captured!.OwnerKind);
        Assert.Equal("run-1", captured.RunId);
        Assert.Equal("proj", captured.Project);
        Assert.Equal("/tmp/ws", captured.WorkspaceRoot);
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

    [Fact]
    public void ArchitectureGuard_ProductionMcpAdaptersDoNotOwnSharedExecution()
    {
        var root = Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Mcp");
        var source = string.Join(Environment.NewLine, Directory.EnumerateFiles(root, "*.cs").Select(File.ReadAllText));

        foreach (var forbidden in new[]
        {
            "WorkspaceOrchestrator",
            "WorkspaceSmokeRunner",
            "WorkspaceSmokeApplicationService",
            "DockerContainerRuntime",
            "RuntimeOwnershipService",
            "SpreadsheetDocument",
            "DesktopWorkspaceService",
            "InteractiveTerminalRuntimeService",
            "McpOperationStore",
            "fallbackService",
            "AddSingleton<OpenCodeWorkspaceMcpService>",
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(Path.Combine(root, "OpenCodeWorkspaceMcpService.cs")));
        Assert.True(File.Exists(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Api", "LocalHostCanonicalWorkspaceService.cs")));
    }

    [Fact]
    public void McpHostLaunch_ResolvesAbsoluteBuiltServerPath_WithoutCurrentDirectoryDependency()
    {
        var originalDirectory = Environment.CurrentDirectory;
        var tempDirectory = Path.Combine(Path.GetTempPath(), "opencode-mcp-launch", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            Environment.CurrentDirectory = tempDirectory;
            var launch = McpHostLaunch.Resolve();

            Assert.True(Path.IsPathRooted(launch.HostDllPath));
            Assert.True(File.Exists(launch.HostDllPath), launch.HostDllPath);
            Assert.True(File.Exists(launch.RuntimeConfigPath), launch.RuntimeConfigPath);
            Assert.True(File.Exists(launch.DepsPath), launch.DepsPath);
            Assert.Equal(AppContext.BaseDirectory, launch.AppBaseDirectory);
            Assert.Equal(tempDirectory, launch.CurrentWorkingDirectory);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void McpHostLaunch_MissingOutputDiagnostic_IsActionable()
    {
        var launch = McpHostLaunch.Resolve() with
        {
            HostDllPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"), "OpenCode.Workspace.Mcp.dll"),
            RuntimeConfigPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"), "OpenCode.Workspace.Mcp.runtimeconfig.json"),
            DepsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"), "OpenCode.Workspace.Mcp.deps.json"),
        };

        var message = Record.Exception(() => McpHostLaunch.AssertHostFilesExist(launch))!.Message;

        Assert.Contains("MCP server command: dotnet", message, StringComparison.Ordinal);
        Assert.Contains("MCP server path exists: False", message, StringComparison.Ordinal);
        Assert.Contains("AppContext.BaseDirectory:", message, StringComparison.Ordinal);
        Assert.Contains("Process architecture:", message, StringComparison.Ordinal);
    }

    private static McpToolEnvelope<T> ReadEnvelope<T>(CallToolResult result)
    {
        if (result.StructuredContent is null)
        {
            throw new InvalidOperationException(string.Join(" | ", result.Content.OfType<TextContentBlock>().Select(item => item.Text)));
        }

        return JsonSerializer.Deserialize<McpToolEnvelope<T>>(result.StructuredContent.Value.GetRawText())!;
    }

    private static StdioClientTransport CreateStdioTransport(Action<string>? stderrLine = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "opencode-mcp-transport", Guid.NewGuid().ToString("n"));
        var stateRoot = Path.Combine(root, "state");
        var smokeArtifactsRoot = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(smokeArtifactsRoot);
        return McpHostLaunch.CreateTransport(
            stderrLine,
            new Dictionary<string, string?>
            {
                ["mcp__catalogRoot"] = Path.Combine(TestPaths.RepositoryRoot, "catalog"),
                ["mcp__workspaceStateRoot"] = stateRoot,
                ["mcp__smokeArtifactsRoot"] = smokeArtifactsRoot,
                ["localHost__stateRoot"] = Path.Combine(root, "local-host-shared"),
                ["localHost__executableDirectory"] = Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Api", "bin", new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0"),
            });
    }

    private static async Task<McpClient> CreateClientAsync(StdioClientTransport transport, IReadOnlyList<string> stderrLines)
    {
        var launch = McpHostLaunch.Resolve();
        try
        {
            return await McpClient.CreateAsync(transport);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(McpHostLaunch.BuildStartupFailureMessage(launch, stderrLines, exception), exception);
        }
    }

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
    public Task<ExistingGitCheckoutPlan> InspectExistingGitCheckoutAsync(string repositoryPath, string workspaceName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<GitBranchValidationResult> ValidateExistingGitCheckoutBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> ImportExistingGitCheckoutAsync(ExistingGitCheckoutImportRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<string> SuggestSavePointMessageAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> CreateSavePointAsync(string workspaceId, string message, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> CreateCheckpointAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceBackupOperationResultModel> BackupWorkspaceAsync(string workspaceId, string destinationPath, bool overwriteExisting, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspacePublishAssessmentModel> AssessWorkspacePublishAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspacePublishOperationResultModel> PublishWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceRemovalOperationResultModel> RemoveWorkspaceAsync(string workspaceId, bool removeOwnedRuntimeResources, bool deleteWorkspaceFiles, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceRecoveryAssessmentModel> AssessWorkspaceRecoveryAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceSynchronizationStatusResult> GetSynchronizationStatusAsync(string workspaceId, string? environmentName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceSynchronizationOperationResult> ExportSynchronizationAsync(string workspaceId, string? environmentName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceSynchronizationOperationResult> ImportSynchronizationAsync(string workspaceId, string? environmentName = null, string? deploymentProfileOverride = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceSynchronizationOperationResult> PullSynchronizationAsync(string workspaceId, string? environmentName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceSynchronizationOperationResult> PushSynchronizationAsync(string workspaceId, string? environmentName = null, string? deploymentProfileOverride = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.WorkspaceSynchronizationExecutionResult> SynchronizeWorkspaceAsync(string workspaceId, string? environmentName = null, string? deploymentProfileOverride = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.OracleAssistantPlanOperationRecord> PlanOracleApexChangeAsync(string workspaceId, OracleApexAssistantRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.OracleAssistantApplyOperationRecord> ExecuteOracleApexPlanAsync(string workspaceId, OracleApexAssistantRequest request, OracleApexEditPlan plan, string planId, string contextRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.OracleAssistantRepairPlanOperationRecord> CreateOracleApexRepairPlanAsync(string workspaceId, OracleApexAssistantRequest request, OracleApexEditPlan sourcePlan, OracleApexValidationResult validation, string planId, string executionId, string contextRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.OracleAssistantRepairOperationRecord> ExecuteOracleApexRepairPlanAsync(string workspaceId, OracleApexAssistantRequest request, OracleApexEditPlan repairPlan, string planId, string executionId, string repairPlanId, string contextRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.OracleAssistantRollbackOperationRecord> RollBackOracleApexGeneratedChangeAsync(string workspaceId, string executionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OracleApexApplicationDiscoveryResult> DiscoverOracleApexApplicationsAsync(string workspaceId, string environmentName, string workspaceName, string parsingSchema, string sqlclProfile, string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OracleApexConnectExistingApplicationResult> ConnectExistingOracleApexApplicationAsync(string workspaceId, string environmentName, string workspaceName, string parsingSchema, int applicationId, string sqlclProfile, string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.OracleAssistantSynchronizationOperationRecord> ValidateOracleAssistantGeneratedApplicationAsync(string workspaceId, string? executionId = null, string? environmentName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.OracleAssistantSynchronizationOperationRecord> ImportOracleAssistantGeneratedApplicationAsync(string workspaceId, string? executionId = null, string? environmentName = null, bool allowNonDevelopmentDeployment = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceSynchronizationOperationResult> ValidateSynchronizationAsync(string workspaceId, string? environmentName = null, string? deploymentProfileOverride = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceSynchronizationDiffResult> DiffSynchronizationAsync(string workspaceId, string? environmentName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceTimeline> GetWorkspaceTimelineAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceCheckpointIndex> GetWorkspaceCheckpointIndexAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> ProvisionWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> PrepareWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> StartWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> ValidateWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> StopWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> RemoveWorkspaceRuntimeAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> RecoverWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> ResetWorkspaceRuntimeAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> AttachWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OpenCode.Workspace.Mcp.WorkspaceRecordModel> ReprovisionWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
