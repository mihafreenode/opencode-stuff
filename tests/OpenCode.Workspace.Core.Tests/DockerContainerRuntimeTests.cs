using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Tests;

public sealed class DockerContainerRuntimeTests
{
    [Fact]
    public async Task RunSimpleDockerCommandAsync_ForwardsToDockerServiceBehavior()
    {
        var runner = new RecordingProcessRunner();
        var runtime = new DockerContainerRuntime(new DockerService(runner));

        var result = await runtime.RunSimpleDockerCommandAsync(["--version"]);

        Assert.True(result.IsSuccess);
        Assert.Equal("docker", runner.LastFileName);
        Assert.Equal(["--version"], runner.LastArguments);
    }

    [Fact]
    public void GetWorkspaceContainerName_UsesExistingDockerNaming()
    {
        var runtime = new DockerContainerRuntime(new DockerService(new RecordingProcessRunner()));

        var name = runtime.GetWorkspaceContainerName(new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "Demo Workspace" },
        });

        Assert.Equal("demo-workspace-workspace", name);
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public string LastFileName { get; private set; } = string.Empty;
        public IReadOnlyList<string> LastArguments { get; private set; } = Array.Empty<string>();

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            LastFileName = fileName;
            LastArguments = arguments.ToArray();
            return Task.FromResult(new ProcessResult
            {
                Command = $"{fileName} {string.Join(' ', LastArguments)}",
                ExitCode = 0,
                StandardOutput = "ok",
                StandardError = string.Empty,
                StandardOutputLines = ["ok"],
                StandardErrorLines = Array.Empty<string>(),
                Duration = TimeSpan.FromMilliseconds(1),
            });
        }
    }
}
