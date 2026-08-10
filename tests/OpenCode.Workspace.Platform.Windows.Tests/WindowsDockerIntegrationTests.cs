using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Platform.Windows;

namespace OpenCode.Workspace.Platform.Windows.Tests;

public sealed class WindowsDockerIntegrationTests
{
    [SkippableFact]
    public async Task DockerComposeExecutionOrchestration_RunsAgainstTemporaryWorkspace_WhenDockerAvailable()
    {
        var capabilities = new WindowsHostCapabilities(new ProcessRunner());
        var dockerCheck = await capabilities.CheckDockerDesktopAsync();
        Skip.IfNot(dockerCheck.IsAvailable, dockerCheck.Reason);

        var root = Path.Combine(Path.GetTempPath(), $"ocwm-docker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var docker = new DockerService(new ProcessRunner());
            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "ocwm-integration" },
            };
            var paths = WorkspacePathBuilder.Build(root);
            File.WriteAllText(paths.ComposePath, string.Join("\n", new[]
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
                await new ProcessRunner().RunAsync("docker", ["rm", "-f", "ocwm-integration-workspace"]);
            }
            catch
            {
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [SkippableFact]
    public async Task DockerComposeExecutionOrchestration_UsesWslComposeTranslationWithoutCorruptingNamedVolumes_WhenDockerAvailable()
    {
        var capabilities = new WindowsHostCapabilities(new ProcessRunner());
        var dockerCheck = await capabilities.CheckDockerDesktopAsync();
        Skip.IfNot(dockerCheck.IsAvailable, dockerCheck.Reason);

        var root = Path.Combine(Path.GetTempPath(), $"ocwm-wsl-compose-{Guid.NewGuid():N}");
        var bindRoot = Path.Combine(root, "bind");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(bindRoot);

        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "ocwm-wsl-compose" },
        };
        var paths = WorkspacePathBuilder.Build(root);
        var docker = new DockerService(new ProcessRunner());
        SetPreferWslDocker(docker, value: true);
        var bindMountSource = bindRoot.Replace('\\', '/');

        File.WriteAllText(paths.ComposePath, string.Join("\n", new[]
        {
            "services:",
            "  workspace:",
            "    image: alpine:3.20",
            "    container_name: ocwm-wsl-compose-workspace",
            "    command:",
            "      - sh",
            "      - -lc",
            "      - sleep 30",
            "    volumes:",
            "      - ocwm-wsl-compose-data:/data",
            $"      - {bindMountSource}:/workspace-bind",
            "volumes:",
            "  ocwm-wsl-compose-data:",
        }));

        try
        {
            var start = await docker.StartAsync(paths, definition);
            Skip.IfNot(start.IsSuccess, $"Docker compose start failed: {start.StandardError}\n{start.StandardOutput}");

            var ps = await docker.GetPsAsync(paths, definition);
            Assert.True(ps.IsSuccess, $"Docker compose ps failed: {ps.StandardError}\n{ps.StandardOutput}");
            Assert.Contains(ps.StandardOutputLines, line => line.Trim() == "workspace");
        }
        finally
        {
            try
            {
                await new ProcessRunner().RunAsync(
                    "docker",
                    ["compose", "--project-name", "ocwm-wsl-compose", "--file", paths.ComposePath, "down", "-v", "--remove-orphans"],
                    root);
            }
            catch
            {
            }

            try
            {
                await new ProcessRunner().RunAsync("docker", ["rm", "-f", "ocwm-wsl-compose-workspace"]);
            }
            catch
            {
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
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
                Features = ["core", "document-processing", "ocr-processing", "spellcheck"],
                Services = [],
                Skills = [],
                Mcp = [],
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

    [SkippableFact]
    public async Task DockerComposeValidationFailure_WithPreferredWslDocker_PreservesComposeStderr()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "WSL Docker command translation requires a Windows host.");

        var root = Path.Combine(Path.GetTempPath(), $"ocwm-docker-wsl-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var runner = new SequencedFakeProcessRunner(
                ProcessResultFor(
                    "wsl.exe -- docker compose --project-name analiza --file",
                    exitCode: 1,
                    standardError: "invalid mount config for type \"volume\": mount path must be absolute"));

            var docker = new DockerService(runner);
            SetPreferWslDocker(docker, value: true);

            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "analiza" },
            };
            var paths = WorkspacePathBuilder.Build(root);
            File.WriteAllText(paths.ComposePath, "services:\n  workspace:\n    image: ubuntu:24.04\n");

            var result = await docker.StartAsync(paths, definition);

            Assert.False(result.IsSuccess);
            Assert.Contains("invalid mount config", result.StandardError, StringComparison.Ordinal);
            Assert.Single(runner.Commands);
            Assert.Contains("docker compose", runner.Commands[0], StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [SkippableFact]
    public async Task DockerExecProvisionFailure_WithPreferredWslDocker_PreservesProvisionStderr()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "WSL Docker fallback behavior requires a Windows host.");

        var root = Path.Combine(Path.GetTempPath(), $"ocwm-docker-exec-stderr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var runner = new SequencedFakeProcessRunner(
                ProcessResultFor(
                    "docker exec ocwm-docker-exec-stderr-workspace bash /opt/opencode-workspace/config/provision.sh",
                    exitCode: 1,
                    standardError: "Failure point: Oracle demo user setup failed."));

            var docker = new DockerService(runner);
            SetPreferWslDocker(docker, value: true);

            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "ocwm-docker-exec-stderr" },
            };
            var paths = WorkspacePathBuilder.Build(root);

            var result = await docker.RunProvisionScriptAsync(definition, paths);

            Assert.False(result.IsSuccess);
            Assert.Contains("Failure point: Oracle demo user setup failed.", result.StandardError, StringComparison.Ordinal);
            Assert.Single(runner.Commands);
            Assert.Contains("docker exec", runner.Commands[0], StringComparison.Ordinal);
            Assert.DoesNotContain("wsl.exe", runner.Commands[0], StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [SkippableFact]
    public async Task DockerExecProvisionTimeout_DoesNotFallbackToWslDocker()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "WSL Docker fallback behavior requires a Windows host.");

        var root = Path.Combine(Path.GetTempPath(), $"ocwm-docker-exec-timeout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var runner = new ThrowingProcessRunner(new TimeoutException("Process timed out after 120 seconds: docker exec ..."));
            var docker = new DockerService(runner);
            SetPreferWslDocker(docker, value: true);

            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "ocwm-docker-exec-timeout" },
            };
            var paths = WorkspacePathBuilder.Build(root);

            var exception = await Assert.ThrowsAsync<TimeoutException>(() => docker.RunProvisionScriptAsync(definition, paths));

            Assert.Contains("Process timed out", exception.Message, StringComparison.Ordinal);
            Assert.Single(runner.Commands);
            Assert.Contains("docker exec", runner.Commands[0], StringComparison.Ordinal);
            Assert.DoesNotContain("wsl.exe", runner.Commands[0], StringComparison.Ordinal);
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

    private static void SetPreferWslDocker(DockerService docker, bool value)
    {
        var field = typeof(DockerService).GetField("_preferWslDocker", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(docker, value);
    }

    private sealed class SequencedFakeProcessRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new(results);

        public List<string> Commands { get; } = new();

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            var command = string.Join(' ', new[] { fileName }.Concat(arguments));
            Commands.Add(command);
            Assert.NotEmpty(_results);
            var result = _results.Dequeue();
            return Task.FromResult(new ProcessResult
            {
                Command = command,
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
                StandardOutputLines = result.StandardOutputLines,
                StandardErrorLines = result.StandardErrorLines,
                Duration = result.Duration,
            });
        }
    }

    private sealed class ThrowingProcessRunner(Exception exception) : IProcessRunner
    {
        private readonly Exception _exception = exception;

        public List<string> Commands { get; } = new();

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            var command = string.Join(' ', new[] { fileName }.Concat(arguments));
            Commands.Add(command);
            return Task.FromException<ProcessResult>(_exception);
        }
    }
}
