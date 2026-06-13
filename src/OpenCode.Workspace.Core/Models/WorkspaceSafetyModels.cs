namespace OpenCode.Workspace.Core.Models;

public enum WorkspaceSafetyLevel
{
    Protected,
    PartiallyProtected,
    AtRisk,
    NeedsReview,
}

public sealed class WorkspaceSafetySnapshot
{
    public required WorkspaceSafetyLevel OverallStatus { get; init; }
    public required string Headline { get; init; }
    public required string Message { get; init; }
    public string WorkingCopyName { get; init; } = string.Empty;
    public required WorkspaceLocalRecoverySnapshot LocalRecovery { get; init; }
    public required WorkspaceBackupSnapshot Backup { get; init; }
    public required WorkspaceIgnorePolicyReview IgnorePolicy { get; init; }
    public required WorkspaceAdvancedGitSnapshot AdvancedGit { get; init; }
}

public sealed class WorkspaceLocalRecoverySnapshot
{
    public bool IsGitInitialized { get; init; }
    public DateTimeOffset? LatestSavePointUtc { get; init; }
    public DateTimeOffset? LatestCheckpointUtc { get; init; }
    public bool HasUncommittedChanges { get; init; }
    public int UncommittedChangeCount { get; init; }
    public int UntrackedFileCount { get; init; }
    public bool AreUntrackedFilesProtected { get; init; }
}

public sealed class WorkspaceBackupSnapshot
{
    public bool HasRemoteConfigured { get; init; }
    public bool HasUnpublishedSavePoints { get; init; }
    public bool IsCurrentWorkingCopyPublished { get; init; }
    public DateTimeOffset? LastSuccessfulPublishUtc { get; init; }
    public bool NeedsReviewBeforePublish { get; init; }
    public bool IsOnProtectedBranch { get; init; }
}

public sealed class WorkspaceAdvancedGitSnapshot
{
    public string WorkingCopyName { get; init; } = string.Empty;
    public string CurrentBranch { get; init; } = string.Empty;
    public string DefaultBranch { get; init; } = string.Empty;
    public string RemoteName { get; init; } = string.Empty;
    public string RemoteUrl { get; init; } = string.Empty;
    public string RemoteBranch { get; init; } = string.Empty;
    public int AheadCount { get; init; }
    public int BehindCount { get; init; }
    public string LatestCommitSha { get; init; } = string.Empty;
    public string StatusSummary { get; init; } = string.Empty;
    public bool PatchExportSupported { get; init; }
    public bool IsProtectedBranch { get; init; }
    public bool IsWorkspaceBranch { get; init; }
    public List<string> ConflictingFiles { get; init; } = new();
    public string LastPatchExportPath { get; init; } = string.Empty;
}

public sealed class WorkspaceCheckpointIndex
{
    public List<WorkspaceCheckpointRecord> Items { get; init; } = new();
}

public sealed class WorkspaceCheckpointRecord
{
    public string Id { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
    public string CurrentBranch { get; init; } = string.Empty;
    public string CurrentCommitSha { get; init; } = string.Empty;
    public bool CapturedUntrackedFiles { get; init; }
    public List<string> UntrackedFiles { get; init; } = new();
}

public sealed class WorkspaceTimeline
{
    public List<WorkspaceTimelineEvent> Events { get; init; } = new();
}

public sealed class WorkspaceTimelineEvent
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public DateTimeOffset OccurredUtc { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
}

public sealed class WorkspaceGitState
{
    public bool IsRepository { get; init; }
    public bool HasRemoteConfigured { get; init; }
    public string RemoteName { get; init; } = string.Empty;
    public string RemoteUrl { get; init; } = string.Empty;
    public string WorkingCopyName { get; init; } = string.Empty;
    public string CurrentBranch { get; init; } = string.Empty;
    public string DefaultBranch { get; init; } = string.Empty;
    public string TrackingBranch { get; init; } = string.Empty;
    public int AheadCount { get; init; }
    public int BehindCount { get; init; }
    public string LatestCommitSha { get; init; } = string.Empty;
    public DateTimeOffset? LatestCommitUtc { get; init; }
    public bool HasUncommittedChanges { get; init; }
    public int UncommittedChangeCount { get; init; }
    public int UntrackedFileCount { get; init; }
    public string StatusSummary { get; init; } = string.Empty;
    public bool IsProtectedBranch { get; init; }
    public bool IsSafeWorkingCopy { get; init; }
    public bool IsWorkspaceBranch { get; init; }
    public List<string> ConflictingFiles { get; init; } = new();
}

public enum WorkspaceContentDisposition
{
    Tracked,
    Ignored,
    NeedsReview,
}

public enum WorkspaceContentFindingKind
{
    UnknownHiddenFolder,
    SecretCandidate,
    DurablePathIgnored,
    LargeGeneratedContent,
}

public sealed class WorkspaceContentClassification
{
    public string RelativePath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public WorkspaceContentDisposition Disposition { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class WorkspaceContentFinding
{
    public WorkspaceContentFindingKind Kind { get; init; }
    public string RelativePath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class WorkspaceIgnorePolicyReview
{
    public List<WorkspaceContentClassification> Classifications { get; init; } = new();
    public List<WorkspaceContentFinding> Findings { get; init; } = new();
    public bool HasReviewRequired => Findings.Count > 0;
    public bool HasSecretCandidates => Findings.Any(item => item.Kind == WorkspaceContentFindingKind.SecretCandidate);
    public bool HasUnknownHiddenFolders => Findings.Any(item => item.Kind == WorkspaceContentFindingKind.UnknownHiddenFolder);
    public bool HasDurableIgnoreConflicts => Findings.Any(item => item.Kind == WorkspaceContentFindingKind.DurablePathIgnored);
}

public sealed class WorkspacePublishReview
{
    public bool IsBlocked { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool RequiresUserConfirmation { get; init; }
    public bool SafeUpdateApplied { get; init; }
    public string WorkingCopyName { get; init; } = string.Empty;
    public string RemoteName { get; init; } = string.Empty;
    public string RemoteBranch { get; init; } = string.Empty;
    public int AheadCount { get; init; }
    public int BehindCount { get; init; }
    public string LatestCommitSha { get; init; } = string.Empty;
    public DateTimeOffset? LatestSavePointUtc { get; init; }
    public List<string> ConflictingFiles { get; init; } = new();
    public string ReviewWorkingCopyBranch { get; init; } = string.Empty;
    public string PatchExportPath { get; init; } = string.Empty;
}
