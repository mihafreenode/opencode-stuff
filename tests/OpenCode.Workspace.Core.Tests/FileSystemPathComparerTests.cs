using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class FileSystemPathComparerTests
{
    [Fact]
    public void AreEquivalent_MacVarAliasPaths_Match()
    {
        Assert.True(FileSystemPathComparer.AreEquivalent(
            "/var/folders/demo/workspace",
            "/private/var/folders/demo/workspace"));
    }

    [Fact]
    public void AreEquivalent_TrailingSeparators_Match()
    {
        Assert.True(FileSystemPathComparer.AreEquivalent(
            "/var/folders/demo/workspace/",
            "/private/var/folders/demo/workspace"));
    }

    [Fact]
    public void AreEquivalent_DotSegments_AreNormalized()
    {
        Assert.True(FileSystemPathComparer.AreEquivalent(
            "/var/folders/demo/current/../workspace/./compose.yaml",
            "/private/var/folders/demo/workspace/compose.yaml"));
    }

    [Fact]
    public void AreEquivalent_WindowsPaths_AreCaseInsensitive()
    {
        Assert.True(FileSystemPathComparer.AreEquivalent(
            "C:/Users/MIHA/Workspace/compose.yaml",
            "c:\\users\\miha\\workspace\\compose.yaml"));
    }

    [Fact]
    public void AreEquivalent_LinuxPaths_RemainCaseSensitive()
    {
        Assert.False(FileSystemPathComparer.AreEquivalent(
            "/tmp/Workspace/compose.yaml",
            "/tmp/workspace/compose.yaml"));
    }
}
