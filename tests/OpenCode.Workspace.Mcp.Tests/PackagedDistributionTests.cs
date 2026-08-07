using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenCode.Workspace.Api;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.LocalClient;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Mcp.Tests;

[Collection("Packaged distribution")]
[Trait("Category", "PackageIntegration")]
public sealed class PackagedDistributionTests(PackagedDistributionFixture fixture) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "opencode package tests", Guid.NewGuid().ToString("n"));
    private readonly string? _artifactRoot = Environment.GetEnvironmentVariable("OPENCODE_PACKAGE_TEST_ARTIFACT_ROOT");
    private readonly string? _existingPackageRoot = Environment.GetEnvironmentVariable("OPENCODE_EXISTING_PACKAGE_ROOT");

    [Fact]
    public async Task ExtractedDistribution_ResolvesPackagedContent_AndHostsExitGracefully()
    {
        var packageRoot = CreateExtractedDistribution();
        var outsideRepositoryRoot = Path.Combine(_root, "outside repo");
        Directory.CreateDirectory(outsideRepositoryRoot);
        WriteTextArtifact(EnsureArtifactDirectory("packaged-host-validation"), "distribution-manifest.txt", BuildDistributionManifest(packageRoot));

        Assert.True(File.Exists(Path.Combine(packageRoot, "LICENSE")));
        Assert.True(File.Exists(Path.Combine(packageRoot, "THIRD-PARTY-NOTICES.md")));
        Assert.True(Directory.Exists(Path.Combine(packageRoot, "catalog", "templates")));

        Assert.True(File.Exists(GetHostExecutablePath(packageRoot, "OpenCode.Workspace")));
        Assert.True(File.Exists(GetHostExecutablePath(Path.Combine(packageRoot, "bin", "local-host"), "OpenCode.Workspace.LocalHost")));
        Assert.True(File.Exists(GetHostExecutablePath(Path.Combine(packageRoot, "bin", "cli"), "OpenCode.Workspace.Cli")));
        Assert.True(File.Exists(GetHostExecutablePath(Path.Combine(packageRoot, "bin", "mcp"), "OpenCode.Workspace.Mcp")));
        Assert.False(Directory.Exists(Path.Combine(packageRoot, "bin", "api")));
        Assert.True(File.Exists(Path.Combine(packageRoot, GetHostFxrFileName())));

        var desktopServices = new WorkspaceDesktopServiceFactory().Create(packageRoot, Path.Combine(_root, "appdata"));
        Assert.Equal(Path.Combine(packageRoot, "catalog"), desktopServices.InstallationLayout.CatalogRoot);
        Assert.NotEmpty(desktopServices.CatalogProvider.LoadTemplates());

        var cliExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "cli"), "OpenCode.Workspace.Cli");
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

        var apiExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "local-host"), "OpenCode.Workspace.LocalHost");
        var apiPort = PackagedHostValidationHelpers.GetFreeTcpPort();
        await using var api = await PackagedProcessHarness.StartAsync(
            "api",
            apiExecutable,
            ["--shutdown-on-stdin-eof"],
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

        var mcpExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "mcp"), "OpenCode.Workspace.Mcp");
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
        var templateEnvelope = JsonSerializer.Deserialize<McpToolEnvelope<IReadOnlyList<WorkspaceTemplateSummaryModel>>>(GetStructuredOrTextPayload(templates))!;
        Assert.Contains(templateEnvelope.Data, item => item.TemplateId == "empty-workspace");
        var smokeDefinitionsTool = await mcp.Client.CallToolAsync("list_smoke_definitions");
        var smokeDefinitionsEnvelope = JsonSerializer.Deserialize<McpToolEnvelope<WorkspaceSmokeDefinitionCatalogResult>>(GetStructuredOrTextPayload(smokeDefinitionsTool))!;
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
    public async Task ExtractedDistribution_McpConfigureAndDoctor_UsePackagedPaths()
    {
        var packageRoot = CreateExtractedDistribution();
        var workingDirectory = Path.Combine(_root, "doctor path with spaces");
        Directory.CreateDirectory(workingDirectory);
        var cli = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "cli"), "OpenCode.Workspace.Cli");
        var expectedMcp = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "mcp"), "OpenCode.Workspace.Mcp");

        foreach (var client in new[] { "codex", "claude", "opencode" })
        {
            await using var configure = await PackagedProcessHarness.StartAsync($"configure-{client}", cli, ["mcp", "configure", client, "--print", "--install-root", packageRoot], workingDirectory);
            await configure.WaitForExitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(0, configure.ExitCode);
            Assert.Contains(expectedMcp, configure.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(TestPaths.RepositoryRoot, configure.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dotnet run", configure.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bin/api", configure.StandardOutput, StringComparison.OrdinalIgnoreCase);
        }

        await using var doctor = await PackagedProcessHarness.StartAsync("mcp-doctor", cli, ["mcp", "doctor", "--install-root", packageRoot, "--json"], workingDirectory);
        await doctor.WaitForExitAsync(TimeSpan.FromSeconds(90));
        Assert.True(doctor.ExitCode == 0, $"doctor stdout:{Environment.NewLine}{doctor.StandardOutput}{Environment.NewLine}doctor stderr:{Environment.NewLine}{doctor.StandardError}");
        using var document = JsonDocument.Parse(doctor.StandardOutput);
        var checks = document.RootElement.EnumerateArray().ToArray();
        Assert.Contains(checks, item => item.GetProperty("Name").GetString() == "McpInitialize" && item.GetProperty("Status").GetString() == "Passed");
        Assert.Contains(checks, item => item.GetProperty("Name").GetString() == "McpToolsList" && item.GetProperty("Status").GetString() == "Passed");
        Assert.Contains(checks, item => item.GetProperty("Name").GetString() == "McpResourcesList" && item.GetProperty("Status").GetString() == "Passed");
        Assert.Contains(checks, item => item.GetProperty("Name").GetString() == "ControllerDisconnected" && item.GetProperty("Status").GetString() == "Passed");
    }

    [Fact]
    public async Task PackagedMcp_SimultaneousStartup_UsesOneCanonicalLocalHost_AndProtocolOnlyStdout()
    {
        var packageRoot = CreateExtractedDistribution();
        var workingDirectory = Path.Combine(_root, "packaged startup race with spaces");
        var stateRoot = Path.Combine(_root, "packaged-race-state");
        var identity = LocalHostProcessIdentity.ForStateRoot(stateRoot);
        var mcpExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "mcp"), "OpenCode.Workspace.Mcp");
        var hostExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "local-host"), "OpenCode.Workspace.LocalHost");
        Directory.CreateDirectory(workingDirectory);
        TeardownAssert.AssertNoLiveState(identity);

        await using var launches = new PackagedLocalHostLaunchRecorder(hostExecutable);
        await using var a = await StartPackagedRawMcpAsync("race-a", mcpExecutable, packageRoot, workingDirectory, stateRoot);
        await using var b = await StartPackagedRawMcpAsync("race-b", mcpExecutable, packageRoot, workingDirectory, stateRoot);
        await Task.WhenAll(InitializeRawMcpAsync(a, 1, "race-a"), InitializeRawMcpAsync(b, 1, "race-b"));

        var host = await WaitForPackagedHostAsync(identity, a.StandardErrorLines.Concat(b.StandardErrorLines).ToArray());
        var controllerA = await WaitForPackagedControllerAsync(host, a.Report.ProcessId, a.StandardErrorLines);
        var controllerB = await WaitForPackagedControllerAsync(host, b.Report.ProcessId, b.StandardErrorLines);
        await Task.Delay(250);
        var launched = launches.Launched;

        Assert.Single(launched);
        Assert.Equal(host.ProcessId, launched.Single().ProcessId);
        Assert.Equal(1, launched.Count(item => item.ProcessId == host.ProcessId));
        Assert.DoesNotContain(launched, item => item.ProcessId != host.ProcessId && item.IsRunning());
        Assert.NotEqual(a.Report.ProcessId, b.Report.ProcessId);
        Assert.NotEqual(controllerA.ControllerSessionId, controllerB.ControllerSessionId);
        Assert.NotEqual(controllerA.ClientInstanceId, controllerB.ClientInstanceId);
        Assert.True(Uri.TryCreate(host.BaseUrl, UriKind.Absolute, out var baseUri) && System.Net.IPAddress.TryParse(baseUri.Host, out var address) && System.Net.IPAddress.IsLoopback(address) && baseUri.Port > 0);
        Assert.Equal(host.InstanceId, ReadDescriptor(identity).InstanceId);
        TeardownAssert.AssertHostLockHeld(identity);
        AssertProtocolOnly(a.StandardOutputLines, a.StandardErrorLines);
        AssertProtocolOnly(b.StandardOutputLines, b.StandardErrorLines);

        await b.RequestGracefulShutdownByClosingStandardInputAsync(TimeSpan.FromSeconds(30));
        await TeardownAssert.AssertControllerDisconnectedAsync(CreateLocalClient(host), controllerB.ControllerSessionId, identity, b.StandardErrorLines, [], TimeSpan.FromSeconds(30), CancellationToken.None);
        await TeardownAssert.AssertProcessStillRunningAsync(host, a.StandardErrorLines, b.StandardErrorLines, TimeSpan.FromSeconds(30), CancellationToken.None);
        await using (var client = CreateLocalClient(host))
        {
            Assert.Equal(ControllerSessionStatus.Connected, (await client.ListControllerSessionsAsync()).Single(item => item.ControllerSessionId == controllerA.ControllerSessionId).Status);
        }

        await a.RequestGracefulShutdownByClosingStandardInputAsync(TimeSpan.FromSeconds(30));
        await TeardownAssert.AssertControllerDisconnectedAsync(CreateLocalClient(host), controllerA.ControllerSessionId, identity, a.StandardErrorLines, b.StandardErrorLines, TimeSpan.FromSeconds(30), CancellationToken.None);
        await TeardownAssert.AssertProcessExitedAsync(host, a.StandardErrorLines, b.StandardErrorLines, TimeSpan.FromSeconds(30), CancellationToken.None);
        await TeardownAssert.AssertDescriptorNotLiveAsync(identity, a.StandardErrorLines, b.StandardErrorLines, TimeSpan.FromSeconds(30), CancellationToken.None);
        await TeardownAssert.AssertHostLockReleasedAsync(identity, a.StandardErrorLines, b.StandardErrorLines, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.False(ProcessStillRunning(a.Report.ProcessId, mcpExecutable));
        Assert.False(ProcessStillRunning(b.Report.ProcessId, mcpExecutable));
    }

    [Fact]
    public async Task PackagedMcp_OperationMatchesCanonicalLocalClient_AndCancellationConverges()
    {
        var packageRoot = CreateExtractedDistribution();
        var workingDirectory = Path.Combine(_root, "packaged parity with spaces");
        var stateRoot = Path.Combine(_root, "packaged-parity-state");
        var identity = LocalHostProcessIdentity.ForStateRoot(stateRoot);
        var mcpExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "mcp"), "OpenCode.Workspace.Mcp");
        Directory.CreateDirectory(workingDirectory);
        await using var mcp = await StartPackagedRawMcpAsync("packaged-parity", mcpExecutable, packageRoot, workingDirectory, stateRoot);
        await InitializeRawMcpAsync(mcp, 1, "packaged-parity");
        var host = await WaitForPackagedHostAsync(identity, mcp.StandardErrorLines);
        var controller = await WaitForPackagedControllerAsync(host, mcp.Report.ProcessId, mcp.StandardErrorLines);
        var started = await CallRawOperationAsync(mcp, 2, "run_smoke", new { templateId = "empty-workspace" });
        await using var client = CreateLocalClient(host);
        var canonical = await client.GetOperationAsync(started.OperationId);

        Assert.Equal(started.OperationId, canonical.OperationId);
        Assert.Equal(started.WorkspaceId, canonical.WorkspaceId);
        Assert.Equal(started.Kind, canonical.OperationKind);
        Assert.Equal(started.CreatedUtc, canonical.CreatedUtc);
        Assert.Equal(controller.ControllerSessionId, canonical.InitiatedBy.ControllerSessionId);

        await client.CancelOperationAsync(started.OperationId, new OperationCommandRequest { CommandId = Guid.NewGuid().ToString("n"), RequestedBy = new OperationInitiator { Kind = "package-test" } });
        var terminal = await WaitForRawTerminalAsync(mcp, started.OperationId, 3);
        var canonicalTerminal = await WaitForCanonicalTerminalAsync(client, started.OperationId);
        Assert.Equal(McpOperationStatus.Cancelled, terminal.Status);
        Assert.Equal(canonicalTerminal.LastEventSequence, terminal.LastEventSequence);
        Assert.Equal(canonicalTerminal.Result.HasValue, terminal.Result.HasValue);
        Assert.Equal(canonicalTerminal.OriginalFailure?.Classification ?? "cancelled", terminal.FailureClassification);
        Assert.Equal(canonicalTerminal.OriginalFailure?.Message ?? string.Empty, terminal.FailureMessage);
        Assert.Equal(canonicalTerminal.CleanupFailure?.Classification ?? string.Empty, terminal.CleanupFailureClassification);
        Assert.Equal(canonicalTerminal.CleanupFailure?.Message ?? string.Empty, terminal.CleanupFailureMessage);
        Assert.Equal(canonicalTerminal.ArtifactReferences.Select(item => item.SafeLocalReference).Where(item => !string.IsNullOrWhiteSpace(item)), terminal.ArtifactReferences);
        Assert.Equal(controller.ControllerSessionId, canonicalTerminal.InitiatedBy.ControllerSessionId);

        await mcp.RequestGracefulShutdownByClosingStandardInputAsync(TimeSpan.FromSeconds(30));
        await TeardownAssert.AssertControllerDisconnectedAsync(CreateLocalClient(host), controller.ControllerSessionId, identity, mcp.StandardErrorLines, [], TimeSpan.FromSeconds(30), CancellationToken.None);
        await TeardownAssert.AssertProcessExitedAsync(host, mcp.StandardErrorLines, [], TimeSpan.FromSeconds(30), CancellationToken.None);
        await TeardownAssert.AssertHostLockReleasedAsync(identity, mcp.StandardErrorLines, [], TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    [Fact]
    public async Task PackagedMcp_RawStdoutContainsOnlyProtocolFrames()
    {
        var packageRoot = CreateExtractedDistribution();
        var workingDirectory = Path.Combine(_root, "packaged raw protocol with spaces");
        var stateRoot = Path.Combine(_root, "packaged-raw-state");
        var identity = LocalHostProcessIdentity.ForStateRoot(stateRoot);
        var mcpExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "mcp"), "OpenCode.Workspace.Mcp");
        Directory.CreateDirectory(workingDirectory);
        await using var mcp = await StartPackagedRawMcpAsync("raw-protocol", mcpExecutable, packageRoot, workingDirectory, stateRoot);
        await InitializeRawMcpAsync(mcp, 1, "raw-protocol");
        await CallRawJsonAsync(mcp, 2, "tools/list", new { });
        await CallRawJsonAsync(mcp, 3, "resources/list", new { });
        var started = await CallRawOperationAsync(mcp, 4, "run_smoke", new { templateId = "empty-workspace" });
        await CallRawOperationAsync(mcp, 5, "get_operation", new { operationId = started.OperationId });
        await CallRawOperationAsync(mcp, 6, "cancel_operation", new { operationId = started.OperationId });
        await WaitForRawTerminalAsync(mcp, started.OperationId, 7);
        AssertProtocolOnly(mcp.StandardOutputLines, mcp.StandardErrorLines);

        var host = await WaitForPackagedHostAsync(identity, mcp.StandardErrorLines);
        var controller = await WaitForPackagedControllerAsync(host, mcp.Report.ProcessId, mcp.StandardErrorLines);
        await mcp.RequestGracefulShutdownByClosingStandardInputAsync(TimeSpan.FromSeconds(30));
        await TeardownAssert.AssertControllerDisconnectedAsync(CreateLocalClient(host), controller.ControllerSessionId, identity, mcp.StandardErrorLines, [], TimeSpan.FromSeconds(30), CancellationToken.None);
        await TeardownAssert.AssertProcessExitedAsync(host, mcp.StandardErrorLines, [], TimeSpan.FromSeconds(30), CancellationToken.None);
        await TeardownAssert.AssertHostLockReleasedAsync(identity, mcp.StandardErrorLines, [], TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "LocalHostIntegration")]
    public async Task PackagedDoctor_ReusesExternalLocalHostWithoutStoppingIt()
    {
        var packageRoot = CreateExtractedDistribution();
        var workingDirectory = Path.Combine(_root, "external doctor path with spaces");
        var stateRoot = Path.Combine(_root, "external-doctor-state");
        Directory.CreateDirectory(workingDirectory);
        var hostExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "local-host"), "OpenCode.Workspace.LocalHost");
        var cli = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "cli"), "OpenCode.Workspace.Cli");
        var port = PackagedHostValidationHelpers.GetFreeTcpPort();
        await using var host = await PackagedProcessHarness.StartAsync("external-doctor-host", hostExecutable, ["--shutdown-on-stdin-eof"], workingDirectory, new Dictionary<string, string?>
        {
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
            ["localHost__stateRoot"] = stateRoot,
        });
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        await PackagedHostValidationHelpers.WaitForApiHealthyAsync(http, TimeSpan.FromSeconds(60));
        var before = JsonDocument.Parse(await http.GetStringAsync("api/v1/local-host/health")).RootElement.GetProperty("data").GetProperty("hostInstanceId").GetString();

        await using var doctor = await PackagedProcessHarness.StartAsync("external-doctor", cli, ["mcp", "doctor", "--install-root", packageRoot, "--state-root", stateRoot, "--json"], workingDirectory);
        await doctor.WaitForExitAsync(TimeSpan.FromSeconds(90));
        Assert.Equal(0, doctor.ExitCode);
        Assert.False(host.HasExited);
        var after = JsonDocument.Parse(await http.GetStringAsync("api/v1/local-host/health")).RootElement.GetProperty("data").GetProperty("hostInstanceId").GetString();
        Assert.Equal(before, after);
        await host.RequestGracefulShutdownByClosingStandardInputAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    [Trait("Category", "LiveDockerIntegration")]
    public async Task PackagedMcp_RunSmoke_ReportsIncrementalProgress_AndShutsDownCleanly()
    {
        if (!await DockerIsAvailableAsync())
        {
            return;
        }

        var packageRoot = CreateExtractedDistribution();
        var outsideRepositoryRoot = Path.Combine(_root, "outside repo smoke");
        Directory.CreateDirectory(outsideRepositoryRoot);
        var mcpExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "mcp"), "OpenCode.Workspace.Mcp");
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
        var preservedRoot = Path.Combine(packageArtifactRoot, "preserved-runtime-root");

        var preflightCleanup = await mcp.Client.CallToolAsync("cleanup_smoke_resources", new Dictionary<string, object?>
        {
            ["dryRun"] = false,
            ["includeAll"] = true,
        });
        var preflightCleanupEnvelope = JsonSerializer.Deserialize<McpToolEnvelope<SmokeCleanupResult>>(GetStructuredOrTextPayload(preflightCleanup))!;
        Assert.True(preflightCleanupEnvelope.Data.Succeeded);
        Assert.True(preflightCleanupEnvelope.Data.VerificationSucceeded);
        WriteJsonArtifact(packageArtifactRoot, "preflight-cleanup.json", preflightCleanupEnvelope.Data);

        var start = await mcp.Client.CallToolAsync("run_smoke", new Dictionary<string, object?>
        {
            ["templateId"] = "empty-workspace",
            ["timeout"] = "00:05:00",
        });
        var operation = JsonSerializer.Deserialize<McpOperationModel>(GetStructuredOrTextPayload(start))!;
        Assert.NotEmpty(operation.OperationId);
        WriteJsonArtifact(packageArtifactRoot, "operation-start.json", operation);

        McpOperationModel current = operation;
        long afterSequence = 0;
        var seenSequences = new HashSet<long>();
        var allEvents = new List<WorkspaceOperationProgressEvent>();
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(4);
        try
        {
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
        }
        catch
        {
            PreservePackagedSmokeFailure(packageArtifactRoot, preservedRoot, current, operation, packageRoot, Path.Combine(_root, "packaged-mcp-state"));
            throw;
        }

        await mcp.DisposeClientAndTransportAsync(TimeSpan.FromSeconds(30));
        Assert.False(mcp.Report.ForcedTerminationRequired);
        Assert.NotNull(mcp.Report.ExitedUtc);
        Assert.False(ProcessStillRunning(mcp.Report.ProcessId, mcp.Report.ExecutablePath));
        WriteJsonArtifact(packageArtifactRoot, "mcp-lifecycle.json", mcp.Report);
        WriteTextArtifact(packageArtifactRoot, "mcp-stderr-final.log", string.Join(Environment.NewLine, mcp.StandardErrorLines));
    }

    private static void PreservePackagedSmokeFailure(string artifactRoot, string preservedRoot, McpOperationModel current, McpOperationModel start, string packageRoot, string stateRoot)
    {
        Directory.CreateDirectory(artifactRoot);
        WriteStaticJsonArtifact(artifactRoot, "operation-current-failure.json", current);
        WriteStaticJsonArtifact(artifactRoot, "operation-start-failure.json", start);
        WriteStaticTextArtifact(artifactRoot, "preserved-root.txt", preservedRoot);
        if (Directory.Exists(preservedRoot))
        {
            Directory.Delete(preservedRoot, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(preservedRoot)!);
        TryCopyDirectory(packageRoot, Path.Combine(preservedRoot, "package-copy"));
        if (Directory.Exists(stateRoot))
        {
            TryCopyDirectory(stateRoot, Path.Combine(preservedRoot, "state-root"));
        }

        var jsonlPath = current.ArtifactReferences.FirstOrDefault(path => path.EndsWith("operation-progress.jsonl", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(jsonlPath) && File.Exists(jsonlPath))
        {
            WriteStaticTextArtifact(artifactRoot, "operation-progress-raw-lines.txt", DumpJsonlLines(jsonlPath));
        }
    }

    private static string DumpJsonlLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var lines = File.ReadAllLines(path);
        var output = new List<string> { $"path={path}", $"bytes={bytes.Length}", $"lines={lines.Length}" };
        var offset = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineBytes = System.Text.Encoding.UTF8.GetBytes(line + Environment.NewLine);
            var parse = "ok";
            try
            {
                JsonSerializer.Deserialize<WorkspaceOperationProgressEvent>(line, OpenCodeWorkspaceMcpContract.JsonOptions);
            }
            catch (Exception exception)
            {
                parse = exception.GetType().Name + ": " + exception.Message;
            }

            output.Add($"line={i + 1} offset={offset} byteLength={lineBytes.Length} parse={parse}");
            output.Add(line.Length > 200 ? line[..200] : line);
            offset += lineBytes.Length;
        }

        return string.Join(Environment.NewLine, output);
    }

    private static void WriteStaticJsonArtifact<T>(string directory, string fileName, T value)
        => File.WriteAllText(Path.Combine(directory, fileName), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

    private static void WriteStaticTextArtifact(string directory, string fileName, string text)
        => File.WriteAllText(Path.Combine(directory, fileName), text);

    [Fact]
    [Trait("Category", "PackagedOracleMcpIntegration")]
    public async Task PackagedMcp_OracleApexlangProvisioning_ReportsProgress_AndCleansUp()
    {
        if (!ShouldRunPackagedOracleValidation())
        {
            return;
        }

        var packageRoot = CreateExtractedDistribution();
        var outsideRepositoryRoot = Path.Combine(_root, "outside repo oracle");
        Directory.CreateDirectory(outsideRepositoryRoot);
        var artifactRoot = EnsureArtifactDirectory("packaged-oracle-mcp");
        WriteTextArtifact(artifactRoot, "distribution-manifest.txt", BuildDistributionManifest(packageRoot));
        var mcpExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "mcp"), "OpenCode.Workspace.Mcp");

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

    private string CreateExtractedDistribution()
    {
        if (!string.IsNullOrWhiteSpace(_existingPackageRoot))
        {
            var packageRoot = Path.GetFullPath(_existingPackageRoot);
            if (!Directory.Exists(packageRoot))
            {
                throw new DirectoryNotFoundException($"Existing package root was not found: '{packageRoot}'.");
            }

            var existingPackageCopyRoot = Path.Combine(_root, "existing-package", Path.GetFileName(packageRoot));
            Directory.CreateDirectory(Path.GetDirectoryName(existingPackageCopyRoot)!);
            CopyDirectory(packageRoot, existingPackageCopyRoot);
            return existingPackageCopyRoot;
        }

        return fixture.CopyPackageTo(_root);
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

    private static void TryCopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            try
            {
                var destinationPath = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(file, destinationPath, overwrite: true);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(destinationPath, File.GetUnixFileMode(file));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string GetHostExecutablePath(string directory, string baseName)
        => Path.Combine(directory, baseName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));

    private static string GetHostFxrFileName()
        => OperatingSystem.IsWindows() ? "hostfxr.dll" : OperatingSystem.IsMacOS() ? "libhostfxr.dylib" : "libhostfxr.so";

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

    private static async Task<PackagedProcessHarness> StartPackagedRawMcpAsync(string clientName, string executablePath, string packageRoot, string workingDirectory, string stateRoot)
        => await PackagedProcessHarness.StartAsync("packaged-mcp-" + clientName, executablePath, [], workingDirectory, new Dictionary<string, string?>
        {
            ["mcp__catalogRoot"] = null,
            ["mcp__workspaceStateRoot"] = Path.Combine(stateRoot, "workspace-state"),
            ["mcp__smokeArtifactsRoot"] = Path.Combine(stateRoot, "artifacts"),
            ["localHost__stateRoot"] = stateRoot,
            ["localHost__executableDirectory"] = Path.Combine(packageRoot, "bin", "local-host"),
            ["localHost__useTestOperation"] = "true",
            ["MCP_CLIENT_NAME"] = clientName,
            ["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "None",
            ["Logging__LogLevel__Default"] = "None",
        });

    private static Task InitializeRawMcpAsync(PackagedProcessHarness mcp, int requestId, string clientName)
        => CallRawJsonAsync(mcp, requestId, "initialize", new { protocolVersion = "2025-03-26", capabilities = new { }, clientInfo = new { name = clientName, version = "1" } });

    private static async Task<JsonElement> CallRawJsonAsync(PackagedProcessHarness mcp, int requestId, string method, object parameters)
    {
        await mcp.WriteStandardInputAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = requestId, method, @params = parameters }));
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            foreach (var line in mcp.StandardOutputLines)
            {
                if (!TryReadRawResult(line, requestId, out var result))
                {
                    continue;
                }
                return result;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException($"Raw MCP request '{method}' did not return. stdout={FormatRawFrames(mcp.StandardOutputLines, mcp.StandardErrorLines)}");
    }

    private static async Task<McpOperationModel> CallRawOperationAsync(PackagedProcessHarness mcp, int requestId, string toolName, object arguments)
    {
        var result = await CallRawJsonAsync(mcp, requestId, "tools/call", new { name = toolName, arguments });
        var payload = result.TryGetProperty("structuredContent", out var structured)
            ? structured
            : result.GetProperty("content")[0].GetProperty("text");
        if (payload.TryGetProperty("Data", out var data))
        {
            payload = data;
        }
        return JsonSerializer.Deserialize<McpOperationModel>(payload.GetRawText())
            ?? throw new InvalidOperationException($"MCP tool '{toolName}' did not return an operation record.");
    }

    private static async Task<McpOperationModel> WaitForRawTerminalAsync(PackagedProcessHarness mcp, string operationId, int requestId)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var current = await CallRawOperationAsync(mcp, requestId++, "get_operation", new { operationId });
            if (current.Status is McpOperationStatus.Succeeded or McpOperationStatus.Failed or McpOperationStatus.Cancelled)
            {
                return current;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"MCP operation '{operationId}' did not terminate through raw stdio.");
    }

    private static async Task<WorkspaceOperationRecord> WaitForCanonicalTerminalAsync(LocalHostClient client, string operationId)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var current = await client.GetOperationAsync(operationId);
            if (current.Status is WorkspaceOperationStatus.Succeeded or WorkspaceOperationStatus.Failed or WorkspaceOperationStatus.Cancelled)
            {
                return current;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Canonical operation '{operationId}' did not terminate.");
    }

    private static bool TryReadRawResult(string line, int requestId, out JsonElement result)
    {
        result = default;
        try
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("id", out var id)
                || id.ValueKind != JsonValueKind.Number
                || id.GetInt32() != requestId
                || !document.RootElement.TryGetProperty("result", out var response))
            {
                return false;
            }
            result = response.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void AssertProtocolOnly(IReadOnlyList<string> frames, IReadOnlyList<string> stderr)
    {
        Assert.NotEmpty(frames);
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            try
            {
                using var document = JsonDocument.Parse(frame);
                Assert.Equal("2.0", document.RootElement.GetProperty("jsonrpc").GetString());
                Assert.True(document.RootElement.TryGetProperty("result", out _) || document.RootElement.TryGetProperty("error", out _) || document.RootElement.TryGetProperty("method", out _));
            }
            catch (Exception exception) when (exception is JsonException or Xunit.Sdk.XunitException)
            {
                throw new Xunit.Sdk.XunitException($"stdout frame {index} is not MCP protocol traffic. {FormatRawFrames(frames, stderr, index)}", exception);
            }
        }
    }

    private static string FormatRawFrames(IReadOnlyList<string> frames, IReadOnlyList<string> stderr, int? focus = null)
    {
        var start = Math.Max(0, (focus ?? Math.Max(0, frames.Count - 1)) - 1);
        var end = Math.Min(frames.Count, start + 3);
        var selected = Enumerable.Range(start, end - start).Select(index => $"frame={index} bytes={System.Text.Encoding.UTF8.GetByteCount(frames[index])} escaped={JsonSerializer.Serialize(frames[index])}");
        return $"{string.Join(Environment.NewLine, selected)}{Environment.NewLine}stderr={string.Join(" | ", stderr.TakeLast(20))}";
    }

    private static async Task<LocalHostProcessIdentity> WaitForPackagedHostAsync(LocalHostProcessIdentity identity, IReadOnlyList<string> stderr)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(identity.DescriptorPath))
            {
                var descriptor = ReadDescriptor(identity);
                if (descriptor is { ProcessId: > 0, InstanceId.Length: > 0 })
                {
                    try
                    {
                        using var process = Process.GetProcessById(descriptor.ProcessId);
                        return identity with { ProcessId = descriptor.ProcessId, ProcessStartedUtc = process.StartTime.ToUniversalTime(), ExecutablePath = descriptor.ExecutablePath, InstanceId = descriptor.InstanceId, BaseUrl = descriptor.BaseUrl };
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }
            await Task.Delay(100);
        }
        throw new TimeoutException(TeardownAssert.Diagnostics(identity, null, stderr, [], "Packaged LocalHost descriptor did not become available."));
    }

    private static async Task<ControllerSessionRecord> WaitForPackagedControllerAsync(LocalHostProcessIdentity host, int processId, IReadOnlyList<string> stderr)
    {
        await using var client = CreateLocalClient(host);
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var controller = (await client.ListControllerSessionsAsync()).SingleOrDefault(item => item.ClientKind == "mcp" && item.Status == ControllerSessionStatus.Connected && item.Metadata.TryGetValue("processId", out var id) && id == processId.ToString());
            if (controller is not null)
            {
                return controller;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException(TeardownAssert.Diagnostics(host, null, stderr, [], "Packaged MCP controller did not register."));
    }

    private static LocalHostDescriptor ReadDescriptor(LocalHostProcessIdentity identity)
        => JsonSerializer.Deserialize<LocalHostDescriptor>(File.ReadAllText(identity.DescriptorPath), LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException("LocalHost descriptor was invalid.");

    private static LocalHostClient CreateLocalClient(LocalHostProcessIdentity host)
        => new(new HttpClient { BaseAddress = new Uri(host.BaseUrl) }, host.BaseUrl);

    private static string GetStructuredOrTextPayload(CallToolResult result)
        => result.StructuredContent is JsonElement structured
            ? structured.GetRawText()
            : result.Content.OfType<TextContentBlock>().First().Text;
}

internal sealed class PackagedLocalHostLaunchRecorder : IAsyncDisposable
{
    private readonly string _executablePath;
    private readonly HashSet<int> _knownProcessIds;
    private readonly List<PackagedLocalHostLaunch> _launched = [];
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _monitor;

    public PackagedLocalHostLaunchRecorder(string executablePath)
    {
        _executablePath = executablePath;
        _knownProcessIds = Snapshot(executablePath).Select(item => item.ProcessId).ToHashSet();
        _monitor = MonitorAsync();
    }

    public IReadOnlyList<PackagedLocalHostLaunch> Launched
    {
        get
        {
            lock (_launched)
            {
                return _launched.ToArray();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync();
        await _monitor;
        _cancellation.Dispose();
    }

    private async Task MonitorAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            foreach (var process in Snapshot(_executablePath))
            {
                lock (_launched)
                {
                    if (_knownProcessIds.Add(process.ProcessId))
                    {
                        _launched.Add(process);
                    }
                }
            }
            try
            {
                await Task.Delay(20, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static IReadOnlyList<PackagedLocalHostLaunch> Snapshot(string executablePath)
    {
        var results = new List<PackagedLocalHostLaunch>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, executablePath, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new PackagedLocalHostLaunch(process.Id, process.StartTime.ToUniversalTime()));
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
        return results;
    }
}

internal sealed record PackagedLocalHostLaunch(int ProcessId, DateTime StartedUtc)
{
    public bool IsRunning()
    {
        try
        {
            using var process = Process.GetProcessById(ProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime() == StartedUtc;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
