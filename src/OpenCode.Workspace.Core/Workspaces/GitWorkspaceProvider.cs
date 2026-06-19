using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class GitWorkspaceProvider : IWorkspaceProvider
{
    private readonly ProcessRunner _processRunner;
    private readonly WorkspaceIgnorePolicyService _ignorePolicyService;
    private readonly GitRepositoryService _gitRepositoryService;

    public GitWorkspaceProvider(ProcessRunner processRunner, WorkspaceIgnorePolicyService? ignorePolicyService = null)
    {
        _processRunner = processRunner;
        _ignorePolicyService = ignorePolicyService ?? new WorkspaceIgnorePolicyService();
        _gitRepositoryService = new GitRepositoryService(processRunner);
    }

    public string Type => "git";

    public GitRepositoryService RepositoryService => _gitRepositoryService;

    public async Task InitializeWorkspaceAsync(WorkspacePaths paths, WorkspaceDefinition definition, bool createInitialSavePoint, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        EnsureProviderType(definition);

        if (!(await _gitRepositoryService.IsRepositoryAsync(paths.RootPath, cancellationToken)))
        {
            await RunGitAsync(paths.RootPath, ["init", "-b", "main"], log, cancellationToken);
        }

        await _gitRepositoryService.EnsureRemoteConfiguredAsync(paths.RootPath, definition.Provider.Url, log, cancellationToken);

        if (createInitialSavePoint)
        {
            var hasCommit = await _gitRepositoryService.HasCommitsAsync(paths.RootPath, cancellationToken);
            if (!hasCommit)
            {
                await CreateSavePointAsync(paths, definition, "Create initial workspace Save Point", log, cancellationToken);
            }
        }

        // Normal users should start work from a Safe Working Copy so later Publish
        // decisions stay away from protected or mainline branches by default.
        await EnsureSafeWorkingCopyAsync(paths.RootPath, definition.Workspace.Name, log, cancellationToken);
    }

    public async Task<WorkspaceGitState> GetGitStateAsync(WorkspacePaths paths, WorkspaceDefinition definition, CancellationToken cancellationToken = default)
    {
        EnsureProviderType(definition);
        var inspection = await _gitRepositoryService.InspectAsync(paths.RootPath, cancellationToken);

        return new WorkspaceGitState
        {
            IsRepository = inspection.IsRepository,
            HasRemoteConfigured = inspection.HasRemoteConfigured,
            RemoteName = inspection.RemoteName,
            RemoteUrl = inspection.RemoteUrl,
            WorkingCopyName = inspection.WorkingCopyName,
            CurrentBranch = inspection.CurrentBranch,
            DefaultBranch = inspection.DefaultBranch,
            TrackingBranch = inspection.TrackingBranch,
            AheadCount = inspection.AheadCount,
            BehindCount = inspection.BehindCount,
            LatestCommitSha = inspection.LatestCommitSha,
            LatestCommitUtc = inspection.LatestCommitUtc,
            HasUncommittedChanges = inspection.HasUncommittedChanges,
            UncommittedChangeCount = inspection.UncommittedChangeCount,
            UntrackedFileCount = inspection.UntrackedFileCount,
            StatusSummary = inspection.StatusSummary,
            IsProtectedBranch = inspection.IsProtectedBranch,
            IsSafeWorkingCopy = inspection.IsSafeWorkingCopy,
            IsWorkspaceBranch = inspection.IsWorkspaceBranch,
            ConflictingFiles = inspection.ConflictingFiles ?? [],
        };
    }

    public async Task<bool> CreateSavePointAsync(WorkspacePaths paths, WorkspaceDefinition definition, string message, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        EnsureProviderType(definition);
        ValidateSavePointContent(paths.RootPath);
        await RunGitAsync(paths.RootPath, ["add", "-A"], log, cancellationToken);

        var status = await TryRunGitAsync(paths.RootPath, ["status", "--porcelain"], cancellationToken);
        if (!status.StandardOutputLines.Any(line => !string.IsNullOrWhiteSpace(line)))
        {
            log?.Invoke(new CommandLogEntry { Source = "git", Message = "No local changes were available for a Save Point." });
            return false;
        }

        var identityName = Environment.UserName;
        var identityEmail = $"{WorkingCopyNaming.SanitizeSegment(Environment.UserName, "user")}@local.workspace";
        await RunGitAsync(
            paths.RootPath,
            ["-c", $"user.name={identityName}", "-c", $"user.email={identityEmail}", "commit", "-m", message],
            log,
            cancellationToken);

        return true;
    }

    public async Task<WorkspacePublishReview> PublishAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        EnsureProviderType(definition);
        var gitState = await GetGitStateAsync(paths, definition, cancellationToken);
        if (gitState.IsProtectedBranch)
        {
            return CreateBlockedReview(gitState, "Your local work is safe. Create a Working Copy before publishing from a protected branch.");
        }

        if (!gitState.HasRemoteConfigured)
        {
            return CreateBlockedReview(gitState, "Your work is protected locally. Configure remote backup before publishing.");
        }

        await RunGitAsync(paths.RootPath, ["fetch", gitState.RemoteName], log, cancellationToken);
        gitState = await GetGitStateAsync(paths, definition, cancellationToken);

        if (gitState.BehindCount > 0)
        {
            // V1 treats remote divergence as a safety boundary. We only attempt an
            // update when the Working Copy is clean and Git can finish without
            // conflicts; otherwise we stop and preserve the user's local work.
            var updateResult = await TrySafeUpdateAsync(paths.RootPath, gitState, log, cancellationToken);
            if (updateResult.SafeUpdateApplied)
            {
                return updateResult;
            }

            return updateResult;
        }

        if (!string.IsNullOrWhiteSpace(gitState.TrackingBranch) && gitState.AheadCount == 0)
        {
            return new WorkspacePublishReview
            {
                IsBlocked = true,
                Message = "No unpublished Save Points are ready to Publish.",
                WorkingCopyName = gitState.WorkingCopyName,
                RemoteName = gitState.RemoteName,
                RemoteBranch = gitState.TrackingBranch,
                AheadCount = gitState.AheadCount,
                BehindCount = gitState.BehindCount,
                LatestCommitSha = gitState.LatestCommitSha,
                LatestSavePointUtc = gitState.LatestCommitUtc,
            };
        }

        if (!string.IsNullOrWhiteSpace(gitState.TrackingBranch))
        {
            await RunGitAsync(paths.RootPath, ["push"], log, cancellationToken);
        }
        else
        {
            await RunGitAsync(paths.RootPath, ["push", "-u", gitState.RemoteName, gitState.CurrentBranch], log, cancellationToken);
        }

        var publishedState = await GetGitStateAsync(paths, definition, cancellationToken);
        return new WorkspacePublishReview
        {
            IsBlocked = false,
            Message = "Working Copy published successfully.",
            WorkingCopyName = publishedState.WorkingCopyName,
            RemoteName = publishedState.RemoteName,
            RemoteBranch = publishedState.TrackingBranch,
            AheadCount = publishedState.AheadCount,
            BehindCount = publishedState.BehindCount,
            LatestCommitSha = publishedState.LatestCommitSha,
            LatestSavePointUtc = publishedState.LatestCommitUtc,
        };
    }

    public async Task<WorkspacePublishReview> UpdateWorkingCopyAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        EnsureProviderType(definition);
        var gitState = await GetGitStateAsync(paths, definition, cancellationToken);
        if (!gitState.HasRemoteConfigured)
        {
            return CreateBlockedReview(gitState, "Your work is protected locally. Configure remote backup before updating from the remote workspace.");
        }

        if (gitState.IsProtectedBranch)
        {
            return CreateBlockedReview(gitState, "Your local work is safe. Create a Working Copy before updating a protected branch.");
        }

        await RunGitAsync(paths.RootPath, ["fetch", gitState.RemoteName], log, cancellationToken);
        gitState = await GetGitStateAsync(paths, definition, cancellationToken);
        if (gitState.BehindCount == 0)
        {
            return new WorkspacePublishReview
            {
                IsBlocked = false,
                Message = "Working Copy is already up to date.",
                WorkingCopyName = gitState.WorkingCopyName,
                RemoteName = gitState.RemoteName,
                RemoteBranch = gitState.TrackingBranch,
                AheadCount = gitState.AheadCount,
                BehindCount = gitState.BehindCount,
                LatestCommitSha = gitState.LatestCommitSha,
                LatestSavePointUtc = gitState.LatestCommitUtc,
            };
        }

        return await TrySafeUpdateAsync(paths.RootPath, gitState, log, cancellationToken);
    }

    public async Task<WorkspacePublishReview> PublishToReviewWorkingCopyAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        EnsureProviderType(definition);
        var gitState = await GetGitStateAsync(paths, definition, cancellationToken);
        if (gitState.IsProtectedBranch)
        {
            return CreateBlockedReview(gitState, "Your local work is safe. Create a Working Copy before publishing from a protected branch.");
        }

        if (!gitState.HasRemoteConfigured)
        {
            return CreateBlockedReview(gitState, "Your work is protected locally. Configure remote backup before publishing a review Working Copy.");
        }

        var reviewBranch = await _gitRepositoryService.GetUniqueBranchNameAsync(
            paths.RootPath,
            WorkingCopyNaming.CreateReview(Environment.UserName, definition.Workspace.Name, DateTimeOffset.UtcNow),
            cancellationToken);
        await RunGitAsync(paths.RootPath, ["push", gitState.RemoteName, $"HEAD:refs/heads/{reviewBranch}"], log, cancellationToken);

        return new WorkspacePublishReview
        {
            IsBlocked = false,
            Message = "Working Copy published to a review branch successfully.",
            WorkingCopyName = gitState.WorkingCopyName,
            RemoteName = gitState.RemoteName,
            RemoteBranch = gitState.TrackingBranch,
            AheadCount = gitState.AheadCount,
            BehindCount = gitState.BehindCount,
            LatestCommitSha = gitState.LatestCommitSha,
            LatestSavePointUtc = gitState.LatestCommitUtc,
            ReviewWorkingCopyBranch = reviewBranch,
        };
    }

    public async Task<string> ExportPatchAsync(WorkspacePaths paths, WorkspaceDefinition definition, string outputPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        EnsureProviderType(definition);
        var result = await RunGitAsync(paths.RootPath, ["diff", "--binary", "HEAD"], log, cancellationToken);
        File.WriteAllText(outputPath, result.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal));
        return outputPath;
    }

    public async Task<IReadOnlyList<string>> GetUntrackedFilesAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var result = await TryRunGitAsync(repositoryRoot, ["ls-files", "--others", "--exclude-standard"], cancellationToken);
        return result.StandardOutputLines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
    }

    public async Task<string> GetTrackedChangesPatchAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var result = await TryRunGitAsync(repositoryRoot, ["diff", "--binary", "HEAD"], cancellationToken);
        return result.StandardOutput;
    }

    private static void EnsureProviderType(WorkspaceDefinition definition)
    {
        if (!string.Equals(definition.Provider.Type, "git", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Workspace provider '{definition.Provider.Type}' is not supported by the Git workspace provider.");
        }
    }

    private async Task EnsureSafeWorkingCopyAsync(string repositoryRoot, string workspaceTitle, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        var currentBranch = (await TryRunGitAsync(repositoryRoot, ["branch", "--show-current"], cancellationToken)).StandardOutput.Trim();
        if (WorkingCopyNaming.IsSafeWorkingCopy(currentBranch))
        {
            return;
        }

        var desiredBranchName = await _gitRepositoryService.CreateUniqueWorkspaceBranchNameAsync(repositoryRoot, workspaceTitle, DateTimeOffset.UtcNow, cancellationToken);
        await _gitRepositoryService.CreateBranchAsync(repositoryRoot, desiredBranchName, log, cancellationToken);
    }

    private async Task<WorkspacePublishReview> TrySafeUpdateAsync(string repositoryRoot, WorkspaceGitState gitState, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        if (gitState.HasUncommittedChanges || gitState.UntrackedFileCount > 0)
        {
            return CreateBlockedReview(gitState, "Your local work is safe. The remote workspace changed and needs review before publishing.");
        }

        if (string.IsNullOrWhiteSpace(gitState.TrackingBranch))
        {
            return CreateBlockedReview(gitState, "Your local work is safe. The remote workspace changed and needs review before publishing.");
        }

        // Rebase is used here only as a non-destructive fast-forward-or-replay step.
        // If Git reports any conflict or uncertainty, we abort immediately and keep
        // the user's current Working Copy intact instead of auto-resolving.
        var rebaseResult = await TryRunGitAsync(repositoryRoot, ["rebase", gitState.TrackingBranch], cancellationToken, log);
        if (!rebaseResult.IsSuccess)
        {
            await TryRunGitAsync(repositoryRoot, ["rebase", "--abort"], cancellationToken, log);
            var conflictedState = await GetGitStateAsync(WorkspacePathBuilder.Build(repositoryRoot), new WorkspaceDefinition { Provider = new WorkspaceProviderDefinition { Type = Type } }, cancellationToken);
            return CreateBlockedReview(conflictedState, "Your local work is safe. The remote workspace changed and needs review before publishing.");
        }

        var updatedState = await GetGitStateAsync(WorkspacePathBuilder.Build(repositoryRoot), new WorkspaceDefinition { Provider = new WorkspaceProviderDefinition { Type = Type } }, cancellationToken);
        return new WorkspacePublishReview
        {
            IsBlocked = true,
            RequiresUserConfirmation = true,
            SafeUpdateApplied = true,
            Message = "Your Working Copy was updated safely. Review changes before publishing.",
            WorkingCopyName = updatedState.WorkingCopyName,
            RemoteName = updatedState.RemoteName,
            RemoteBranch = updatedState.TrackingBranch,
            AheadCount = updatedState.AheadCount,
            BehindCount = updatedState.BehindCount,
            LatestCommitSha = updatedState.LatestCommitSha,
            LatestSavePointUtc = updatedState.LatestCommitUtc,
            ConflictingFiles = updatedState.ConflictingFiles,
        };
    }

    private static WorkspacePublishReview CreateBlockedReview(WorkspaceGitState gitState, string message)
    {
        return new WorkspacePublishReview
        {
            IsBlocked = true,
            Message = message,
            WorkingCopyName = gitState.WorkingCopyName,
            RemoteName = gitState.RemoteName,
            RemoteBranch = gitState.TrackingBranch,
            AheadCount = gitState.AheadCount,
            BehindCount = gitState.BehindCount,
            LatestCommitSha = gitState.LatestCommitSha,
            LatestSavePointUtc = gitState.LatestCommitUtc,
            ConflictingFiles = gitState.ConflictingFiles,
        };
    }

    private async Task EnsureRemoteConfiguredAsync(string repositoryRoot, string? remoteUrl, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
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

    private async Task<bool> IsRepositoryAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await TryRunGitAsync(repositoryRoot, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
        return result.IsSuccess && string.Equals(result.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> HasCommitsAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await TryRunGitAsync(repositoryRoot, ["rev-parse", "--verify", "HEAD"], cancellationToken);
        return result.IsSuccess && !string.IsNullOrWhiteSpace(result.StandardOutput);
    }

    private async Task<ProcessResult> RunGitAsync(string repositoryRoot, IReadOnlyList<string> arguments, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        var result = await TryRunGitAsync(repositoryRoot, arguments, cancellationToken, log);
        if (result.IsSuccess)
        {
            return result;
        }

        var details = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        throw new InvalidOperationException($"Git operation failed.{Environment.NewLine}Command: {result.Command}{Environment.NewLine}Exit code: {result.ExitCode}{Environment.NewLine}{details}".Trim());
    }

    private async Task<ProcessResult> TryRunGitAsync(string repositoryRoot, IReadOnlyList<string> arguments, CancellationToken cancellationToken, Action<CommandLogEntry>? log = null)
    {
        try
        {
            return await _processRunner.RunAsync(
                "git",
                arguments,
                repositoryRoot,
                (isError, line) => log?.Invoke(new CommandLogEntry
                {
                    Source = isError ? "git:err" : "git",
                    Message = line,
                }),
                cancellationToken);
        }
        catch (Exception exception)
        {
            log?.Invoke(new CommandLogEntry
            {
                Source = "git:err",
                Message = exception.Message,
            });

            return new ProcessResult
            {
                Command = $"git {string.Join(" ", arguments)}",
                ExitCode = 1,
                StandardOutput = string.Empty,
                StandardError = exception.Message,
                StandardOutputLines = Array.Empty<string>(),
                StandardErrorLines = new[] { exception.Message },
                Duration = TimeSpan.Zero,
            };
        }
    }

    private static string BuildStatusSummary(int uncommittedCount, int untrackedCount, int aheadCount, int behindCount, string currentBranch, string trackingBranch, int conflictingFileCount)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(currentBranch))
        {
            parts.Add($"branch {currentBranch}");
        }

        if (!string.IsNullOrWhiteSpace(trackingBranch))
        {
            parts.Add($"tracks {trackingBranch}");
        }

        parts.Add($"{uncommittedCount} changed");
        parts.Add($"{untrackedCount} untracked");

        if (!string.IsNullOrWhiteSpace(trackingBranch))
        {
            parts.Add($"ahead {aheadCount}");
            parts.Add($"behind {behindCount}");
        }

        if (conflictingFileCount > 0)
        {
            parts.Add($"conflicts {conflictingFileCount}");
        }

        return string.Join(", ", parts);
    }

    private void ValidateSavePointContent(string workspaceRoot)
    {
        var review = BuildSavePointReview(workspaceRoot);
        if (!review.HasReviewRequired)
        {
            return;
        }

        var message = string.Join(
            Environment.NewLine,
            new[] { "Workspace Review required before creating a Save Point." }
                .Concat(review.Findings.Select(item => $"- {item.RelativePath}: {item.Message}")));
        throw new InvalidOperationException(message);
    }

    private WorkspaceIgnorePolicyReview BuildSavePointReview(string workspaceRoot)
    {
        try
        {
            var statusResult = _processRunner.RunAsync(
                "git",
                ["status", "--porcelain", "--untracked-files=all"],
                workspaceRoot).GetAwaiter().GetResult();

            if (statusResult.IsSuccess)
            {
                var changedPaths = statusResult.StandardOutputLines
                    .Where(line => !string.IsNullOrWhiteSpace(line) && line.Length >= 4)
                    .Select(line => line[3..].Trim())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path.Contains(" -> ", StringComparison.Ordinal) ? path[(path.LastIndexOf(" -> ", StringComparison.Ordinal) + 4)..] : path)
                    .ToList();

                if (changedPaths.Count > 0)
                {
                    // Save Point validation should inspect what is about to enter the
                    // Save Point, not only top-level workspace entries. Git status is
                    // the most precise V1 source for changed and untracked content.
                    return _ignorePolicyService.ReviewChangedPathsForProtection(workspaceRoot, changedPaths);
                }
            }
        }
        catch
        {
            // Fall back to recursive scanning below.
        }

        // If Git status is unavailable, preserve safety by scanning the workspace
        // recursively while skipping known disposable locations.
        return _ignorePolicyService.ReviewWorkspaceForProtection(workspaceRoot);
    }
}
