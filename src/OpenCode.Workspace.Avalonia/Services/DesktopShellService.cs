using System.Diagnostics;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Platform.Windows;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class DesktopShellService : IDesktopShellService
{
    private readonly WorkspaceOrchestrator _workspaceOrchestrator;
    private readonly WorkspaceDiscoveryReportService _workspaceDiscoveryReportService;
    private readonly WorkspaceRepository _workspaceRepository;
    private readonly WorkspaceTimelineService _timelineService;
    private readonly WorkspaceCheckpointService _checkpointService;
    private readonly WorkspaceSavePointMessageService _savePointMessageService;
    private readonly WorkspaceBackupExportService _workspaceBackupExportService;
    private readonly WorkspaceBackupManifestService _workspaceBackupManifestService;
    private readonly WorkspacePublishAssessmentService _workspacePublishAssessmentService;
    private readonly WorkspaceRemovalService _workspaceRemovalService;
    private readonly OracleSoftwareNoticeService _oracleSoftwareNoticeService;
    private readonly WindowsTerminalProfileSetupService _windowsTerminalProfileSetupService;

    public DesktopShellService(
        WorkspaceOrchestrator workspaceOrchestrator,
        WorkspaceRepository workspaceRepository,
        WorkspaceTimelineService timelineService,
        WorkspaceCheckpointService checkpointService,
        WorkspaceSavePointMessageService savePointMessageService,
        WorkspaceBackupExportService workspaceBackupExportService,
        WorkspaceBackupManifestService workspaceBackupManifestService,
        WorkspacePublishAssessmentService workspacePublishAssessmentService,
        WorkspaceRemovalService workspaceRemovalService,
        OracleSoftwareNoticeService oracleSoftwareNoticeService,
        WindowsTerminalProfileSetupService windowsTerminalProfileSetupService)
    {
        _workspaceOrchestrator = workspaceOrchestrator;
        _workspaceDiscoveryReportService = new WorkspaceDiscoveryReportService(workspaceOrchestrator, workspaceRepository);
        _workspaceRepository = workspaceRepository;
        _timelineService = timelineService;
        _checkpointService = checkpointService;
        _savePointMessageService = savePointMessageService;
        _workspaceBackupExportService = workspaceBackupExportService;
        _workspaceBackupManifestService = workspaceBackupManifestService;
        _workspacePublishAssessmentService = workspacePublishAssessmentService;
        _workspaceRemovalService = workspaceRemovalService;
        _oracleSoftwareNoticeService = oracleSoftwareNoticeService;
        _windowsTerminalProfileSetupService = windowsTerminalProfileSetupService;
    }

    public async Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(bool includeRuntimeInspection, Action<WorkspaceLoadProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
        => await _workspaceDiscoveryReportService.LoadWorkspaceItemsAsync(includeRuntimeInspection, progress, cancellationToken);

    public IReadOnlyList<WorkspaceReference> LoadWorkspaceReferences()
        => _workspaceOrchestrator.LoadWorkspaceRecords()
            .Select(record => new WorkspaceReference(record.Name, record.RootPath))
            .ToList();

    public WorkspaceTimeline LoadTimeline(string timelinePath) => _timelineService.Load(timelinePath);

    public WorkspaceCheckpointIndex LoadCheckpointIndex(string checkpointIndexPath) => _checkpointService.LoadIndex(checkpointIndexPath);

    public OracleSoftwareNoticePrompt? BuildOracleSoftwareNotice(TemplateManifest template, string workspaceName)
        => _oracleSoftwareNoticeService.RequiresAcknowledgement(template)
            ? _oracleSoftwareNoticeService.BuildPrompt(template, workspaceName)
            : null;

    public OracleSoftwareNoticePrompt? BuildOracleSoftwareNotice(WorkspaceSnapshot snapshot)
        => _oracleSoftwareNoticeService.RequiresAcknowledgement(snapshot)
            ? _oracleSoftwareNoticeService.BuildPrompt(snapshot)
            : null;

    public async Task<WorkspaceSnapshot> AcknowledgeOracleSoftwareNoticeAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default)
    {
        var snapshot = currentSnapshot ?? await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
        var updatedRecord = _oracleSoftwareNoticeService.Acknowledge(snapshot.Record);
        return new WorkspaceSnapshot
        {
            Record = updatedRecord,
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = snapshot.RuntimeState,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = snapshot.AppliedState,
            LocalRuntimeState = snapshot.LocalRuntimeState,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = snapshot.UpdateRequired,
        };
    }

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

    public async Task<WorkspaceOperationResult> PrepareWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var transcript = CreateTranscript("Prepare", currentSnapshot?.Definition.Workspace.Name, rootPath, logSink, out var append, out var log);
        var snapshot = currentSnapshot;
        try
        {
            append(OperationTranscriptLineKind.Status, "Loading current workspace state...");
            snapshot ??= await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            append(OperationTranscriptLineKind.Comment, $"Selected workspace '{snapshot.Definition.Workspace.Name}'.");

            var wasRunning = snapshot.RuntimeState == WorkspaceRuntimeState.Running;
            var wasUpdated = false;
            var wasStarted = false;

            if (snapshot.UpdateRequired || snapshot.AppliedState is null)
            {
                if (wasRunning)
                {
                    append(OperationTranscriptLineKind.Status, "Stopping workspace before update...");
                    await _workspaceOrchestrator.StopAsync(snapshot, log, cancellationToken);
                    snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
                }

                append(OperationTranscriptLineKind.Status, "Preparing workspace runtime...");
                await _workspaceOrchestrator.ProvisionAsync(snapshot, log, cancellationToken);
                wasUpdated = true;
                snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
                wasStarted = snapshot.RuntimeState == WorkspaceRuntimeState.Running;
                await PersistWorkspaceRecordAsync(snapshot, "Prepare", "Prepared workspace runtime.", true, cancellationToken, DateTimeOffset.UtcNow);
            }
            else
            {
                if (snapshot.RuntimeState != WorkspaceRuntimeState.Running)
                {
                    append(OperationTranscriptLineKind.Status, "Starting workspace runtime...");
                    await _workspaceOrchestrator.StartAsync(snapshot, log, cancellationToken);
                    wasStarted = true;
                }

                snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
                await PersistWorkspaceRecordAsync(snapshot, "Prepare", wasStarted ? "Started workspace runtime." : "Workspace runtime already ready.", true, cancellationToken, wasUpdated ? DateTimeOffset.UtcNow : null);
            }

            append(OperationTranscriptLineKind.Result, "Completed.");
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = true;
            var message = wasUpdated
                ? $"Workspace '{snapshot.Definition.Workspace.Name}' was prepared and is ready to open."
                : wasStarted
                    ? $"Workspace '{snapshot.Definition.Workspace.Name}' is running and ready to open."
                    : $"Workspace '{snapshot.Definition.Workspace.Name}' is already ready to open.";
            return new WorkspaceOperationResult { Snapshot = snapshot, Message = message, Transcript = transcript };
        }
        catch (Exception exception)
        {
            if (snapshot is not null)
            {
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, "Prepare");
            }

            AppendFailureTranscript(exception, append);
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = false;
            throw;
        }
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

    public async Task<WorkspaceCheckpointOperationResult> CreateCheckpointAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var transcript = CreateTranscript("Create Checkpoint", currentSnapshot?.Definition.Workspace.Name, rootPath, logSink, out var append, out var log);
        var snapshot = currentSnapshot;
        try
        {
            append(OperationTranscriptLineKind.Status, "Loading current workspace state...");
            snapshot ??= await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            append(OperationTranscriptLineKind.Comment, $"Selected workspace '{snapshot.Definition.Workspace.Name}'.");
            append(OperationTranscriptLineKind.Status, "Creating checkpoint...");
            var checkpoint = await _workspaceOrchestrator.CreateCheckpointAsync(snapshot, log, cancellationToken);
            snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            await PersistWorkspaceRecordAsync(snapshot, "Create Checkpoint", $"Created checkpoint '{checkpoint.Id}'.", true, cancellationToken);
            append(OperationTranscriptLineKind.Result, "Completed.");
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = true;
            return new WorkspaceCheckpointOperationResult
            {
                Snapshot = snapshot,
                Message = $"Checkpoint '{checkpoint.Id}' created.",
                Transcript = transcript,
                Checkpoint = checkpoint,
            };
        }
        catch (Exception exception)
        {
            if (snapshot is not null)
            {
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, "Create Checkpoint");
            }

            AppendFailureTranscript(exception, append);
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = false;
            throw;
        }
    }

    public async Task<WorkspaceRemovalOperationResult> RemoveWorkspaceAsync(string rootPath, WorkspaceRemovalChoice choice, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var record = _workspaceRepository.LoadAll().FirstOrDefault(item => string.Equals(item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
        var workspaceName = currentSnapshot?.Definition.Workspace.Name
            ?? currentSnapshot?.Record.Name
            ?? record?.Name
            ?? Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var transcript = CreateTranscript("Remove", workspaceName, rootPath, logSink, out var append, out _);

        append(OperationTranscriptLineKind.Status, "Preparing removal...");
        append(OperationTranscriptLineKind.Comment, $"Selected workspace '{workspaceName}'.");
        if (choice is WorkspaceRemovalChoice.DockerResources or WorkspaceRemovalChoice.DeleteFiles)
        {
            append(OperationTranscriptLineKind.Status, "Removing Docker resources...");
            if (currentSnapshot is null)
            {
                currentSnapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            }

            await _workspaceOrchestrator.RemoveDockerResourcesAsync(currentSnapshot, cancellationToken: cancellationToken);
        }

        if (choice == WorkspaceRemovalChoice.DeleteFiles)
        {
            append(OperationTranscriptLineKind.Status, "Repairing file permissions before deletion...");
            currentSnapshot ??= await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            await _workspaceOrchestrator.RepairWorkspaceFilePermissionsAsync(currentSnapshot, cancellationToken: cancellationToken);
            append(OperationTranscriptLineKind.Status, "Deleting workspace files...");
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }

        append(OperationTranscriptLineKind.Status, "Removing workspace from list...");

        var removal = await _workspaceRemovalService.RemoveAsync(new WorkspaceRemovalRequest
        {
            WorkspaceName = workspaceName,
            WorkspaceRoot = rootPath,
            DeleteWorkspaceFiles = choice == WorkspaceRemovalChoice.DeleteFiles,
        }, cancellationToken);

        foreach (var warning in removal.Warnings)
        {
            append(OperationTranscriptLineKind.Comment, warning);
        }

        if (!removal.Succeeded)
        {
            append(OperationTranscriptLineKind.StandardError, removal.FailureReason);
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = false;
            throw new InvalidOperationException(removal.FailureReason);
        }

        append(OperationTranscriptLineKind.Result, "Completed.");
        transcript.CompletedUtc = DateTimeOffset.UtcNow;
        transcript.Succeeded = true;
        return new WorkspaceRemovalOperationResult
        {
            Message = choice switch
            {
                WorkspaceRemovalChoice.DeleteFiles => $"Deleted workspace '{removal.WorkspaceName}' and removed it from the workspace list.",
                WorkspaceRemovalChoice.DockerResources => $"Removed Docker resources for '{removal.WorkspaceName}' and unregistered it from the workspace list.",
                _ => $"Removed '{removal.WorkspaceName}' from the workspace list.",
            },
            Transcript = transcript,
            Removal = removal,
        };
    }

    public async Task<WindowsTerminalProfileOperationResult> EnsureWindowsTerminalProfileAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default)
    {
        var snapshot = currentSnapshot ?? await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
        var setup = await _windowsTerminalProfileSetupService.EnsureAsync(snapshot.Definition, cancellationToken);
        return new WindowsTerminalProfileOperationResult
        {
            Message = setup.Summary,
            Setup = setup,
        };
    }

    public async Task<WorkspacePublishAssessment> AssessWorkspacePublishAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var snapshot = currentSnapshot ?? await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
        Action<CommandLogEntry>? log = entry => logSink?.Append(new OperationTranscriptLine { Kind = MapLineKind(entry), Text = entry.Message });
        return await _workspacePublishAssessmentService.AssessAsync(snapshot, log, cancellationToken);
    }

    public async Task<WorkspacePublishResult> PublishWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var transcript = CreateTranscript("Publish", currentSnapshot?.Definition.Workspace.Name, rootPath, logSink, out var append, out var log);
        var snapshot = currentSnapshot;
        try
        {
            append(OperationTranscriptLineKind.Status, "Loading current workspace state...");
            snapshot ??= await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            append(OperationTranscriptLineKind.Comment, $"Selected workspace '{snapshot.Definition.Workspace.Name}'.");
            append(OperationTranscriptLineKind.Status, "Publishing Working Copy...");
            var review = await _workspaceOrchestrator.PublishAsync(snapshot, log, cancellationToken);
            snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);

            if (review.IsBlocked)
            {
                append(OperationTranscriptLineKind.StandardError, review.Message);
                transcript.CompletedUtc = DateTimeOffset.UtcNow;
                transcript.Succeeded = false;
                throw new InvalidOperationException(review.Message);
            }

            await PersistWorkspaceRecordAsync(snapshot, "Publish", review.Message, true, cancellationToken);
            append(OperationTranscriptLineKind.Result, "Completed.");
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = true;
            return new WorkspacePublishResult
            {
                Snapshot = snapshot,
                Message = review.Message,
                Transcript = transcript,
                Review = review,
            };
        }
        catch (Exception exception)
        {
            if (snapshot is not null)
            {
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, "Publish");
            }

            AppendFailureTranscript(exception, append);
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = false;
            throw;
        }
    }

    public async Task<WorkspaceBackupResult> BackupWorkspaceAsync(string rootPath, string archivePath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var transcript = CreateTranscript("Backup", currentSnapshot?.Definition.Workspace.Name, rootPath, logSink, out var append, out _);
        var snapshot = currentSnapshot;
        try
        {
            append(OperationTranscriptLineKind.Status, "Loading current workspace state...");
            snapshot ??= await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            append(OperationTranscriptLineKind.Comment, $"Selected workspace '{snapshot.Definition.Workspace.Name}'.");
            append(OperationTranscriptLineKind.Status, "Applying backup export rules...");
            var export = await _workspaceBackupExportService.ExportAsync(snapshot, archivePath, logSink, cancellationToken);
            append(OperationTranscriptLineKind.Status, "Writing backup manifest...");
            var manifest = _workspaceBackupManifestService.WriteAndEmbedManifest(snapshot, export, archivePath, DateTimeOffset.UtcNow);
            var message = $"Backup created at '{export.ArchivePath}' with {export.FileCount} file(s).";
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = true;
            return new WorkspaceBackupResult
            {
                Snapshot = snapshot,
                Message = message,
                Transcript = transcript,
                Export = export,
                Manifest = manifest,
            };
        }
        catch (Exception exception)
        {
            AppendFailureTranscript(exception, append);
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = false;
            throw;
        }
    }

    public async Task<WorkspaceOperationResult> CreateSavePointAsync(string rootPath, string message, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        StartupLog.WriteGlobal($"DesktopShellService.CreateSavePointAsync called for '{rootPath}'. Message length: {message.Length}.");
        var transcript = CreateTranscript("Create Save Point", currentSnapshot?.Definition.Workspace.Name, rootPath, logSink, out var append, out var log);
        var snapshot = currentSnapshot;
        try
        {
            append(OperationTranscriptLineKind.Status, "Loading current workspace state...");
            StartupLog.WriteGlobal("DesktopShellService.CreateSavePointAsync loading workspace snapshot.");
            snapshot ??= await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            append(OperationTranscriptLineKind.Comment, $"Selected workspace '{snapshot.Definition.Workspace.Name}'.");
            append(OperationTranscriptLineKind.Status, "Creating Save Point...");
            StartupLog.WriteGlobal("DesktopShellService.CreateSavePointAsync invoking WorkspaceOrchestrator.CreateSavePointAsync.");
            var created = await _workspaceOrchestrator.CreateSavePointAsync(snapshot, message, log, cancellationToken);
            StartupLog.WriteGlobal($"DesktopShellService.CreateSavePointAsync orchestrator returned. Created: {created}.");
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
            StartupLog.WriteGlobalException("DesktopShellService.CreateSavePointAsync failed", exception);
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
