using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenCode.Workspace.Api;
using OpenCode.Workspace.AppSupport;
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

    [Fact]
    [Trait("Category", "PackageIntegration")]
    public async Task ExtractedDistribution_ResolvesPackagedContent_AndHostsExitGracefully()
    {
        var packageRoot = await CreateExtractedDistributionAsync();
        var outsideRepositoryRoot = Path.Combine(_root, "outside repo");
        Directory.CreateDirectory(outsideRepositoryRoot);

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

        var start = await mcp.Client.CallToolAsync("run_smoke", new Dictionary<string, object?>
        {
            ["templateId"] = "empty-workspace",
            ["timeout"] = "00:05:00",
        });
        var operation = JsonSerializer.Deserialize<McpOperationModel>(start.StructuredContent!.Value.GetRawText())!;
        Assert.NotEmpty(operation.OperationId);

        McpOperationModel current = operation;
        long afterSequence = 0;
        var seenSequences = new HashSet<long>();
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
        var smokeResult = current.Result!.Value.Deserialize<OpenCode.Workspace.Core.Smoke.WorkspaceSmokeResult>();
        Assert.NotNull(smokeResult);
        Assert.True(smokeResult!.CleanupVerificationSucceeded);
        await mcp.DisposeClientAndTransportAsync(TimeSpan.FromSeconds(30));
        Assert.False(mcp.Report.ForcedTerminationRequired);
        Assert.Equal(0, mcp.Report.ExitCode);
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
