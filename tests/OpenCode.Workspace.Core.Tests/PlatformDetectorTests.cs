using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Tests;

public sealed class PlatformDetectorTests
{
    [Fact]
    public async Task DetectAsync_UsesHostOsArchitectureAndDockerSignals()
    {
        var detector = new PlatformDetector(new StubProcessRunner(
            ("docker", ["--version"], Success("Docker version 27.0.0")),
            ("docker", ["info"], Success("Server: Docker")),
            ("docker", ["buildx", "ls"], Success("NAME/NODE DRIVER/ENDPOINT STATUS BUILDKIT PLATFORMS\ndefault* docker running v0.0.0 linux/amd64, linux/arm64"))));

        var result = await detector.DetectAsync();

        Assert.NotEqual(HostOperatingSystem.Unknown, result.OperatingSystem);
        Assert.NotEqual(HostArchitecture.Unknown, result.Architecture);
        Assert.NotEmpty(result.HostDescription);
        Assert.True(result.Docker.CliAvailable);
        Assert.True(result.Docker.EngineReachable);
        Assert.True(result.Docker.BuildxAvailable);
        Assert.Contains("linux/amd64", result.Docker.SupportedPlatforms);
        Assert.Contains("linux/arm64", result.Docker.SupportedPlatforms);
    }

    [Fact]
    public void ParseSupportedPlatforms_ExtractsPlatformsFromBuildxOutput()
    {
        var platforms = PlatformDetector.ParseSupportedPlatforms(
        [
            "NAME/NODE       DRIVER/ENDPOINT   STATUS    BUILDKIT   PLATFORMS",
            "default*        docker                                     linux/amd64, linux/arm64",
            "stale           docker-container                           linux/amd64",
        ]);

        Assert.Equal(["linux/amd64", "linux/arm64"], platforms);
    }

    [Fact]
    public void ParseSupportedPlatforms_NormalizesBuilderSummaryTokens()
    {
        var platforms = PlatformDetector.ParseSupportedPlatforms(
        [
            "default* docker running v0.0.0 linux/amd64 (+3)",
            "arm-builder docker running v0.0.0 linux/arm64*",
        ]);

        Assert.Equal(["linux/amd64", "linux/arm64"], platforms);
    }

    private static ProcessResult Success(string stdout) => new()
    {
        Command = "docker",
        ExitCode = 0,
        StandardOutput = stdout,
        StandardError = string.Empty,
        StandardOutputLines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        StandardErrorLines = Array.Empty<string>(),
        Duration = TimeSpan.FromMilliseconds(1),
    };

    private sealed class StubProcessRunner(params (string FileName, string[] Arguments, ProcessResult Result)[] commands) : IProcessRunner
    {
        private readonly Queue<(string FileName, string[] Arguments, ProcessResult Result)> _commands = new(commands);

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            var expected = _commands.Dequeue();
            Assert.Equal(expected.FileName, fileName);
            Assert.Equal(expected.Arguments, arguments.ToArray());
            return Task.FromResult(expected.Result);
        }
    }
}
