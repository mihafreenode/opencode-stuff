using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Tests;

public sealed class RuntimeResolverTests
{
    private readonly RuntimeResolver _resolver = new();

    [Fact]
    public async Task ResolveAsync_PrefersNativePlatformWhenAvailable()
    {
        var plan = await _resolver.ResolveAsync(new WorkspaceDefinition(), new HostPlatformInfo
        {
            OperatingSystem = HostOperatingSystem.Windows,
            Architecture = HostArchitecture.X64,
            NativeContainerPlatform = "linux/amd64",
            Docker = new ContainerRuntimeAvailability
            {
                EngineId = "docker",
                CliAvailable = true,
                EngineReachable = true,
                BuildxAvailable = true,
                SupportedPlatforms = ["linux/amd64", "linux/arm64"],
            },
        });

        Assert.True(plan.IsAvailable);
        Assert.Equal("docker", plan.Runtime);
        Assert.Equal("linux/amd64", plan.TargetPlatform);
        Assert.Equal(RuntimeCompatibilityMode.MultiArchitecture, plan.CompatibilityMode);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToCompatiblePlatformWhenNativeMissing()
    {
        var plan = await _resolver.ResolveAsync(new WorkspaceDefinition(), new HostPlatformInfo
        {
            OperatingSystem = HostOperatingSystem.MacOS,
            Architecture = HostArchitecture.Arm64,
            NativeContainerPlatform = "linux/arm64",
            Docker = new ContainerRuntimeAvailability
            {
                EngineId = "docker",
                CliAvailable = true,
                EngineReachable = true,
                BuildxAvailable = true,
                SupportedPlatforms = ["linux/amd64"],
            },
        });

        Assert.True(plan.IsAvailable);
        Assert.Equal("linux/amd64", plan.TargetPlatform);
        Assert.Equal(RuntimeCompatibilityMode.Emulated, plan.CompatibilityMode);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsUnavailableWhenDockerIsNotReachable()
    {
        var plan = await _resolver.ResolveAsync(new WorkspaceDefinition(), new HostPlatformInfo
        {
            OperatingSystem = HostOperatingSystem.Linux,
            Architecture = HostArchitecture.X64,
            NativeContainerPlatform = "linux/amd64",
            Docker = new ContainerRuntimeAvailability
            {
                EngineId = "docker",
                CliAvailable = true,
                EngineReachable = false,
                DiagnosticSummary = "Docker engine check failed.",
            },
        });

        Assert.False(plan.IsAvailable);
        Assert.Equal(RuntimeCompatibilityMode.Unavailable, plan.CompatibilityMode);
        Assert.Contains("not reachable", plan.DiagnosticExplanation, StringComparison.OrdinalIgnoreCase);
    }
}
