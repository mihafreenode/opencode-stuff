using Microsoft.Extensions.Logging.Abstractions;
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

        var result = await OpenCodeWorkspaceMcpTools.RunSmokeMatrixCompatibility(null, "lightweight", false, "00:10:00", fake, operations);
        var payload = JsonSerializer.Deserialize<McpOperationModel>(result.StructuredContent!.Value.GetRawText())!;

        Assert.Equal("run_smoke_matrix", payload.Kind);
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

        await OpenCodeWorkspaceMcpTools.ListRuntimeResources("smoke", "run-1", "proj", "/tmp/ws", fake);

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

        await OpenCodeWorkspaceMcpTools.CleanupSmokeResources(true, true, "run-1", fake);

        Assert.NotNull(captured);
        Assert.True(captured!.DryRun);
        Assert.True(captured.IncludeAll);
        Assert.Equal("run-1", captured.RunId);
    }

    [Fact]
    public void OperationStore_SupportsCancellation()
    {
        var store = new McpOperationStore(new OpenCodeWorkspaceMcpOptions(), NullLogger<McpOperationStore>.Instance);
        var operation = store.Start("run_smoke", string.Empty, "running", async (_, token) =>
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

    [Fact]
    public async Task OperationStore_RemovesExpiredCompletedOperations_ButKeepsActiveOnes()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var options = new OpenCodeWorkspaceMcpOptions
        {
            Operations = new OpenCodeWorkspaceMcpOperationOptions { Retention = TimeSpan.FromMinutes(5), CleanupTimeout = TimeSpan.FromSeconds(5) },
        };
        var store = new McpOperationStore(options, NullLogger<McpOperationStore>.Instance, clock);
        var completed = store.Start("run_smoke", string.Empty, "done", (_, _) => Task.FromResult<object>(new RuntimeResourceInventory()));
        await Task.Delay(100);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = store.Start("run_smoke", string.Empty, "active", async (_, token) =>
        {
            await gate.Task.WaitAsync(token);
            return new RuntimeResourceInventory();
        });

        clock.Advance(TimeSpan.FromHours(2));
        store.TrimExpired();

        Assert.DoesNotContain(store.List(), item => item.OperationId == completed.OperationId);
        Assert.Contains(store.List(), item => item.OperationId == active.OperationId);
        gate.SetCanceled();
    }

    [Fact]
    public async Task OperationStore_StopAsync_CancelsActiveOperations()
    {
        var store = new McpOperationStore(new OpenCodeWorkspaceMcpOptions(), NullLogger<McpOperationStore>.Instance);
        var operation = store.Start("run_smoke", string.Empty, "active", async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(5), token);
            return new RuntimeResourceInventory();
        });

        await store.StopAsync(CancellationToken.None);
        var stopped = store.Get(operation.OperationId);

        Assert.True(stopped.CancellationRequested);
        Assert.Equal(McpOperationStatus.Cancelled, stopped.Status);
    }

    [Fact]
    public async Task OperationStore_BoundsRecentEvents_AndReportsTruncation()
    {
        var root = Path.Combine(Path.GetTempPath(), "opencode-mcp-events", Guid.NewGuid().ToString("n"));
        var options = new OpenCodeWorkspaceMcpOptions
        {
            WorkspaceStateRoot = root,
            Operations = new OpenCodeWorkspaceMcpOperationOptions { MaxRecentEvents = 2 },
        };
        var store = new McpOperationStore(options, NullLogger<McpOperationStore>.Instance);

        var operation = store.Start("run_smoke", string.Empty, "start", async (reporter, _) =>
        {
            reporter.ReportProgress("phase1", "first", currentStep: 1, totalSteps: 3);
            reporter.ReportProgress("phase2", "second", currentStep: 2, totalSteps: 3);
            reporter.ReportProgress("phase3", "third", currentStep: 3, totalSteps: 3);
            await Task.Yield();
            return new RuntimeResourceInventory();
        });

        await Task.Delay(250);
        var current = store.Get(operation.OperationId);
        Assert.Equal(5, current.LastEventSequence);
        Assert.True(current.EventsTruncated);
        Assert.Equal(2, current.RecentEvents.Count);
        Assert.Equal("completed", current.RecentEvents[^1].Phase);
        Assert.Equal([4L, 5L], current.RecentEvents.Select(item => item.Sequence).ToArray());

        var incremental = store.Get(operation.OperationId, afterSequence: 4);
        Assert.Single(incremental.RecentEvents);
        Assert.Equal(5, incremental.RecentEvents[0].Sequence);
        Assert.False(incremental.EventsTruncated);

        var progressJsonl = current.ArtifactReferences.Single(path => path.EndsWith("operation-progress.jsonl", StringComparison.Ordinal));
        var lines = File.ReadAllLines(progressJsonl);
        Assert.Equal(5, lines.Length);
        Assert.DoesNotContain(lines, line => line.Contains("password=secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OperationStore_WritesStructuredProgressLog_AndSanitizesSecrets()
    {
        var root = Path.Combine(Path.GetTempPath(), "opencode-mcp-progress", Guid.NewGuid().ToString("n"));
        var options = new OpenCodeWorkspaceMcpOptions
        {
            WorkspaceStateRoot = root,
        };
        var store = new McpOperationStore(options, NullLogger<McpOperationStore>.Instance);

        var operation = store.Start("provision_workspace", "workspace-1", "start", async (reporter, _) =>
        {
            reporter.ReportProgress(new CommandLogEntry
            {
                Source = "docker",
                Message = "Connecting with password=secret-value",
                Phase = "startingOracleDatabase",
            });
            await Task.Yield();
            return new WorkspaceRecordModel { WorkspaceId = "workspace-1" };
        });

        await Task.Delay(250);
        var current = store.Get(operation.OperationId);
        Assert.Contains(current.RecentEvents, item => item.Message.Contains("[redacted]", StringComparison.Ordinal));

        var progressText = current.ArtifactReferences.Single(path => path.EndsWith("operation-progress.txt", StringComparison.Ordinal));
        Assert.DoesNotContain("secret-value", File.ReadAllText(progressText), StringComparison.Ordinal);
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
                ["localHost__executableDirectory"] = Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Api", "bin", "Release", "net10.0"),
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

internal sealed class FakeClock : ISystemClock
{
    public FakeClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan duration)
    {
        UtcNow += duration;
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
