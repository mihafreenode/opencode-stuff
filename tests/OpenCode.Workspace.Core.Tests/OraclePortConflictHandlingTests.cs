using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OraclePortConflictHandlingTests
{
    [Fact]
    public void EnvironmentFileGenerator_UsesConfiguredOraclePorts()
    {
        var generator = new EnvironmentFileGenerator();
        var content = generator.Generate(new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-demo" },
            Services = new List<string> { "oracle-demo", "oracle-ords" },
            Oracle = new OracleWorkspacePreferences { HostPort = 1522, OrdsPort = 8182 },
        });

        Assert.Contains("ORACLE_HOST_PORT=1522", content);
        Assert.Contains("ORACLE_ORDS_PORT=8182", content);
        Assert.Contains("ORACLE_ORDS_BASE_URL=http://localhost:8182/ords", content);
        Assert.Contains("ORACLE_APEX_LOGIN_URL=http://localhost:8182/ords/apex", content);
    }

    [Fact]
    public void ComposeGenerator_UsesConfigurableOracleHostPortVariables()
    {
        var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
        var resolved = resolver.Resolve(new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-apex-ports" },
            Features = new List<string> { "core", "oracle-demo", "oracle-apex-demo" },
            Services = new List<string> { "oracle-demo", "oracle-ords" },
            Oracle = new OracleWorkspacePreferences { HostPort = 1522, OrdsPort = 8182 },
        });

        var compose = new ComposeGenerator().Generate(resolved, WorkspacePathBuilder.Build(Path.Combine(Path.GetTempPath(), "oracle-apex-ports")));

        Assert.Contains("\"${ORACLE_HOST_PORT}:1521\"", compose);
        Assert.Contains("\"${ORACLE_ORDS_PORT}:8080\"", compose);
    }

    [Fact]
    public async Task CreateWorkspaceAsync_TwoOracleApexLangWorkspaces_GetDistinctGeneratedHostPorts()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var root = Path.Combine(Path.GetTempPath(), $"oracle-multi-ports-{Guid.NewGuid():N}");
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"oracle-multi-ports-appdata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(appDataRoot);

        try
        {
            var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
            var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
            var ignorePolicyService = new WorkspaceIgnorePolicyService();
            var orchestrator = new WorkspaceOrchestrator(
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
                ignorePolicyService,
                new GitWorkspaceProvider(new ProcessRunner(), ignorePolicyService),
                new DockerService(new ProcessRunner()),
                new NoOpTerminalLauncher(),
                workspaceImageBuilder: new NoOpWorkspaceImageBuilder());

            var firstDefinition = CreateOracleApexLangDefinition("oracle-apexlang-a");
            var secondDefinition = CreateOracleApexLangDefinition("oracle-apexlang-b");
            var firstRoot = Path.Combine(root, "workspace-a");
            var secondRoot = Path.Combine(root, "workspace-b");

            var first = await orchestrator.CreateWorkspaceAsync(firstRoot, firstDefinition, includeRuntimeInspection: false);
            var second = await orchestrator.CreateWorkspaceAsync(secondRoot, secondDefinition, includeRuntimeInspection: false);

            var firstEnv = await File.ReadAllTextAsync(first.Paths.EnvironmentFilePath);
            var secondEnv = await File.ReadAllTextAsync(second.Paths.EnvironmentFilePath);

            Assert.Contains("ORACLE_HOST_PORT=1521", firstEnv);
            Assert.Contains("ORACLE_ORDS_PORT=8181", firstEnv);
            Assert.Contains("ORACLE_HOST_PORT=1522", secondEnv);
            Assert.Contains("ORACLE_ORDS_PORT=8182", secondEnv);

            var secondCompose = await File.ReadAllTextAsync(second.Paths.ComposePath);
            Assert.Contains("\"${ORACLE_HOST_PORT}:1521\"", secondCompose);
            Assert.Contains("\"${ORACLE_ORDS_PORT}:8080\"", secondCompose);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(appDataRoot))
            {
                Directory.Delete(appDataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DockerService_StartAsync_DetectsOraclePortConflictBeforeComposeUp()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oracle-port-conflict-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            File.WriteAllText(paths.ComposePath, "services:\n  workspace:\n    image: ubuntu:24.04\n");
            var runner = new SequenceProcessRunner(
                Match(" compose ", ProcessResultFor("docker compose config", 0)),
                Match("ps --format", ProcessResultFor("docker ps", 0, standardOutput: "other-oracle\t0.0.0.0:1521->1521/tcp")),
                Match(" compose ", ProcessResultFor("docker compose ps", 0, standardOutput: "NAME STATUS PORTS")),
                Match(GetHostPortCommandFragment(), ProcessResultFor(GetHostPortCommandFragment(), 0, standardOutput: GetHostPortDiagnosticOutput())));

            var docker = new DockerService(runner);
            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "oracle-demo" },
                Features = new List<string> { "core", "oracle-demo" },
                Services = new List<string> { "oracle-demo" },
            };

            var result = await docker.StartAsync(paths, definition);

            Assert.False(result.IsSuccess);
            Assert.Equal(WorkspaceFailureClassification.EnvironmentPortConflict, result.FailureClassification);
            Assert.Contains("Oracle port 1521 is already in use.", result.StandardError);
            Assert.Contains("Port 1521 currently owned by: other-oracle", result.StandardError);
            Assert.Contains("Last checked:", result.StandardError);
            Assert.Contains("Stop other Oracle workspace", result.StandardError);
            Assert.Contains("Use a different port", result.StandardError);
            Assert.DoesNotContain(runner.Commands, command => command.Contains(" up ", StringComparison.Ordinal));
            Assert.DoesNotContain(runner.Commands, command => command.Contains(" down ", StringComparison.Ordinal));
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
    public async Task DockerService_StartAsync_UsesAllocatedOracleHostPortFromRuntimeState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oracle-port-conflict-runtime-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            File.WriteAllText(paths.ComposePath, "services:\n  workspace:\n    image: ubuntu:24.04\n");
            new WorkspaceRuntimeStateService().Write(paths.RuntimeStatePath, new WorkspaceRuntimeStateRecord
            {
                Resources = new WorkspaceManagedRuntimeResources
                {
                    Ports =
                    [
                        new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.OracleDatabaseResourceId, ServiceId = "oracle-database", DisplayName = "Oracle Database", Protocol = "tcp", PreferredPort = 1521, AllocatedPort = 1522, ContainerPort = 1521, Endpoint = "tcp://localhost:1522", OpenUrl = "tcp://localhost:1522" },
                    ],
                },
            });

            var runner = new SequenceProcessRunner(
                Match(" compose ", ProcessResultFor("docker compose config", 0)),
                Match("ps --format", ProcessResultFor("docker ps", 0, standardOutput: "other-oracle\t0.0.0.0:1522->1521/tcp")),
                Match(" compose ", ProcessResultFor("docker compose ps", 0, standardOutput: "NAME STATUS PORTS")),
                Match(GetHostPortCommandFragment(), ProcessResultFor(GetHostPortCommandFragment(), 0, standardOutput: GetHostPortDiagnosticOutput(1522))));

            var docker = new DockerService(runner, new WorkspaceRuntimeStateService());
            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "oracle-demo" },
                Features = new List<string> { "core", "oracle-demo" },
                Services = new List<string> { "oracle-demo" },
            };

            var result = await docker.StartAsync(paths, definition);

            Assert.False(result.IsSuccess);
            Assert.Contains("Oracle port 1522 is already in use.", result.StandardError);
            Assert.DoesNotContain("Oracle port 1521 is already in use.", result.StandardError, StringComparison.Ordinal);
            Assert.Contains("Port 1522 currently owned by: other-oracle", result.StandardError);
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
    public async Task ProvisionAsync_PortConflictPreservesOracleDataAndDoesNotResetVolumes()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var root = Path.Combine(Path.GetTempPath(), $"oracle-provision-conflict-{Guid.NewGuid():N}");
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"oracle-provision-conflict-appdata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(appDataRoot);

        try
        {
            var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
            var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "oracle-demo" },
                Features = new List<string> { "core", "oracle-demo" },
                Services = new List<string> { "oracle-demo" },
            };

            var runner = new SequenceProcessRunner(
                Match(" compose ", ProcessResultFor("docker compose config", 0)),
                Match("ps --format", ProcessResultFor("docker ps", 0, standardOutput: "other-oracle\t0.0.0.0:1521->1521/tcp")),
                Match(" compose ", ProcessResultFor("docker compose ps", 0, standardOutput: "NAME STATUS PORTS")),
                Match(GetHostPortCommandFragment(), ProcessResultFor(GetHostPortCommandFragment(), 0, standardOutput: GetHostPortDiagnosticOutput())));

            var ignorePolicyService = new WorkspaceIgnorePolicyService();
            var orchestrator = new WorkspaceOrchestrator(
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
                ignorePolicyService,
                new GitWorkspaceProvider(new ProcessRunner(), ignorePolicyService),
                new DockerService(runner),
                new NoOpTerminalLauncher(),
                workspaceImageBuilder: new NoOpWorkspaceImageBuilder());

            var snapshot = await orchestrator.CreateWorkspaceAsync(root, definition, includeRuntimeInspection: false);
            var volumeMarkerPath = Path.Combine(snapshot.Paths.RootPath, "mounts", "user", "oracle-volume-marker.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(volumeMarkerPath)!);
            File.WriteAllText(volumeMarkerPath, "keep-oracle-data");

            var exception = await Assert.ThrowsAsync<WorkspaceEnvironmentConflictException>(() => orchestrator.ProvisionAsync(snapshot));

            Assert.Contains("Oracle port ", exception.Message, StringComparison.Ordinal);
            Assert.Contains("is already in use.", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Run recovery cleanup for this workspace only", exception.Message);
            Assert.Equal("keep-oracle-data", File.ReadAllText(volumeMarkerPath));
            Assert.DoesNotContain(runner.Commands, command => command.Contains(" down -v ", StringComparison.Ordinal));
            Assert.DoesNotContain(runner.Commands, command => command.Contains(" up -d", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(appDataRoot))
            {
                Directory.Delete(appDataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ValidateVolatileEnvironmentAsync_UsesWslDockerForPortPreflightWhenWindowsDockerPsTimesOut()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var root = Path.Combine(Path.GetTempPath(), $"oracle-wsl-fallback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var runner = new ScriptedProcessRunner(
                ExpectException("docker ps --format", new TimeoutException("docker ps timed out"), TimeSpan.FromSeconds(5)),
                ExpectResult("wsl.exe -- docker ps --format", ProcessResultFor("wsl docker ps", 0)),
                ExpectResult("docker compose --project-name oracle-demo --file", ProcessResultFor("docker compose ps", 0, standardOutput: "NAME STATUS PORTS"), TimeSpan.FromSeconds(5)),
                ExpectResult(GetHostPortCommandFragment(), ProcessResultFor(GetHostPortCommandFragment(), 0)),
                ExpectResult(GetHostPortCommandFragment(), ProcessResultFor(GetHostPortCommandFragment(), 0)));

            var docker = new DockerService(runner);
            var result = await docker.ValidateVolatileEnvironmentAsync(paths, CreateOracleApexLangDefinition("oracle-demo"));

            Assert.Null(result);
            Assert.Contains(runner.Commands, command => command.Contains("wsl.exe -- docker ps --format", StringComparison.Ordinal));
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
    public async Task ValidateVolatileEnvironmentAsync_UsesShortTimeoutForDockerPsPreflight()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var root = Path.Combine(Path.GetTempPath(), $"oracle-docker-timeout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var runner = new ScriptedProcessRunner(
                ExpectException("docker ps --format", new TimeoutException("docker ps timed out"), TimeSpan.FromSeconds(5)),
                ExpectResult("wsl.exe -- docker ps --format", ProcessResultFor("wsl docker ps", 0)),
                ExpectResult("docker compose --project-name oracle-demo --file", ProcessResultFor("docker compose ps", 0, standardOutput: "NAME STATUS PORTS"), TimeSpan.FromSeconds(5)),
                ExpectResult(GetHostPortCommandFragment(), ProcessResultFor(GetHostPortCommandFragment(), 0)),
                ExpectResult(GetHostPortCommandFragment(), ProcessResultFor(GetHostPortCommandFragment(), 0)));

            var docker = new DockerService(runner);
            await docker.ValidateVolatileEnvironmentAsync(paths, CreateOracleApexLangDefinition("oracle-demo"));

            var dockerPsCall = runner.Invocations.First(invocation => invocation.Command.Contains("docker ps --format", StringComparison.Ordinal));
            Assert.Equal(TimeSpan.FromSeconds(5), dockerPsCall.Timeout);
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
    public async Task RunSimpleDockerCommandAsync_WhenWindowsDockerUnavailableButWslDockerAvailable_ThrowsPreciseMessage()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var runner = new ScriptedProcessRunner(
            ExpectException("docker version", new TimeoutException("docker version timed out")),
            ExpectResult("wsl.exe -- docker ps --format", ProcessResultFor("wsl docker ps", 0)));

        var docker = new DockerService(runner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => docker.RunSimpleDockerCommandAsync(["version"]));

        Assert.Equal("Docker is reachable from WSL but not from Windows. Enable Docker Desktop Windows CLI integration or configure this workspace to use WSL Docker.", exception.Message);
    }

    private static bool CanRunGit()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            process?.WaitForExit(5000);
            return process is not null && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static ProcessResult ProcessResultFor(string command, int exitCode, string standardOutput = "", string standardError = "")
        => new()
        {
            Command = command,
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            StandardOutputLines = string.IsNullOrWhiteSpace(standardOutput) ? Array.Empty<string>() : standardOutput.Split(Environment.NewLine),
            StandardErrorLines = string.IsNullOrWhiteSpace(standardError) ? Array.Empty<string>() : standardError.Split(Environment.NewLine),
            Duration = TimeSpan.FromMilliseconds(10),
        };

    private static ExpectedCommand Match(string fragment, ProcessResult result)
        => new(fragment, result);

    private static string GetHostPortCommandFragment()
        => OperatingSystem.IsWindows() ? "powershell.exe" : "bash";

    private static string GetHostPortDiagnosticOutput(int port = 1521)
        => OperatingSystem.IsWindows()
            ? $"LISTEN port={port} pid=123 process=com.docker.backend"
            : $"State Recv-Q Send-Q Local Address:Port Peer Address:PortProcess\nLISTEN 0 4096 0.0.0.0:{port} 0.0.0.0:* users:((\"docker-proxy\",pid=123,fd=4))";

    private sealed record ExpectedCommand(string Fragment, ProcessResult Result);

    private sealed record ScriptedCommand(string Fragment, ProcessResult? Result, Exception? Exception, TimeSpan? ExpectedTimeout);

    private sealed class SequenceProcessRunner : IProcessRunner
    {
        private readonly Queue<ExpectedCommand> _expectedCommands;

        public SequenceProcessRunner(params ExpectedCommand[] expectedCommands)
        {
            _expectedCommands = new Queue<ExpectedCommand>(expectedCommands);
        }

        public List<string> Commands { get; } = new();

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            var command = string.Join(' ', new[] { fileName }.Concat(arguments));
            Commands.Add(command);

            Assert.NotEmpty(_expectedCommands);
            var expected = _expectedCommands.Dequeue();
            Assert.Contains(expected.Fragment, command, StringComparison.Ordinal);

            return Task.FromResult(new ProcessResult
            {
                Command = command,
                ExitCode = expected.Result.ExitCode,
                StandardOutput = expected.Result.StandardOutput,
                StandardError = expected.Result.StandardError,
                StandardOutputLines = expected.Result.StandardOutputLines,
                StandardErrorLines = expected.Result.StandardErrorLines,
                Duration = expected.Result.Duration,
                FailureClassification = expected.Result.FailureClassification,
            });
        }
    }

    private static ScriptedCommand ExpectResult(string fragment, ProcessResult result, TimeSpan? expectedTimeout = null)
        => new(fragment, result, null, expectedTimeout);

    private static ScriptedCommand ExpectException(string fragment, Exception exception, TimeSpan? expectedTimeout = null)
        => new(fragment, null, exception, expectedTimeout);

    private sealed class ScriptedProcessRunner(params ScriptedCommand[] scriptedCommands) : IProcessRunner
    {
        private readonly List<ScriptedCommand> _scriptedCommands = new(scriptedCommands);

        public List<string> Commands { get; } = new();
        public List<(string Command, TimeSpan? Timeout)> Invocations { get; } = new();

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            var command = string.Join(' ', new[] { fileName }.Concat(arguments));
            Commands.Add(command);
            Invocations.Add((command, timeout));

            Assert.NotEmpty(_scriptedCommands);
            var scriptedIndex = _scriptedCommands.FindIndex(item => command.Contains(item.Fragment, StringComparison.Ordinal));
            Assert.True(scriptedIndex >= 0, $"No scripted command matched: {command}");
            var scripted = _scriptedCommands[scriptedIndex];
            _scriptedCommands.RemoveAt(scriptedIndex);
            if (scripted.ExpectedTimeout is not null)
            {
                Assert.Equal(scripted.ExpectedTimeout, timeout);
            }

            if (scripted.Exception is not null)
            {
                throw scripted.Exception;
            }

            var result = scripted.Result!;
            return Task.FromResult(new ProcessResult
            {
                Command = command,
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
                StandardOutputLines = result.StandardOutputLines,
                StandardErrorLines = result.StandardErrorLines,
                Duration = result.Duration,
                FailureClassification = result.FailureClassification,
            });
        }
    }

    private static WorkspaceDefinition CreateOracleApexLangDefinition(string workspaceName)
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = workspaceName, Image = "ubuntu:24.04" },
            Features = new List<string> { "core", "oracle-demo", "oracle-apex-demo", "oracle-apexlang-demo" },
            Services = new List<string> { "oracle-demo", "oracle-ords" },
        };

    private sealed class NoOpTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoOpWorkspaceImageBuilder : IWorkspaceImageBuilder
    {
        public Task EnsureImageAsync(WorkspaceDefinition definition, WorkspacePaths paths, GeneratedWorkspaceArtifacts artifacts, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
