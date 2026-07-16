using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenCode.Workspace.Api;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Mcp.Tests;

public sealed class PackagedDistributionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "opencode package tests", Guid.NewGuid().ToString("n"));
    private readonly string? _artifactRoot = Environment.GetEnvironmentVariable("OPENCODE_PACKAGE_TEST_ARTIFACT_ROOT");

    [Fact]
    [Trait("Category", "PackageIntegration")]
    public async Task ExtractedDistribution_ResolvesPackagedContent_AndHostsExitGracefully()
    {
        var packageRoot = await CreateExtractedDistributionAsync();
        var outsideRepositoryRoot = Path.Combine(_root, "outside repo");
        Directory.CreateDirectory(outsideRepositoryRoot);
        WriteTextArtifact(EnsureArtifactDirectory("packaged-host-validation"), "distribution-manifest.txt", BuildDistributionManifest(packageRoot));

        Assert.True(File.Exists(Path.Combine(packageRoot, "LICENSE")));
        Assert.True(File.Exists(Path.Combine(packageRoot, "THIRD-PARTY-NOTICES.md")));
        Assert.True(Directory.Exists(Path.Combine(packageRoot, "catalog", "templates")));

        var desktopServices = new WorkspaceDesktopServiceFactory().Create(Path.Combine(packageRoot, "bin", "desktop"), Path.Combine(_root, "appdata"));
        Assert.Equal(Path.Combine(packageRoot, "catalog"), desktopServices.InstallationLayout.CatalogRoot);
        Assert.NotEmpty(desktopServices.CatalogProvider.LoadTemplates());

        var cliExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "cli"), "opencode-workspace-cli");
        await using var cliSmoke = await PackagedProcessHarness.StartAsync("cli-smoke-list", cliExecutable, ["smoke", "list", "--format", "json"], outsideRepositoryRoot);
        await cliSmoke.WaitForExitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(0, cliSmoke.ExitCode);
        Assert.Contains("empty-workspace", cliSmoke.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(TestPaths.RepositoryRoot, cliSmoke.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fatal", cliSmoke.StandardError, StringComparison.OrdinalIgnoreCase);

        await using var cliRuntime = await PackagedProcessHarness.StartAsync("cli-runtime-list", cliExecutable, ["runtime", "list", "--format", "json"], outsideRepositoryRoot);
        await cliRuntime.WaitForExitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(0, cliRuntime.ExitCode);
        Assert.Contains("resources", cliRuntime.StandardOutput, StringComparison.Ordinal);

        var apiExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "api"), "opencode-workspace-api");
        var apiPort = PackagedHostValidationHelpers.GetFreeTcpPort();
        await using var api = await PackagedProcessHarness.StartAsync(
            "api",
            apiExecutable,
            Array.Empty<string>(),
            outsideRepositoryRoot,
            new Dictionary<string, string?>
            {
                ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{apiPort}",
                ["mcp__catalogRoot"] = null,
                ["mcp__workspaceStateRoot"] = Path.Combine(_root, "api-state"),
                ["mcp__smokeArtifactsRoot"] = Path.Combine(_root, "api-artifacts"),
            });
        using var apiClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{apiPort}/") };
        await PackagedHostValidationHelpers.WaitForApiHealthyAsync(apiClient, TimeSpan.FromSeconds(60));
        Assert.Equal("live", (await apiClient.GetFromJsonAsync<ApiHealthResponse>("api/v1/health/live"))!.Status);
        var ready = await apiClient.GetFromJsonAsync<ApiHealthResponse>("api/v1/health/ready");
        Assert.NotNull(ready);
        var apiTemplates = await apiClient.GetFromJsonAsync<ApiEnvelope<IReadOnlyList<WorkspaceTemplateSummaryModel>>>("api/v1/templates");
        Assert.Contains(apiTemplates!.Data, item => item.TemplateId == "empty-workspace");
        var smokeDefinitions = await apiClient.GetStringAsync("api/v1/smoke/definitions");
        Assert.Contains("empty-workspace", smokeDefinitions, StringComparison.Ordinal);
        var apiHealth = await apiClient.GetFromJsonAsync<ApiEnvelope<ServerHealthModel>>("api/v1/server/health");
        Assert.Equal(Path.Combine(packageRoot, "catalog"), apiHealth!.Data.CatalogRoot);
        await api.RequestGracefulShutdownByClosingStandardInputAsync(TimeSpan.FromSeconds(30));
        Assert.False(api.Report.ForcedTerminationRequired);
        Assert.Equal(0, api.ExitCode);

        var mcpExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "mcp"), "opencode-workspace-mcp");
        await using var mcp = await PackagedMcpHarness.StartAsync(
            mcpExecutable,
            outsideRepositoryRoot,
            new Dictionary<string, string?>
            {
                ["mcp__catalogRoot"] = null,
                ["mcp__workspaceStateRoot"] = Path.Combine(_root, "mcp-state"),
                ["mcp__smokeArtifactsRoot"] = Path.Combine(_root, "mcp-artifacts"),
            },
            TimeSpan.FromSeconds(60));
        var tools = await mcp.Client.ListToolsAsync();
        Assert.Contains(tools, item => item.Name == "list_workspace_templates");
        Assert.Contains(tools, item => item.Name == "get_operation");
        var templates = await mcp.Client.CallToolAsync("list_workspace_templates");
        var templateEnvelope = JsonSerializer.Deserialize<McpToolEnvelope<IReadOnlyList<WorkspaceTemplateSummaryModel>>>(templates.StructuredContent!.Value.GetRawText())!;
        Assert.Contains(templateEnvelope.Data, item => item.TemplateId == "empty-workspace");
        var smokeDefinitionsTool = await mcp.Client.CallToolAsync("list_smoke_definitions");
        var smokeDefinitionsEnvelope = JsonSerializer.Deserialize<McpToolEnvelope<WorkspaceSmokeDefinitionCatalogResult>>(smokeDefinitionsTool.StructuredContent!.Value.GetRawText())!;
        Assert.Contains(smokeDefinitionsEnvelope.Data.Definitions, item => item.TemplateId == "empty-workspace");
        var resourceTemplates = await mcp.Client.ListResourceTemplatesAsync();
        Assert.Contains(resourceTemplates, item => item.UriTemplate == "opencode://templates/{templateId}");
        var serverHealth = await mcp.Client.ReadResourceAsync("opencode://server/health");
        var serverHealthText = serverHealth.Contents.OfType<TextResourceContents>().Single().Text;
        var mcpHealth = JsonSerializer.Deserialize<ServerHealthModel>(serverHealthText, OpenCodeWorkspaceMcpContract.JsonOptions)!;
        Assert.Equal(Path.Combine(packageRoot, "catalog"), mcpHealth.CatalogRoot);
        await mcp.DisposeClientAndTransportAsync(TimeSpan.FromSeconds(30));
        Assert.False(mcp.Report.ForcedTerminationRequired);
        Assert.NotNull(mcp.Report.ExitedUtc);
        Assert.False(ProcessStillRunning(mcp.Report.ProcessId, mcp.Report.ExecutablePath));

        Directory.Delete(packageRoot, recursive: true);
        Assert.False(Directory.Exists(packageRoot));
    }

    [Fact]
    [Trait("Category", "PackageIntegration")]
    [Trait("Category", "LiveDockerIntegration")]
    public async Task PackagedMcp_RunSmoke_ReportsIncrementalProgress_AndShutsDownCleanly()
    {
        if (!await DockerIsAvailableAsync())
        {
            return;
        }

        var packageRoot = await CreateExtractedDistributionAsync();
        var outsideRepositoryRoot = Path.Combine(_root, "outside repo smoke");
        Directory.CreateDirectory(outsideRepositoryRoot);
        var mcpExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "mcp"), "opencode-workspace-mcp");
        var smokeArtifactsRoot = Path.Combine(_root, "packaged-smoke-artifacts");

        await using var mcp = await PackagedMcpHarness.StartAsync(
            mcpExecutable,
            outsideRepositoryRoot,
            new Dictionary<string, string?>
            {
                ["mcp__catalogRoot"] = null,
                ["mcp__workspaceStateRoot"] = Path.Combine(_root, "packaged-mcp-state"),
                ["mcp__smokeArtifactsRoot"] = smokeArtifactsRoot,
            },
            TimeSpan.FromSeconds(60));

        var packageArtifactRoot = EnsureArtifactDirectory("packaged-lightweight-smoke");
        WriteTextArtifact(packageArtifactRoot, "distribution-manifest.txt", BuildDistributionManifest(packageRoot));
        WriteTextArtifact(packageArtifactRoot, "mcp-stderr-startup.log", string.Join(Environment.NewLine, mcp.StandardErrorLines));

        var preflightCleanup = await mcp.Client.CallToolAsync("cleanup_smoke_resources", new Dictionary<string, object?>
        {
            ["dryRun"] = false,
            ["includeAll"] = true,
        });
        var preflightCleanupEnvelope = JsonSerializer.Deserialize<McpToolEnvelope<SmokeCleanupResult>>(preflightCleanup.StructuredContent!.Value.GetRawText())!;
        Assert.True(preflightCleanupEnvelope.Data.Succeeded);
        Assert.True(preflightCleanupEnvelope.Data.VerificationSucceeded);
        WriteJsonArtifact(packageArtifactRoot, "preflight-cleanup.json", preflightCleanupEnvelope.Data);

        var start = await mcp.Client.CallToolAsync("run_smoke", new Dictionary<string, object?>
        {
            ["templateId"] = "empty-workspace",
            ["timeout"] = "00:05:00",
        });
        var operation = JsonSerializer.Deserialize<McpOperationModel>(start.StructuredContent!.Value.GetRawText())!;
        Assert.NotEmpty(operation.OperationId);
        WriteJsonArtifact(packageArtifactRoot, "operation-start.json", operation);

        McpOperationModel current = operation;
        long afterSequence = 0;
        var seenSequences = new HashSet<long>();
        var allEvents = new List<WorkspaceOperationProgressEvent>();
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(4);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await mcp.Client.CallToolAsync("get_operation", new Dictionary<string, object?>
            {
                ["operationId"] = operation.OperationId,
                ["afterSequence"] = afterSequence,
            });
            current = JsonSerializer.Deserialize<McpToolEnvelope<McpOperationModel>>(result.StructuredContent!.Value.GetRawText())!.Data;
            foreach (var progressEvent in current.RecentEvents)
            {
                Assert.True(seenSequences.Add(progressEvent.Sequence));
                allEvents.Add(progressEvent);
            }

            afterSequence = current.LastEventSequence;
            if (current.Status is McpOperationStatus.Succeeded or McpOperationStatus.Failed or McpOperationStatus.Cancelled)
            {
                break;
            }

            await Task.Delay(250);
        }

        Assert.Equal(McpOperationStatus.Succeeded, current.Status);
        Assert.True(current.LastEventSequence > 0);
        Assert.NotEmpty(current.ArtifactReferences);
        Assert.Contains(current.ArtifactReferences, path => path.EndsWith("operation-progress.jsonl", StringComparison.Ordinal));
        Assert.NotEmpty(allEvents);
        Assert.Contains(allEvents, item => item.Phase == "queued");
        Assert.Contains(allEvents, item => item.Phase == "completed");
        Assert.Contains(allEvents, item => item.Phase == "creatingWorkspace");
        Assert.Contains(allEvents, item => item.Phase == "provisioning");
        Assert.Contains(allEvents, item => item.Phase == "validating");
        Assert.Contains(allEvents, item => item.Phase == "cleaningUp");
        Assert.Contains(allEvents, item => item.Phase == "verifyingCleanup");
        Assert.Equal(allEvents.Select(item => item.Sequence).OrderBy(item => item).ToArray(), allEvents.Select(item => item.Sequence).ToArray());

        var smokeResult = current.Result!.Value.Deserialize<OpenCode.Workspace.Core.Smoke.WorkspaceSmokeResult>();
        Assert.NotNull(smokeResult);
        Assert.True(smokeResult!.CleanupVerificationSucceeded);
        Assert.True(smokeResult.CleanupResult?.VerificationSucceeded ?? false);
        WriteJsonArtifact(packageArtifactRoot, "operation-final.json", current);
        WriteJsonArtifact(packageArtifactRoot, "smoke-result.json", smokeResult);

        var smokeSummary = await mcp.Client.ReadResourceAsync($"opencode://smoke/{smokeResult.RunId}/summary");
        var smokeSummaryText = smokeSummary.Contents.OfType<TextResourceContents>().Single().Text;
        Assert.Contains("empty-workspace", smokeSummaryText, StringComparison.Ordinal);
        WriteTextArtifact(packageArtifactRoot, "smoke-summary.json", smokeSummaryText);

        var runtimeDoctor = await mcp.Client.CallToolAsync("run_runtime_doctor", new Dictionary<string, object?> { ["owner"] = "smoke" });
        var runtimeDoctorEnvelope = JsonSerializer.Deserialize<McpToolEnvelope<RuntimeResourceInventory>>(runtimeDoctor.StructuredContent!.Value.GetRawText())!;
        Assert.Empty(runtimeDoctorEnvelope.Data.Resources);
        Assert.Empty(runtimeDoctorEnvelope.Data.Orphans);
        WriteJsonArtifact(packageArtifactRoot, "runtime-doctor.json", runtimeDoctorEnvelope.Data);

        var runtimeInventory = await mcp.Client.CallToolAsync("list_runtime_resources", new Dictionary<string, object?> { ["owner"] = "smoke" });
        var runtimeInventoryEnvelope = JsonSerializer.Deserialize<McpToolEnvelope<RuntimeResourceInventory>>(runtimeInventory.StructuredContent!.Value.GetRawText())!;
        Assert.Empty(runtimeInventoryEnvelope.Data.Resources);
        WriteJsonArtifact(packageArtifactRoot, "runtime-inventory.json", runtimeInventoryEnvelope.Data);

        var jsonlPath = current.ArtifactReferences.Single(path => path.EndsWith("operation-progress.jsonl", StringComparison.Ordinal));
        var textPath = current.ArtifactReferences.Single(path => path.EndsWith("operation-progress.txt", StringComparison.Ordinal));
        Assert.True(File.Exists(jsonlPath));
        Assert.True(File.Exists(textPath));
        var jsonlLines = File.ReadAllLines(jsonlPath);
        var textLog = File.ReadAllText(textPath);
        Assert.NotEmpty(jsonlLines);
        Assert.DoesNotContain(jsonlLines, line => line.Contains("password=", StringComparison.OrdinalIgnoreCase) || line.Contains("token=", StringComparison.OrdinalIgnoreCase) || line.Contains("secret=", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("password=", textLog, StringComparison.OrdinalIgnoreCase);
        var progressEntries = jsonlLines.Select(line => JsonSerializer.Deserialize<WorkspaceOperationProgressEvent>(line, OpenCodeWorkspaceMcpContract.JsonOptions)!).ToArray();
        Assert.Equal(progressEntries.Select(item => item.Sequence).OrderBy(item => item).ToArray(), progressEntries.Select(item => item.Sequence).ToArray());
        Assert.All(progressEntries, entry => Assert.Equal(TimeSpan.Zero, entry.TimestampUtc.Offset));
        Assert.Equal(current.LastEventSequence, progressEntries[^1].Sequence);
        Assert.Equal(current.CurrentPhase, progressEntries[^1].Phase);
        Assert.Equal(current.ProgressMessage, progressEntries[^1].Message);
        WriteTextArtifact(packageArtifactRoot, "operation-progress.jsonl", string.Join(Environment.NewLine, jsonlLines));
        WriteTextArtifact(packageArtifactRoot, "operation-progress.txt", textLog);

        await mcp.DisposeClientAndTransportAsync(TimeSpan.FromSeconds(30));
        Assert.False(mcp.Report.ForcedTerminationRequired);
        Assert.NotNull(mcp.Report.ExitedUtc);
        Assert.False(ProcessStillRunning(mcp.Report.ProcessId, mcp.Report.ExecutablePath));
        WriteJsonArtifact(packageArtifactRoot, "mcp-lifecycle.json", mcp.Report);
        WriteTextArtifact(packageArtifactRoot, "mcp-stderr-final.log", string.Join(Environment.NewLine, mcp.StandardErrorLines));
    }

    [Fact]
    [Trait("Category", "PackagedOracleMcpIntegration")]
    public async Task PackagedMcp_OracleApexlangProvisioning_ReportsProgress_AndCleansUp()
    {
        if (!ShouldRunPackagedOracleValidation())
        {
            return;
        }

        var packageRoot = await CreateExtractedDistributionAsync();
        var outsideRepositoryRoot = Path.Combine(_root, "outside repo oracle");
        Directory.CreateDirectory(outsideRepositoryRoot);
        var artifactRoot = EnsureArtifactDirectory("packaged-oracle-mcp");
        WriteTextArtifact(artifactRoot, "distribution-manifest.txt", BuildDistributionManifest(packageRoot));
        var mcpExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "mcp"), "opencode-workspace-mcp");

        await using var mcp = await PackagedMcpHarness.StartAsync(
            mcpExecutable,
            outsideRepositoryRoot,
            new Dictionary<string, string?>
            {
                ["mcp__catalogRoot"] = null,
                ["mcp__workspaceStateRoot"] = Path.Combine(_root, "packaged-oracle-state"),
                ["mcp__smokeArtifactsRoot"] = Path.Combine(_root, "packaged-oracle-artifacts"),
            },
            TimeSpan.FromSeconds(60));

        var templates = await mcp.Client.CallToolAsync("list_workspace_templates");
        WriteTextArtifact(artifactRoot, "templates.json", templates.StructuredContent!.Value.GetRawText());

        var create = await mcp.Client.CallToolAsync("create_workspace", new Dictionary<string, object?>
        {
            ["templateId"] = "oracle-apexlang-demo",
            ["workspaceName"] = "packaged-oracle-apexlang",
            ["destinationRoot"] = outsideRepositoryRoot,
        });
        var workspace = JsonSerializer.Deserialize<McpToolEnvelope<WorkspaceRecordModel>>(create.StructuredContent!.Value.GetRawText())!.Data;
        WriteJsonArtifact(artifactRoot, "workspace-created.json", workspace);

        var provision = await mcp.Client.CallToolAsync("provision_workspace", new Dictionary<string, object?>
        {
            ["workspaceId"] = workspace.WorkspaceId,
        });
        var operation = JsonSerializer.Deserialize<McpOperationModel>(provision.StructuredContent!.Value.GetRawText())!;
        WriteJsonArtifact(artifactRoot, "provision-operation-start.json", operation);

        var seen = new HashSet<long>();
        var oracleEvents = new List<WorkspaceOperationProgressEvent>();
        var afterSequence = 0L;
        McpOperationModel current = operation;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(45);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var polled = await mcp.Client.CallToolAsync("get_operation", new Dictionary<string, object?>
            {
                ["operationId"] = operation.OperationId,
                ["afterSequence"] = afterSequence,
            });
            current = JsonSerializer.Deserialize<McpToolEnvelope<McpOperationModel>>(polled.StructuredContent!.Value.GetRawText())!.Data;
            foreach (var progressEvent in current.RecentEvents)
            {
                Assert.True(seen.Add(progressEvent.Sequence));
                oracleEvents.Add(progressEvent);
            }

            afterSequence = current.LastEventSequence;
            if (current.Status is McpOperationStatus.Succeeded or McpOperationStatus.Failed or McpOperationStatus.Cancelled)
            {
                break;
            }

            await Task.Delay(1000);
        }

        WriteJsonArtifact(artifactRoot, "provision-operation-final.json", current);
        WriteJsonArtifact(artifactRoot, "provision-events.json", oracleEvents);
        Assert.Equal(McpOperationStatus.Succeeded, current.Status);
        Assert.Contains(oracleEvents, item => item.Phase.Contains("preparing", StringComparison.OrdinalIgnoreCase) || item.Message.Contains("Preparing workspace", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(oracleEvents, item => item.Phase.Contains("buildingWorkspaceImage", StringComparison.OrdinalIgnoreCase) || item.Message.Contains("Building workspace image", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(oracleEvents, item => item.Phase.Contains("starting", StringComparison.OrdinalIgnoreCase) || item.Message.Contains("Starting Oracle", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(oracleEvents, item => item.Phase.Contains("validatingXdb", StringComparison.OrdinalIgnoreCase) || item.Message.Contains("XDB", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(oracleEvents, item => item.Phase.Contains("installingApex", StringComparison.OrdinalIgnoreCase) || item.Message.Contains("APEX", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(oracleEvents, item => item.Phase.Contains("configuringOrds", StringComparison.OrdinalIgnoreCase) || item.Message.Contains("ORDS", StringComparison.OrdinalIgnoreCase));

        var validate = await mcp.Client.CallToolAsync("validate_workspace", new Dictionary<string, object?> { ["workspaceId"] = workspace.WorkspaceId });
        var validatedWorkspace = JsonSerializer.Deserialize<McpToolEnvelope<WorkspaceRecordModel>>(validate.StructuredContent!.Value.GetRawText())!.Data;
        WriteJsonArtifact(artifactRoot, "workspace-validated.json", validatedWorkspace);

        Assert.Contains(validatedWorkspace.Snapshot.Health.Services, item => item.ServiceId.Contains("oracle", StringComparison.OrdinalIgnoreCase) && item.Status is WorkspaceHealthStatus.Healthy or WorkspaceHealthStatus.Attention);
        Assert.Contains(validatedWorkspace.Snapshot.AvailableServices, item => item.HostUrl.Contains("ords", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validatedWorkspace.Snapshot.Health.Services.SelectMany(item => item.Evidence), item => item.Label.Contains("APEX", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.Value));
        Assert.Contains(validatedWorkspace.Snapshot.Health.Services.SelectMany(item => item.Evidence), item => item.Label.Contains("XDB", StringComparison.OrdinalIgnoreCase) && item.Value.Contains("VALID", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validatedWorkspace.Snapshot.Health.Services.SelectMany(item => item.Evidence), item => item.Label.Contains("ORDS", StringComparison.OrdinalIgnoreCase));
        Assert.True(validatedWorkspace.Snapshot.Record.LastProvisioningHealth?.Succeeded ?? false);
        Assert.False(string.IsNullOrWhiteSpace(validatedWorkspace.Snapshot.Record.LastProvisioningHealth?.ApexVersion));

        var runtimeInventory = await mcp.Client.CallToolAsync("list_runtime_resources", new Dictionary<string, object?> { ["owner"] = workspace.WorkspaceId });
        WriteTextArtifact(artifactRoot, "runtime-inventory-before-cleanup.json", runtimeInventory.StructuredContent!.Value.GetRawText());

        var stop = await mcp.Client.CallToolAsync("stop_workspace", new Dictionary<string, object?> { ["workspaceId"] = workspace.WorkspaceId });
        WriteTextArtifact(artifactRoot, "workspace-stop.json", stop.StructuredContent!.Value.GetRawText());

        var remove = await mcp.Client.CallToolAsync("remove_workspace_runtime", new Dictionary<string, object?> { ["workspaceId"] = workspace.WorkspaceId });
        WriteTextArtifact(artifactRoot, "workspace-remove-runtime.json", remove.StructuredContent!.Value.GetRawText());

        var finalDoctor = await mcp.Client.CallToolAsync("run_runtime_doctor", new Dictionary<string, object?> { ["owner"] = workspace.WorkspaceId });
        var finalDoctorEnvelope = JsonSerializer.Deserialize<McpToolEnvelope<RuntimeResourceInventory>>(finalDoctor.StructuredContent!.Value.GetRawText())!;
        WriteJsonArtifact(artifactRoot, "runtime-doctor-after-cleanup.json", finalDoctorEnvelope.Data);
        Assert.Empty(finalDoctorEnvelope.Data.Resources);
        Assert.Empty(finalDoctorEnvelope.Data.Orphans);

        await mcp.DisposeClientAndTransportAsync(TimeSpan.FromSeconds(30));
        Assert.False(mcp.Report.ForcedTerminationRequired);
        Assert.NotNull(mcp.Report.ExitedUtc);
        Assert.False(ProcessStillRunning(mcp.Report.ProcessId, mcp.Report.ExecutablePath));
        WriteJsonArtifact(artifactRoot, "mcp-lifecycle.json", mcp.Report);
        WriteTextArtifact(artifactRoot, "mcp-stderr.log", string.Join(Environment.NewLine, mcp.StandardErrorLines));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<string> CreateExtractedDistributionAsync()
    {
        Directory.CreateDirectory(_root);
        var runtime = GetRuntimeIdentifier();
        var publishRoot = Path.Combine(_root, "publish");
        var outputRoot = Path.Combine(_root, "dist");
        Directory.CreateDirectory(publishRoot);

        await RunSetupCommandAsync("dotnet", ["publish", "src/OpenCode.Workspace.Avalonia/OpenCode.Workspace.Avalonia.csproj", "-c", "Release", "-r", runtime, "--self-contained", "false", "-o", Path.Combine(publishRoot, "desktop")], TestPaths.RepositoryRoot);
        await RunSetupCommandAsync("dotnet", ["publish", "src/OpenCode.Workspace.Cli/OpenCode.Workspace.Cli.csproj", "-c", "Release", "-r", runtime, "--self-contained", "false", "-o", Path.Combine(publishRoot, "cli")], TestPaths.RepositoryRoot);
        await RunSetupCommandAsync("dotnet", ["publish", "src/OpenCode.Workspace.Api/OpenCode.Workspace.Api.csproj", "-c", "Release", "-r", runtime, "--self-contained", "false", "-o", Path.Combine(publishRoot, "api")], TestPaths.RepositoryRoot);
        await RunSetupCommandAsync("dotnet", ["publish", "src/OpenCode.Workspace.Mcp/OpenCode.Workspace.Mcp.csproj", "-c", "Release", "-r", runtime, "--self-contained", "false", "-o", Path.Combine(publishRoot, "mcp")], TestPaths.RepositoryRoot);
        await RunSetupCommandAsync("dotnet", ["run", "--project", "tools/OpenCode.Workspace.ReleaseTool/OpenCode.Workspace.ReleaseTool.csproj", "--", "assemble", "--source-root", TestPaths.RepositoryRoot, "--output-root", outputRoot, "--runtime", runtime, "--version", "0.0.0-test", "--desktop-publish-dir", Path.Combine(publishRoot, "desktop"), "--cli-publish-dir", Path.Combine(publishRoot, "cli") , "--api-publish-dir", Path.Combine(publishRoot, "api"), "--mcp-publish-dir", Path.Combine(publishRoot, "mcp"), "--create-zip", OperatingSystem.IsWindows() ? "true" : "false"], TestPaths.RepositoryRoot);

        if (OperatingSystem.IsWindows())
        {
            var zipPath = Path.Combine(outputRoot, $"opencode-workspace-0.0.0-test-{runtime}.zip");
            var extractRoot = Path.Combine(_root, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);
            return Path.Combine(extractRoot, $"opencode-workspace-0.0.0-test-{runtime}");
        }

        var sourcePackageRoot = Path.Combine(outputRoot, $"opencode-workspace-0.0.0-test-{runtime}");
        var copiedRoot = Path.Combine(_root, "copied", $"opencode-workspace-0.0.0-test-{runtime}");
        CopyDirectory(sourcePackageRoot, copiedRoot);
        return copiedRoot;
    }

    private static async Task RunSetupCommandAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var stdout = new List<string>();
        var stderr = new List<string>();
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                stdout.Add(eventArgs.Data);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                stderr.Add(eventArgs.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            if (!process.HasExited)
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnExited(object? sender, EventArgs args) => completion.TrySetResult();
                process.EnableRaisingEvents = true;
                process.Exited += OnExited;
                try
                {
                    using var registration = timeoutCts.Token.Register(() => completion.TrySetCanceled(timeoutCts.Token));
                    await completion.Task;
                }
                finally
                {
                    process.Exited -= OnExited;
                }
            }
        }
        catch (OperationCanceledException exception)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException($"Setup command timed out: {fileName} {string.Join(' ', arguments)}", exception);
        }

        Assert.True(process.ExitCode == 0, $"Setup command failed: {fileName} {string.Join(' ', arguments)}{Environment.NewLine}{string.Join(Environment.NewLine, stdout)}{Environment.NewLine}{string.Join(Environment.NewLine, stderr)}");
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(destinationPath, File.GetUnixFileMode(file));
            }
        }
    }

    private static string GetHostExecutablePath(string directory, string baseName)
        => Path.Combine(directory, baseName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));

    private static string GetRuntimeIdentifier()
        => OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsMacOS() ? "osx-arm64" : "linux-x64";

    private static async Task<bool> DockerIsAvailableAsync()
    {
        try
        {
            await using var docker = await PackagedProcessHarness.StartAsync("docker-version", "docker", ["version", "--format", "{{.Server.Version}}"], Path.GetTempPath());
            await docker.WaitForExitAsync(TimeSpan.FromSeconds(30));
            return docker.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private string EnsureArtifactDirectory(string name)
    {
        var path = string.IsNullOrWhiteSpace(_artifactRoot)
            ? Path.Combine(_root, "artifacts", name)
            : Path.Combine(_artifactRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteJsonArtifact<T>(string root, string fileName, T value)
        => File.WriteAllText(Path.Combine(root, fileName), JsonSerializer.Serialize(value, OpenCodeWorkspaceMcpContract.JsonOptions));

    private static void WriteTextArtifact(string root, string fileName, string text)
        => File.WriteAllText(Path.Combine(root, fileName), text);

    private static bool ShouldRunPackagedOracleValidation()
        => string.Equals(Environment.GetEnvironmentVariable("OPENCODE_RUN_PACKAGED_ORACLE_MCP"), "true", StringComparison.OrdinalIgnoreCase);

    private static string BuildDistributionManifest(string packageRoot)
        => string.Join(
            Environment.NewLine,
            Directory.EnumerateFileSystemEntries(packageRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(packageRoot, path))
                .OrderBy(path => path, StringComparer.Ordinal));

    private static bool ProcessStillRunning(int processId, string executablePath)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return string.Equals(process.MainModule?.FileName, executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
