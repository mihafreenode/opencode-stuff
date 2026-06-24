using System.Diagnostics;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspacePublishAssessmentServiceTests
{
    [Fact]
    public async Task AssessAsync_WhenRemoteConfiguredAndAhead_CanPublish()
    {
        Assert.True(CanRunGit(), "Git is required for publish assessment tests.");
        var rootPath = CreateTempPath();
        var remotePath = CreateTempPath();

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
            var assessment = await new WorkspacePublishAssessmentService(new ProcessRunner()).AssessAsync(CreateSnapshot(rootPath, definition, provider, paths));

            Assert.True(assessment.CanPublish, assessment.Summary);
            Assert.False(assessment.IsBlocked);
            Assert.True(assessment.RequiresConfirmation);
            Assert.Equal("origin", assessment.RemoteName);
            Assert.Contains("create upstream tracking", assessment.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(remotePath);
        }
    }

    [Fact]
    public async Task AssessAsync_WhenWorkingTreeIsDirty_BlocksUntilSavePoint()
    {
        Assert.True(CanRunGit(), "Git is required for publish assessment tests.");
        var rootPath = CreateTempPath();
        var remotePath = CreateTempPath();

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
            File.AppendAllText(Path.Combine(rootPath, "notes.txt"), "dirty change\n");

            var assessment = await new WorkspacePublishAssessmentService(new ProcessRunner()).AssessAsync(CreateSnapshot(rootPath, definition, provider, paths));

            Assert.True(assessment.IsBlocked);
            Assert.True(assessment.RequiresSavePoint);
            Assert.Contains("Save Point", assessment.Summary, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(remotePath);
        }
    }

    [Fact]
    public async Task AssessAsync_WhenRemoteMissing_BlocksPublish()
    {
        Assert.True(CanRunGit(), "Git is required for publish assessment tests.");
        var rootPath = CreateTempPath();

        try
        {
            Directory.CreateDirectory(rootPath);
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");

            var provider = new GitWorkspaceProvider(new ProcessRunner());
            var definition = CreateDefinition();
            var paths = WorkspacePathBuilder.Build(rootPath);

            await provider.InitializeWorkspaceAsync(paths, definition, createInitialSavePoint: true);

            var assessment = await new WorkspacePublishAssessmentService(new ProcessRunner()).AssessAsync(CreateSnapshot(rootPath, definition, provider, paths));

            Assert.True(assessment.IsBlocked);
            Assert.False(assessment.HasRemoteConfigured);
            Assert.Contains("Remote backup is not configured", assessment.Summary, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempPath(rootPath);
        }
    }

    [Fact]
    public async Task AssessAsync_WhenRemoteChanged_BlocksUntilReview()
    {
        Assert.True(CanRunGit(), "Git is required for publish assessment tests.");
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
            var state = await provider.GetGitStateAsync(paths, definition);

            await RunGitAsync(Path.GetTempPath(), "clone", remotePath, collaboratorPath);
            await RunGitAsync(collaboratorPath, "checkout", state.CurrentBranch);
            File.AppendAllText(Path.Combine(collaboratorPath, "notes.txt"), "remote change\n");
            await RunGitAsync(collaboratorPath, "add", "-A");
            await RunGitAsync(collaboratorPath, "-c", "user.name=Collaborator", "-c", "user.email=collab@local.workspace", "commit", "-m", "Remote update");
            await RunGitAsync(collaboratorPath, "push");

            var assessment = await new WorkspacePublishAssessmentService(new ProcessRunner()).AssessAsync(CreateSnapshot(rootPath, definition, provider, paths));

            Assert.True(assessment.IsBlocked);
            Assert.True(assessment.BehindCount > 0);
            Assert.Contains("Remote backup changed", assessment.Summary, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(remotePath);
            DeleteTempPath(collaboratorPath);
        }
    }

    private static WorkspaceSnapshot CreateSnapshot(string rootPath, WorkspaceDefinition definition, GitWorkspaceProvider provider, WorkspacePaths paths)
    {
        var state = provider.GetGitStateAsync(paths, definition).GetAwaiter().GetResult();
        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = definition.Workspace.Name,
                RootPath = rootPath,
                RepositoryPath = rootPath,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            },
            Definition = definition,
            Paths = paths,
            ConfigurationPath = "workspace.yaml",
            RuntimeState = WorkspaceRuntimeState.Stopped,
            Safety = new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.PartiallyProtected,
                Headline = "Protected working copy",
                Message = "Workspace is on a safe working copy.",
                WorkingCopyName = state.WorkingCopyName,
                LocalRecovery = new WorkspaceLocalRecoverySnapshot
                {
                    IsGitInitialized = state.IsRepository,
                    LatestSavePointUtc = state.LatestCommitUtc,
                    AreUntrackedFilesProtected = true,
                },
                Backup = new WorkspaceBackupSnapshot
                {
                    HasRemoteConfigured = state.HasRemoteConfigured,
                    HasUnpublishedSavePoints = state.AheadCount > 0,
                    IsCurrentWorkingCopyPublished = state.AheadCount == 0,
                    NeedsReviewBeforePublish = state.BehindCount > 0,
                    LastSuccessfulPublishUtc = null,
                    IsOnProtectedBranch = state.IsProtectedBranch,
                },
                IgnorePolicy = new WorkspaceIgnorePolicyReview(),
                AdvancedGit = new WorkspaceAdvancedGitSnapshot
                {
                    WorkingCopyName = state.WorkingCopyName,
                    CurrentBranch = state.CurrentBranch,
                    DefaultBranch = state.DefaultBranch,
                    RemoteName = state.RemoteName,
                    RemoteUrl = state.RemoteUrl,
                    RemoteBranch = state.TrackingBranch,
                    AheadCount = state.AheadCount,
                    BehindCount = state.BehindCount,
                    LatestCommitSha = state.LatestCommitSha,
                    StatusSummary = state.StatusSummary,
                    IsProtectedBranch = state.IsProtectedBranch,
                    IsWorkspaceBranch = state.IsWorkspaceBranch,
                    ConflictingFiles = state.ConflictingFiles,
                },
            },
            Session = new WorkspaceSessionSnapshot { SessionName = definition.Workspace.Name, State = WorkspaceSessionState.Unknown },
            UpdateRequired = false,
        };
    }

    private static WorkspaceDefinition CreateDefinition(string? remoteUrl = null)
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = "publish-test", Image = "ubuntu:24.04" },
            Provider = new WorkspaceProviderDefinition { Type = "git", Url = remoteUrl },
            Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = WorkspaceRuntimeDefinition.DefaultNodeMajorVersion },
            Features = ["core"],
        };

    private static bool CanRunGit()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            process?.WaitForExit(5000);
            return process is { ExitCode: 0 };
        }
        catch
        {
            return false;
        }
    }

    private static string CreateTempPath()
        => Path.Combine(Path.GetTempPath(), $"oc-publish-assess-{Guid.NewGuid():N}");

    private static void DeleteTempPath(string path)
    {
        if (Directory.Exists(path))
        {
            foreach (var filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }

            Directory.Delete(path, recursive: true);
        }
    }

    private static async Task<ProcessResult> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var result = await new ProcessRunner().RunAsync("git", arguments, workingDirectory);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? "Git command failed." : result.StandardError);
        }

        return result;
    }
}
