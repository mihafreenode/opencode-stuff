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
}
