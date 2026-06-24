using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.AppSupport;

public sealed class WorkspacePublishAssessmentService
{
    private readonly GitWorkspaceProvider _gitWorkspaceProvider;
    private readonly GitRepositoryService _gitRepositoryService;

    public WorkspacePublishAssessmentService(ProcessRunner? processRunner = null)
    {
        var runner = processRunner ?? new ProcessRunner();
        _gitWorkspaceProvider = new GitWorkspaceProvider(runner);
        _gitRepositoryService = _gitWorkspaceProvider.RepositoryService;
    }

    public async Task<WorkspacePublishAssessment> AssessAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!string.Equals(snapshot.Definition.Provider.Type, "git", StringComparison.OrdinalIgnoreCase))
        {
            return new WorkspacePublishAssessment
            {
                WorkspaceName = snapshot.Definition.Workspace.Name,
                CurrentBranch = snapshot.Safety.AdvancedGit.CurrentBranch,
                Summary = "Publish is only available for Git-backed workspaces.",
                ConfirmationMessage = string.Empty,
                Findings = ["Workspace provider is not Git-backed."],
                Warnings = [],
                CanPublish = false,
                IsBlocked = true,
                RequiresConfirmation = false,
                RequiresSavePoint = false,
                HasRemoteConfigured = false,
                RemoteName = string.Empty,
                RemoteBranch = string.Empty,
                AheadCount = 0,
                BehindCount = 0,
            };
        }

        var gitState = await _gitWorkspaceProvider.GetGitStateAsync(snapshot.Paths, snapshot.Definition, cancellationToken);
        if (gitState.HasRemoteConfigured && !string.IsNullOrWhiteSpace(gitState.RemoteName))
        {
            await _gitRepositoryService.FetchAsync(snapshot.Paths.RootPath, gitState.RemoteName, log, cancellationToken);
            gitState = await _gitWorkspaceProvider.GetGitStateAsync(snapshot.Paths, snapshot.Definition, cancellationToken);
        }

        var findings = BuildFindings(snapshot, gitState);
        var warnings = BuildWarnings(snapshot, gitState);
        var blockedReason = GetBlockedReason(snapshot, gitState);
        var canPublish = string.IsNullOrWhiteSpace(blockedReason);
        var summary = canPublish
            ? BuildReadySummary(gitState)
            : blockedReason!;

        return new WorkspacePublishAssessment
        {
            WorkspaceName = snapshot.Definition.Workspace.Name,
            CurrentBranch = gitState.CurrentBranch,
            Summary = summary,
            ConfirmationMessage = canPublish ? BuildConfirmationMessage(gitState) : string.Empty,
            Findings = findings,
            Warnings = warnings,
            CanPublish = canPublish,
            IsBlocked = !canPublish,
            RequiresConfirmation = canPublish,
            RequiresSavePoint = gitState.HasUncommittedChanges || gitState.UntrackedFileCount > 0,
            HasRemoteConfigured = gitState.HasRemoteConfigured,
            RemoteName = gitState.RemoteName,
            RemoteBranch = gitState.TrackingBranch,
            AheadCount = gitState.AheadCount,
            BehindCount = gitState.BehindCount,
        };
    }

    private static IReadOnlyList<string> BuildFindings(WorkspaceSnapshot snapshot, WorkspaceGitState gitState)
    {
        var findings = new List<string>
        {
            $"Working Copy: {DescribeWorkingCopy(gitState)}",
            $"Current branch: {DescribeValue(gitState.CurrentBranch, "Unknown")}",
        };

        findings.Add(gitState.HasRemoteConfigured
            ? $"Remote backup: {gitState.RemoteName} ({DescribeValue(gitState.RemoteUrl, "Configured")})"
            : "Remote backup: not configured.");

        findings.Add(string.IsNullOrWhiteSpace(gitState.TrackingBranch)
            ? "Tracking branch: first publish will create upstream tracking."
            : $"Tracking branch: {gitState.TrackingBranch}");

        findings.Add($"Ahead/behind: {gitState.AheadCount}/{gitState.BehindCount}");

        if (gitState.HasUncommittedChanges || gitState.UntrackedFileCount > 0)
        {
            findings.Add($"Working tree changes: {gitState.UncommittedChangeCount} changed, {gitState.UntrackedFileCount} untracked.");
        }
        else
        {
            findings.Add("Working tree is clean.");
        }

        if (gitState.ConflictingFiles.Count > 0)
        {
            findings.Add($"Conflicts: {string.Join(", ", gitState.ConflictingFiles)}");
        }

        if (snapshot.Safety.LocalRecovery.LatestSavePointUtc is { } latestSavePointUtc)
        {
            findings.Add($"Latest Save Point: {latestSavePointUtc:O}");
        }
        else
        {
            findings.Add("Latest Save Point: none recorded.");
        }

        return findings;
    }

    private static IReadOnlyList<string> BuildWarnings(WorkspaceSnapshot snapshot, WorkspaceGitState gitState)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(gitState.TrackingBranch) && gitState.HasRemoteConfigured)
        {
            warnings.Add("This is the first publish for the current Working Copy. Upstream tracking will be created.");
        }

        if (snapshot.Safety.LocalRecovery.LatestSavePointUtc is null && gitState.AheadCount > 0)
        {
            warnings.Add("No recorded Save Point timestamp was found for this workspace. Publish will send current commits only.");
        }

        return warnings;
    }

    private static string? GetBlockedReason(WorkspaceSnapshot snapshot, WorkspaceGitState gitState)
    {
        if (!gitState.IsRepository)
        {
            return "Git is not initialized for this workspace, so Publish cannot run.";
        }

        if (gitState.IsProtectedBranch)
        {
            return "This workspace is on a protected or mainline branch. Create a Safe Working Copy before publishing.";
        }

        if (!gitState.HasRemoteConfigured)
        {
            return "Remote backup is not configured. Add a remote before publishing.";
        }

        if (gitState.ConflictingFiles.Count > 0)
        {
            return "Git conflicts are present. Resolve conflicts before publishing.";
        }

        if (gitState.HasUncommittedChanges || gitState.UntrackedFileCount > 0)
        {
            return "Uncommitted or untracked work is present. Create a Save Point before publishing.";
        }

        if (gitState.BehindCount > 0 && gitState.AheadCount > 0)
        {
            return "Local and remote history diverged. Update and review the Working Copy before publishing.";
        }

        if (gitState.BehindCount > 0)
        {
            return "Remote backup changed since your last sync. Update and review the Working Copy before publishing.";
        }

        if (gitState.AheadCount == 0 && !string.IsNullOrWhiteSpace(gitState.TrackingBranch))
        {
            return "No unpublished Save Points are ready to Publish.";
        }

        if (string.IsNullOrWhiteSpace(gitState.LatestCommitSha))
        {
            return "No unpublished Save Points are ready to Publish.";
        }

        return null;
    }

    private static string BuildReadySummary(WorkspaceGitState gitState)
    {
        if (string.IsNullOrWhiteSpace(gitState.TrackingBranch))
        {
            return $"Ready to publish {gitState.AheadCount} commit(s) and create upstream tracking on '{gitState.RemoteName}'.";
        }

        return $"Ready to publish {gitState.AheadCount} commit(s) to '{gitState.TrackingBranch}'.";
    }

    private static string BuildConfirmationMessage(WorkspaceGitState gitState)
    {
        if (string.IsNullOrWhiteSpace(gitState.TrackingBranch))
        {
            return $"Publish this Working Copy to remote '{gitState.RemoteName}' now?";
        }

        return $"Publish this Working Copy to '{gitState.TrackingBranch}' now?";
    }

    private static string DescribeWorkingCopy(WorkspaceGitState gitState)
        => string.IsNullOrWhiteSpace(gitState.WorkingCopyName) ? DescribeValue(gitState.CurrentBranch, "Unknown") : gitState.WorkingCopyName;

    private static string DescribeValue(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;
}

public sealed class WorkspacePublishAssessment
{
    public required string WorkspaceName { get; init; }
    public required string CurrentBranch { get; init; }
    public required string Summary { get; init; }
    public required string ConfirmationMessage { get; init; }
    public required IReadOnlyList<string> Findings { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool CanPublish { get; init; }
    public required bool IsBlocked { get; init; }
    public required bool RequiresConfirmation { get; init; }
    public required bool RequiresSavePoint { get; init; }
    public required bool HasRemoteConfigured { get; init; }
    public required string RemoteName { get; init; }
    public required string RemoteBranch { get; init; }
    public required int AheadCount { get; init; }
    public required int BehindCount { get; init; }
}
