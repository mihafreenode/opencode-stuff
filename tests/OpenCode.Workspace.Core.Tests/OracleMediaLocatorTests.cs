using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleMediaLocatorTests
{
    [Fact]
    public void LocateApexMedia_WorkspaceLocalOverridesGlobalCache()
    {
        var root = CreateTempRoot();
        var localAppData = Path.Combine(root, "localappdata");
        var userProfile = Path.Combine(root, "userprofile");
        var repo = Path.Combine(root, "repo");
        Directory.CreateDirectory(localAppData);
        Directory.CreateDirectory(userProfile);
        Directory.CreateDirectory(repo);

        try
        {
            var paths = WorkspacePathBuilder.Build(Path.Combine(root, "workspace"));
            var workspaceLocal = OracleMediaLocator.GetWorkspaceLocalApexDirectory(paths);
            Directory.CreateDirectory(workspaceLocal);
            File.WriteAllText(Path.Combine(workspaceLocal, "apex.zip"), "workspace");

            var shared = OracleMediaLocator.GetSharedApexCacheDirectory(localAppData);
            Directory.CreateDirectory(shared);
            File.WriteAllText(Path.Combine(shared, "apex_24.2_en.zip"), "shared");

            var locator = new OracleMediaLocator(_ => repo, localAppData, userProfile);
            var result = locator.LocateApexMedia(paths);

            Assert.NotNull(result.ResolvedPath);
            Assert.EndsWith(Path.Combine("workspace", ".local", "oracle", "downloads", "apex", "apex.zip"), result.ResolvedPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(result.IsWorkspaceLocalOverride);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void LocateApexMedia_DiscoversLocalApplicationDataCache()
    {
        var root = CreateTempRoot();
        var localAppData = Path.Combine(root, "localappdata");
        var userProfile = Path.Combine(root, "userprofile");
        Directory.CreateDirectory(localAppData);
        Directory.CreateDirectory(userProfile);

        try
        {
            var paths = WorkspacePathBuilder.Build(Path.Combine(root, "workspace"));
            var shared = OracleMediaLocator.GetSharedApexCacheDirectory(localAppData);
            Directory.CreateDirectory(shared);
            File.WriteAllText(Path.Combine(shared, "apex_24.2_en.zip"), "shared");

            var locator = new OracleMediaLocator(_ => null, localAppData, userProfile);
            var result = locator.LocateApexMedia(paths);

            Assert.NotNull(result.ResolvedPath);
            Assert.EndsWith(Path.Combine("APEX", "apex_24.2_en.zip"), result.ResolvedPath, StringComparison.OrdinalIgnoreCase);
            Assert.False(result.IsWorkspaceLocalOverride);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void LocateApexMedia_DiscoversUserProfileCache()
    {
        var root = CreateTempRoot();
        var localAppData = Path.Combine(root, "localappdata");
        var userProfile = Path.Combine(root, "userprofile");
        Directory.CreateDirectory(localAppData);
        Directory.CreateDirectory(userProfile);

        try
        {
            var paths = WorkspacePathBuilder.Build(Path.Combine(root, "workspace"));
            var homeCache = OracleMediaLocator.GetHomeApexCacheDirectory(userProfile);
            Directory.CreateDirectory(homeCache);
            File.WriteAllText(Path.Combine(homeCache, "apex24.2.zip"), "home");

            var locator = new OracleMediaLocator(_ => null, localAppData, userProfile);
            var result = locator.LocateApexMedia(paths);

            Assert.NotNull(result.ResolvedPath);
            Assert.EndsWith(Path.Combine("apex", "apex24.2.zip"), result.ResolvedPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void LocateApexMedia_DiscoversConfiguredRepository()
    {
        var root = CreateTempRoot();
        var localAppData = Path.Combine(root, "localappdata");
        var userProfile = Path.Combine(root, "userprofile");
        var repo = Path.Combine(root, "oracle-downloads");
        Directory.CreateDirectory(localAppData);
        Directory.CreateDirectory(userProfile);
        Directory.CreateDirectory(repo);

        try
        {
            var paths = WorkspacePathBuilder.Build(Path.Combine(root, "workspace"));
            var apexRepo = Path.Combine(repo, "APEX");
            Directory.CreateDirectory(apexRepo);
            File.WriteAllText(Path.Combine(apexRepo, "apex.zip"), "repo");

            var locator = new OracleMediaLocator(_ => repo, localAppData, userProfile);
            var result = locator.LocateApexMedia(paths);

            Assert.NotNull(result.ResolvedPath);
            Assert.EndsWith(Path.Combine("APEX", "apex.zip"), result.ResolvedPath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(result.SearchedLocations, item => string.Equals(item, repo, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.SearchedLocations, item => string.Equals(item, apexRepo, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void LocateApexMedia_WhenMissing_ReportsAllSearchedLocations()
    {
        var root = CreateTempRoot();
        var localAppData = Path.Combine(root, "localappdata");
        var userProfile = Path.Combine(root, "userprofile");
        var repo = Path.Combine(root, "oracle-downloads");
        Directory.CreateDirectory(localAppData);
        Directory.CreateDirectory(userProfile);
        Directory.CreateDirectory(repo);

        try
        {
            var paths = WorkspacePathBuilder.Build(Path.Combine(root, "workspace"));
            var locator = new OracleMediaLocator(_ => repo, localAppData, userProfile);
            var result = locator.LocateApexMedia(paths);

            Assert.Null(result.ResolvedPath);
            Assert.Equal(["apex.zip", "apex_*.zip", "apex*.zip"], result.AcceptedFileNames);
            Assert.Contains(OracleMediaLocator.GetWorkspaceLocalApexDirectory(paths), result.SearchedLocations);
            Assert.Contains(OracleMediaLocator.GetSharedApexCacheDirectory(localAppData), result.SearchedLocations);
            Assert.Contains(OracleMediaLocator.GetHomeApexCacheDirectory(userProfile), result.SearchedLocations);
            Assert.Contains(repo, result.SearchedLocations);
            Assert.Contains(Path.Combine(repo, "APEX"), result.SearchedLocations);
            Assert.Contains(Path.Combine(repo, "apex"), result.SearchedLocations);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static string CreateTempRoot() => Path.Combine(Path.GetTempPath(), $"oracle-media-locator-{Guid.NewGuid():N}");

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
