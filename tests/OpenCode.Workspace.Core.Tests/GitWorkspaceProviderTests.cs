using System.Diagnostics;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class GitWorkspaceProviderTests
{
    [Fact]
    public async Task InitializeWorkspaceAsync_CreatesSafeWorkingCopyBranch()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var rootPath = CreateTempPath();

        try
        {
            Directory.CreateDirectory(rootPath);
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft");
            var provider = new GitWorkspaceProvider(new ProcessRunner());
            var definition = CreateDefinition();

            await provider.InitializeWorkspaceAsync(WorkspacePathBuilder.Build(rootPath), definition, createInitialSavePoint: true);
            var state = await provider.GetGitStateAsync(WorkspacePathBuilder.Build(rootPath), definition);

            Assert.True(state.IsSafeWorkingCopy);
            Assert.Matches(@"^workspace/[a-z0-9-]+-\d{8}-\d{4}(-\d+)?$", state.CurrentBranch);
        }
        finally
        {
            DeleteTempPath(rootPath);
        }
    }

    [Fact]
    public async Task InitializeWorkspaceAsync_DoesNotAutoPublishToRemote()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var rootPath = CreateTempPath();
        var remotePath = CreateTempPath();

        try
        {
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(remotePath);
            await RunGitAsync(remotePath, "init", "--bare");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft");

            var provider = new GitWorkspaceProvider(new ProcessRunner());
            var definition = CreateDefinition(remotePath);

            await provider.InitializeWorkspaceAsync(WorkspacePathBuilder.Build(rootPath), definition, createInitialSavePoint: true);
            var refs = await RunGitAsync(rootPath, "ls-remote", "--heads", "origin");

            Assert.True(string.IsNullOrWhiteSpace(refs.StandardOutput));
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(remotePath);
        }
    }

    [Fact]
    public async Task PublishAsync_WhenRemoteUnchanged_PublishesWorkingCopy()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var rootPath = CreateTempPath();
        var remotePath = CreateTempPath();

        try
        {
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(remotePath);
            await RunGitAsync(remotePath, "init", "--bare");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft");

            var provider = new GitWorkspaceProvider(new ProcessRunner());
            var definition = CreateDefinition(remotePath);
            var paths = WorkspacePathBuilder.Build(rootPath);

            await provider.InitializeWorkspaceAsync(paths, definition, createInitialSavePoint: true);
            var review = await provider.PublishAsync(paths, definition);
            var state = await provider.GetGitStateAsync(paths, definition);

            Assert.False(review.IsBlocked);
            Assert.Equal(0, state.AheadCount);
            Assert.False(string.IsNullOrWhiteSpace(state.TrackingBranch));
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(remotePath);
        }
    }

    [Fact]
    public async Task PublishAsync_WhenCurrentBranchIsProtected_ReturnsBlockedReview()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var rootPath = CreateTempPath();

        try
        {
            Directory.CreateDirectory(rootPath);
            await RunGitAsync(rootPath, "init", "-b", "main");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft");
            await RunGitAsync(rootPath, "add", "-A");
            await RunGitAsync(rootPath, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial Save Point");

            var provider = new GitWorkspaceProvider(new ProcessRunner());
            var definition = CreateDefinition();
            var review = await provider.PublishAsync(WorkspacePathBuilder.Build(rootPath), definition);

            Assert.True(review.IsBlocked);
            Assert.Contains("protected branch", review.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempPath(rootPath);
        }
    }

    [Fact]
    public async Task PublishToReviewWorkingCopyAsync_WhenCurrentBranchIsProtected_ReturnsBlockedReview()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var rootPath = CreateTempPath();

        try
        {
            Directory.CreateDirectory(rootPath);
            await RunGitAsync(rootPath, "init", "-b", "main");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft");
            await RunGitAsync(rootPath, "add", "-A");
            await RunGitAsync(rootPath, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial Save Point");

            var provider = new GitWorkspaceProvider(new ProcessRunner());
            var definition = CreateDefinition();
            var review = await provider.PublishToReviewWorkingCopyAsync(WorkspacePathBuilder.Build(rootPath), definition);

            Assert.True(review.IsBlocked);
            Assert.Contains("protected branch", review.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempPath(rootPath);
        }
    }

    [Fact]
    public async Task PublishAsync_WhenRemoteChangedAndWorkingTreeIsDirty_ReturnsNeedsReviewWithoutDiscardingWork()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var rootPath = CreateTempPath();
        var remotePath = CreateTempPath();
        var collaboratorPath = CreateTempPath();

        try
        {
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(remotePath);
            await RunGitAsync(remotePath, "init", "--bare");

            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");
            var provider = new GitWorkspaceProvider(new ProcessRunner());
            var definition = CreateDefinition(remotePath);
            var paths = WorkspacePathBuilder.Build(rootPath);

            await provider.InitializeWorkspaceAsync(paths, definition, createInitialSavePoint: true);
            await provider.PublishAsync(paths, definition);
            var publishedState = await provider.GetGitStateAsync(paths, definition);

            await RunGitAsync(Path.GetTempPath(), "clone", remotePath, collaboratorPath);
            await RunGitAsync(collaboratorPath, "checkout", publishedState.CurrentBranch);
            File.AppendAllText(Path.Combine(collaboratorPath, "notes.txt"), "remote change\n");
            await RunGitAsync(collaboratorPath, "add", "-A");
            await RunGitAsync(collaboratorPath, "-c", "user.name=Collaborator", "-c", "user.email=collab@local.workspace", "commit", "-m", "Remote update");
            await RunGitAsync(collaboratorPath, "push");

            File.AppendAllText(Path.Combine(rootPath, "notes.txt"), "local unsaved change\n");
            var beforePublishContents = File.ReadAllText(Path.Combine(rootPath, "notes.txt"));

            var review = await provider.PublishAsync(paths, definition);
            var afterPublishContents = File.ReadAllText(Path.Combine(rootPath, "notes.txt"));

            Assert.True(review.IsBlocked);
            Assert.Contains("needs review", review.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(beforePublishContents, afterPublishContents);
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(remotePath);
            DeleteTempPath(collaboratorPath);
        }
    }

    [Fact]
    public async Task CreateSavePointAsync_WhenSecretCandidateExists_ThrowsBeforeCommit()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var rootPath = CreateTempPath();

        try
        {
            Directory.CreateDirectory(rootPath);
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");
            File.WriteAllText(Path.Combine(rootPath, ".env"), "API_KEY=secret\n");

            var provider = new GitWorkspaceProvider(new ProcessRunner(), new WorkspaceIgnorePolicyService());
            var definition = CreateDefinition();
            var paths = WorkspacePathBuilder.Build(rootPath);

            await provider.InitializeWorkspaceAsync(paths, definition, createInitialSavePoint: false);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CreateSavePointAsync(paths, definition, "Save current work"));
            Assert.Contains("Workspace Review required", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".env", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempPath(rootPath);
        }
    }

    [Fact]
    public async Task CreateSavePointAsync_WhenUnknownHiddenFolderExists_ThrowsBeforeCommit()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var rootPath = CreateTempPath();

        try
        {
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(Path.Combine(rootPath, ".custom-tool"));
            File.WriteAllText(Path.Combine(rootPath, ".custom-tool", "state.json"), "{}");

            var provider = new GitWorkspaceProvider(new ProcessRunner(), new WorkspaceIgnorePolicyService());
            var definition = CreateDefinition();
            var paths = WorkspacePathBuilder.Build(rootPath);

            await provider.InitializeWorkspaceAsync(paths, definition, createInitialSavePoint: false);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CreateSavePointAsync(paths, definition, "Save current work"));
            Assert.Contains("Workspace Review required", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".custom-tool/", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempPath(rootPath);
        }
    }

    [Fact]
    public async Task CreateSavePointAsync_WhenNestedSecretCandidateExists_ThrowsBeforeCommit()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var rootPath = CreateTempPath();

        try
        {
            Directory.CreateDirectory(Path.Combine(rootPath, "config"));
            File.WriteAllText(Path.Combine(rootPath, "config", "private.key"), "secret");

            var provider = new GitWorkspaceProvider(new ProcessRunner(), new WorkspaceIgnorePolicyService());
            var definition = CreateDefinition();
            var paths = WorkspacePathBuilder.Build(rootPath);

            await provider.InitializeWorkspaceAsync(paths, definition, createInitialSavePoint: false);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CreateSavePointAsync(paths, definition, "Save current work"));
            Assert.Contains("config/private.key", exception.Message, StringComparison.OrdinalIgnoreCase);

            var headResult = await RunGitAsync(rootPath, "rev-parse", "--verify", "HEAD");
            Assert.False(headResult.IsSuccess);
        }
        finally
        {
            DeleteTempPath(rootPath);
        }
    }

    [Fact]
    public async Task CreateSavePointAsync_WhenNestedUnknownHiddenFolderExists_ThrowsBeforeCommit()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var rootPath = CreateTempPath();

        try
        {
            Directory.CreateDirectory(Path.Combine(rootPath, "src", ".custom-tool"));
            File.WriteAllText(Path.Combine(rootPath, "src", ".custom-tool", "state.json"), "{}");

            var provider = new GitWorkspaceProvider(new ProcessRunner(), new WorkspaceIgnorePolicyService());
            var definition = CreateDefinition();
            var paths = WorkspacePathBuilder.Build(rootPath);

            await provider.InitializeWorkspaceAsync(paths, definition, createInitialSavePoint: false);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CreateSavePointAsync(paths, definition, "Save current work"));
            Assert.Contains("src/.custom-tool/", exception.Message, StringComparison.OrdinalIgnoreCase);

            var headResult = await RunGitAsync(rootPath, "rev-parse", "--verify", "HEAD");
            Assert.False(headResult.IsSuccess);
        }
        finally
        {
            DeleteTempPath(rootPath);
        }
    }

    [Fact]
    public async Task CreateSavePointAsync_WhenValidationFails_DoesNotCreatePartialFollowUpSavePoint()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var rootPath = CreateTempPath();

        try
        {
            Directory.CreateDirectory(rootPath);
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");

            var provider = new GitWorkspaceProvider(new ProcessRunner(), new WorkspaceIgnorePolicyService());
            var definition = CreateDefinition();
            var paths = WorkspacePathBuilder.Build(rootPath);

            await provider.InitializeWorkspaceAsync(paths, definition, createInitialSavePoint: true);
            var beforeHead = (await RunGitAsync(rootPath, "rev-parse", "HEAD")).StandardOutput.Trim();

            File.WriteAllText(Path.Combine(rootPath, ".env.local"), "TOKEN=secret\n");

            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CreateSavePointAsync(paths, definition, "Second Save Point"));
            var afterHead = (await RunGitAsync(rootPath, "rev-parse", "HEAD")).StandardOutput.Trim();

            Assert.Equal(beforeHead, afterHead);
        }
        finally
        {
            DeleteTempPath(rootPath);
        }
    }

    private static WorkspaceDefinition CreateDefinition(string? remoteUrl = null)
    {
        return new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Id = "workspace-safety",
                Name = "Workspace Safety",
                Image = "ubuntu:24.04",
            },
            Provider = new WorkspaceProviderDefinition
            {
                Type = "git",
                Url = remoteUrl,
            },
            Runtime = new WorkspaceRuntimeDefinition
            {
                Default = "default",
            },
        };
    }

    private static async Task<ProcessResult> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        return await new ProcessRunner().RunAsync("git", arguments, workingDirectory);
    }

    private static string CreateTempPath() => Path.Combine(Path.GetTempPath(), $"git-provider-{Guid.NewGuid():N}");

    private static void DeleteTempPath(string path)
    {
        if (Directory.Exists(path))
        {
            TestFileSystem.DeleteDirectoryIfExists(path);
        }
    }

    private static bool CanRunGit()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            process?.WaitForExit(5000);
            return process is not null && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
