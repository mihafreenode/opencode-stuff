using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenCode.Workspace.Api;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.LocalClient;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Net.WebSockets;
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
        Assert.True(File.Exists(Path.Combine(packageRoot, "README.md")));
        Assert.True(File.Exists(Path.Combine(packageRoot, "release-manifest.json")));
        Assert.True(Directory.Exists(Path.Combine(packageRoot, "catalog", "templates")));
        Assert.True(File.Exists(Path.Combine(packageRoot, "docs", "getting-started.md")));
        Assert.True(File.Exists(Path.Combine(packageRoot, "docs", "integrations", "mcp.md")));
        Assert.True(File.Exists(Path.Combine(packageRoot, "docs", "integrations", "local-browser-terminal.md")));
        Assert.True(File.Exists(Path.Combine(packageRoot, "docs", "integrations", "cloudflare-remote-access.md")));
        Assert.True(File.Exists(Path.Combine(packageRoot, "docs", "reference", "configuration.md")));
        Assert.False(Directory.Exists(Path.Combine(packageRoot, "docs", "development")));
        Assert.False(Directory.Exists(Path.Combine(packageRoot, "docs", "history")));
        Assert.False(Directory.Exists(Path.Combine(packageRoot, "docs", "reference", "agent-onboarding")));

        Assert.True(File.Exists(GetHostExecutablePath(packageRoot, "OpenCode.Workspace")));
        Assert.True(File.Exists(GetHostExecutablePath(Path.Combine(packageRoot, "bin", "local-host"), "OpenCode.Workspace.LocalHost")));
        Assert.True(File.Exists(GetHostExecutablePath(Path.Combine(packageRoot, "bin", "cli"), "OpenCode.Workspace.Cli")));
        Assert.True(File.Exists(GetHostExecutablePath(Path.Combine(packageRoot, "bin", "mcp"), "OpenCode.Workspace.Mcp")));
        Assert.True(File.Exists(GetHostExecutablePath(Path.Combine(packageRoot, "bin", "remote-bridge"), "OpenCode.Workspace.RemoteBridge")));
        var remoteBridgeConfigPath = Path.Combine(packageRoot, "config", "remote-bridge", "appsettings.json");
        Assert.True(File.Exists(remoteBridgeConfigPath));
        var remoteBridgeConfigText = File.ReadAllText(remoteBridgeConfigPath);
        using (var remoteBridgeConfig = JsonDocument.Parse(remoteBridgeConfigText))
        {
            var remoteAccess = remoteBridgeConfig.RootElement.GetProperty("RemoteAccess");
            var cloudflare = remoteBridgeConfig.RootElement.GetProperty("Cloudflare");
            Assert.False(remoteAccess.GetProperty("Enabled").GetBoolean());
            Assert.Equal(string.Empty, remoteAccess.GetProperty("PublicOrigin").GetString());
            Assert.Equal(string.Empty, cloudflare.GetProperty("TeamDomain").GetString());
            Assert.Equal(string.Empty, cloudflare.GetProperty("Issuer").GetString());
            Assert.Equal(string.Empty, cloudflare.GetProperty("Audience").GetString());
        }
        Assert.DoesNotContain("token", remoteBridgeConfigText, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(packageRoot, "bin", "api")));
        Assert.False(Directory.Exists(Path.Combine(packageRoot, "bin", "desktop")));
        foreach (var hostDirectory in new[]
        {
            packageRoot,
            Path.Combine(packageRoot, "bin", "local-host"),
            Path.Combine(packageRoot, "bin", "cli"),
            Path.Combine(packageRoot, "bin", "mcp"),
            Path.Combine(packageRoot, "bin", "remote-bridge"),
        })
        {
            Assert.True(File.Exists(Path.Combine(hostDirectory, GetHostFxrFileName())), $"Self-contained runtime missing from '{hostDirectory}'.");
        }

        Assert.Empty(FindHostPayloadOutside(packageRoot, "OpenCode.Workspace.Mcp", Path.Combine("bin", "mcp"), "mcp.appsettings.json"));
        Assert.Empty(FindHostPayloadOutside(packageRoot, "OpenCode.Workspace.RemoteBridge", Path.Combine("bin", "remote-bridge")));
        Assert.DoesNotContain(Directory.EnumerateFiles(packageRoot, "*.pdb", SearchOption.AllDirectories), _ => true);

        using (var releaseManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(packageRoot, "release-manifest.json"))))
        {
            var manifestVersion = releaseManifest.RootElement.GetProperty("version").GetString();
            if (!fixture.IsExternalPackage)
            {
                Assert.Equal("0.0.0-test", manifestVersion);
            }
            else
            {
                Assert.Matches("^[0-9A-Za-z][0-9A-Za-z._+-]*$", manifestVersion!);
            }
            Assert.Equal(GetRuntimeIdentifier(), releaseManifest.RootElement.GetProperty("runtimeIdentifier").GetString());
            Assert.True(releaseManifest.RootElement.GetProperty("selfContained").GetBoolean());
            Assert.Equal(40, releaseManifest.RootElement.GetProperty("gitCommit").GetString()!.Length);
            Assert.NotEqual(default, releaseManifest.RootElement.GetProperty("buildTimestamp").GetDateTimeOffset());
            AssertAssemblyMetadataVersion(packageRoot, releaseManifest.RootElement.GetProperty("version").GetString()!);
        }

        if (!OperatingSystem.IsWindows())
        {
            foreach (var executable in new[]
            {
                GetHostExecutablePath(packageRoot, "OpenCode.Workspace"),
                GetHostExecutablePath(Path.Combine(packageRoot, "bin", "local-host"), "OpenCode.Workspace.LocalHost"),
                GetHostExecutablePath(Path.Combine(packageRoot, "bin", "cli"), "OpenCode.Workspace.Cli"),
                GetHostExecutablePath(Path.Combine(packageRoot, "bin", "mcp"), "OpenCode.Workspace.Mcp"),
                GetHostExecutablePath(Path.Combine(packageRoot, "bin", "remote-bridge"), "OpenCode.Workspace.RemoteBridge"),
            })
            {
                Assert.True((File.GetUnixFileMode(executable) & UnixFileMode.UserExecute) != 0, $"Package host is not user-executable: '{executable}'.");
            }
        }

        var desktopServices = new WorkspaceDesktopServiceFactory().Create(packageRoot, Path.Combine(_root, "appdata"));
        Assert.Equal(Path.Combine(packageRoot, "catalog"), desktopServices.InstallationLayout.CatalogRoot);
        Assert.NotEmpty(desktopServices.CatalogProvider.LoadTemplates());

        var cliExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "cli"), "OpenCode.Workspace.Cli");
        await using (var cliHelp = await PackagedProcessHarness.StartAsync("cli-help", cliExecutable, ["--help"], outsideRepositoryRoot))
        {
            await cliHelp.WaitForExitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(0, cliHelp.ExitCode);
        }
        await using var cliSmoke = await PackagedProcessHarness.StartAsync("cli-smoke-list", cliExecutable, ["smoke", "list", "--format", "json"], outsideRepositoryRoot);
        await cliSmoke.WaitForExitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(0, cliSmoke.ExitCode);
        Assert.Contains("empty-workspace", cliSmoke.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(TestPaths.RepositoryRoot, cliSmoke.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fatal", cliSmoke.StandardError, StringComparison.OrdinalIgnoreCase);

        if (!OperatingSystem.IsMacOS())
        {
            await using var cliRuntime = await PackagedProcessHarness.StartAsync("cli-runtime-list", cliExecutable, ["runtime", "list", "--format", "json"], outsideRepositoryRoot);
            await cliRuntime.WaitForExitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(0, cliRuntime.ExitCode);
            Assert.Contains("resources", cliRuntime.StandardOutput, StringComparison.Ordinal);
        }

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
        if (!OperatingSystem.IsMacOS())
        {
            var ready = await apiClient.GetFromJsonAsync<ApiHealthResponse>("api/v1/health/ready");
            Assert.NotNull(ready);
        }
        var apiTemplates = await apiClient.GetFromJsonAsync<ApiEnvelope<IReadOnlyList<WorkspaceTemplateSummaryModel>>>("api/v1/templates");
        Assert.Contains(apiTemplates!.Data, item => item.TemplateId == "empty-workspace");
        var smokeDefinitions = await apiClient.GetStringAsync("api/v1/smoke/definitions");
        Assert.Contains("empty-workspace", smokeDefinitions, StringComparison.Ordinal);
        var apiHealth = await apiClient.GetFromJsonAsync<ApiEnvelope<ServerHealthModel>>("api/v1/server/health");
        Assert.Equal(UnixPackageArchive.ResolvePhysicalPath(Path.Combine(packageRoot, "catalog")), UnixPackageArchive.ResolvePhysicalPath(apiHealth!.Data.CatalogRoot));
        Assert.True((await apiClient.GetAsync("terminal/vendor/xterm.js")).IsSuccessStatusCode);
        Assert.True((await apiClient.GetAsync("terminal/vendor/xterm.css")).IsSuccessStatusCode);
        Assert.True(File.Exists(Path.Combine(packageRoot, "bin", "local-host", "wwwroot", "terminal", "terminal.js")));
        Assert.True(File.Exists(Path.Combine(packageRoot, "bin", "local-host", "wwwroot", "terminal", "terminal.css")));
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
        Assert.Equal(UnixPackageArchive.ResolvePhysicalPath(Path.Combine(packageRoot, "catalog")), UnixPackageArchive.ResolvePhysicalPath(mcpHealth.CatalogRoot));
        await mcp.DisposeClientAndTransportAsync(TimeSpan.FromSeconds(30));
        Assert.False(mcp.Report.ForcedTerminationRequired);
        Assert.NotNull(mcp.Report.ExitedUtc);
        Assert.False(ProcessStillRunning(mcp.Report.ProcessId, mcp.Report.ExecutablePath));

        var remoteBridgeExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "remote-bridge"), "OpenCode.Workspace.RemoteBridge");
        await using var remoteBridge = await PackagedProcessHarness.StartAsync(
            "remote-bridge-disabled",
            remoteBridgeExecutable,
            ["--RemoteAccess:Enabled=false"],
            outsideRepositoryRoot);
        await remoteBridge.WaitForExitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(0, remoteBridge.ExitCode);
        Assert.False(remoteBridge.Report.ForcedTerminationRequired);

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
            Assert.Contains(expectedMcp, configure.StandardOutput.Replace("\\\\", "\\", StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.DoesNotContain(TestPaths.RepositoryRoot, configure.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dotnet run", configure.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bin/api", configure.StandardOutput, StringComparison.OrdinalIgnoreCase);
        }

        if (OperatingSystem.IsMacOS()) return;

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
    public async Task PackagedLocalHost_ConPtyHelper_Detaches_Reattaches_And_Stops_Cleanly()
    {
        if (!OperatingSystem.IsWindows()) return;
        var packageRoot = CreateExtractedDistribution();
        var workingDirectory = Path.Combine(_root, "packaged pty acceptance with spaces");
        var stateRoot = Path.Combine(_root, "packaged pty state");
        Directory.CreateDirectory(workingDirectory);
        var workspaceId = $"pty-package-{Guid.NewGuid():N}";
        var hostExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "local-host"), "OpenCode.Workspace.LocalHost");
        var cliExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "cli"), "OpenCode.Workspace.Cli");
        var childExecutable = GetHostExecutablePath(Path.Combine(packageRoot, "bin", "local-host", "test-assets", "conpty-child"), "OpenCode.Workspace.ConPtyTestChild");
        Assert.True(File.Exists(childExecutable));
        var port = PackagedHostValidationHelpers.GetFreeTcpPort();
        await using var host = await PackagedProcessHarness.StartAsync("packaged-pty-host", hostExecutable, ["--shutdown-on-stdin-eof"], workingDirectory, new Dictionary<string, string?>
        {
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
            ["localHost__stateRoot"] = stateRoot,
            ["mcp__workspaceStateRoot"] = stateRoot,
            ["OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME"] = "1",
            ["OPENCODE_LOCALHOST_TERMINAL_TEST_EXECUTABLE"] = childExecutable,
        });
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        await PackagedHostValidationHelpers.WaitForApiHealthyAsync(http, TimeSpan.FromSeconds(60));
        await using var client = new LocalHostClient(new HttpClient { BaseAddress = http.BaseAddress }, http.BaseAddress!.ToString());
        var session = await client.CreateInteractiveAgentSessionAsync(workspaceId, new CreateInteractiveAgentSessionRequest { CommandId = "package-pty-session", WorkspaceId = workspaceId, Title = "Package PTY" });
        var runtime = await client.StartInteractiveTerminalAsync(session.InteractiveAgentSessionId, new StartInteractiveTerminalRequest());
        Assert.Equal(InteractiveTerminalRuntimeStatus.Running, runtime.Status);
        Assert.True(runtime.ProcessId > 0);
        using (var provider = Process.GetProcessById(runtime.ProcessId!.Value))
        {
            Assert.Equal(Path.GetFullPath(childExecutable), Path.GetFullPath(provider.MainModule!.FileName), ignoreCase: true);
        }

        var firstAttachment = await client.AttachInteractiveSessionAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "package-attach-a", ClientInstanceId = "package-helper-a" });
        await client.ReportInteractiveSessionProviderSessionAsync(session.InteractiveAgentSessionId, firstAttachment.Attachment.AttachmentId, new InteractiveSessionAttachmentProviderSessionRequest { AttachmentToken = firstAttachment.AttachmentToken, ProviderSessionId = "package-provider-session", IdentitySource = ProviderSessionIdentitySource.DirectHandshake });
        AssertNoLegacyTerminalDependency(packageRoot, firstAttachment);
        await using var firstHelper = await StartPackagedHelperAsync("packaged-helper-a", cliExecutable, workingDirectory, stateRoot, firstAttachment);
        await WaitForAttachmentStatusAsync(client, session.InteractiveAgentSessionId, firstAttachment.Attachment.AttachmentId, InteractiveAttachmentStatus.Active, TimeSpan.FromSeconds(15));
        await firstHelper.WriteStandardInputAsync("package-first");
        await WaitForHarnessOutputAsync(firstHelper, "ECHO:package-first", TimeSpan.FromSeconds(15));

        await client.DetachInteractiveSessionAttachmentAsync(session.InteractiveAgentSessionId, firstAttachment.Attachment.AttachmentId, new DetachInteractiveSessionAttachmentRequest { ClientInstanceId = "package-helper-a", Reason = "package_detach" });
        await firstHelper.WaitForExitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, firstHelper.ExitCode);
        var detachedRuntime = await client.GetInteractiveTerminalAsync(session.InteractiveAgentSessionId);
        Assert.Equal(InteractiveTerminalRuntimeStatus.Running, detachedRuntime.Status);
        Assert.Equal(runtime.RuntimeId, detachedRuntime.RuntimeId);
        Assert.Equal(runtime.ProcessId, detachedRuntime.ProcessId);

        var browserPage = await http.GetAsync($"terminal/{session.InteractiveAgentSessionId}");
        Assert.True(browserPage.IsSuccessStatusCode, $"Browser page returned {(int)browserPage.StatusCode}: {await browserPage.Content.ReadAsStringAsync()}");
        Assert.Contains("default-src 'none'", browserPage.Headers.GetValues("Content-Security-Policy").Single());
        Assert.True(File.Exists(Path.Combine(packageRoot, "bin", "local-host", "wwwroot", "terminal", "terminal.js")));
        Assert.True(File.Exists(Path.Combine(packageRoot, "bin", "local-host", "wwwroot", "terminal", "terminal.css")));
        Assert.True((await http.GetAsync("terminal/vendor/xterm.js")).IsSuccessStatusCode);

        var browserAttachment = await client.AttachInteractiveSessionAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "package-browser-attach", ClientInstanceId = "package-browser", AttachmentKind = InteractiveAttachmentKind.WebTerminal });
        using (var browserSocket = new ClientWebSocket())
        {
            browserSocket.Options.AddSubProtocol(InteractiveTerminalWebSocketService.SubProtocol);
            browserSocket.Options.SetRequestHeader("Origin", http.BaseAddress!.GetLeftPart(UriPartial.Authority));
            await browserSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal/ws"), CancellationToken.None);
            await SendWebSocketJsonAsync(browserSocket, new InteractiveTerminalWebSocketHello
            {
                InteractiveAgentSessionId = session.InteractiveAgentSessionId,
                TerminalRuntimeId = runtime.RuntimeId,
                AttachmentId = browserAttachment.Attachment.AttachmentId,
                AttachmentToken = browserAttachment.AttachmentToken,
                AfterSequence = detachedRuntime.LatestSequence,
            });
            Assert.Equal("attached", (await ReceiveWebSocketControlAsync(browserSocket)).Type);
            await browserSocket.SendAsync(System.Text.Encoding.ASCII.GetBytes("package-browser\r"), WebSocketMessageType.Binary, true, CancellationToken.None);
            Assert.Equal("ack", (await ReceiveWebSocketControlAsync(browserSocket)).Type);
            Assert.Contains("ECHO:package-browser", await WaitForWebSocketOutputAsync(browserSocket, "ECHO:package-browser"), StringComparison.Ordinal);
            await browserSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "browser handoff", CancellationToken.None);
        }
        await WaitForSessionDetachedAsync(client, session.InteractiveAgentSessionId, TimeSpan.FromSeconds(15));
        var afterBrowser = await client.GetInteractiveTerminalAsync(session.InteractiveAgentSessionId);
        Assert.Equal(runtime.RuntimeId, afterBrowser.RuntimeId);
        Assert.Equal(runtime.ProcessId, afterBrowser.ProcessId);
        Assert.Equal("package-provider-session", (await client.GetInteractiveAgentSessionAsync(session.InteractiveAgentSessionId)).ProviderSessionId);

        var secondAttachment = await client.AttachInteractiveSessionAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "package-attach-b", ClientInstanceId = "package-helper-b" });
        await using var secondHelper = await StartPackagedHelperAsync("packaged-helper-b", cliExecutable, workingDirectory, stateRoot, secondAttachment);
        await WaitForAttachmentStatusAsync(client, session.InteractiveAgentSessionId, secondAttachment.Attachment.AttachmentId, InteractiveAttachmentStatus.Active, TimeSpan.FromSeconds(15));
        await secondHelper.WriteStandardInputAsync("package-second");
        await WaitForHarnessOutputAsync(secondHelper, "ECHO:package-second", TimeSpan.FromSeconds(15));
        var resized = await client.ResizeInteractiveTerminalAsync(session.InteractiveAgentSessionId, new TerminalResizeRequest { AttachmentId = secondAttachment.Attachment.AttachmentId, AttachmentToken = secondAttachment.AttachmentToken, Columns = 151, Rows = 47 });
        Assert.Equal(new InteractiveTerminalDimensions { Columns = 151, Rows = 47 }, resized.Dimensions);
        Assert.Equal(runtime.RuntimeId, resized.RuntimeId);
        Assert.Equal(runtime.ProcessId, resized.ProcessId);
        Assert.Equal("package-provider-session", (await client.GetInteractiveAgentSessionAsync(session.InteractiveAgentSessionId)).ProviderSessionId);

        var stopped = await client.StopInteractiveTerminalAsync(session.InteractiveAgentSessionId);
        Assert.Equal(InteractiveTerminalRuntimeStatus.Exited, stopped.Status);
        await secondHelper.WaitForExitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, secondHelper.ExitCode);
        Assert.False(ProcessStillRunning(runtime.ProcessId.Value, childExecutable));
        await host.RequestGracefulShutdownByClosingStandardInputAsync(TimeSpan.FromSeconds(30));
        Assert.False(host.Report.ForcedTerminationRequired);
        AssertFileIsNotLocked(Path.Combine(stateRoot, "local-host", "host.lock"));
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
        var cleanupOperation = JsonSerializer.Deserialize<McpOperationModel>(GetStructuredOrTextPayload(preflightCleanup))!;
        McpOperationModel cleanupCurrent = cleanupOperation;
        var cleanupDeadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2);
        while (DateTimeOffset.UtcNow < cleanupDeadline)
        {
            var polled = await mcp.Client.CallToolAsync("get_operation", new Dictionary<string, object?> { ["operationId"] = cleanupOperation.OperationId });
            cleanupCurrent = JsonSerializer.Deserialize<McpToolEnvelope<McpOperationModel>>(GetStructuredOrTextPayload(polled))!.Data;
            if (cleanupCurrent.Status is McpOperationStatus.Succeeded or McpOperationStatus.Failed or McpOperationStatus.Cancelled)
            {
                break;
            }
            await Task.Delay(250);
        }
        Assert.Equal(McpOperationStatus.Succeeded, cleanupCurrent.Status);
        var cleanupResult = cleanupCurrent.Result!.Value.Deserialize<SmokeCleanupResult>(OpenCodeWorkspaceMcpContract.JsonOptions)!;
        Assert.True(cleanupResult.Succeeded);
        Assert.True(cleanupResult.VerificationSucceeded);
        WriteJsonArtifact(packageArtifactRoot, "preflight-cleanup.json", cleanupResult);

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
        var createOperation = JsonSerializer.Deserialize<McpOperationModel>(create.StructuredContent!.Value.GetRawText())!;
        McpOperationModel createCurrent = createOperation;
        var createDeadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2);
        while (DateTimeOffset.UtcNow < createDeadline)
        {
            var polled = await mcp.Client.CallToolAsync("get_operation", new Dictionary<string, object?> { ["operationId"] = createOperation.OperationId });
            createCurrent = JsonSerializer.Deserialize<McpToolEnvelope<McpOperationModel>>(polled.StructuredContent!.Value.GetRawText())!.Data;
            if (createCurrent.Status is McpOperationStatus.Succeeded or McpOperationStatus.Failed or McpOperationStatus.Cancelled)
            {
                break;
            }
            await Task.Delay(250);
        }
        Assert.Equal(McpOperationStatus.Succeeded, createCurrent.Status);
        var workspace = createCurrent.Result!.Value.Deserialize<WorkspaceRecordModel>(OpenCodeWorkspaceMcpContract.JsonOptions)!;
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

    private static void AssertAssemblyMetadataVersion(string packageRoot, string version)
    {
        foreach (var relativePath in new[]
        {
            "OpenCode.Workspace.dll",
            Path.Combine("bin", "cli", "OpenCode.Workspace.Cli.dll"),
            Path.Combine("bin", "local-host", "OpenCode.Workspace.LocalHost.dll"),
            Path.Combine("bin", "mcp", "OpenCode.Workspace.Mcp.dll"),
            Path.Combine("bin", "remote-bridge", "OpenCode.Workspace.RemoteBridge.dll"),
        })
        {
            var productVersion = FileVersionInfo.GetVersionInfo(Path.Combine(packageRoot, relativePath)).ProductVersion;
            Assert.StartsWith(version, productVersion, StringComparison.Ordinal);
            if (version.Contains("-rc.", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("-ci.", productVersion, StringComparison.Ordinal);
                Assert.DoesNotContain("-local.", productVersion, StringComparison.Ordinal);
            }
        }
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

    private static IReadOnlyList<string> FindHostPayloadOutside(string packageRoot, string hostName, string canonicalDirectory, params string[] additionalPayloadNames)
    {
        var payloadNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            hostName,
            hostName + ".exe",
            hostName + ".deps.json",
            hostName + ".runtimeconfig.json",
        };
        payloadNames.UnionWith(additionalPayloadNames);

        return Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Where(path => payloadNames.Contains(Path.GetFileName(path)))
            .Select(path => Path.GetRelativePath(packageRoot, path))
            .Where(path => !path.StartsWith(canonicalDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

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


    private static async Task<PackagedProcessHarness> StartPackagedHelperAsync(string name, string cliExecutable, string workingDirectory, string stateRoot, InteractiveSessionAttachResult attachment)
        => await PackagedProcessHarness.StartAsync(name, cliExecutable,
        [
            "interactive-session", "attach",
            "--state-root", stateRoot,
            "--session-id", attachment.Session.InteractiveAgentSessionId,
            "--attachment-id", attachment.Attachment.AttachmentId,
            "--attachment-token", attachment.AttachmentToken,
        ], workingDirectory);

    private static async Task WaitForAttachmentStatusAsync(LocalHostClient client, string sessionId, string attachmentId, InteractiveAttachmentStatus status, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await client.GetInteractiveAttachmentsAsync(sessionId)).Any(item => item.AttachmentId == attachmentId && item.Status == status)) return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Attachment '{attachmentId}' did not reach {status}.");
    }

    private static async Task WaitForSessionDetachedAsync(LocalHostClient client, string sessionId, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (string.IsNullOrWhiteSpace((await client.GetInteractiveAgentSessionAsync(sessionId)).ActiveAttachmentId)) return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Interactive session '{sessionId}' did not detach its presentation.");
    }

    private static Task SendWebSocketJsonAsync<T>(ClientWebSocket socket, T value)
        => socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value, LocalHostContract.JsonOptions), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<InteractiveTerminalWebSocketControl> ReceiveWebSocketControlAsync(ClientWebSocket socket)
        => JsonSerializer.Deserialize<InteractiveTerminalWebSocketControl>(await ReceiveWebSocketMessageAsync(socket, WebSocketMessageType.Text), LocalHostContract.JsonOptions)!;

    private static Task<byte[]> ReceiveWebSocketBinaryAsync(ClientWebSocket socket)
        => ReceiveWebSocketMessageAsync(socket, WebSocketMessageType.Binary);

    private static async Task<string> WaitForWebSocketOutputAsync(ClientWebSocket socket, string expected)
    {
        var output = new System.Text.StringBuilder();
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var control = await ReceiveWebSocketControlAsync(socket);
            Assert.Equal("output", control.Type);
            output.Append(System.Text.Encoding.UTF8.GetString(await ReceiveWebSocketBinaryAsync(socket)));
            await SendWebSocketJsonAsync(socket, new InteractiveTerminalWebSocketControl { Type = "ack", Sequence = control.Sequence });
            if (output.ToString().Contains(expected, StringComparison.Ordinal)) return output.ToString();
        }
        throw new TimeoutException($"WebSocket output did not contain '{expected}'. Output: {output}");
    }

    private static async Task<byte[]> ReceiveWebSocketMessageAsync(ClientWebSocket socket, WebSocketMessageType expectedType)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var content = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, timeout.Token);
            Assert.Equal(expectedType, result.MessageType);
            content.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return content.ToArray();
    }

    private static async Task WaitForHarnessOutputAsync(PackagedProcessHarness harness, string expected, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (harness.StandardOutput.Contains(expected, StringComparison.Ordinal)) return;
            if (harness.HasExited) break;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Packaged helper output did not contain '{expected}'. stdout={harness.StandardOutput} stderr={harness.StandardError}");
    }

    private static void AssertNoLegacyTerminalDependency(string packageRoot, InteractiveSessionAttachResult attachment)
    {
        var command = string.Join(' ', attachment.LaunchDescriptor.Arguments);
        Assert.Contains(Path.Combine(packageRoot, "bin", "cli"), command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestPaths.RepositoryRoot, command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scripts/windows-debug", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attach-workspace.ps1", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".ps1", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet run", command, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertFileIsNotLocked(string path)
    {
        if (!File.Exists(path)) return;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(stream.CanWrite);
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
