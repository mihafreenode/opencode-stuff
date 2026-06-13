using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceSafetyService
{
    public WorkspaceSafetySnapshot Build(WorkspaceGitState gitState, WorkspaceCheckpointRecord? latestCheckpoint, DateTimeOffset? lastSuccessfulPublishUtc, WorkspaceIgnorePolicyReview? ignorePolicyReview = null)
    {
        ignorePolicyReview ??= new WorkspaceIgnorePolicyReview();
        var localRecovery = new WorkspaceLocalRecoverySnapshot
        {
            IsGitInitialized = gitState.IsRepository,
            LatestSavePointUtc = gitState.LatestCommitUtc,
            LatestCheckpointUtc = latestCheckpoint?.CreatedUtc,
            HasUncommittedChanges = gitState.HasUncommittedChanges,
            UncommittedChangeCount = gitState.UncommittedChangeCount,
            UntrackedFileCount = gitState.UntrackedFileCount,
            // Untracked files are treated conservatively. A Save Point may protect
            // tracked work, but we only report untracked files as protected when a
            // checkpoint explicitly says they were captured.
            AreUntrackedFilesProtected = gitState.UntrackedFileCount == 0 || latestCheckpoint?.CapturedUntrackedFiles == true,
        };

        var backup = new WorkspaceBackupSnapshot
        {
            HasRemoteConfigured = gitState.HasRemoteConfigured,
            HasUnpublishedSavePoints = gitState.HasRemoteConfigured && gitState.AheadCount > 0,
            IsCurrentWorkingCopyPublished = gitState.HasRemoteConfigured && gitState.AheadCount == 0 && gitState.BehindCount == 0 && !string.IsNullOrWhiteSpace(gitState.TrackingBranch),
            LastSuccessfulPublishUtc = lastSuccessfulPublishUtc,
            NeedsReviewBeforePublish = gitState.HasRemoteConfigured && gitState.BehindCount > 0,
            IsOnProtectedBranch = gitState.IsProtectedBranch,
        };

        var advancedGit = new WorkspaceAdvancedGitSnapshot
        {
            WorkingCopyName = gitState.WorkingCopyName,
            CurrentBranch = gitState.CurrentBranch,
            DefaultBranch = gitState.DefaultBranch,
            RemoteName = gitState.RemoteName,
            RemoteUrl = gitState.RemoteUrl,
            RemoteBranch = gitState.TrackingBranch,
            AheadCount = gitState.AheadCount,
            BehindCount = gitState.BehindCount,
            LatestCommitSha = gitState.LatestCommitSha,
            StatusSummary = gitState.StatusSummary,
            PatchExportSupported = gitState.IsRepository,
            IsProtectedBranch = gitState.IsProtectedBranch,
            IsWorkspaceBranch = gitState.IsWorkspaceBranch,
            ConflictingFiles = gitState.ConflictingFiles,
        };

        if (ignorePolicyReview.HasSecretCandidates)
        {
            return new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.AtRisk,
                Headline = "At Risk",
                Message = "Potential secret content detected. Review before creating a Save Point.",
                WorkingCopyName = gitState.WorkingCopyName,
                LocalRecovery = localRecovery,
                Backup = backup,
                IgnorePolicy = ignorePolicyReview,
                AdvancedGit = advancedGit,
            };
        }

        if (ignorePolicyReview.HasReviewRequired)
        {
            return new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.NeedsReview,
                Headline = "Needs Review",
                Message = "Workspace content needs review before the next Save Point.",
                WorkingCopyName = gitState.WorkingCopyName,
                LocalRecovery = localRecovery,
                Backup = backup,
                IgnorePolicy = ignorePolicyReview,
                AdvancedGit = advancedGit,
            };
        }

        if (gitState.IsProtectedBranch)
        {
            return new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.NeedsReview,
                Headline = "Needs Review",
                Message = "Working directly on protected branch.",
                WorkingCopyName = gitState.WorkingCopyName,
                LocalRecovery = localRecovery,
                Backup = backup,
                IgnorePolicy = ignorePolicyReview,
                AdvancedGit = advancedGit,
            };
        }

        // Remote divergence is shown as Needs Review instead of attempting to hide
        // Git complexity with automatic conflict handling. Conflict is not failure;
        // losing work would be failure.
        if (backup.NeedsReviewBeforePublish)
        {
            return new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.NeedsReview,
                Headline = "Needs Review",
                Message = "Your local work is safe. The remote workspace changed and needs review before publishing.",
                WorkingCopyName = gitState.WorkingCopyName,
                LocalRecovery = localRecovery,
                Backup = backup,
                IgnorePolicy = ignorePolicyReview,
                AdvancedGit = advancedGit,
            };
        }

        // When safety is uncertain, V1 reports the workspace as unsafe rather than
        // overstating protection.
        if (!localRecovery.IsGitInitialized
            || localRecovery.LatestSavePointUtc is null
            || (localRecovery.UntrackedFileCount > 0 && !localRecovery.AreUntrackedFilesProtected)
            || (localRecovery.LatestSavePointUtc is null && (localRecovery.HasUncommittedChanges || localRecovery.UntrackedFileCount > 0)))
        {
            return new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.AtRisk,
                Headline = "At Risk",
                Message = !localRecovery.IsGitInitialized || localRecovery.LatestSavePointUtc is null
                    ? "Local recovery is not enabled yet. Create a Save Point before continuing."
                    : "Some local files are not proven to be protected yet.",
                WorkingCopyName = gitState.WorkingCopyName,
                LocalRecovery = localRecovery,
                Backup = backup,
                IgnorePolicy = ignorePolicyReview,
                AdvancedGit = advancedGit,
            };
        }

        if (!backup.HasRemoteConfigured)
        {
            return new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.PartiallyProtected,
                Headline = "Partially Protected",
                Message = gitState.IsWorkspaceBranch
                    ? $"Working on isolated workspace branch {gitState.CurrentBranch}."
                    : "Your work is protected locally. Configure remote backup to protect against machine loss.",
                WorkingCopyName = gitState.WorkingCopyName,
                LocalRecovery = localRecovery,
                Backup = backup,
                IgnorePolicy = ignorePolicyReview,
                AdvancedGit = advancedGit,
            };
        }

        if (backup.HasUnpublishedSavePoints || !backup.IsCurrentWorkingCopyPublished)
        {
            return new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.PartiallyProtected,
                Headline = "Partially Protected",
                Message = "You have local Save Points that are not backed up remotely.",
                WorkingCopyName = gitState.WorkingCopyName,
                LocalRecovery = localRecovery,
                Backup = backup,
                IgnorePolicy = ignorePolicyReview,
                AdvancedGit = advancedGit,
            };
        }

        return new WorkspaceSafetySnapshot
        {
            OverallStatus = WorkspaceSafetyLevel.Protected,
            Headline = "Protected",
            Message = gitState.IsWorkspaceBranch
                ? $"Working on isolated workspace branch {gitState.CurrentBranch}."
                : "Local recovery and remote backup are both up to date.",
            WorkingCopyName = gitState.WorkingCopyName,
            LocalRecovery = localRecovery,
            Backup = backup,
            IgnorePolicy = ignorePolicyReview,
            AdvancedGit = advancedGit,
        };
    }
}
