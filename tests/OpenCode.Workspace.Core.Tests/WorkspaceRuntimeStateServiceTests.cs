using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceRuntimeStateServiceTests
{
    [Fact]
    public void WorkspacePathBuilder_UsesCrossPlatformRuntimeStatePath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"runtime-state-paths-{Guid.NewGuid():N}");

        var paths = WorkspacePathBuilder.Build(root);

        Assert.Equal(Path.Combine(root, ".opencode", "local", "runtime-state.yaml"), paths.RuntimeStatePath);

        if (!OperatingSystem.IsWindows())
        {
            var incorrectSingleSegmentPath = Path.Combine(root, ".opencode\\local\\runtime-state.yaml");
            Assert.NotEqual(incorrectSingleSegmentPath, paths.RuntimeStatePath);
        }
    }

    [Fact]
    public void Read_WhenFileIsMissing_ReturnsNull()
    {
        var service = new WorkspaceRuntimeStateService();
        var filePath = Path.Combine(Path.GetTempPath(), $"missing-runtime-state-{Guid.NewGuid():N}.yaml");

        var loaded = service.Read(filePath);

        Assert.Null(loaded);
    }

    [Fact]
    public void WriteAndRead_RoundTripsMachineLocalRuntimeState()
    {
        var service = new WorkspaceRuntimeStateService();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"runtime-state-{Guid.NewGuid():N}");
        var filePath = Path.Combine(tempRoot, ".opencode", "local", "runtime-state.yaml");
        var state = new WorkspaceRuntimeStateRecord
        {
            ResolvedEngine = "docker",
            ResolvedPlatform = "linux/arm64",
            CompatibilityMode = RuntimeCompatibilityMode.Native.ToString(),
            LastSuccessfulProvision = DateTimeOffset.Parse("2026-06-19T08:00:00Z"),
        };

        try
        {
            service.Write(filePath, state);

            var loaded = service.Read(filePath);

            Assert.NotNull(loaded);
            Assert.Equal("docker", loaded.ResolvedEngine);
            Assert.Equal("linux/arm64", loaded.ResolvedPlatform);
            Assert.Equal("Native", loaded.CompatibilityMode);
            Assert.Equal(DateTimeOffset.Parse("2026-06-19T08:00:00Z"), loaded.LastSuccessfulProvision);

            if (!OperatingSystem.IsWindows())
            {
                Assert.False(File.Exists(Path.Combine(tempRoot, ".opencode\\local\\runtime-state.yaml")));
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                TestFileSystem.DeleteDirectoryIfExists(tempRoot);
            }
        }
    }

    [Fact]
    public void Read_WhenFileIsCorrupted_ReturnsNull()
    {
        var service = new WorkspaceRuntimeStateService();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"runtime-state-corrupt-{Guid.NewGuid():N}");
        var filePath = Path.Combine(tempRoot, ".opencode", "local", "runtime-state.yaml");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "resolvedEngine: [broken\n");

            var loaded = service.Read(filePath);

            Assert.Null(loaded);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                TestFileSystem.DeleteDirectoryIfExists(tempRoot);
            }
        }
    }

    [Fact]
    public void CreateState_MapsResolvedRuntimePlan()
    {
        var service = new WorkspaceRuntimeStateService();
        var state = service.CreateState(new ResolvedRuntimePlan
        {
            Runtime = "docker",
            TargetPlatform = "linux/amd64",
            CompatibilityMode = RuntimeCompatibilityMode.Emulated,
        }, DateTimeOffset.Parse("2026-06-19T09:00:00Z"));

        Assert.Equal("docker", state.ResolvedEngine);
        Assert.Equal("linux/amd64", state.ResolvedPlatform);
        Assert.Equal("Emulated", state.CompatibilityMode);
        Assert.Equal(DateTimeOffset.Parse("2026-06-19T09:00:00Z"), state.LastSuccessfulProvision);
    }

    [Fact]
    public void CreateState_PreservesUnavailableRuntimePlanForMachineLocalRecoveryState()
    {
        var service = new WorkspaceRuntimeStateService();

        var state = service.CreateState(new ResolvedRuntimePlan
        {
            Runtime = "docker",
            TargetPlatform = "linux/arm64",
            CompatibilityMode = RuntimeCompatibilityMode.Unavailable,
            SupportLevel = SupportLevel.Unavailable,
            IsAvailable = false,
            DiagnosticExplanation = "Docker is unavailable on this host.",
            HostPlatform = new HostPlatformInfo
            {
                OperatingSystem = HostOperatingSystem.MacOS,
                Architecture = HostArchitecture.Arm64,
                HostDescription = "macOS arm64",
                NativeContainerPlatform = "linux/arm64",
                Docker = new ContainerRuntimeAvailability
                {
                    EngineId = "docker",
                    CliAvailable = false,
                    EngineReachable = false,
                    BuildxAvailable = false,
                    SupportedPlatforms = [],
                },
            },
        });

        Assert.Equal("docker", state.ResolvedEngine);
        Assert.Equal("linux/arm64", state.ResolvedPlatform);
        Assert.Equal("Unavailable", state.CompatibilityMode);
    }

    [Fact]
    public void ManagedRuntimePathSources_DoNotUseLiteralBackslashRuntimeStateSegments()
    {
        var repoRoot = GetRepositoryRoot();
        var pathBuilder = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Core", "Workspaces", "WorkspacePathBuilder.cs"));
        var orchestrator = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Core", "Workspaces", "WorkspaceOrchestrator.cs"));

        Assert.DoesNotContain(".opencode\\local", pathBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain(".opencode\\local", orchestrator, StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenCode.Workspace.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
