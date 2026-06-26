using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Avalonia.Tests;

public sealed class WorkspaceDiscoveryParityTests
{
    [Fact]
    public void SharedResolver_PreservesExistingWorkspaceManagerDataRootForCompatibility()
    {
        var dataRoot = WorkspaceAppDataPaths.GetWorkspaceManagerDataRoot();

        // Preserve the original app-data root until user-data migration exists.
        Assert.EndsWith("OpenCode.Workspace.Manager", dataRoot, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(dataRoot, "workspaces.json"), WorkspaceAppDataPaths.GetWorkspaceIndexPath());
    }

    [Fact]
    public void SharedResolver_DoesNotDependOnCurrentWorkingDirectory()
    {
        var originalDirectory = Environment.CurrentDirectory;
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"avalonia-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var before = WorkspaceAppDataPaths.GetWorkspaceIndexPath();
            Environment.CurrentDirectory = temporaryDirectory;
            var after = WorkspaceAppDataPaths.GetWorkspaceIndexPath();

            Assert.Equal(before, after);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
    }

    [Fact]
    public void WorkspaceRepository_IndexPathMatchesSharedResolver()
    {
        var repository = new WorkspaceRepository(WorkspaceAppDataPaths.GetWorkspaceManagerDataRoot());

        Assert.Equal(WorkspaceAppDataPaths.GetWorkspaceIndexPath(), repository.IndexFilePath);
    }
}
