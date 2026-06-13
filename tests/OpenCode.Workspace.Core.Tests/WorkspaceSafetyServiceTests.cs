using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceSafetyServiceTests
{
    private readonly WorkspaceSafetyService _service = new();

    [Fact]
    public void Build_WhenGitIsMissing_ReturnsAtRisk()
    {
        var snapshot = _service.Build(new WorkspaceGitState { IsRepository = false }, null, null);

        Assert.Equal(WorkspaceSafetyLevel.AtRisk, snapshot.OverallStatus);
        Assert.False(snapshot.LocalRecovery.IsGitInitialized);
    }

    [Fact]
    public void Build_WhenRemoteIsMissing_ReturnsPartiallyProtected()
    {
        var snapshot = _service.Build(
            new WorkspaceGitState
            {
                IsRepository = true,
                LatestCommitSha = "abc123",
                LatestCommitUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                CurrentBranch = "users/user/workspace-20260613-1200",
                WorkingCopyName = "users/user/workspace-20260613-1200",
                IsSafeWorkingCopy = true,
                StatusSummary = "clean",
            },
            null,
            null);

        Assert.Equal(WorkspaceSafetyLevel.PartiallyProtected, snapshot.OverallStatus);
        Assert.False(snapshot.Backup.HasRemoteConfigured);
    }

    [Fact]
    public void Build_WhenIgnorePolicyHasUnknownHiddenFolder_ReturnsNeedsReview()
    {
        var review = new WorkspaceIgnorePolicyReview
        {
            Classifications = new List<WorkspaceContentClassification> { new() { RelativePath = ".foo/", IsDirectory = true, Disposition = WorkspaceContentDisposition.NeedsReview, Reason = "Unknown hidden folder." } },
            Findings = new List<WorkspaceContentFinding> { new() { Kind = WorkspaceContentFindingKind.UnknownHiddenFolder, RelativePath = ".foo/", Message = "Unknown hidden folder detected." } },
        };

        var snapshot = _service.Build(
            new WorkspaceGitState
            {
                IsRepository = true,
                LatestCommitSha = "abc123",
                LatestCommitUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                CurrentBranch = "users/user/workspace-20260613-1200",
                WorkingCopyName = "users/user/workspace-20260613-1200",
                IsSafeWorkingCopy = true,
                StatusSummary = "clean",
            },
            null,
            null,
            review);

        Assert.Equal(WorkspaceSafetyLevel.NeedsReview, snapshot.OverallStatus);
        Assert.True(snapshot.IgnorePolicy.HasUnknownHiddenFolders);
    }

    [Fact]
    public void Build_WhenIgnorePolicyHasSecretCandidate_ReturnsAtRisk()
    {
        var review = new WorkspaceIgnorePolicyReview
        {
            Classifications = new List<WorkspaceContentClassification> { new() { RelativePath = ".env", IsDirectory = false, Disposition = WorkspaceContentDisposition.NeedsReview, Reason = "Potential secret." } },
            Findings = new List<WorkspaceContentFinding> { new() { Kind = WorkspaceContentFindingKind.SecretCandidate, RelativePath = ".env", Message = "Potential secret detected." } },
        };

        var snapshot = _service.Build(
            new WorkspaceGitState
            {
                IsRepository = true,
                LatestCommitSha = "abc123",
                LatestCommitUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                CurrentBranch = "users/user/workspace-20260613-1200",
                WorkingCopyName = "users/user/workspace-20260613-1200",
                IsSafeWorkingCopy = true,
                StatusSummary = "clean",
            },
            null,
            null,
            review);

        Assert.Equal(WorkspaceSafetyLevel.AtRisk, snapshot.OverallStatus);
        Assert.True(snapshot.IgnorePolicy.HasSecretCandidates);
        Assert.Contains("secret", snapshot.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WhenDurableContentIsIgnored_ReturnsNeedsReview()
    {
        var review = new WorkspaceIgnorePolicyReview
        {
            Findings = new List<WorkspaceContentFinding> { new() { Kind = WorkspaceContentFindingKind.DurablePathIgnored, RelativePath = ".opencode/", Message = "Durable content appears ignored." } },
        };

        var snapshot = _service.Build(
            new WorkspaceGitState
            {
                IsRepository = true,
                LatestCommitSha = "abc123",
                LatestCommitUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                CurrentBranch = "users/user/workspace-20260613-1200",
                WorkingCopyName = "users/user/workspace-20260613-1200",
                IsSafeWorkingCopy = true,
                StatusSummary = "clean",
            },
            null,
            null,
            review);

        Assert.Equal(WorkspaceSafetyLevel.NeedsReview, snapshot.OverallStatus);
        Assert.True(snapshot.IgnorePolicy.HasDurableIgnoreConflicts);
    }

    [Fact]
    public void Build_WhenUntrackedFilesLackCheckpointProof_ReturnsAtRisk()
    {
        var snapshot = _service.Build(
            new WorkspaceGitState
            {
                IsRepository = true,
                LatestCommitSha = "abc123",
                LatestCommitUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                UntrackedFileCount = 2,
                CurrentBranch = "users/user/workspace-20260613-1200",
                WorkingCopyName = "users/user/workspace-20260613-1200",
                IsSafeWorkingCopy = true,
                StatusSummary = "2 untracked",
            },
            null,
            null);

        Assert.Equal(WorkspaceSafetyLevel.AtRisk, snapshot.OverallStatus);
        Assert.False(snapshot.LocalRecovery.AreUntrackedFilesProtected);
    }

    [Fact]
    public void Build_WhenRemoteHasMovedAhead_ReturnsNeedsReview()
    {
        var snapshot = _service.Build(
            new WorkspaceGitState
            {
                IsRepository = true,
                HasRemoteConfigured = true,
                CurrentBranch = "users/user/workspace-20260613-1200",
                WorkingCopyName = "users/user/workspace-20260613-1200",
                TrackingBranch = "origin/users/user/workspace-20260613-1200",
                AheadCount = 1,
                BehindCount = 1,
                LatestCommitSha = "abc123",
                LatestCommitUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                IsSafeWorkingCopy = true,
                StatusSummary = "ahead 1, behind 1",
            },
            new WorkspaceCheckpointRecord { Id = "cp1", CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-5), CapturedUntrackedFiles = true },
            DateTimeOffset.UtcNow.AddHours(-1));

        Assert.Equal(WorkspaceSafetyLevel.NeedsReview, snapshot.OverallStatus);
        Assert.True(snapshot.Backup.NeedsReviewBeforePublish);
        Assert.Equal("Your local work is safe. The remote workspace changed and needs review before publishing.", snapshot.Message);
    }

    [Fact]
    public void Build_WhenRemoteIsUpToDateAndProtected_ReturnsProtected()
    {
        var snapshot = _service.Build(
            new WorkspaceGitState
            {
                IsRepository = true,
                HasRemoteConfigured = true,
                CurrentBranch = "users/user/workspace-20260613-1200",
                WorkingCopyName = "users/user/workspace-20260613-1200",
                TrackingBranch = "origin/users/user/workspace-20260613-1200",
                LatestCommitSha = "abc123",
                LatestCommitUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                IsSafeWorkingCopy = true,
                StatusSummary = "clean",
            },
            new WorkspaceCheckpointRecord { Id = "cp1", CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-5), CapturedUntrackedFiles = true },
            DateTimeOffset.UtcNow.AddMinutes(-2));

        Assert.Equal(WorkspaceSafetyLevel.Protected, snapshot.OverallStatus);
        Assert.True(snapshot.Backup.IsCurrentWorkingCopyPublished);
    }

    [Fact]
    public void Build_WhenOnProtectedBranch_ReturnsNeedsReview()
    {
        var snapshot = _service.Build(
            new WorkspaceGitState
            {
                IsRepository = true,
                HasRemoteConfigured = true,
                CurrentBranch = "main",
                TrackingBranch = "origin/main",
                LatestCommitSha = "abc123",
                LatestCommitUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                IsProtectedBranch = true,
                StatusSummary = "clean",
            },
            null,
            DateTimeOffset.UtcNow.AddMinutes(-2));

        Assert.Equal(WorkspaceSafetyLevel.NeedsReview, snapshot.OverallStatus);
        Assert.Equal("Your local work is safe. Create a Working Copy before publishing from a protected branch.", snapshot.Message);
    }

    [Fact]
    public void Build_WhenCheckpointCapturedUntrackedFiles_IsNotAtRiskForThatReason()
    {
        var snapshot = _service.Build(
            new WorkspaceGitState
            {
                IsRepository = true,
                LatestCommitSha = "abc123",
                LatestCommitUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                UntrackedFileCount = 2,
                CurrentBranch = "users/user/workspace-20260613-1200",
                WorkingCopyName = "users/user/workspace-20260613-1200",
                IsSafeWorkingCopy = true,
                StatusSummary = "2 untracked",
            },
            new WorkspaceCheckpointRecord { Id = "cp1", CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-3), CapturedUntrackedFiles = true },
            null);

        Assert.NotEqual(WorkspaceSafetyLevel.AtRisk, snapshot.OverallStatus);
        Assert.True(snapshot.LocalRecovery.AreUntrackedFilesProtected);
    }
}
