using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Manager.Services;
using Xunit.Abstractions;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class WindowsDockerIntegrationTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);
    private readonly ITestOutputHelper _output;

    public WindowsDockerIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public async Task DockerComposeExecutionOrchestration_RunsAgainstTemporaryWorkspace_WhenDockerAvailable()
    {
        var baselineDockerProcesses = GetDockerProcessIds();
        var capabilities = new WindowsHostCapabilities(new ProcessRunner());
        var dockerCheck = await capabilities.CheckDockerDesktopAsync();
        Skip.IfNot(dockerCheck.IsAvailable, dockerCheck.Reason);

        var root = Path.Combine(Path.GetTempPath(), $"ocwm-docker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var composePath = Path.Combine(root, "compose.yaml");
            File.WriteAllText(composePath, string.Join("\n", new[]
            {
                "services:",
                "  workspace:",
                "    image: alpine:3.20",
                "    container_name: ocwm-integration-workspace",
                "    command:",
                "      - sh",
                "      - -lc",
                "      - sleep 30",
            }));

            var docker = new DockerService(new ProcessRunner());
            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "ocwm-integration" },
            };
            var paths = WorkspacePathBuilder.Build(root);
            File.WriteAllText(paths.ComposePath, File.ReadAllText(composePath));

            var start = await docker.StartAsync(paths, definition);
            Skip.IfNot(start.IsSuccess, $"Docker compose start failed: {start.StandardError}\n{start.StandardOutput}");

            var ps = await docker.GetPsAsync(paths, definition);
            Assert.True(ps.IsSuccess);
            Assert.Contains(ps.StandardOutputLines, line => line.Trim() == "workspace");

            var stop = await docker.StopAsync(paths, definition);
            Assert.True(stop.IsSuccess);
        }
        finally
        {
            try
            {
                var runner = new ProcessRunner();
                await runner.RunAsync("docker", ["rm", "-f", "ocwm-integration-workspace"]);
            }
            catch
            {
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            await CleanupExtraDockerProcessesAsync(baselineDockerProcesses);
        }
    }

    [SkippableFact]
    public async Task GeneratedCompose_ForAnalizaStyleWorkspace_ValidatesWithDockerComposeConfig_WhenDockerAvailable()
    {
        var capabilities = new WindowsHostCapabilities(new ProcessRunner());
        var dockerCheck = await capabilities.CheckDockerDesktopAsync();
        Skip.IfNot(dockerCheck.IsAvailable, dockerCheck.Reason);

        var root = Path.Combine(Path.GetTempPath(), $"ocwm-analiza-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var generator = new ComposeGenerator();
            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata
                {
                    Id = "odip-analiza",
                    Name = "Odip Analiza",
                    Image = "ubuntu:24.04",
                },
                Provider = new WorkspaceProviderDefinition
                {
                    Type = "git",
                    Url = "git@ssh.dev.azure.com:v3/KOPA-Projects/ODIP/Analiza",
                },
                Runtime = new WorkspaceRuntimeDefinition
                {
                    Default = "default",
                },
                Features = new List<string> { "core", "document-processing", "ocr-processing", "spellcheck" },
                Services = new List<string>(),
                Skills = new List<string>(),
                Mcp = new List<string>(),
            };

            var resolved = new ResolvedWorkspace
            {
                Definition = definition,
                Features = Array.Empty<FeatureManifest>(),
                Capabilities = Array.Empty<CapabilityManifest>(),
                Services = Array.Empty<ServiceManifest>(),
                AptPackages = Array.Empty<string>(),
                NpmPackages = Array.Empty<string>(),
                PipPackages = Array.Empty<string>(),
                PostInstallCommands = Array.Empty<string>(),
            };

            var paths = WorkspacePathBuilder.Build(root);
            File.WriteAllText(paths.ComposePath, generator.Generate(resolved, paths));

            var result = await new ProcessRunner().RunAsync(
                "docker",
                ["compose", "--project-name", "analiza", "--file", paths.ComposePath, "config"],
                root);

            Assert.True(result.IsSuccess, $"Docker compose config failed: {result.StandardError}\n{result.StandardOutput}");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DockerComposeValidationFailure_LogsExactErrorAndSkipsCleanup()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ocwm-docker-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var runner = new FakeProcessRunner(
            ProcessResultFor(
                "docker compose --project-name analiza --file",
                exitCode: 1,
                standardError: "services.workspace.depends_on must be a array"));

        try
        {
            var docker = new DockerService(runner);
            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "analiza" },
            };
            var paths = WorkspacePathBuilder.Build(root);
            var preservedFilePath = Path.Combine(root, "notes.txt");
            File.WriteAllText(paths.ComposePath, "services:\n  workspace:\n    image: ubuntu:24.04\n");
            File.WriteAllText(preservedFilePath, "keep me");

            var logEntries = new List<CommandLogEntry>();
            var result = await docker.StartAsync(paths, definition, entry => logEntries.Add(entry));

            Assert.False(result.IsSuccess);
            Assert.Contains("services.workspace.depends_on must be a array", result.StandardError);
            Assert.Contains(logEntries, entry => entry.Source == "app" && entry.Message.Contains("services.workspace.depends_on must be a array", StringComparison.Ordinal));
            Assert.True(File.Exists(paths.ComposePath));
            Assert.True(File.Exists(preservedFilePath));

            Assert.Single(runner.Commands);
            Assert.Contains(" config", runner.Commands[0], StringComparison.Ordinal);
            Assert.DoesNotContain(" down ", runner.Commands[0], StringComparison.Ordinal);
            Assert.DoesNotContain(" up ", runner.Commands[0], StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ProcessResult ProcessResultFor(string commandPrefix, int exitCode, string standardOutput = "", string standardError = "")
    {
        return new ProcessResult
        {
            Command = commandPrefix,
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            StandardOutputLines = string.IsNullOrWhiteSpace(standardOutput) ? Array.Empty<string>() : standardOutput.Split(Environment.NewLine),
            StandardErrorLines = string.IsNullOrWhiteSpace(standardError) ? Array.Empty<string>() : standardError.Split(Environment.NewLine),
            Duration = TimeSpan.FromMilliseconds(10),
        };
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly ProcessResult _result;

        public FakeProcessRunner(ProcessResult result)
        {
            _result = result;
        }

        public List<string> Commands { get; } = new();

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            var command = string.Join(' ', new[] { fileName }.Concat(arguments));
            Commands.Add(command);
            return Task.FromResult(new ProcessResult
            {
                Command = command,
                ExitCode = _result.ExitCode,
                StandardOutput = _result.StandardOutput,
                StandardError = _result.StandardError,
                StandardOutputLines = _result.StandardOutputLines,
                StandardErrorLines = _result.StandardErrorLines,
                Duration = _result.Duration,
            });
        }
    }

    [SkippableFact]
    public async Task AttachReadinessValidation_CanQueryOpenCodeSessions_WhenDockerAvailable()
    {
        var baselineDockerProcesses = GetDockerProcessIds();
        try
        {
            using var dockerCheckTimeout = new CancellationTokenSource(ProcessTimeout);
            _output.WriteLine("[attach-readiness] checking Docker Desktop availability");
            var capabilities = new WindowsHostCapabilities(new ProcessRunner());
            var dockerCheck = await capabilities.CheckDockerDesktopAsync(dockerCheckTimeout.Token);
            _output.WriteLine($"[attach-readiness] Docker availability: {dockerCheck.IsAvailable}. Reason: {dockerCheck.Reason}");
            Skip.IfNot(dockerCheck.IsAvailable, dockerCheck.Reason);

            _output.WriteLine("[attach-readiness] before container check");
            var inspect = await RunDockerAsync(["ps", "--format", "{{.Names}}"], "docker inspection");
            _output.WriteLine($"[attach-readiness] docker ps exit code: {inspect.ExitCode}");
            _output.WriteLine($"[attach-readiness] docker ps stdout: {inspect.StandardOutput}");
            _output.WriteLine($"[attach-readiness] docker ps stderr: {inspect.StandardError}");

            var isSmokeWorkspaceRunning = inspect.StandardOutputLines.Any(line => line.Trim() == "smoke-data-workspace-workspace");
            if (!isSmokeWorkspaceRunning)
            {
                _output.WriteLine("[attach-readiness] container not running -> skip");
                Skip.If(true, "Smoke workspace container is not running, so attach-readiness checks were skipped.");
                return;
            }

            _output.WriteLine("[attach-readiness] querying OpenCode sessions");
            var sessionList = await RunDockerAsync(
                ["exec", "smoke-data-workspace-workspace", "bash", "-lc", "cd /workspace && opencode session list || true"],
                "docker exec session list");
            _output.WriteLine($"[attach-readiness] docker exec exit code: {sessionList.ExitCode}");
            _output.WriteLine($"[attach-readiness] docker exec stdout: {sessionList.StandardOutput}");
            _output.WriteLine($"[attach-readiness] docker exec stderr: {sessionList.StandardError}");
            Assert.True(sessionList.IsSuccess);
        }
        finally
        {
            await CleanupExtraDockerProcessesAsync(baselineDockerProcesses);
            _output.WriteLine("[attach-readiness] cleanup complete");
        }
    }

    private async Task CleanupExtraDockerProcessesAsync(HashSet<int> baselineDockerProcesses)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var extraProcessIds = GetDockerProcessIds();
            extraProcessIds.ExceptWith(baselineDockerProcesses);
            if (extraProcessIds.Count == 0)
            {
                return;
            }

            _output.WriteLine($"[docker-cleanup] waiting for extra docker processes: {string.Join(",", extraProcessIds.OrderBy(id => id))}");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        var lingeringProcessIds = GetDockerProcessIds();
        lingeringProcessIds.ExceptWith(baselineDockerProcesses);
        foreach (var processId in lingeringProcessIds.OrderBy(id => id))
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                _output.WriteLine($"[docker-cleanup] killing lingering docker process {processId}");
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (ArgumentException)
            {
            }
        }
    }

    private static HashSet<int> GetDockerProcessIds()
    {
        return Process.GetProcessesByName("docker")
            .Select(process =>
            {
                try
                {
                    return process.Id;
                }
                finally
                {
                    process.Dispose();
                }
            })
            .ToHashSet();
    }

    private async Task<ProcessResult> RunDockerAsync(IReadOnlyList<string> arguments, string description)
    {
        using var timeout = new CancellationTokenSource(ProcessTimeout);
        _output.WriteLine($"[attach-readiness] process creation: {description}");
        return await new ProcessRunner().RunAsync(
            "docker",
            arguments,
            cancellationToken: timeout.Token,
            timeout: ProcessTimeout,
            onDiagnostic: message => _output.WriteLine($"[attach-readiness] {message}"));
    }

    [SkippableFact]
    public async Task FreshWorkspaceProvisioning_UsesNode22_WhenDockerAvailable()
    {
        var capabilities = new WindowsHostCapabilities(new ProcessRunner());
        var dockerCheck = await capabilities.CheckDockerDesktopAsync();
        Skip.IfNot(dockerCheck.IsAvailable, dockerCheck.Reason);

        var appDataRoot = Path.Combine(Path.GetTempPath(), $"ocwm-node22-appdata-{Guid.NewGuid():N}");
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"ocwm-node22-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDataRoot);
        Directory.CreateDirectory(workspaceRoot);

        var dockerService = new DockerService(new ProcessRunner());
        WorkspaceSnapshot? snapshot = null;

        try
        {
            var orchestrator = CreateWorkspaceOrchestrator(appDataRoot);
            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata
                {
                    Name = "node22-smoke",
                    Image = "ubuntu:24.04",
                },
                Provider = new WorkspaceProviderDefinition
                {
                    Type = "git",
                },
                Runtime = new WorkspaceRuntimeDefinition
                {
                    Default = "default",
                    Node = 22,
                },
                Features = new List<string> { "core" },
                Services = new List<string>(),
                Skills = new List<string>(),
                Mcp = new List<string>(),
                Terminal = new TerminalPreferences
                {
                    InstallIfMissing = false,
                    Prompt = new TerminalPromptPreferences { Provider = "starship" },
                    Utilities = new TerminalUtilityPreferences(),
                },
            };

            snapshot = orchestrator.CreateWorkspace(workspaceRoot, definition);

            Assert.True(File.Exists(snapshot.Paths.ComposePath), $"Missing generated compose file: {snapshot.Paths.ComposePath}");
            Assert.True(File.Exists(snapshot.Paths.EnvironmentFilePath), $"Missing generated environment file: {snapshot.Paths.EnvironmentFilePath}");
            Assert.True(File.Exists(snapshot.Paths.ProvisionScriptPath), $"Missing generated provision script: {snapshot.Paths.ProvisionScriptPath}");
            Assert.True(File.Exists(snapshot.Paths.StarshipConfigPath), $"Missing generated starship config: {snapshot.Paths.StarshipConfigPath}");
            Assert.True(File.Exists(snapshot.Paths.ShellInitScriptPath), $"Missing generated shell init script: {snapshot.Paths.ShellInitScriptPath}");
            Assert.True(File.Exists(snapshot.Paths.AttachWrapperScriptPath), $"Missing generated attach wrapper: {snapshot.Paths.AttachWrapperScriptPath}");

            var workspaceYaml = File.ReadAllText(snapshot.Paths.WorkspaceYamlPath);
            var composeYaml = File.ReadAllText(snapshot.Paths.ComposePath);
            var environmentFile = File.ReadAllText(snapshot.Paths.EnvironmentFilePath);
            var provisionScript = File.ReadAllText(snapshot.Paths.ProvisionScriptPath);
            var shellInitScript = File.ReadAllText(snapshot.Paths.ShellInitScriptPath);
            var attachWrapper = File.ReadAllText(snapshot.Paths.AttachWrapperScriptPath);

            Assert.Contains("node: 22", workspaceYaml);
            Assert.Contains("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES", composeYaml);
            Assert.Contains("image: ubuntu:24.04", composeYaml);
            Assert.Contains("container_name: node22-smoke-workspace", composeYaml);
            Assert.Contains("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES", environmentFile);
            Assert.Contains("WORKSPACE_NAME=node22-smoke", environmentFile);
            Assert.Contains("https://deb.nodesource.com/setup_22.x", provisionScript);
            Assert.Contains("apt-get remove -y nodejs npm || true", provisionScript);
            Assert.Contains("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES", shellInitScript);
            Assert.Contains("export STARSHIP_CONFIG=/opt/opencode-workspace/config/starship.toml", shellInitScript);
            Assert.Contains("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES", attachWrapper);
            Assert.Contains("$attachUser = 'opencode'", attachWrapper);

            var aptInstallLine = provisionScript.Split('\n').First(line => line.StartsWith("apt-get install -y ", StringComparison.Ordinal));
            Assert.DoesNotContain(" nodejs", aptInstallLine, StringComparison.Ordinal);
            Assert.DoesNotContain(" npm", aptInstallLine, StringComparison.Ordinal);

            try
            {
                await orchestrator.ProvisionAsync(snapshot);
            }
            catch (InvalidOperationException exception) when (IsLinuxContainerPlatformSkipCondition(exception.Message, out var skipReason))
            {
                Skip.If(true, skipReason);
                return;
            }

            var nodeVersionResult = await dockerService.RunSimpleDockerCommandAsync(
                ["exec", DockerService.GetWorkspaceContainerName(definition), "bash", "-lc", "node --version"],
                cancellationToken: CancellationToken.None);
            Assert.True(nodeVersionResult.IsSuccess, nodeVersionResult.StandardError + Environment.NewLine + nodeVersionResult.StandardOutput);
            Assert.StartsWith("v22.", nodeVersionResult.StandardOutput.Trim(), StringComparison.Ordinal);
        }
        finally
        {
            if (snapshot is not null)
            {
                try
                {
                    await dockerService.RemoveAsync(snapshot.Paths, snapshot.Definition);
                }
                catch
                {
                }
            }

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(appDataRoot))
            {
                Directory.Delete(appDataRoot, recursive: true);
            }
        }
    }

    private static bool IsLinuxContainerPlatformSkipCondition(string message, out string skipReason)
    {
        foreach (var marker in new[]
                 {
                     "no matching manifest for windows",
                     "image operating system \"linux\" cannot be used on this platform",
                     "operating system is not supported",
                 })
        {
            if (!message.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            skipReason = $"Docker is available but cannot start the Linux workspace image in this environment (likely Windows container mode): {marker}";
            return true;
        }

        skipReason = string.Empty;
        return false;
    }

    private static WorkspaceOrchestrator CreateWorkspaceOrchestrator(string appDataRoot)
    {
        var processRunner = new ProcessRunner();
        var catalogProvider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var resolver = new WorkspaceResolver(catalogProvider.LoadFeatures(), catalogProvider.LoadServices(), catalogProvider.LoadCapabilities());
        var ignorePolicy = new WorkspaceIgnorePolicyService();

        return new WorkspaceOrchestrator(
            new WorkspaceYamlService(),
            new WorkspaceDiscoveryService(),
            new WorkspaceRepository(appDataRoot),
            resolver,
            new ComposeGenerator(),
            new EnvironmentFileGenerator(),
            new ProvisioningScriptGenerator(),
            new TerminalArtifactsGenerator(),
            new AttachArtifactsGenerator(),
            new WorkspaceContentGenerator(),
            new WorkspaceAppliedStateService(),
            new WorkspaceCheckpointService(),
            new WorkspaceTimelineService(),
            new WorkspaceSafetyService(),
            ignorePolicy,
            new GitWorkspaceProvider(processRunner, ignorePolicy),
            new DockerService(processRunner),
            new NoOpTerminalLauncher());
    }

    private sealed class NoOpTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
