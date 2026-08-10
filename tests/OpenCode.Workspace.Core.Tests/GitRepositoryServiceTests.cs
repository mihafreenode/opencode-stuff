using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class GitRepositoryServiceTests
{
    [Fact]
    public async Task InspectAsync_DetectsRepositoryBranchAndDirtyState()
    {
        var rootPath = CreateTempPath();
        try
        {
            Directory.CreateDirectory(rootPath);
            await RunGitAsync(rootPath, "init", "-b", "main");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");
            await RunGitAsync(rootPath, "add", "-A");
            await RunGitAsync(rootPath, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");
            File.AppendAllText(Path.Combine(rootPath, "notes.txt"), "dirty\n");

            var service = new GitRepositoryService(new ProcessRunner());
            var inspection = await service.InspectAsync(rootPath);

            Assert.True(inspection.IsRepository);
            Assert.Equal("main", inspection.CurrentBranch);
            Assert.Equal("main", inspection.DefaultBranch);
            Assert.True(inspection.HasUncommittedChanges);
        }
        finally
        {
            DeleteTempPath(rootPath);
        }
    }

    [Fact]
    public async Task CreateUniqueWorkspaceBranchNameAsync_AppendsSuffixWhenBranchExists()
    {
        var rootPath = CreateTempPath();
        try
        {
            Directory.CreateDirectory(rootPath);
            await RunGitAsync(rootPath, "init", "-b", "main");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");
            await RunGitAsync(rootPath, "add", "-A");
            await RunGitAsync(rootPath, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");
            await RunGitAsync(rootPath, "checkout", "-b", "workspace/my-project-20260613-1430");
            await RunGitAsync(rootPath, "checkout", "main");

            var service = new GitRepositoryService(new ProcessRunner());
            var branchName = await service.CreateUniqueWorkspaceBranchNameAsync(rootPath, "My Project", new DateTimeOffset(2026, 6, 13, 14, 30, 0, TimeSpan.Zero));

            Assert.Equal("workspace/my-project-20260613-1430-2", branchName);
        }
        finally
        {
            DeleteTempPath(rootPath);
        }
    }

    [Fact]
    public async Task ValidateBranchNameAsync_RejectsInvalidBranchNames()
    {
        var rootPath = CreateTempPath();
        try
        {
            Directory.CreateDirectory(rootPath);
            await RunGitAsync(rootPath, "init", "-b", "main");

            var service = new GitRepositoryService(new ProcessRunner());
            var validation = await service.ValidateBranchNameAsync(rootPath, "bad branch name");

            Assert.False(validation.IsValid);
        }
        finally
        {
            DeleteTempPath(rootPath);
        }
    }

    private static async Task<ProcessResult> RunGitAsync(string workingDirectory, params string[] arguments)
        => await new ProcessRunner().RunAsync("git", arguments, workingDirectory);

    private static string CreateTempPath() => Path.Combine(Path.GetTempPath(), $"git-repository-service-{Guid.NewGuid():N}");

    private static void DeleteTempPath(string path)
    {
        if (Directory.Exists(path))
        {
            TestFileSystem.DeleteDirectoryIfExists(path);
        }
    }

}
