using System.Diagnostics;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class DesktopShellService : IDesktopShellService
{
    private readonly WorkspaceOrchestrator _workspaceOrchestrator;
    private readonly WorkspaceDiscoveryReportService _workspaceDiscoveryReportService;
    private readonly WorkspaceRepository _workspaceRepository;
    private readonly WorkspaceTimelineService _timelineService;
    private readonly WorkspaceCheckpointService _checkpointService;
    private readonly WorkspaceSavePointMessageService _savePointMessageService;

    public DesktopShellService(
        WorkspaceOrchestrator workspaceOrchestrator,
        WorkspaceRepository workspaceRepository,
        WorkspaceTimelineService timelineService,
        WorkspaceCheckpointService checkpointService,
        WorkspaceSavePointMessageService savePointMessageService)
    {
        _workspaceOrchestrator = workspaceOrchestrator;
        _workspaceDiscoveryReportService = new WorkspaceDiscoveryReportService(workspaceOrchestrator, workspaceRepository);
        _workspaceRepository = workspaceRepository;
        _timelineService = timelineService;
        _checkpointService = checkpointService;
        _savePointMessageService = savePointMessageService;
    }

    public async Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(bool includeRuntimeInspection, Action<WorkspaceLoadProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
        => await _workspaceDiscoveryReportService.LoadWorkspaceItemsAsync(includeRuntimeInspection, progress, cancellationToken);

    public IReadOnlyList<WorkspaceReference> LoadWorkspaceReferences()
        => _workspaceOrchestrator.LoadWorkspaceRecords()
            .Select(record => new WorkspaceReference(record.Name, record.RootPath))
            .ToList();

    public WorkspaceTimeline LoadTimeline(string timelinePath) => _timelineService.Load(timelinePath);

    public WorkspaceCheckpointIndex LoadCheckpointIndex(string checkpointIndexPath) => _checkpointService.LoadIndex(checkpointIndexPath);

    public Task<string> SuggestSavePointMessageAsync(string rootPath, CancellationToken cancellationToken = default)
        => _savePointMessageService.SuggestAsync(rootPath, cancellationToken);

    public Task<ExistingGitCheckoutPlan> InspectExistingGitCheckoutAsync(string repositoryPath, string workspaceName, CancellationToken cancellationToken = default)
        => _workspaceOrchestrator.InspectExistingGitCheckoutAsync(repositoryPath, workspaceName, cancellationToken);

    public async Task<GitBranchValidationResult> ValidateExistingGitCheckoutBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default)
    {
        var repositoryService = new GitRepositoryService(new ProcessRunner());
        return await repositoryService.ValidateBranchNameAsync(repositoryPath, branchName, cancellationToken);
    }

    public async Task<WorkspaceSnapshot> ImportExistingGitCheckoutAsync(ExistingGitCheckoutImportRequest request, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        Action<CommandLogEntry>? log = entry => logSink?.Append(new OperationTranscriptLine { Kind = MapLineKind(entry), Text = entry.Message });
        return await _workspaceOrchestrator.ImportExistingGitCheckoutAsync(request, log, cancellationToken);
    }

    public WorkspaceDefinition BuildWorkspaceDefinition(CreateWorkspaceDraft draft)
        => new()
        {
            Workspace = new WorkspaceMetadata
            {
                Name = draft.WorkspaceName,
                Id = WorkspacePathBuilder.Slugify(draft.WorkspaceName),
                Image = string.IsNullOrWhiteSpace(draft.Template.WorkspaceImage) ? "ubuntu:24.04" : draft.Template.WorkspaceImage,
            },
            Provider = new WorkspaceProviderDefinition { Type = "git" },
            Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = WorkspaceRuntimeDefinition.DefaultNodeMajorVersion },
            Features = draft.Template.Features.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Services = draft.Template.Services.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Skills = draft.Template.Skills.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Mcp = draft.Template.Mcp.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Agent = new AgentPreferences { Profile = AgentProfileResolver.BuiltInDefault.ProfileId },
            Terminal = new TerminalPreferences
            {
                InstallIfMissing = false,
                Font = new TerminalFontPreferences { Provider = "nerd-fonts", Family = "JetBrainsMono Nerd Font" },
                Prompt = new TerminalPromptPreferences { Provider = "starship" },
                Utilities = new TerminalUtilityPreferences(),
            },
        };

    public async Task<WorkspaceSnapshot> CreateWorkspaceAsync(string rootPath, WorkspaceDefinition definition, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        Action<CommandLogEntry>? log = entry => logSink?.Append(new OperationTranscriptLine { Kind = MapLineKind(entry), Text = entry.Message });
        return await _workspaceOrchestrator.CreateWorkspaceAsync(rootPath, definition, log, cancellationToken, includeRuntimeInspection: false);
    }

    public async Task<WorkspaceOperationResult> StartWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var transcript = CreateTranscript("Start", currentSnapshot?.Definition.Workspace.Name, rootPath, logSink, out var append, out var log);
        var snapshot = currentSnapshot;
        try
        {
            append(OperationTranscriptLineKind.Status, "Loading current workspace state...");
            snapshot ??= await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            append(OperationTranscriptLineKind.Comment, $"Selected workspace '{snapshot.Definition.Workspace.Name}'.");

            if (snapshot.UpdateRequired || snapshot.AppliedState is null)
            {
                append(OperationTranscriptLineKind.Status, "Preparing runtime...");
                await _workspaceOrchestrator.ProvisionAsync(snapshot, log, cancellationToken);
                snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
                await PersistWorkspaceRecordAsync(snapshot, "Start", "Provisioned and started workspace.", true, cancellationToken, DateTimeOffset.UtcNow);
            }
            else
            {
                if (snapshot.RuntimeState != WorkspaceRuntimeState.Running)
                {
                    append(OperationTranscriptLineKind.Status, "Starting services...");
                    await _workspaceOrchestrator.StartAsync(snapshot, log, cancellationToken);
                }

                snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
                await PersistWorkspaceRecordAsync(snapshot, "Start", "Started workspace.", true, cancellationToken);
            }

            append(OperationTranscriptLineKind.Result, "Completed.");
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = true;
            return new WorkspaceOperationResult { Snapshot = snapshot, Message = $"Workspace '{snapshot.Definition.Workspace.Name}' is running.", Transcript = transcript };
        }
        catch (Exception exception)
        {
            if (snapshot is not null)
            {
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, "Start");
            }

            AppendFailureTranscript(exception, append);
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = false;
            throw;
        }
    }

    public async Task<WorkspaceOperationResult> CreateSavePointAsync(string rootPath, string message, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var transcript = CreateTranscript("Create Save Point", currentSnapshot?.Definition.Workspace.Name, rootPath, logSink, out var append, out var log);
        var snapshot = currentSnapshot;
        try
        {
            append(OperationTranscriptLineKind.Status, "Loading current workspace state...");
            snapshot ??= await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            append(OperationTranscriptLineKind.Comment, $"Selected workspace '{snapshot.Definition.Workspace.Name}'.");
            append(OperationTranscriptLineKind.Status, "Creating Save Point...");
            var created = await _workspaceOrchestrator.CreateSavePointAsync(snapshot, message, log, cancellationToken);
            await PersistWorkspaceRecordAsync(snapshot, "Create Save Point", created ? "Created Save Point." : "Save Point skipped because there were no changes to capture.", true, cancellationToken);
            append(OperationTranscriptLineKind.Result, created ? "Completed." : "Skipped.");
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = true;
            return new WorkspaceOperationResult
            {
                Snapshot = snapshot,
                Message = created ? "Save Point created." : "Save Point skipped because there were no changes to capture.",
                Transcript = transcript,
            };
        }
        catch (Exception exception)
        {
            if (snapshot is not null)
            {
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, "Create Save Point");
            }

            AppendFailureTranscript(exception, append);
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = false;
            throw;
        }
    }

    public async Task<WorkspaceRecoveryAssessment> AssessWorkspaceRecoveryAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default)
    {
        var snapshot = currentSnapshot ?? await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
        var findings = new List<string>();
        if (snapshot.UpdateRequired || snapshot.AppliedState is null)
        {
            findings.Add("Generated runtime files are out of date and need repair.");
        }

        if (snapshot.LocalRuntimeState is null)
        {
            findings.Add("Local runtime state is missing and will be regenerated.");
        }

        if (snapshot.RuntimeState != WorkspaceRuntimeState.Running)
        {
            findings.Add($"Workspace runtime is currently {snapshot.RuntimeState}.");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Record.LastOperationResult) && snapshot.Record.LastOperationSucceeded == false)
        {
            findings.Add($"Last operation failed: {snapshot.Record.LastOperationResult}");
        }

        if (findings.Count == 0)
        {
            findings.Add("No blocking issues were detected, but recovery can still revalidate generated files and runtime state.");
        }

        return new WorkspaceRecoveryAssessment
        {
            Title = $"Recover {snapshot.Definition.Workspace.Name}",
            Summary = "Recovery validates generated files, repairs Docker compose state, and refreshes runtime readiness without deleting user work.",
            Findings = findings,
            ConfirmationMessage = "Run workspace recovery now?",
        };
    }

    public async Task<WorkspaceOperationResult> RecoverWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var transcript = CreateTranscript("Recover", currentSnapshot?.Definition.Workspace.Name, rootPath, logSink, out var append, out var log);
        var snapshot = currentSnapshot;
        try
        {
            append(OperationTranscriptLineKind.Status, "Loading current workspace state...");
            snapshot ??= await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            append(OperationTranscriptLineKind.Comment, $"Selected workspace '{snapshot.Definition.Workspace.Name}'.");
            append(OperationTranscriptLineKind.Status, "Recovering workspace runtime...");
            await _workspaceOrchestrator.RecoverAsync(snapshot, log, cancellationToken);
            snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
            await PersistWorkspaceRecordAsync(snapshot, "Recover", "Repaired workspace runtime and validated generated files.", true, cancellationToken);
            append(OperationTranscriptLineKind.Result, "Completed.");
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = true;
            return new WorkspaceOperationResult { Snapshot = snapshot, Message = $"Workspace '{snapshot.Definition.Workspace.Name}' runtime was repaired.", Transcript = transcript };
        }
        catch (Exception exception)
        {
            if (snapshot is not null)
            {
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, "Recover");
            }

            AppendFailureTranscript(exception, append);
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = false;
            throw;
        }
    }

    public async Task<WorkspaceOperationResult> AttachWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var transcript = CreateTranscript("Attach", currentSnapshot?.Definition.Workspace.Name, rootPath, logSink, out var append, out var log);
        var snapshot = currentSnapshot;
        try
        {
            append(OperationTranscriptLineKind.Status, "Preparing attach...");
            append(OperationTranscriptLineKind.Status, "Validating runtime...");
            snapshot ??= await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            await _workspaceOrchestrator.AttachAsync(snapshot, log, cancellationToken);
            snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
            await PersistWorkspaceRecordAsync(snapshot, "Attach", "Opened workspace attach session.", true, cancellationToken);
            append(OperationTranscriptLineKind.Status, "Launching terminal...");
            append(OperationTranscriptLineKind.Result, "Completed.");
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = true;
            return new WorkspaceOperationResult { Snapshot = snapshot, Message = $"Attach launched for '{snapshot.Definition.Workspace.Name}'.", Transcript = transcript };
        }
        catch (Exception exception)
        {
            if (snapshot is not null)
            {
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, "Attach");
            }

            AppendFailureTranscript(exception, append);
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = false;
            throw;
        }
    }

    public async Task<WorkspaceReprovisionResult> ReprovisionWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var snapshot = currentSnapshot;
        var wasRunning = snapshot?.RuntimeState == WorkspaceRuntimeState.Running;
        var transcript = new OperationTranscript
        {
            OperationName = "Reprovision",
            WorkspaceName = snapshot?.Definition.Workspace.Name ?? Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            StartedUtc = DateTimeOffset.UtcNow,
        };
        Action<OperationTranscriptLineKind, string> append = (kind, text) =>
        {
            var line = new OperationTranscriptLine { Kind = kind, Text = text };
            transcript.Lines.Add(line);
            logSink?.Append(line);
        };
        Action<CommandLogEntry>? log = entry => append(MapLineKind(entry), entry.Message);

        try
        {
            append(OperationTranscriptLineKind.Status, snapshot is null ? "Loading current workspace state..." : "Using current workspace state...");
            snapshot ??= await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            wasRunning = snapshot.RuntimeState == WorkspaceRuntimeState.Running;
            append(OperationTranscriptLineKind.Comment, $"Selected workspace '{snapshot.Definition.Workspace.Name}'.");
            append(OperationTranscriptLineKind.Comment, BuildReprovisionReason(snapshot));
            append(OperationTranscriptLineKind.Status, "Preparing workspace operation...");
            if (wasRunning == true)
            {
                append(OperationTranscriptLineKind.Status, "Stopping running workspace...");
                await _workspaceOrchestrator.StopAsync(snapshot, log, cancellationToken);
                snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            }

            append(OperationTranscriptLineKind.Status, "Starting Docker provisioning...");
            await _workspaceOrchestrator.ProvisionAsync(snapshot, log, cancellationToken);

            append(OperationTranscriptLineKind.Status, "Validating compose...");
            var validationSnapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            await _workspaceOrchestrator.RecoverAsync(validationSnapshot, log, cancellationToken);

            append(OperationTranscriptLineKind.Status, "Refreshing workspace snapshot...");
            var refreshed = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
            if (wasRunning == false)
            {
                await _workspaceOrchestrator.StopAsync(refreshed, log, cancellationToken);
                refreshed = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
            }

            await PersistWorkspaceRecordAsync(refreshed, "Reprovision", "Workspace reprovisioned successfully.", true, cancellationToken, DateTimeOffset.UtcNow);
            _timelineService.Append(refreshed.Paths.TimelinePath, "reprovision-succeeded", "Reprovisioned workspace", "Regenerated runtime files and refreshed workspace state.");
            refreshed = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
            append(OperationTranscriptLineKind.Result, "Completed");
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = true;

            return new WorkspaceReprovisionResult
            {
                Snapshot = refreshed,
                Succeeded = true,
                Message = "Workspace reprovisioned successfully.",
                Transcript = transcript,
            };
        }
        catch (Exception exception)
        {
            if (snapshot is not null)
            {
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken);
                _timelineService.Append(snapshot.Paths.TimelinePath, "reprovision-failed", "Reprovision failed", exception.Message);
            }
            AppendFailureTranscript(exception, append);
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = false;
            throw;
        }
    }

    public Task OpenPathAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            : OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open", $"\"{path}\"")
                : new ProcessStartInfo("xdg-open", $"\"{path}\"");

        startInfo.UseShellExecute = false;
        Process.Start(startInfo);
        return Task.CompletedTask;
    }

    private Task PersistWorkspaceRecordAsync(WorkspaceSnapshot snapshot, string operationName, string operationResult, bool succeeded, CancellationToken cancellationToken, DateTimeOffset? lastPreparedUtc = null)
    {
        var record = new WorkspaceRecord
        {
            Name = snapshot.Record.Name,
            RootPath = snapshot.Record.RootPath,
            RepositoryPath = snapshot.Record.RepositoryPath,
            ConfigurationPath = snapshot.Record.ConfigurationPath,
            SourceType = snapshot.Record.SourceType,
            ImportedFromExistingCheckout = snapshot.Record.ImportedFromExistingCheckout,
            OriginalDefaultBranch = snapshot.Record.OriginalDefaultBranch,
            SelectedWorkspaceBranch = snapshot.Record.SelectedWorkspaceBranch,
            RemoteOriginUrl = snapshot.Record.RemoteOriginUrl,
            CreatedUtc = snapshot.Record.CreatedUtc,
            LastOpenedUtc = snapshot.Record.LastOpenedUtc,
            LastPreparedUtc = lastPreparedUtc ?? snapshot.Record.LastPreparedUtc,
            OracleSoftwareNoticeShown = snapshot.Record.OracleSoftwareNoticeShown,
            LastOperationName = operationName,
            LastOperationResult = operationResult,
            LastOperationSucceeded = succeeded,
            LastOperationUtc = DateTimeOffset.UtcNow,
        };

        return _workspaceRepository.SaveAsync(record, cancellationToken);
    }

    private Task PersistWorkspaceRecordFailureAsync(WorkspaceRecord record, string errorMessage, CancellationToken cancellationToken, string operationName = "Reprovision")
    {
        var failureRecord = new WorkspaceRecord
        {
            Name = record.Name,
            RootPath = record.RootPath,
            RepositoryPath = record.RepositoryPath,
            ConfigurationPath = record.ConfigurationPath,
            SourceType = record.SourceType,
            ImportedFromExistingCheckout = record.ImportedFromExistingCheckout,
            OriginalDefaultBranch = record.OriginalDefaultBranch,
            SelectedWorkspaceBranch = record.SelectedWorkspaceBranch,
            RemoteOriginUrl = record.RemoteOriginUrl,
            CreatedUtc = record.CreatedUtc,
            LastOpenedUtc = record.LastOpenedUtc,
            LastPreparedUtc = record.LastPreparedUtc,
            OracleSoftwareNoticeShown = record.OracleSoftwareNoticeShown,
            LastOperationName = operationName,
            LastOperationResult = errorMessage,
            LastOperationSucceeded = false,
            LastOperationUtc = DateTimeOffset.UtcNow,
        };

        return _workspaceRepository.SaveAsync(failureRecord, cancellationToken);
    }

    private static OperationTranscriptLineKind MapLineKind(CommandLogEntry entry)
    {
        if (entry.Source.EndsWith(":cmd", StringComparison.OrdinalIgnoreCase))
        {
            return OperationTranscriptLineKind.Command;
        }

        if (entry.Source.EndsWith(":err", StringComparison.OrdinalIgnoreCase))
        {
            return OperationTranscriptLineKind.StandardError;
        }

        return entry.Source switch
        {
            "docker" or "git" => OperationTranscriptLineKind.StandardOutput,
            "app" or "attach" or "runtime" or "terminal" or "dev" => OperationTranscriptLineKind.Comment,
            _ => OperationTranscriptLineKind.StandardOutput,
        };
    }

    private static string BuildReprovisionReason(WorkspaceSnapshot snapshot)
    {
        if (snapshot.LocalRuntimeState is null)
        {
            return "Runtime state is missing. Reprovision will regenerate local runtime state.";
        }

        if (snapshot.UpdateRequired || snapshot.AppliedState is null)
        {
            return "Workspace files are out of date. Reprovision to regenerate runtime files.";
        }

        return "Manual reprovision requested.";
    }

    private static void AppendFailureTranscript(Exception exception, Action<OperationTranscriptLineKind, string> append)
    {
        append(OperationTranscriptLineKind.Result, "Failed");
        foreach (var line in exception.Message.Split([Environment.NewLine], StringSplitOptions.None))
        {
            if (line.StartsWith("Command:", StringComparison.OrdinalIgnoreCase))
            {
                append(OperationTranscriptLineKind.Command, line[8..].Trim());
            }
            else if (line.StartsWith("Exit code:", StringComparison.OrdinalIgnoreCase))
            {
                append(OperationTranscriptLineKind.Result, line.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                append(OperationTranscriptLineKind.StandardError, line);
            }
        }
    }

    private static OperationTranscript CreateTranscript(string operationName, string? workspaceName, string rootPath, IOperationLogSink? logSink, out Action<OperationTranscriptLineKind, string> append, out Action<CommandLogEntry>? log)
    {
        var transcript = new OperationTranscript
        {
            OperationName = operationName,
            WorkspaceName = workspaceName ?? Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            StartedUtc = DateTimeOffset.UtcNow,
        };

        Action<OperationTranscriptLineKind, string> appender = (kind, text) =>
        {
            var line = new OperationTranscriptLine { Kind = kind, Text = text };
            transcript.Lines.Add(line);
            logSink?.Append(line);
        };

        append = appender;
        log = entry => appender(MapLineKind(entry), entry.Message);
        return transcript;
    }
}
