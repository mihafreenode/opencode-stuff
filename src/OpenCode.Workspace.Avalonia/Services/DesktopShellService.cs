using System.Diagnostics;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Platform.Windows;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class DesktopShellService : IDesktopShellService
{
    private static readonly TimeSpan OpenWorkspaceLoadTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OpenWorkspaceStartTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan OpenWorkspaceProvisionTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan OpenWorkspaceAttachTimeout = TimeSpan.FromMinutes(2);

    private const string DeleteWorkspaceFilesUnavailableMessage = "Delete workspace files is not available in this version. Use File Explorer or terminal after creating a backup.";

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
    private readonly WorkspaceLaunchPlanResolver _workspaceLaunchPlanResolver;

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
        _workspaceLaunchPlanResolver = new WorkspaceLaunchPlanResolver();
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

    public async Task<WorkspaceOperationResult> OpenWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var transcript = CreateTranscript("Open Workspace", currentSnapshot?.Definition.Workspace.Name, rootPath, logSink, out var append, out var log);
        var snapshot = currentSnapshot;

        try
        {
            append(OperationTranscriptLineKind.Status, "Checking workspace...");
            for (var phaseIndex = 0; phaseIndex < 4; phaseIndex++)
            {
                snapshot = await LoadOpenWorkspaceSnapshotAsync(rootPath, snapshot, append, log, cancellationToken);
                var plan = _workspaceLaunchPlanResolver.Resolve(snapshot);
                LogOpenContext(log, snapshot, plan, phaseIndex);

                if (plan.NeedsRecover)
                {
                    throw new InvalidOperationException("Runtime files need repair. Run Recover Workspace.");
                }

                if (plan.TerminalUnavailable)
                {
                    throw new InvalidOperationException("Terminal launch is unavailable. Run Diagnostics.");
                }

                if (plan.NeedsProvision)
                {
                    await RunOpenPhaseAsync(
                        snapshot,
                        append,
                        log,
                        "Provisioning runtime...",
                        OpenWorkspaceProvisionTimeout,
                        token => _workspaceOrchestrator.ProvisionAsync(snapshot, log, token),
                        cancellationToken);
                    snapshot = await ReloadSnapshotAfterOpenPhaseAsync(rootPath, snapshot, append, log, cancellationToken);
                    await EnsureOpenRuntimeArtifactsReadyAsync(snapshot, append, log, cancellationToken, reportStatus: true);
                    continue;
                }

                if (plan.NeedsStart)
                {
                    await RunOpenPhaseAsync(
                        snapshot,
                        append,
                        log,
                        "Starting containers...",
                        OpenWorkspaceStartTimeout,
                        token => _workspaceOrchestrator.StartAsync(snapshot, log, token),
                        cancellationToken);
                    snapshot = await ReloadSnapshotAfterOpenPhaseAsync(rootPath, snapshot, append, log, cancellationToken);
                    await EnsureOpenRuntimeArtifactsReadyAsync(snapshot, append, log, cancellationToken, reportStatus: true);
                    continue;
                }

                if (plan.NeedsDiagnostics)
                {
                    throw new InvalidOperationException("Workspace runtime could not be validated. Run Diagnostics.");
                }

                if (!plan.CanAttach)
                {
                    throw new InvalidOperationException("Workspace runtime could not be validated. Run Diagnostics.");
                }

                await EnsureOpenRuntimeArtifactsReadyAsync(snapshot, append, log, cancellationToken, reportStatus: false);
                await RunOpenPhaseAsync(
                    snapshot,
                    append,
                    log,
                    "Opening terminal...",
                    OpenWorkspaceAttachTimeout,
                    token => _workspaceOrchestrator.LaunchAttachForRunningWorkspaceAsync(snapshot, log, token),
                    cancellationToken);
                snapshot = await LoadSnapshotWithTimingAsync(rootPath, append, log, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false, OpenWorkspaceLoadTimeout);
                await PersistWorkspaceRecordAsync(snapshot, "Open Workspace", "Opened workspace terminal session.", true, cancellationToken);
                append(OperationTranscriptLineKind.Result, "Ready.");
                transcript.CompletedUtc = DateTimeOffset.UtcNow;
                transcript.Succeeded = true;
                return new WorkspaceOperationResult { Snapshot = snapshot, Message = $"Workspace '{snapshot.Definition.Workspace.Name}' is open.", Transcript = transcript };
            }

            throw new InvalidOperationException("Workspace open did not reach a terminal-ready state. Run Recover Workspace.");
        }
        catch (Exception exception)
        {
            if (snapshot is not null)
            {
                log?.Invoke(new CommandLogEntry { Source = "app", Message = exception.ToString() });
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, "Open Workspace");
            }

            AppendFailureTranscript(exception, append);
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = false;
            throw;
        }
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
        var record = _workspaceRepository.LoadAll().FirstOrDefault(item => string.Equals(item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(WorkspaceRecordPathResolver.GetWorkspaceRoot(item), rootPath, StringComparison.OrdinalIgnoreCase));
        var workspaceName = currentSnapshot?.Definition.Workspace.Name
            ?? currentSnapshot?.Record.Name
            ?? record?.Name
            ?? Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var transcript = CreateTranscript("Remove", workspaceName, rootPath, logSink, out var append, out _);
        var snapshot = currentSnapshot;

        try
        {
            if (choice == WorkspaceRemovalChoice.DeleteFiles)
            {
                throw new InvalidOperationException(DeleteWorkspaceFilesUnavailableMessage);
            }

            append(OperationTranscriptLineKind.Status, "Preparing removal...");
            append(OperationTranscriptLineKind.Comment, $"Selected workspace '{workspaceName}' at '{rootPath}'.");

            var configurationPath = snapshot?.Paths.WorkspaceYamlPath
                ?? (record is null ? Path.Combine(rootPath, "workspace.yaml") : WorkspaceRecordPathResolver.GetWorkspaceConfigurationPath(record));

            if (!File.Exists(configurationPath))
            {
                throw new InvalidOperationException($"Workspace '{workspaceName}' configuration file was not found before removal could start. Probed path: '{configurationPath}'.");
            }

            if (choice == WorkspaceRemovalChoice.DockerResources)
            {
                append(OperationTranscriptLineKind.Status, "Removing Docker resources...");
                if (snapshot is null)
                {
                    snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
                }

                await _workspaceOrchestrator.RemoveDockerResourcesAsync(snapshot, cancellationToken: cancellationToken);
            }

            append(OperationTranscriptLineKind.Status, "Removing workspace from list...");

            var removal = await _workspaceRemovalService.RemoveAsync(new WorkspaceRemovalRequest
            {
                WorkspaceName = workspaceName,
                WorkspaceRoot = rootPath,
                DeleteWorkspaceFiles = false,
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
                    WorkspaceRemovalChoice.DockerResources => $"Removed Docker resources for '{removal.WorkspaceName}' and unregistered it from the workspace list.",
                    _ => $"Removed '{removal.WorkspaceName}' from the workspace list.",
                },
                Transcript = transcript,
                Removal = removal,
            };
        }
        catch (Exception exception)
        {
            if (snapshot?.Record is not null)
            {
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, "Remove");
            }
            else if (record is not null)
            {
                await PersistWorkspaceRecordFailureAsync(record, exception.Message, cancellationToken, "Remove");
            }

            AppendFailureTranscript(exception, append);
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = false;
            throw;
        }
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
        var currentProblems = new List<string>();
        var previousFailureContext = new List<string>();
        if (snapshot.UpdateRequired || snapshot.AppliedState is null)
        {
            findings.Add("Generated runtime files are out of date and need repair.");
            currentProblems.Add("Runtime files need repair");
        }

        if (snapshot.LocalRuntimeState is null)
        {
            findings.Add("Local runtime state is missing and will be regenerated.");
            currentProblems.Add("Runtime metadata is missing");
        }

        if (snapshot.RuntimeState != WorkspaceRuntimeState.Running)
        {
            findings.Add($"Workspace runtime is currently {snapshot.RuntimeState}.");
            currentProblems.Add($"Workspace is currently {snapshot.RuntimeState.ToString().ToLowerInvariant()}");
        }

        if (snapshot.RuntimeState == WorkspaceRuntimeState.Unknown)
        {
            currentProblems.Add("Docker availability could not be confirmed");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Record.LastOperationResult) && snapshot.Record.LastOperationSucceeded == false)
        {
            findings.Add($"Last operation failed: {snapshot.Record.LastOperationResult}");
            foreach (var problem in BuildPreviousFailureContext(snapshot.Record.LastOperationResult!))
            {
                previousFailureContext.Add(problem);
            }
        }

        if (findings.Count == 0)
        {
            findings.Add("No blocking issues were detected, but recovery can still revalidate generated files and runtime state.");
        }

        if (currentProblems.Count == 0)
        {
            currentProblems.Add("No live blocking issues detected");
        }

        var manualActionSummary = BuildRecoveryManualActionSummary(snapshot.Record.LastOperationResult);
        var manualActions = BuildRecoveryManualActions(snapshot.Record.LastOperationResult);

        return new WorkspaceRecoveryAssessment
        {
            Title = "Recover Workspace",
            Summary = "Recovery validates generated files, repairs Docker compose state, and refreshes runtime readiness without deleting user work.",
            Findings = findings,
            ConfirmationMessage = "Run workspace recovery now?",
            WorkspaceName = snapshot.Definition.Workspace.Name,
            StatusSummary = BuildRecoveryStatusSummary(snapshot),
            RecoverActions =
            [
                "Regenerate runtime files",
                "Refresh Docker Compose state",
                "Rebuild runtime metadata",
                "Validate generated scripts",
                "Keep your project files",
            ],
            CurrentProblems = currentProblems.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            PreviousFailureContext = previousFailureContext.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            WillNotChange =
            [
                "Delete project files",
                "Modify Git history",
                "Delete documents",
                "Remove untracked work",
            ],
            ManualActionSummary = manualActionSummary,
            ManualActions = manualActions,
            AdvancedDetails = BuildRecoveryAdvancedDetails(snapshot, findings),
            LastCheckedAt = DateTimeOffset.Now,
        };
    }

    private static string BuildRecoveryStatusSummary(WorkspaceSnapshot snapshot)
    {
        var lastFailure = snapshot.Record.LastOperationSucceeded == false ? snapshot.Record.LastOperationResult : null;
        return lastFailure?.Contains("could not start", StringComparison.OrdinalIgnoreCase) == true
            ? "Workspace could not start"
            : "Workspace needs repair";
    }

    private static IReadOnlyList<string> BuildPreviousFailureContext(string failureText)
    {
        var items = new List<string>();
        foreach (var line in failureText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Contains("already in use", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(line);
                continue;
            }

            if (!line.StartsWith("Command:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("Exit code:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("Likely causes:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("Suggested actions:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("Host port details:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("This workspace docker compose ps:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("Running containers:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("- ", StringComparison.Ordinal))
            {
                items.Add(line);
            }
        }

        return items;
    }

    private static string BuildRecoveryManualActionSummary(string? failureText)
    {
        if (string.IsNullOrWhiteSpace(failureText))
        {
            return string.Empty;
        }

        return failureText.Contains("already in use", StringComparison.OrdinalIgnoreCase)
            ? BuildPreviousFailureContext(failureText).FirstOrDefault() ?? string.Empty
            : string.Empty;
    }

    private static IReadOnlyList<string> BuildRecoveryManualActions(string? failureText)
    {
        if (string.IsNullOrWhiteSpace(failureText) || !failureText.Contains("already in use", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        var actions = new List<string> { "Stop the other workspace" };
        if (failureText.Contains("1521", StringComparison.OrdinalIgnoreCase))
        {
            actions.Add("Change the Oracle port");
        }

        return actions;
    }

    private static string BuildRecoveryAdvancedDetails(WorkspaceSnapshot snapshot, IReadOnlyList<string> findings)
        => string.Join(
            Environment.NewLine,
            new[]
            {
                $"Workspace root: {snapshot.Paths.RootPath}",
                $"Compose path: {snapshot.Paths.ComposePath}",
                $"Runtime-state path: {snapshot.Paths.RuntimeStatePath}",
                $"Applied-state path: {snapshot.Paths.AppliedStatePath}",
                $"Attach script path: {snapshot.Paths.AttachWrapperScriptPath}",
                "Primary service: workspace",
                $"Runtime target: {snapshot.ResolvedRuntimePlan?.TargetPlatform ?? snapshot.LocalRuntimeState?.ResolvedPlatform ?? "Unavailable"}",
                $"Runtime state: {snapshot.RuntimeState}",
                $"Update required: {snapshot.UpdateRequired}",
                string.Empty,
                "Diagnostics:",
            }.Concat(findings));

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
            if (snapshot.LocalRuntimeState is null)
            {
                append(OperationTranscriptLineKind.Status, "Regenerating local runtime state...");
                await _workspaceOrchestrator.EnsureRuntimeStateCurrentAsync(snapshot, log, cancellationToken);
                snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
            }

            if (snapshot.LocalRuntimeState is null)
            {
                throw new InvalidOperationException($"Workspace recovery did not regenerate all required managed runtime files.{Environment.NewLine}Missing:{Environment.NewLine}- {snapshot.Paths.RuntimeStatePath}");
            }

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
            snapshot ??= await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false);
            var plan = _workspaceLaunchPlanResolver.Resolve(snapshot);
            LogOpenContext(log, snapshot, plan);

            if (plan.NeedsRecover)
            {
                throw new InvalidOperationException("Runtime files need repair. Run Recover Workspace.");
            }

            if (plan.NeedsDiagnostics)
            {
                throw new InvalidOperationException("Workspace runtime could not be validated. Run Diagnostics.");
            }

            if (snapshot.RuntimeState != WorkspaceRuntimeState.Running)
            {
                throw new InvalidOperationException("Workspace is not running. Start it first.");
            }

            append(OperationTranscriptLineKind.Status, "Opening terminal...");
            await _workspaceOrchestrator.LaunchAttachForRunningWorkspaceAsync(snapshot, log, cancellationToken);
            snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
            await PersistWorkspaceRecordAsync(snapshot, "Attach", "Opened workspace attach session.", true, cancellationToken);
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

    private static void LogOpenContext(Action<CommandLogEntry>? log, WorkspaceSnapshot snapshot, WorkspaceLaunchPlan plan)
        => LogOpenContext(log, snapshot, plan, null);

    private static void LogOpenContext(Action<CommandLogEntry>? log, WorkspaceSnapshot snapshot, WorkspaceLaunchPlan plan, int? phaseIndex)
    {
        var prefix = phaseIndex is null
            ? $"[open:{snapshot.Definition.Workspace.Name}]"
            : $"[open:{snapshot.Definition.Workspace.Name}:phase-{phaseIndex.Value + 1}]";
        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{prefix} Workspace root: {snapshot.Paths.RootPath}" });
        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{prefix} Compose file: {snapshot.Paths.ComposePath}" });
        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{prefix} Runtime-state path: {snapshot.Paths.RuntimeStatePath}" });
        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{prefix} Applied-state path: {snapshot.Paths.AppliedStatePath}" });
        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{prefix} Attach script path: {snapshot.Paths.AttachWrapperScriptPath}" });
        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{prefix} Runtime target: {snapshot.ResolvedRuntimePlan?.TargetPlatform ?? snapshot.LocalRuntimeState?.ResolvedPlatform ?? "Unavailable"}" });
        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{prefix} Template id: unavailable" });
        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{prefix} Selected service: {plan.PrimaryServiceName}" });
        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{prefix} Launch plan: provision={plan.NeedsProvision}, start={plan.NeedsStart}, attach={plan.CanAttach}, recover={plan.NeedsRecover}, diagnostics={plan.NeedsDiagnostics}, terminalUnavailable={plan.TerminalUnavailable}" });
    }

    private async Task<WorkspaceSnapshot> LoadOpenWorkspaceSnapshotAsync(string rootPath, WorkspaceSnapshot? currentSnapshot, Action<OperationTranscriptLineKind, string> append, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
        => currentSnapshot ?? await LoadSnapshotWithTimingAsync(rootPath, append, log, cancellationToken, includeRuntimeInspection: false, includeSessionInspection: false, OpenWorkspaceLoadTimeout);

    private async Task<WorkspaceSnapshot> ReloadSnapshotAfterOpenPhaseAsync(string rootPath, WorkspaceSnapshot snapshot, Action<OperationTranscriptLineKind, string> append, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
        => await LoadSnapshotWithTimingAsync(rootPath, append, log, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false, OpenWorkspaceLoadTimeout);

    private async Task<WorkspaceSnapshot> LoadSnapshotWithTimingAsync(string rootPath, Action<OperationTranscriptLineKind, string> append, Action<CommandLogEntry>? log, CancellationToken cancellationToken, bool includeRuntimeInspection, bool includeSessionInspection, TimeSpan timeout)
    {
        var startedAt = DateTimeOffset.UtcNow;
        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"[open:{Path.GetFileName(rootPath)}] Loading workspace snapshot. includeRuntimeInspection={includeRuntimeInspection} includeSessionInspection={includeSessionInspection}" });
        var snapshot = await RunOpenTimedAsync(
            cancellationToken,
            timeout,
            token => _workspaceOrchestrator.LoadSnapshotAsync(rootPath, token, includeRuntimeInspection: includeRuntimeInspection, includeSessionInspection: includeSessionInspection),
            $"Workspace open snapshot load timed out after {timeout.TotalMinutes:F0} minutes.");
        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"[open:{snapshot.Definition.Workspace.Name}] Workspace snapshot loaded in {(DateTimeOffset.UtcNow - startedAt).TotalSeconds:F1}s." });
        return snapshot;
    }

    private async Task EnsureOpenRuntimeArtifactsReadyAsync(WorkspaceSnapshot snapshot, Action<OperationTranscriptLineKind, string> append, Action<CommandLogEntry>? log, CancellationToken cancellationToken, bool reportStatus)
    {
        if (reportStatus)
        {
            append(OperationTranscriptLineKind.Status, "Writing runtime state...");
        }

        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"[open:{snapshot.Definition.Workspace.Name}] Verifying runtime-state and applied-state after runtime phase." });

        if (!File.Exists(snapshot.Paths.RuntimeStatePath))
        {
            await RunOpenTimedAsync(
                cancellationToken,
                OpenWorkspaceLoadTimeout,
                token => _workspaceOrchestrator.EnsureRuntimeStateCurrentAsync(snapshot, log, token),
                "Workspace runtime-state update timed out.");
        }

        if (!File.Exists(snapshot.Paths.RuntimeStatePath) || !File.Exists(snapshot.Paths.AppliedStatePath))
        {
            throw new InvalidOperationException("Runtime files need repair. Run Recover Workspace.");
        }

        if (!File.Exists(snapshot.Paths.AttachWrapperScriptPath) || !File.Exists(snapshot.Paths.ComposePath))
        {
            throw new InvalidOperationException("Runtime files need repair. Run Recover Workspace.");
        }

        if (snapshot.RuntimeState != WorkspaceRuntimeState.Running)
        {
            throw new InvalidOperationException("Workspace runtime could not be validated. Run Diagnostics.");
        }
    }

    private async Task RunOpenPhaseAsync(WorkspaceSnapshot snapshot, Action<OperationTranscriptLineKind, string> append, Action<CommandLogEntry>? log, string statusText, TimeSpan timeout, Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        append(OperationTranscriptLineKind.Status, statusText);
        var startedAt = DateTimeOffset.UtcNow;
        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"[open:{snapshot.Definition.Workspace.Name}] Starting phase '{statusText}' with timeout {timeout}." });
        await RunOpenTimedAsync(cancellationToken, timeout, operation, $"Open Workspace phase '{statusText}' timed out.");
        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"[open:{snapshot.Definition.Workspace.Name}] Completed phase '{statusText}' in {(DateTimeOffset.UtcNow - startedAt).TotalSeconds:F1}s." });
    }

    private static async Task RunOpenTimedAsync(CancellationToken cancellationToken, TimeSpan timeout, Func<CancellationToken, Task> operation, string timeoutMessage)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await operation(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(timeoutMessage);
        }
    }

    private static async Task<T> RunOpenTimedAsync<T>(CancellationToken cancellationToken, TimeSpan timeout, Func<CancellationToken, Task<T>> operation, string timeoutMessage)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            return await operation(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(timeoutMessage);
        }
    }
}
