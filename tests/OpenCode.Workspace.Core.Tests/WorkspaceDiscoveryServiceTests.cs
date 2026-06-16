using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceDiscoveryServiceTests
{
    private readonly WorkspaceDiscoveryService _service = new();

    [Fact]
    public void Discover_EmptyRepository_ReturnsNotFound()
    {
        var rootPath = CreateTempRoot();

        try
        {
            Directory.CreateDirectory(rootPath);

            var result = _service.Discover(rootPath);

            Assert.Equal(WorkspaceDiscoveryStatus.NotFound, result.Status);
            Assert.Null(result.ConfigurationPath);
        }
        finally
        {
            DeleteTempRoot(rootPath);
        }
    }

    [Theory]
    [InlineData("workspace.yaml")]
    [InlineData("workspace.yml")]
    [InlineData(".opencode/profile.yaml")]
    [InlineData(".opencode/profile.yml")]
    public void Discover_SupportedConfigurationPath_ReturnsFound(string relativePath)
    {
        var rootPath = CreateTempRoot();

        try
        {
            var fullPath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, "workspace:\n  name: demo\n");

            var result = _service.Discover(rootPath);

            Assert.Equal(WorkspaceDiscoveryStatus.Found, result.Status);
            Assert.Equal(relativePath, result.ConfigurationPath);
        }
        finally
        {
            DeleteTempRoot(rootPath);
        }
    }

    [Fact]
    public void Discover_InvalidYaml_ReturnsInvalid()
    {
        var rootPath = CreateTempRoot();

        try
        {
            Directory.CreateDirectory(rootPath);
            File.WriteAllText(Path.Combine(rootPath, "workspace.yaml"), "workspace: [\n");

            var result = _service.Discover(rootPath);

            Assert.Equal(WorkspaceDiscoveryStatus.Invalid, result.Status);
            Assert.Equal("workspace.yaml", result.ConfigurationPath);
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        }
        finally
        {
            DeleteTempRoot(rootPath);
        }
    }

    private static string CreateTempRoot() => Path.Combine(Path.GetTempPath(), $"workspace-discovery-{Guid.NewGuid():N}");

    private static void DeleteTempRoot(string rootPath)
    {
        if (Directory.Exists(rootPath))
        {
            TestFileSystem.DeleteDirectoryIfExists(rootPath);
        }
    }
}
