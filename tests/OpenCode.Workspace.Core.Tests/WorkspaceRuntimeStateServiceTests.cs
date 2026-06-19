using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceRuntimeStateServiceTests
{
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
}
