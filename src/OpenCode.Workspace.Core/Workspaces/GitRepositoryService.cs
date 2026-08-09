using System.Globalization;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class GitRepositoryService
{
    private readonly ProcessRunner _processRunner;

    public GitRepositoryService(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<GitRepositoryInspection> InspectAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var probe = await ProbeRepositoryAsync(repositoryRoot, cancellationToken);
        if (!probe.IsRepository)
        {
            return new GitRepositoryInspection
            {
                IsRepository = false,
                StatusSummary = "Git is not initialized.",
                ProbeFailureDetails = DescribeRepositoryProbeFailure(probe),
            };
        }

        var statusResult = await TryRunGitAsync(repositoryRoot, ["status", "--porcelain"], cancellationToken);
        var remoteUrlResult = await TryRunGitAsync(repositoryRoot, ["remote", "get-url", "origin"], cancellationToken);
        var branchResult = await TryRunGitAsync(repositoryRoot, ["branch", "--show-current"], cancellationToken);
        var trackingResult = await TryRunGitAsync(repositoryRoot, ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"], cancellationToken);
        var latestCommitResult = await TryRunGitAsync(repositoryRoot, ["log", "-1", "--format=%H|%cI"], cancellationToken);
        var conflictResult = await TryRunGitAsync(repositoryRoot, ["diff", "--name-only", "--diff-filter=U"], cancellationToken);

        var currentBranch = branchResult.StandardOutput.Trim();
        var trackingBranch = trackingResult.IsSuccess ? trackingResult.StandardOutput.Trim() : string.Empty;
        var defaultBranch = await DetectDefaultBranchAsync(repositoryRoot, cancellationToken);
        var remoteUrl = remoteUrlResult.IsSuccess ? remoteUrlResult.StandardOutput.Trim() : string.Empty;
        var isProtectedBranch = WorkingCopyNaming.IsProtectedBranch(currentBranch);
        var isWorkspaceBranch = WorkingCopyNaming.IsWorkspaceBranch(currentBranch);
        var isSafeWorkingCopy = WorkingCopyNaming.IsSafeWorkingCopy(currentBranch);

        var aheadCount = 0;
        var behindCount = 0;
        if (!string.IsNullOrWhiteSpace(trackingBranch))
        {
            var aheadBehindResult = await TryRunGitAsync(repositoryRoot, ["rev-list", "--left-right", "--count", $"{trackingBranch}...HEAD"], cancellationToken);
            if (aheadBehindResult.IsSuccess)
            {
                var counts = aheadBehindResult.StandardOutput.Trim().Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
                if (counts.Length == 2)
                {
                    _ = int.TryParse(counts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out behindCount);
                    _ = int.TryParse(counts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out aheadCount);
                }
            }
        }

        var uncommittedCount = 0;
        var untrackedCount = 0;
        var changedPaths = new List<string>();
        foreach (var line in statusResult.StandardOutputLines.Select(item => item.TrimEnd()).Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (line.Length >= 4)
            {
                var path = line[3..].Trim();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    changedPaths.Add(path.Contains(" -> ", StringComparison.Ordinal) ? path[(path.LastIndexOf(" -> ", StringComparison.Ordinal) + 4)..] : path);
                }
            }

            if (line.StartsWith("??", StringComparison.Ordinal))
            {
                untrackedCount++;
            }
            else
            {
                uncommittedCount++;
            }
        }

        var latestSha = string.Empty;
        DateTimeOffset? latestUtc = null;
        if (latestCommitResult.IsSuccess)
        {
            var parts = latestCommitResult.StandardOutput.Trim().Split('|', 2);
            if (parts.Length == 2)
            {
                latestSha = parts[0];
                if (DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                {
                    latestUtc = parsed;
                }
            }
        }

        var conflictingFiles = conflictResult.StandardOutputLines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new GitRepositoryInspection
        {
            IsRepository = true,
            HasRemoteConfigured = !string.IsNullOrWhiteSpace(remoteUrl),
            RemoteName = string.IsNullOrWhiteSpace(remoteUrl) ? string.Empty : "origin",
            RemoteUrl = remoteUrl,
            WorkingCopyName = isWorkspaceBranch ? currentBranch : string.Empty,
            CurrentBranch = currentBranch,
            DefaultBranch = defaultBranch,
            TrackingBranch = trackingBranch,
            AheadCount = aheadCount,
            BehindCount = behindCount,
            LatestCommitSha = latestSha,
            LatestCommitUtc = latestUtc,
            HasUncommittedChanges = uncommittedCount > 0,
            UncommittedChangeCount = uncommittedCount,
            UntrackedFileCount = untrackedCount,
            StatusSummary = BuildStatusSummary(uncommittedCount, untrackedCount, aheadCount, behindCount, currentBranch, trackingBranch, conflictingFiles.Count),
            IsProtectedBranch = isProtectedBranch,
            IsSafeWorkingCopy = isSafeWorkingCopy,
            IsWorkspaceBranch = isWorkspaceBranch,
            ConflictingFiles = conflictingFiles,
            ChangedPaths = changedPaths,
        };
    }

    public async Task<GitBranchValidationResult> ValidateBranchNameAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return new GitBranchValidationResult(false, "Enter a branch name.", false);
        }

        var result = await TryRunGitAsync(repositoryRoot, ["check-ref-format", "--branch", branchName], cancellationToken);
        if (!result.IsSuccess)
        {
            return new GitBranchValidationResult(false, "Enter a valid branch name.", false);
        }

        var exists = await BranchExistsAsync(repositoryRoot, branchName, cancellationToken);
        return new GitBranchValidationResult(true, exists ? "Branch already exists." : string.Empty, exists);
    }

    public async Task<string> CreateUniqueWorkspaceBranchNameAsync(string repositoryRoot, string workspaceName, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        var baseBranchName = WorkingCopyNaming.CreateImportedWorkspace(workspaceName, timestamp);
        return await GetUniqueBranchNameAsync(repositoryRoot, baseBranchName, cancellationToken);
    }

    public async Task<string> GetUniqueBranchNameAsync(string repositoryRoot, string baseBranchName, CancellationToken cancellationToken = default)
    {
        var candidate = baseBranchName;
        var suffix = 2;
        while (await BranchExistsInternalAsync(repositoryRoot, candidate, cancellationToken))
        {
            candidate = $"{baseBranchName}-{suffix}";
            suffix++;
        }

        return candidate;
    }

    public Task<bool> BranchExistsAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
        => BranchExistsInternalAsync(repositoryRoot, branchName, cancellationToken);

    public async Task CreateBranchAsync(string repositoryRoot, string branchName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => await RunGitAsync(repositoryRoot, ["checkout", "-b", branchName], log, cancellationToken);

    public async Task CheckoutBranchAsync(string repositoryRoot, string branchName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => await RunGitAsync(repositoryRoot, ["checkout", branchName], log, cancellationToken);

    public async Task<bool> HasCommitsAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var result = await TryRunGitAsync(repositoryRoot, ["rev-parse", "--verify", "HEAD"], cancellationToken);
        return result.IsSuccess;
    }

    public async Task EnsureRemoteConfiguredAsync(string repositoryRoot, string? remoteUrl, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return;
        }

        var remoteResult = await TryRunGitAsync(repositoryRoot, ["remote", "get-url", "origin"], cancellationToken);
        if (remoteResult.IsSuccess)
        {
            if (!string.Equals(remoteResult.StandardOutput.Trim(), remoteUrl.Trim(), StringComparison.Ordinal))
            {
                await RunGitAsync(repositoryRoot, ["remote", "set-url", "origin", remoteUrl.Trim()], log, cancellationToken);
            }

            return;
        }

        await RunGitAsync(repositoryRoot, ["remote", "add", "origin", remoteUrl.Trim()], log, cancellationToken);
    }

    public async Task FetchAsync(string repositoryRoot, string remoteName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remoteName))
        {
            throw new ArgumentException("Remote name is required.", nameof(remoteName));
        }

        await RunGitAsync(repositoryRoot, ["fetch", remoteName], log, cancellationToken);
    }

    public async Task<bool> IsRepositoryAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var probe = await ProbeRepositoryAsync(repositoryRoot, cancellationToken);
        return probe.IsRepository;
    }

    public async Task<GitRepositoryProbe> ProbeRepositoryAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(repositoryRoot)
            ? string.Empty
            : Path.GetFullPath(repositoryRoot.Trim());
        var primary = await TryRunGitAsync(normalizedPath, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
        var primarySucceeded = primary.IsSuccess && string.Equals(primary.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        ProcessResult? explicitPathCheck = null;
        var explicitSucceeded = false;
        if (!primarySucceeded && !string.IsNullOrWhiteSpace(normalizedPath) && Directory.Exists(normalizedPath))
        {
            explicitPathCheck = await _processRunner.RunAsync("git", ["-C", normalizedPath, "rev-parse", "--is-inside-work-tree"], cancellationToken: cancellationToken);
            explicitSucceeded = explicitPathCheck.IsSuccess && string.Equals(explicitPathCheck.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        return new GitRepositoryProbe(
            normalizedPath,
            primarySucceeded || explicitSucceeded,
            !string.IsNullOrWhiteSpace(normalizedPath) && (Directory.Exists(Path.Combine(normalizedPath, ".git")) || File.Exists(Path.Combine(normalizedPath, ".git"))),
            !string.IsNullOrWhiteSpace(normalizedPath) && (File.Exists(Path.Combine(normalizedPath, "workspace.yaml")) || File.Exists(Path.Combine(normalizedPath, "workspace.yml"))),
            primary,
            explicitPathCheck);
    }

    public static string DescribeRepositoryProbeFailure(GitRepositoryProbe probe)
    {
        var lines = new List<string>
        {
            $"The selected folder is not a Git checkout.",
            $"Path: {probe.RepositoryPath}",
            $".git present: {probe.GitDirectoryExists}",
            $"workspace.yaml present: {probe.WorkspaceConfigurationExists}",
            $"Working-directory probe: {probe.WorkingDirectoryCheck.Command}",
            $"Working-directory exit code: {probe.WorkingDirectoryCheck.ExitCode}",
        };

        if (!string.IsNullOrWhiteSpace(probe.WorkingDirectoryCheck.StandardOutput))
        {
            lines.Add($"Working-directory stdout: {probe.WorkingDirectoryCheck.StandardOutput.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(probe.WorkingDirectoryCheck.StandardError))
        {
            lines.Add($"Working-directory stderr: {probe.WorkingDirectoryCheck.StandardError.Trim()}");
        }

        if (probe.ExplicitPathCheck is not null)
        {
            lines.Add($"Explicit-path probe: {probe.ExplicitPathCheck.Command}");
            lines.Add($"Explicit-path exit code: {probe.ExplicitPathCheck.ExitCode}");
            if (!string.IsNullOrWhiteSpace(probe.ExplicitPathCheck.StandardOutput))
            {
                lines.Add($"Explicit-path stdout: {probe.ExplicitPathCheck.StandardOutput.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(probe.ExplicitPathCheck.StandardError))
            {
                lines.Add($"Explicit-path stderr: {probe.ExplicitPathCheck.StandardError.Trim()}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private async Task<bool> BranchExistsInternalAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken)
    {
        var result = await TryRunGitAsync(repositoryRoot, ["show-ref", "--verify", $"refs/heads/{branchName}"], cancellationToken);
        return result.IsSuccess;
    }

    private async Task<string> DetectDefaultBranchAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var remoteHeadResult = await TryRunGitAsync(repositoryRoot, ["symbolic-ref", "refs/remotes/origin/HEAD", "--short"], cancellationToken);
        if (remoteHeadResult.IsSuccess)
        {
            var value = remoteHeadResult.StandardOutput.Trim();
            if (value.StartsWith("origin/", StringComparison.OrdinalIgnoreCase))
            {
                return value["origin/".Length..];
            }
        }

        foreach (var candidate in new[] { "main", "master" })
        {
            if (await BranchExistsInternalAsync(repositoryRoot, candidate, cancellationToken))
            {
                return candidate;
            }
        }

        var currentBranchResult = await TryRunGitAsync(repositoryRoot, ["branch", "--show-current"], cancellationToken);
        return currentBranchResult.IsSuccess ? currentBranchResult.StandardOutput.Trim() : string.Empty;
    }

    private static string BuildStatusSummary(int uncommittedCount, int untrackedCount, int aheadCount, int behindCount, string currentBranch, string trackingBranch, int conflictCount)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(currentBranch))
        {
            parts.Add($"Branch {currentBranch}");
        }

        if (uncommittedCount > 0)
        {
            parts.Add($"{uncommittedCount} changed");
        }

        if (untrackedCount > 0)
        {
            parts.Add($"{untrackedCount} untracked");
        }

        if (!string.IsNullOrWhiteSpace(trackingBranch))
        {
            parts.Add($"ahead {aheadCount}");
            parts.Add($"behind {behindCount}");
        }

        if (conflictCount > 0)
        {
            parts.Add($"{conflictCount} conflicts");
        }

        return parts.Count == 0 ? "Working tree is clean." : string.Join(", ", parts);
    }

    private async Task<ProcessResult> RunGitAsync(string repositoryRoot, IReadOnlyList<string> arguments, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        log?.Invoke(new CommandLogEntry { Source = "git", Message = $"git {string.Join(' ', arguments)}" });
        var result = await _processRunner.RunAsync("git", arguments, repositoryRoot, cancellationToken: cancellationToken, timeout: TimeSpan.FromSeconds(30));
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? "Git command failed." : result.StandardError.Trim());
        }

        return result;
    }

    private async Task<ProcessResult> TryRunGitAsync(string repositoryRoot, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        => await _processRunner.RunAsync("git", arguments, repositoryRoot, cancellationToken: cancellationToken, timeout: TimeSpan.FromSeconds(30));
}

public sealed record GitRepositoryInspection(
    bool IsRepository = false,
    bool HasRemoteConfigured = false,
    string RemoteName = "",
    string RemoteUrl = "",
    string WorkingCopyName = "",
    string CurrentBranch = "",
    string DefaultBranch = "",
    string TrackingBranch = "",
    int AheadCount = 0,
    int BehindCount = 0,
    string LatestCommitSha = "",
    DateTimeOffset? LatestCommitUtc = null,
    bool HasUncommittedChanges = false,
    int UncommittedChangeCount = 0,
    int UntrackedFileCount = 0,
    string StatusSummary = "",
    bool IsProtectedBranch = false,
    bool IsSafeWorkingCopy = false,
    bool IsWorkspaceBranch = false,
    List<string>? ConflictingFiles = null,
    List<string>? ChangedPaths = null,
    string ProbeFailureDetails = "");

public sealed record GitRepositoryProbe(
    string RepositoryPath,
    bool IsRepository,
    bool GitDirectoryExists,
    bool WorkspaceConfigurationExists,
    ProcessResult WorkingDirectoryCheck,
    ProcessResult? ExplicitPathCheck);

public sealed record GitBranchValidationResult(bool IsValid, string Message, bool BranchExists);
