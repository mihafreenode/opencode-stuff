using System.Diagnostics;
using System.Text;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Platform.Windows;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class DesktopShellService : IDesktopShellService
{
    private const string OpenWorkspaceTerminalReadinessFailureMessage = "Open Workspace could not finish preparing the terminal. Troubleshoot Workspace can inspect the runtime files and launch readiness.";
    private const string TerminalLaunchReadinessFailureMessage = "Terminal launch readiness failed. Troubleshoot Workspace can inspect attach scripts and runtime state.";

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
    private readonly WorkspaceRuntimeExplorerService _workspaceRuntimeExplorerService;

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
        _workspaceRuntimeExplorerService = new WorkspaceRuntimeExplorerService(workspaceRepository, new WorkspaceRuntimeStateService(), new WorkspaceYamlService(), timelineService, new ProcessRunner());
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

    public WorkspaceRuntimeResetPrompt BuildRuntimeResetPrompt(WorkspaceSnapshot snapshot)
        => new()
        {
            WorkspaceName = snapshot.Definition.Workspace.Name,
            WorkspaceRoot = snapshot.Paths.RootPath,
            Summary = "Reset recreates managed runtime resources for this workspace while keeping your workspace files and downloads.",
            Removes =
            [
                "Managed containers for this workspace",
                "Managed Docker volumes for this workspace",
                "Generated runtime state",
            ],
            Keeps =
            [
                "Workspace files",
                "Git history",
                "Documentation",
                "Downloads/cache",
                "workspace.yaml",
            ],
            ConfirmationMessage = "Reset runtime and continue?",
        };

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

    public async Task<WorkspaceSnapshot> RefreshVolatileWorkspaceStateAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        Action<CommandLogEntry>? log = entry => logSink?.Append(new OperationTranscriptLine { Kind = MapLineKind(entry), Text = entry.Message });
        var snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
        var volatileFailureWasCurrent = HasCurrentVolatileFailure(snapshot.Record);
        var volatileEnvironmentFailure = await _workspaceOrchestrator.RevalidateVolatileEnvironmentAsync(snapshot, log, cancellationToken);

        if (volatileEnvironmentFailure is not null)
        {
            var refreshedHealth = BuildVolatileFailureHealth(snapshot, volatileEnvironmentFailure, DateTimeOffset.UtcNow);
            await PersistWorkspaceRecordFailureAsync(snapshot.Record, volatileEnvironmentFailure.StandardError, cancellationToken, "Health Recheck", refreshedHealth);
            throw new WorkspaceProvisioningException(refreshedHealth, volatileEnvironmentFailure.StandardError);
        }

        if (!volatileFailureWasCurrent)
        {
            return snapshot;
        }

        var updatedRecord = CloneRecord(
            snapshot.Record,
            operationName: "Health Recheck",
            operationResult: BuildVolatileSuccessMessage(snapshot.Record),
            succeeded: true,
            lastPreparedUtc: snapshot.Record.LastPreparedUtc,
            provisioningHealth: null);
        await _workspaceRepository.SaveAsync(updatedRecord, cancellationToken);
        return CloneSnapshot(snapshot, updatedRecord);
    }

    public async Task<WorkspaceOperationResult> OpenWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var transcript = CreateTranscript("Open Workspace", currentSnapshot?.Definition.Workspace.Name, rootPath, logSink, out var append, out var log);
        var snapshot = currentSnapshot;
        var repairBaseline = currentSnapshot?.Record.LastProvisioningHealth;
        var repairSnapshotBefore = currentSnapshot;
        var automaticRepairAttempted = false;

        try
        {
            append(OperationTranscriptLineKind.Status, "Checking workspace...");
            snapshot = await RefreshVolatileWorkspaceStateAsync(rootPath, snapshot, logSink, cancellationToken);
            repairBaseline ??= snapshot.Record.LastProvisioningHealth;
            repairSnapshotBefore ??= snapshot;
            for (var phaseIndex = 0; phaseIndex < 4; phaseIndex++)
            {
                snapshot = await LoadOpenWorkspaceSnapshotAsync(rootPath, snapshot, append, log, cancellationToken);
                var plan = _workspaceLaunchPlanResolver.Resolve(snapshot);
                LogOpenContext(log, snapshot, plan, phaseIndex);

                if (plan.NeedsRecover)
                {
                    if (automaticRepairAttempted)
                    {
                        throw new InvalidOperationException(OpenWorkspaceTerminalReadinessFailureMessage);
                    }

                    automaticRepairAttempted = true;
                    await RunOpenPhaseAsync(
                        snapshot,
                        append,
                        log,
                        "Repairing runtime...",
                        OpenWorkspaceProvisionTimeout,
                        token => _workspaceOrchestrator.RecoverAsync(snapshot, log, token),
                        cancellationToken);
                    snapshot = await ReloadSnapshotAfterOpenPhaseAsync(rootPath, snapshot, append, log, cancellationToken);
                    if (snapshot.LocalRuntimeState is null)
                    {
                        append(OperationTranscriptLineKind.Status, "Regenerating runtime state...");
                        await _workspaceOrchestrator.EnsureRuntimeStateCurrentAsync(snapshot, log, cancellationToken);
                        snapshot = await ReloadSnapshotAfterOpenPhaseAsync(rootPath, snapshot, append, log, cancellationToken);
                    }

                    await EnsureOpenRuntimeArtifactsReadyAsync(snapshot, append, log, cancellationToken, reportStatus: true);
                    continue;
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

            throw new InvalidOperationException(OpenWorkspaceTerminalReadinessFailureMessage);
        }
        catch (Exception exception)
        {
            if (snapshot is not null)
            {
                log?.Invoke(new CommandLogEntry { Source = "app", Message = exception.ToString() });
                WorkspaceProvisioningHealthRecord? provisioningHealth = null;
                if (IsTerminalLaunchReadinessProblem(exception.Message))
                {
                    var completedUtc = DateTimeOffset.UtcNow;
                    var terminalReadinessHealth = BuildTerminalLaunchReadinessHealth(snapshot, completedUtc, exception.Message);
                    provisioningHealth = automaticRepairAttempted
                        ? WorkspaceTroubleshootingEngine.RecordRepairAttempt(
                            repairBaseline,
                            "Recover Workspace",
                            transcript.StartedUtc,
                            completedUtc,
                            repairSnapshotBefore,
                            snapshot,
                            terminalReadinessHealth,
                            WorkspaceRepairOutcome.RepairNoEffect)
                        : terminalReadinessHealth;
                }

                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, "Open Workspace", provisioningHealth);
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
            snapshot = await RefreshVolatileWorkspaceStateAsync(rootPath, snapshot, logSink, cancellationToken);
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

    public async Task<WorkspaceOperationResult> StopWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var transcript = CreateTranscript("Stop", currentSnapshot?.Definition.Workspace.Name, rootPath, logSink, out var append, out var log);
        var snapshot = currentSnapshot;
        try
        {
            append(OperationTranscriptLineKind.Status, "Loading current workspace state...");
            snapshot ??= await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
            append(OperationTranscriptLineKind.Comment, $"Selected workspace '{snapshot.Definition.Workspace.Name}'.");
            append(OperationTranscriptLineKind.Status, "Stopping runtime...");
            await _workspaceOrchestrator.StopAsync(snapshot, log, cancellationToken);
            snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
            await PersistWorkspaceRecordAsync(snapshot, "Stop", "Stopped workspace runtime.", true, cancellationToken);
            append(OperationTranscriptLineKind.Result, "Completed.");
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = true;
            return new WorkspaceOperationResult { Snapshot = snapshot, Message = $"Workspace '{snapshot.Definition.Workspace.Name}' was stopped.", Transcript = transcript };
        }
        catch (Exception exception)
        {
            if (snapshot is not null)
            {
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, "Stop");
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
        var snapshot = await RefreshVolatileWorkspaceStateAsync(rootPath, currentSnapshot, null, cancellationToken);
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
        var repairBaseline = currentSnapshot?.Record.LastProvisioningHealth;
        var repairSnapshotBefore = currentSnapshot;
        try
        {
            append(OperationTranscriptLineKind.Status, "Loading current workspace state...");
            snapshot = await RefreshVolatileWorkspaceStateAsync(rootPath, snapshot, logSink, cancellationToken);
            repairBaseline ??= snapshot.Record.LastProvisioningHealth;
            repairSnapshotBefore ??= snapshot;
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

            var completedUtc = DateTimeOffset.UtcNow;
            var recoverHealth = WorkspaceTroubleshootingEngine.RecordRepairAttempt(
                repairBaseline,
                "Recover Workspace",
                transcript.StartedUtc,
                completedUtc,
                repairSnapshotBefore,
                snapshot,
                BuildSuccessfulProvisioningHealth(snapshot, transcript.StartedUtc, completedUtc));
            await PersistWorkspaceRecordAsync(snapshot, "Recover", "Repaired workspace runtime and validated generated files.", true, cancellationToken, provisioningHealth: recoverHealth);
            _timelineService.Append(snapshot.Paths.TimelinePath, "recover-succeeded", "Recovered workspace", BuildProvisioningTimelineDetails(recoverHealth));
            AppendRepairOutcomeTranscript(append, recoverHealth);
            append(OperationTranscriptLineKind.Result, "Completed.");
            transcript.CompletedUtc = completedUtc;
            transcript.Succeeded = true;
            return new WorkspaceOperationResult { Snapshot = snapshot, Message = $"Workspace '{snapshot.Definition.Workspace.Name}' runtime was repaired.", Transcript = transcript };
        }
        catch (Exception exception)
        {
            if (snapshot is not null)
            {
                var completedUtc = DateTimeOffset.UtcNow;
                var provisioningHealth = WorkspaceTroubleshootingEngine.RecordRepairAttempt(
                    repairBaseline,
                    "Recover Workspace",
                    transcript.StartedUtc,
                    completedUtc,
                    repairSnapshotBefore,
                    snapshot,
                    ExtractProvisioningHealth(exception, snapshot.Record.LastProvisioningHealth) ?? BuildFallbackFailureHealth(snapshot, completedUtc, exception.Message),
                    WorkspaceRepairOutcome.RepairFailed);
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, "Recover", provisioningHealth);
                _timelineService.Append(snapshot.Paths.TimelinePath, "recover-failed", "Recover failed", BuildProvisioningTimelineDetails(provisioningHealth, exception.Message));
                AppendRepairOutcomeTranscript(append, provisioningHealth);
            }

            AppendFailureTranscript(exception, append);
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = false;
            throw;
        }
    }

    public async Task<WorkspaceOperationResult> ResetRuntimeAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var transcript = CreateTranscript("Reset Runtime", currentSnapshot?.Definition.Workspace.Name, rootPath, logSink, out var append, out var log);
        var snapshot = currentSnapshot;
        var repairBaseline = currentSnapshot?.Record.LastProvisioningHealth;
        var repairSnapshotBefore = currentSnapshot;

        try
        {
            append(OperationTranscriptLineKind.Status, "Loading current workspace state...");
            snapshot = await RefreshVolatileWorkspaceStateAsync(rootPath, snapshot, logSink, cancellationToken);
            repairBaseline ??= snapshot.Record.LastProvisioningHealth;
            repairSnapshotBefore ??= snapshot;
            append(OperationTranscriptLineKind.Status, "Resetting runtime...");
            await _workspaceOrchestrator.ResetRuntimeAsync(snapshot, log, cancellationToken);
            append(OperationTranscriptLineKind.Status, "Reprovisioning runtime...");
            await _workspaceOrchestrator.ProvisionAsync(snapshot, log, cancellationToken);
            snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
            var completedUtc = DateTimeOffset.UtcNow;
            var health = WorkspaceTroubleshootingEngine.RecordRepairAttempt(
                repairBaseline,
                "Reset Runtime",
                transcript.StartedUtc,
                completedUtc,
                repairSnapshotBefore,
                snapshot,
                BuildCleanupProvisioningHealth(snapshot, transcript.StartedUtc, completedUtc));
            await PersistWorkspaceRecordAsync(snapshot, "Reset Runtime", "Managed runtime was reset and reprovisioned.", true, cancellationToken, DateTimeOffset.UtcNow, health);
            _timelineService.Append(snapshot.Paths.TimelinePath, "runtime-reset-succeeded", "Reset runtime", BuildProvisioningTimelineDetails(health));
            _timelineService.Append(snapshot.Paths.TimelinePath, "runtime-reset", "Reset runtime", "Removed and recreated managed runtime resources.");
            AppendRepairOutcomeTranscript(append, health);
            append(OperationTranscriptLineKind.Result, "Completed.");
            transcript.CompletedUtc = completedUtc;
            transcript.Succeeded = true;
            return new WorkspaceOperationResult { Snapshot = snapshot, Message = $"Runtime for '{snapshot.Definition.Workspace.Name}' was reset.", Transcript = transcript };
        }
        catch (Exception exception)
        {
            if (snapshot is not null)
            {
                var completedUtc = DateTimeOffset.UtcNow;
                var provisioningHealth = WorkspaceTroubleshootingEngine.RecordRepairAttempt(
                    repairBaseline,
                    "Reset Runtime",
                    transcript.StartedUtc,
                    completedUtc,
                    repairSnapshotBefore,
                    snapshot,
                    ExtractProvisioningHealth(exception, snapshot.Record.LastProvisioningHealth) ?? BuildFallbackFailureHealth(snapshot, completedUtc, exception.Message),
                    WorkspaceRepairOutcome.RepairFailed);
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, "Reset Runtime", provisioningHealth);
                _timelineService.Append(snapshot.Paths.TimelinePath, "runtime-reset-failed", "Reset runtime failed", BuildProvisioningTimelineDetails(provisioningHealth, exception.Message));
                AppendRepairOutcomeTranscript(append, provisioningHealth);
            }

            AppendFailureTranscript(exception, append);
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = false;
            throw;
        }
    }

    public async Task<WorkspaceOperationResult> ReleaseRuntimeResourcesAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        var transcript = CreateTranscript("Release Resources", currentSnapshot?.Definition.Workspace.Name, rootPath, logSink, out var append, out var log);
        var snapshot = currentSnapshot;
        try
        {
            append(OperationTranscriptLineKind.Status, "Loading current workspace state...");
            snapshot ??= await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
            append(OperationTranscriptLineKind.Comment, $"Selected workspace '{snapshot.Definition.Workspace.Name}'.");
            append(OperationTranscriptLineKind.Status, "Releasing managed runtime resources...");
            await _workspaceOrchestrator.RemoveDockerResourcesAsync(snapshot, log, cancellationToken);
            snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
            await PersistWorkspaceRecordAsync(snapshot, "Release Resources", "Released managed Docker resources for this workspace.", true, cancellationToken);
            _timelineService.Append(snapshot.Paths.TimelinePath, "resource-release", "Released runtime resources", "Released managed Docker resources for the workspace runtime.");
            append(OperationTranscriptLineKind.Result, "Completed.");
            transcript.CompletedUtc = DateTimeOffset.UtcNow;
            transcript.Succeeded = true;
            return new WorkspaceOperationResult { Snapshot = snapshot, Message = $"Released managed runtime resources for '{snapshot.Definition.Workspace.Name}'.", Transcript = transcript };
        }
        catch (Exception exception)
        {
            if (snapshot is not null)
            {
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, "Release Resources");
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
            snapshot = await RefreshVolatileWorkspaceStateAsync(rootPath, snapshot, logSink, cancellationToken);
            var plan = _workspaceLaunchPlanResolver.Resolve(snapshot);
            LogOpenContext(log, snapshot, plan);

            if (plan.NeedsRecover)
            {
                throw new InvalidOperationException(TerminalLaunchReadinessFailureMessage);
            }

            if (plan.NeedsDiagnostics)
            {
                throw new InvalidOperationException(TerminalLaunchReadinessFailureMessage);
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
        var repairBaseline = currentSnapshot?.Record.LastProvisioningHealth;
        var repairSnapshotBefore = currentSnapshot;
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
            repairBaseline ??= snapshot.Record.LastProvisioningHealth;
            repairSnapshotBefore ??= snapshot;
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

            var completedUtc = DateTimeOffset.UtcNow;
            var reprovisionHealth = WorkspaceTroubleshootingEngine.RecordRepairAttempt(
                repairBaseline,
                "Reprovision",
                transcript.StartedUtc,
                completedUtc,
                repairSnapshotBefore,
                refreshed,
                BuildSuccessfulProvisioningHealth(refreshed, transcript.StartedUtc, completedUtc));
            await PersistWorkspaceRecordAsync(refreshed, "Reprovision", "Workspace reprovisioned successfully.", true, cancellationToken, DateTimeOffset.UtcNow, reprovisionHealth);
            _timelineService.Append(refreshed.Paths.TimelinePath, "reprovision-succeeded", "Reprovisioned workspace", BuildProvisioningTimelineDetails(reprovisionHealth));
            refreshed = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: false);
            AppendRepairOutcomeTranscript(append, reprovisionHealth);
            append(OperationTranscriptLineKind.Result, "Completed");
            transcript.CompletedUtc = completedUtc;
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
                var completedUtc = DateTimeOffset.UtcNow;
                var provisioningHealth = WorkspaceTroubleshootingEngine.RecordRepairAttempt(
                    repairBaseline,
                    "Reprovision",
                    transcript.StartedUtc,
                    completedUtc,
                    repairSnapshotBefore,
                    snapshot,
                    ExtractProvisioningHealth(exception, snapshot.Record.LastProvisioningHealth) ?? BuildFallbackFailureHealth(snapshot, completedUtc, exception.Message),
                    WorkspaceRepairOutcome.RepairFailed);
                await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken, provisioningHealth: provisioningHealth);
                _timelineService.Append(snapshot.Paths.TimelinePath, "reprovision-failed", "Reprovision failed", BuildProvisioningTimelineDetails(provisioningHealth, exception.Message));
                AppendRepairOutcomeTranscript(append, provisioningHealth);
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

    public Task<WorkspaceRuntimeExplorerReport> GetRuntimeResourceExplorerAsync(CancellationToken cancellationToken = default)
        => _workspaceRuntimeExplorerService.BuildAsync(cancellationToken);

    public Task<WorkspaceRuntimeInspectResult> InspectRuntimeResourceAsync(WorkspaceRuntimeResourceEntry resource, CancellationToken cancellationToken = default)
        => _workspaceRuntimeExplorerService.InspectResourceAsync(resource, cancellationToken);

    public async Task<RuntimeResourceCleanupResult> CleanOrphanedRuntimeResourcesAsync(CancellationToken cancellationToken = default)
    {
        var transcript = CreateTranscript("Clean Orphaned Resources", string.Empty, string.Empty, null, out var append, out _);
        append(OperationTranscriptLineKind.Status, "Scanning for orphaned runtime resources...");
        await _workspaceRuntimeExplorerService.CleanOrphanedResourcesAsync(cancellationToken);
        foreach (var record in _workspaceRepository.LoadAll())
        {
            var paths = WorkspacePathBuilder.Build(record.RootPath, record.ConfigurationPath);
            if (File.Exists(paths.TimelinePath))
            {
                _timelineService.Append(paths.TimelinePath, "orphan-cleaned", "Cleaned orphaned resources", "Removed orphaned managed Docker resources from the host runtime.");
            }
        }

        append(OperationTranscriptLineKind.Result, "Completed.");
        transcript.CompletedUtc = DateTimeOffset.UtcNow;
        transcript.Succeeded = true;
        return new RuntimeResourceCleanupResult { Message = "Cleaned orphaned runtime resources.", Transcript = transcript };
    }

    public async Task<WorkspaceTroubleshootingReport> GetWorkspaceTroubleshootingReportAsync(WorkspaceTroubleshootingRequest request, CancellationToken cancellationToken = default)
    {
        var context = await BuildWorkspaceTroubleshootingContextAsync(request, cancellationToken);
        return BuildWorkspaceTroubleshootingReport(request, context);
    }

    public async Task<WorkspaceTroubleshootingReport> ExecuteWorkspaceTroubleshootingActionAsync(WorkspaceTroubleshootingRequest request, string actionId, CancellationToken cancellationToken = default)
    {
        var context = await BuildWorkspaceTroubleshootingContextAsync(request, cancellationToken);
        var execution = WorkspaceTroubleshootingEngine.ExecuteInvestigation(context, actionId);
        var updatedRecord = CloneRecord(context.Snapshot.Record, context.Snapshot.Record.LastOperationName ?? "Troubleshoot Workspace", context.Snapshot.Record.LastOperationResult ?? context.Snapshot.Record.LastProvisioningHealth?.Summary ?? "Workspace troubleshooting updated.", context.Snapshot.Record.LastOperationSucceeded ?? false, context.Snapshot.Record.LastPreparedUtc, execution.UpdatedHealth);
        await _workspaceRepository.SaveAsync(updatedRecord, cancellationToken);
        var updatedSnapshot = CloneSnapshot(context.Snapshot, updatedRecord);
        var updatedRequest = new WorkspaceTroubleshootingRequest
        {
            RootPath = request.RootPath,
            Snapshot = updatedSnapshot,
            WorkspaceName = string.IsNullOrWhiteSpace(request.WorkspaceName) ? updatedSnapshot.Definition.Workspace.Name : request.WorkspaceName,
            IsOperationInProgress = request.IsOperationInProgress,
            CurrentOperationName = request.CurrentOperationName,
            CurrentStatusMessage = request.CurrentStatusMessage,
            TranscriptFilePath = request.TranscriptFilePath,
        };
        var updatedContext = await BuildWorkspaceTroubleshootingContextAsync(updatedRequest, cancellationToken);
        return BuildWorkspaceTroubleshootingReport(updatedRequest, updatedContext);
    }

    private Task PersistWorkspaceRecordAsync(WorkspaceSnapshot snapshot, string operationName, string operationResult, bool succeeded, CancellationToken cancellationToken, DateTimeOffset? lastPreparedUtc = null, WorkspaceProvisioningHealthRecord? provisioningHealth = null)
    {
        var record = CloneRecord(
            snapshot.Record,
            operationName,
            operationResult,
            succeeded,
            lastPreparedUtc ?? snapshot.Record.LastPreparedUtc,
            provisioningHealth ?? snapshot.Record.LastProvisioningHealth);

        return _workspaceRepository.SaveAsync(record, cancellationToken);
    }

    private Task PersistWorkspaceRecordFailureAsync(WorkspaceRecord record, string errorMessage, CancellationToken cancellationToken, string operationName = "Reprovision", WorkspaceProvisioningHealthRecord? provisioningHealth = null)
    {
        var failureRecord = CloneRecord(record, operationName, errorMessage, false, record.LastPreparedUtc, provisioningHealth ?? record.LastProvisioningHealth);

        return _workspaceRepository.SaveAsync(failureRecord, cancellationToken);
    }

    private async Task<WorkspaceTroubleshootingContext> BuildWorkspaceTroubleshootingContextAsync(WorkspaceTroubleshootingRequest request, CancellationToken cancellationToken)
    {
        var snapshot = request.Snapshot;
        if (snapshot is null)
        {
            snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(request.RootPath, cancellationToken, includeRuntimeInspection: true, includeSessionInspection: true);
        }

        var volatileValidation = await _workspaceOrchestrator.RevalidateVolatileEnvironmentAsync(snapshot, cancellationToken: cancellationToken);
        var timeline = _timelineService.Load(snapshot.Paths.TimelinePath);
        var transcriptFilePath = ResolveTroubleshootingTranscriptPath(request, snapshot);
        var terminalReadinessChecks = await InspectTerminalReadinessAsync(snapshot, cancellationToken);
        var lastAttachFailureReason = ResolveLastAttachFailureReason(snapshot.Record, ReadTranscriptExcerpt(transcriptFilePath));
        var transcriptExcerpt = ReadTranscriptExcerpt(transcriptFilePath);

        return new WorkspaceTroubleshootingContext
        {
            Snapshot = snapshot,
            Health = snapshot.Record.LastProvisioningHealth,
            IsProvisioningInProgress = request.IsOperationInProgress,
            CurrentOperationName = request.CurrentOperationName,
            CurrentStatusMessage = request.CurrentStatusMessage,
            TranscriptFilePath = transcriptFilePath,
            TranscriptExcerpt = transcriptExcerpt,
            VolatileValidation = volatileValidation,
            LastTimelineEvent = timeline.Events.OrderByDescending(item => item.OccurredUtc).FirstOrDefault(),
            TerminalReadinessChecks = terminalReadinessChecks,
            LastAttachFailureReason = lastAttachFailureReason,
        };
    }

    private WorkspaceTroubleshootingReport BuildWorkspaceTroubleshootingReport(WorkspaceTroubleshootingRequest request, WorkspaceTroubleshootingContext context)
    {
        var snapshot = context.Snapshot;
        var health = context.Health;
        var facts = new List<WorkspaceTroubleshootingFact>();
        var isOracleWorkspace = IsOracleWorkspace(snapshot, health);
        var hostProblem = IsHostProblem(health);
        var runtimeStateMissing = !File.Exists(snapshot.Paths.RuntimeStatePath);
        var appliedStateMissing = !File.Exists(snapshot.Paths.AppliedStatePath);
        var attachScriptMissing = !File.Exists(snapshot.Paths.AttachWrapperScriptPath);
        var stage = context.IsProvisioningInProgress
            ? context.CurrentStatusMessage
            : health?.Stage ?? snapshot.Record.LastOperationName ?? "Unknown";

        facts.Add(new WorkspaceTroubleshootingFact { Label = "Current operation", Value = context.IsProvisioningInProgress ? context.CurrentOperationName : snapshot.Record.LastOperationName ?? "None" });
        facts.Add(new WorkspaceTroubleshootingFact { Label = "Current stage", Value = string.IsNullOrWhiteSpace(stage) ? "Unknown" : stage });
        facts.Add(new WorkspaceTroubleshootingFact { Label = "Provisioning state", Value = context.IsProvisioningInProgress ? "Provisioning is still running." : snapshot.Record.LastOperationSucceeded == false ? "Last provisioning attempt exited with an error." : "No active provisioning detected." });
        facts.Add(new WorkspaceTroubleshootingFact { Label = "Runtime state", Value = snapshot.RuntimeState.ToString() });
        facts.Add(new WorkspaceTroubleshootingFact { Label = "Runtime-state.yaml", Value = DescribeFileState(snapshot.Paths.RuntimeStatePath) });
        facts.Add(new WorkspaceTroubleshootingFact { Label = "Applied-state.yaml", Value = DescribeFileState(snapshot.Paths.AppliedStatePath) });
        facts.Add(new WorkspaceTroubleshootingFact { Label = "Attach script", Value = DescribeFileState(snapshot.Paths.AttachWrapperScriptPath) });
        facts.Add(new WorkspaceTroubleshootingFact { Label = "Workspace shell script", Value = DescribeFileState(snapshot.Paths.OpencodeWorkspaceShellPath) });
        facts.Add(new WorkspaceTroubleshootingFact { Label = "Attach diagnostics log", Value = DescribeFileState(snapshot.Paths.AttachDiagnosticsLogPath) });
        facts.Add(new WorkspaceTroubleshootingFact { Label = "Provision script", Value = DescribeFileState(snapshot.Paths.ProvisionScriptPath) });

        foreach (var check in context.TerminalReadinessChecks)
        {
            facts.Add(new WorkspaceTroubleshootingFact { Label = check.Label, Value = check.Value });
        }

        if (!string.IsNullOrWhiteSpace(context.LastAttachFailureReason))
        {
            facts.Add(new WorkspaceTroubleshootingFact { Label = "Last attach failure", Value = context.LastAttachFailureReason });
        }

        if (health is not null)
        {
            if (!string.IsNullOrWhiteSpace(health.Reason))
            {
                facts.Add(new WorkspaceTroubleshootingFact { Label = "Last failure reason", Value = health.Reason });
            }

            if (!string.IsNullOrWhiteSpace(health.Evidence))
            {
                facts.Add(new WorkspaceTroubleshootingFact { Label = "Last failure evidence", Value = health.Evidence });
            }

            if (!string.IsNullOrWhiteSpace(health.Repairability))
            {
                facts.Add(new WorkspaceTroubleshootingFact { Label = "Repairability", Value = health.Repairability });
            }

            if (!string.IsNullOrWhiteSpace(health.OracleVersion) || !string.IsNullOrWhiteSpace(health.ApexVersion) || !string.IsNullOrWhiteSpace(health.OrdsVersion))
            {
                facts.Add(new WorkspaceTroubleshootingFact { Label = "Oracle provider", Value = BuildOracleProviderSummary(health) });
            }
        }

        if (context.VolatileValidation is not null)
        {
            facts.Add(new WorkspaceTroubleshootingFact { Label = "Docker validation", Value = BuildProcessSummary(context.VolatileValidation) });
            var validationEvidence = ExtractValidationEvidence(context.VolatileValidation);
            if (!string.IsNullOrWhiteSpace(validationEvidence))
            {
                facts.Add(new WorkspaceTroubleshootingFact { Label = "Compose and container evidence", Value = validationEvidence });
            }
        }

        if (context.LastTimelineEvent is not null)
        {
            facts.Add(new WorkspaceTroubleshootingFact { Label = "Last timeline event", Value = $"{context.LastTimelineEvent.Summary} at {context.LastTimelineEvent.OccurredUtc:O}" });
        }

        if (snapshot.Health.Services.Count > 0)
        {
            foreach (var service in snapshot.Health.Services)
            {
                facts.Add(new WorkspaceTroubleshootingFact { Label = $"Service: {service.Name}", Value = service.StatusLabel });
                if (service.Evidence.Count > 0)
                {
                    facts.Add(new WorkspaceTroubleshootingFact { Label = $"{service.Name} evidence", Value = string.Join("; ", service.Evidence.Select(item => $"{item.Label}: {item.Value}")) });
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(context.TranscriptFilePath))
        {
            facts.Add(new WorkspaceTroubleshootingFact { Label = "Transcript file", Value = context.TranscriptFilePath });
        }

        var terminalReadinessProblem = IsTerminalLaunchReadinessProblem(health?.Reason ?? snapshot.Record.LastOperationResult ?? string.Empty)
            || context.TerminalReadinessChecks.Any(item => item.Label is "Docker exec" or "Workspace shell script in container" && item.Value.StartsWith("Failed", StringComparison.OrdinalIgnoreCase));
        var suggestedNextSteps = BuildSuggestedNextSteps(request, health, runtimeStateMissing, appliedStateMissing, hostProblem, isOracleWorkspace, terminalReadinessProblem);
        var canResetRuntime = string.Equals(health?.Repairability, WorkspaceRepairability.CleanupRepair.ToString(), StringComparison.Ordinal);
        var headline = BuildTroubleshootingHeadline(request, health, hostProblem, runtimeStateMissing, appliedStateMissing, terminalReadinessProblem);
        var summary = BuildTroubleshootingSummary(request, health, isOracleWorkspace, hostProblem, runtimeStateMissing, appliedStateMissing, attachScriptMissing, terminalReadinessProblem);
        var serviceRecommendation = snapshot.Health.Services.FirstOrDefault(item => item.Status is WorkspaceHealthStatus.Degraded or WorkspaceHealthStatus.Unavailable or WorkspaceHealthStatus.Attention)?.Recommendation;
        var recommendation = string.IsNullOrWhiteSpace(serviceRecommendation)
            ? BuildTroubleshootingRecommendation(request, health, hostProblem, canResetRuntime, runtimeStateMissing, isOracleWorkspace, terminalReadinessProblem)
            : serviceRecommendation;
        var investigations = WorkspaceTroubleshootingEngine.GetAvailableInvestigations(context)
            .Select(item => new WorkspaceTroubleshootingAction
            {
                Id = item.Id,
                Label = item.Title,
                Description = item.Description,
                EstimatedDuration = item.EstimatedDuration,
                ProviderName = item.ProviderName,
            })
            .ToList();

        return new WorkspaceTroubleshootingReport
        {
            WorkspaceName = string.IsNullOrWhiteSpace(request.WorkspaceName) ? snapshot.Definition.Workspace.Name : request.WorkspaceName,
            RootPath = request.RootPath,
            Headline = headline,
            Summary = summary,
            Recommendation = recommendation,
            CurrentDiagnosis = string.IsNullOrWhiteSpace(health?.Reason) ? headline : health.Reason,
            CurrentEvidence = string.IsNullOrWhiteSpace(health?.Evidence) ? context.TranscriptExcerpt : health.Evidence,
            Confidence = string.IsNullOrWhiteSpace(health?.Confidence) ? "MEDIUM" : health.Confidence,
            RecommendedNextStep = ResolveRecommendedNextStepLabel(recommendation),
            RecommendedNextStepDescription = BuildRecommendedNextStepDescription(recommendation, health, request.IsOperationInProgress),
            RecommendedNextStepDuration = string.IsNullOrWhiteSpace(health?.EstimatedDuration) ? "10-30 seconds" : health.EstimatedDuration,
            Facts = facts,
            SuggestedNextSteps = suggestedNextSteps,
            Services = snapshot.Health.Services.Select(service => new WorkspaceTroubleshootingServiceEntry
            {
                Name = service.Name,
                Status = service.StatusLabel,
                Summary = service.Summary,
                Applications = string.Join(Environment.NewLine, service.Applications),
                PrimaryUrl = service.PrimaryUrl,
                Highlights = string.Join(Environment.NewLine, service.Highlights.Select(item => $"{item.Label}: {item.Value}")),
                Details = string.Join(Environment.NewLine, service.Evidence.Select(item => $"{item.Label}: {item.Value}")),
                ActionLabel = service.ActionLabel,
                OpenUrl = service.OpenUrl,
            }).ToList(),
            InvestigationActions = investigations,
            RepairHistory = BuildRepairHistory(health),
            InvestigationHistory = BuildInvestigationHistory(health),
            IsProvisioningInProgress = request.IsOperationInProgress,
            RecommendHostDiagnostics = hostProblem,
            CanKeepWaiting = request.IsOperationInProgress,
            CanViewLog = !string.IsNullOrWhiteSpace(context.TranscriptFilePath),
            CanOpenWorkspace = !request.IsOperationInProgress,
            CanRecoverWorkspace = !request.IsOperationInProgress,
            CanResetRuntime = canResetRuntime,
            TranscriptFilePath = context.TranscriptFilePath,
            TranscriptExcerpt = context.TranscriptExcerpt,
        };
    }

    private static WorkspaceRecord CloneRecord(WorkspaceRecord source, string operationName, string operationResult, bool succeeded, DateTimeOffset? lastPreparedUtc, WorkspaceProvisioningHealthRecord? provisioningHealth)
        => new()
        {
            Name = source.Name,
            RootPath = source.RootPath,
            RepositoryPath = source.RepositoryPath,
            ConfigurationPath = source.ConfigurationPath,
            SourceType = source.SourceType,
            ImportedFromExistingCheckout = source.ImportedFromExistingCheckout,
            OriginalDefaultBranch = source.OriginalDefaultBranch,
            SelectedWorkspaceBranch = source.SelectedWorkspaceBranch,
            RemoteOriginUrl = source.RemoteOriginUrl,
            CreatedUtc = source.CreatedUtc,
            LastOpenedUtc = source.LastOpenedUtc,
            LastPreparedUtc = lastPreparedUtc ?? source.LastPreparedUtc,
            OracleSoftwareNoticeShown = source.OracleSoftwareNoticeShown,
            LastOperationName = operationName,
            LastOperationResult = operationResult,
            LastOperationSucceeded = succeeded,
            LastOperationUtc = DateTimeOffset.UtcNow,
            LastProvisioningHealth = provisioningHealth,
        };

    private static string ResolveTroubleshootingTranscriptPath(WorkspaceTroubleshootingRequest request, WorkspaceSnapshot snapshot)
        => !string.IsNullOrWhiteSpace(request.TranscriptFilePath) && File.Exists(request.TranscriptFilePath)
            ? request.TranscriptFilePath
            : File.Exists(snapshot.Paths.AttachDiagnosticsLogPath)
                ? snapshot.Paths.AttachDiagnosticsLogPath
                : string.Empty;

    private async Task<IReadOnlyList<WorkspaceTroubleshootingCheck>> InspectTerminalReadinessAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken)
    {
        var checks = new List<WorkspaceTroubleshootingCheck>
        {
            new() { Label = "Workspace container", Value = snapshot.RuntimeState == WorkspaceRuntimeState.Running ? "Running" : snapshot.RuntimeState.ToString() },
            new() { Label = "Windows Terminal launch readiness", Value = _workspaceLaunchPlanResolver.Resolve(snapshot).TerminalUnavailable ? "Launch plan reports terminal unavailable." : "Launch plan allows terminal handoff." },
        };

        if (snapshot.RuntimeState != WorkspaceRuntimeState.Running)
        {
            checks.Add(new WorkspaceTroubleshootingCheck { Label = "Docker exec", Value = "Skipped: workspace container is not running." });
            checks.Add(new WorkspaceTroubleshootingCheck { Label = "Workspace shell script in container", Value = "Skipped: workspace container is not running." });
            return checks;
        }

        var containerName = DockerService.GetWorkspaceContainerName(snapshot.Definition);
        var processRunner = new ProcessRunner();
        var dockerExec = await processRunner.RunAsync("docker", ["exec", containerName, "sh", "-lc", "true"], cancellationToken: cancellationToken);
        checks.Add(new WorkspaceTroubleshootingCheck
        {
            Label = "Docker exec",
            Value = dockerExec.IsSuccess ? "Docker exec to workspace container works." : $"Failed: {FirstFailureLine(dockerExec)}",
        });

        var shellScript = await processRunner.RunAsync("docker", ["exec", containerName, "sh", "-lc", "test -f /opt/opencode-workspace/config/opencode-workspace-shell.sh && echo present"], cancellationToken: cancellationToken);
        checks.Add(new WorkspaceTroubleshootingCheck
        {
            Label = "Workspace shell script in container",
            Value = shellScript.IsSuccess ? "opencode-workspace-shell.sh exists in the container." : $"Failed: {FirstFailureLine(shellScript)}",
        });

        return checks;
    }

    private static string ResolveLastAttachFailureReason(WorkspaceRecord record, string transcriptExcerpt)
    {
        if (record.LastOperationSucceeded == false
            && (string.Equals(record.LastOperationName, "Open Workspace", StringComparison.Ordinal)
                || string.Equals(record.LastOperationName, "Attach", StringComparison.Ordinal)))
        {
            return string.IsNullOrWhiteSpace(record.LastOperationResult)
                ? string.Empty
                : ExtractPrimaryFailureLine(record.LastOperationResult);
        }

        if (string.IsNullOrWhiteSpace(transcriptExcerpt))
        {
            return string.Empty;
        }

        foreach (var line in transcriptExcerpt.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            if (line.Contains("attach", StringComparison.OrdinalIgnoreCase) || line.Contains("terminal", StringComparison.OrdinalIgnoreCase))
            {
                return line;
            }
        }

        return string.Empty;
    }

    private static string DescribeFileState(string path)
        => File.Exists(path) ? $"Present: {path}" : $"Missing: {path}";

    private static string FirstFailureLine(ProcessResult result)
        => result.StandardErrorLines.Concat(result.StandardOutputLines).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? $"Exit code {result.ExitCode}";

    private static string ExtractPrimaryFailureLine(string message)
        => message.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => !line.StartsWith("Command:", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("Exit code:", StringComparison.OrdinalIgnoreCase))
            ?? message;

    private static string ReadTranscriptExcerpt(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return string.Empty;
        }

        var lines = new Queue<string>();
        foreach (var line in File.ReadLines(filePath))
        {
            lines.Enqueue(line);
            while (lines.Count > 80)
            {
                lines.Dequeue();
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static bool IsOracleWorkspace(WorkspaceSnapshot snapshot, WorkspaceProvisioningHealthRecord? health)
        => snapshot.Definition.Features.Any(feature => feature.Contains("oracle", StringComparison.OrdinalIgnoreCase) || feature.Contains("apex", StringComparison.OrdinalIgnoreCase))
            || snapshot.Definition.Services.Any(service => service.Contains("oracle", StringComparison.OrdinalIgnoreCase) || service.Contains("ords", StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrWhiteSpace(health?.OracleVersion)
            || !string.IsNullOrWhiteSpace(health?.ApexVersion)
            || !string.IsNullOrWhiteSpace(health?.OrdsVersion);

    private static bool IsHostProblem(WorkspaceProvisioningHealthRecord? health)
        => string.Equals(health?.ProblemScope, "HostProblem", StringComparison.Ordinal)
            || string.Equals(health?.RecommendedAction, "Run Diagnostics.", StringComparison.Ordinal)
            || health?.Reason.Contains("Docker engine", StringComparison.OrdinalIgnoreCase) == true
            || health?.Reason.Contains("Windows Terminal", StringComparison.OrdinalIgnoreCase) == true;

    private static string BuildOracleProviderSummary(WorkspaceProvisioningHealthRecord health)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(health.OracleVersion))
        {
            parts.Add($"Oracle {health.OracleVersion}");
        }

        if (!string.IsNullOrWhiteSpace(health.ApexVersion))
        {
            parts.Add($"APEX {health.ApexVersion}");
        }

        if (!string.IsNullOrWhiteSpace(health.OrdsVersion))
        {
            parts.Add($"ORDS {health.OrdsVersion}");
        }

        return string.Join(", ", parts);
    }

    private static string BuildProcessSummary(ProcessResult processResult)
        => $"Exit code {processResult.ExitCode}. Command: {processResult.Command}";

    private static IReadOnlyList<WorkspaceTroubleshootingHistoryEntry> BuildRepairHistory(WorkspaceProvisioningHealthRecord? health)
    {
        if (health is null)
        {
            return Array.Empty<WorkspaceTroubleshootingHistoryEntry>();
        }

        return health.RepairHistory.Select(attempt => new WorkspaceTroubleshootingHistoryEntry
        {
            Title = attempt.RepairType,
            Outcome = FormatRepairOutcome(attempt.Result),
            Summary = string.IsNullOrWhiteSpace(attempt.RootCauseAfter) ? attempt.Result : attempt.RootCauseAfter,
            Evidence = string.IsNullOrWhiteSpace(attempt.EvidenceAfter) ? attempt.EvidenceBefore : attempt.EvidenceAfter,
            Recommendation = attempt.UpdatedRecommendation,
            Confidence = attempt.Confidence,
            OccurredUtc = attempt.CompletedUtc,
            Duration = attempt.Duration,
            Source = "Repair",
        }).ToList();
    }

    private static IReadOnlyList<WorkspaceTroubleshootingHistoryEntry> BuildInvestigationHistory(WorkspaceProvisioningHealthRecord? health)
    {
        if (health is null)
        {
            return Array.Empty<WorkspaceTroubleshootingHistoryEntry>();
        }

        return health.InvestigationHistory.Select(item => new WorkspaceTroubleshootingHistoryEntry
        {
            Title = item.Title,
            Outcome = item.Outcome,
            Summary = item.Summary,
            Evidence = item.Evidence,
            Recommendation = item.Recommendation,
            Confidence = item.Confidence,
            EstimatedDuration = item.EstimatedDuration,
            OccurredUtc = item.CompletedUtc,
            Duration = item.Duration,
            Source = item.ProviderName,
        }).ToList();
    }

    private static string ResolveRecommendedNextStepLabel(string recommendation)
    {
        if (string.IsNullOrWhiteSpace(recommendation))
        {
            return "Review workspace evidence";
        }

        var normalized = recommendation.Trim().TrimEnd('.');
        return normalized switch
        {
            "Reset Runtime" => "Reset Runtime",
            "Recover Workspace" => "Recover Workspace",
            "Open Workspace" => "Open Workspace",
            "Keep Waiting" => "Keep Waiting",
            "Inspect ORDS" => "Inspect ORDS",
            "Inspect provisioning transcript" => "Inspect provisioning transcript",
            "Inspect Docker resources" => "Inspect Docker resources",
            "Provide Oracle APEX media" => "Provide Oracle APEX media",
            "Stop conflicting workspace and Retry" => "Resolve Docker conflict",
            "Run Host Diagnostics" => "Run Host Diagnostics",
            _ when normalized.StartsWith("Inspect ", StringComparison.OrdinalIgnoreCase) => normalized,
            _ => normalized,
        };
    }

    private static string BuildRecommendedNextStepDescription(string recommendation, WorkspaceProvisioningHealthRecord? health, bool isProvisioningInProgress)
    {
        if (isProvisioningInProgress)
        {
            return "OpenCode is still collecting runtime evidence. Keep waiting unless the process exits, times out, or the transcript stops changing.";
        }

        if (string.IsNullOrWhiteSpace(recommendation))
        {
            return "Review the current evidence and choose the next investigation from the list below.";
        }

        if (string.Equals(recommendation.Trim().TrimEnd('.'), "Manual intervention required", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenCode already tried the obvious repairs without improvement, so the next step likely requires manual runtime intervention.";
        }

        return string.IsNullOrWhiteSpace(health?.Summary)
            ? recommendation
            : health.Summary;
    }

    private static string ExtractValidationEvidence(ProcessResult processResult)
    {
        var lines = processResult.StandardErrorLines
            .Concat(processResult.StandardOutputLines)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(20)
            .ToList();
        return lines.Count == 0 ? string.Empty : string.Join(Environment.NewLine, lines);
    }

    private static string BuildTroubleshootingHeadline(WorkspaceTroubleshootingRequest request, WorkspaceProvisioningHealthRecord? health, bool hostProblem, bool runtimeStateMissing, bool appliedStateMissing, bool terminalReadinessProblem)
    {
        if (request.IsOperationInProgress)
        {
            return "Provisioning still running";
        }

        if (hostProblem)
        {
            return "Host-level issue detected";
        }

        if (runtimeStateMissing || appliedStateMissing)
        {
            return "Managed runtime files need repair";
        }

        if (terminalReadinessProblem)
        {
            return "Terminal launch readiness failed";
        }

        return string.IsNullOrWhiteSpace(health?.Stage) ? "Workspace troubleshooting" : health.Stage;
    }

    private static string BuildTroubleshootingSummary(WorkspaceTroubleshootingRequest request, WorkspaceProvisioningHealthRecord? health, bool isOracleWorkspace, bool hostProblem, bool runtimeStateMissing, bool appliedStateMissing, bool attachScriptMissing, bool terminalReadinessProblem)
    {
        if (request.IsOperationInProgress)
        {
            return isOracleWorkspace
                ? "Provisioning is still running. APEX installation may take several minutes. Keep waiting unless the process exits, times out, or new error evidence appears."
                : "Provisioning is still running. Keep waiting while OpenCode continues the workspace operation and streams the log.";
        }

        if (hostProblem)
        {
            return "This looks like a host-level problem rather than a workspace-only failure. Review the workspace evidence first, then run host diagnostics if needed.";
        }

        if (runtimeStateMissing || appliedStateMissing || attachScriptMissing)
        {
            return "Managed runtime artifacts are missing or stale after the last operation. Open Workspace can usually repair these safely and continue.";
        }

        if (terminalReadinessProblem)
        {
            return "Workspace services are available, but OpenCode terminal could not be prepared. Troubleshoot Workspace can inspect attach scripts and runtime state.";
        }

        return string.IsNullOrWhiteSpace(health?.Summary)
            ? "OpenCode gathered the current workspace evidence so you can see what failed and what to try next."
            : health.Summary;
    }

    private static string BuildTroubleshootingRecommendation(WorkspaceTroubleshootingRequest request, WorkspaceProvisioningHealthRecord? health, bool hostProblem, bool canResetRuntime, bool runtimeStateMissing, bool isOracleWorkspace, bool terminalReadinessProblem)
    {
        if (request.IsOperationInProgress)
        {
            return isOracleWorkspace
                ? "Provisioning is still running. View Log or Keep Waiting while Oracle APEX and ORDS continue to install."
                : "Provisioning is still running. View Log or Keep Waiting before treating this as a failed workspace.";
        }

        if (hostProblem)
        {
            return "Run Host Diagnostics only if the workspace evidence still points to Docker, Windows Terminal, or another host prerequisite.";
        }

        if (runtimeStateMissing)
        {
            return "Open Workspace should safely repair the missing runtime state and continue.";
        }

        if (terminalReadinessProblem)
        {
            return "Troubleshoot Workspace can inspect attach scripts and runtime state.";
        }

        if (canResetRuntime)
        {
            return "Investigate the workspace evidence first. Reset Runtime is available in Advanced if cleanup repair is actually needed.";
        }

        return string.IsNullOrWhiteSpace(health?.RecommendedAction)
            ? "Use the workspace evidence below to choose the next safe action."
            : health.RecommendedAction;
    }

    private static IReadOnlyList<string> BuildSuggestedNextSteps(WorkspaceTroubleshootingRequest request, WorkspaceProvisioningHealthRecord? health, bool runtimeStateMissing, bool appliedStateMissing, bool hostProblem, bool isOracleWorkspace, bool terminalReadinessProblem)
    {
        if (request.IsOperationInProgress)
        {
            return isOracleWorkspace
                ? ["Keep waiting while Oracle APEX and ORDS continue to provision.", "Use View Log to confirm that stage output is still moving.", "Treat this as failed only if the process exits, times out, or new error evidence appears."]
                : ["Keep waiting while provisioning continues.", "Use View Log to confirm that output is still streaming."];
        }

        var steps = new List<string>();

        if (terminalReadinessProblem)
        {
            steps.Add("Inspect terminal readiness checks before retrying Open Workspace.");
            steps.Add("Use Troubleshoot Workspace to verify attach scripts, runtime-state, and container exec readiness.");
        }
        if (runtimeStateMissing || appliedStateMissing)
        {
            steps.Add("Open Workspace to let OpenCode repair the missing managed runtime files.");
        }

        if (hostProblem)
        {
            steps.Add("Run Host Diagnostics only after reviewing the workspace-specific evidence above.");
        }

        if (!string.IsNullOrWhiteSpace(health?.Reason))
        {
            steps.Add($"Review the last failure reason: {health.Reason}");
        }

        return steps;
    }

    private static WorkspaceSnapshot CloneSnapshot(WorkspaceSnapshot source, WorkspaceRecord record)
    {
        var snapshot = new WorkspaceSnapshot
        {
            Record = record,
            Definition = source.Definition,
            Paths = source.Paths,
            ConfigurationPath = source.ConfigurationPath,
            RuntimeState = source.RuntimeState,
            Safety = source.Safety,
            Session = source.Session,
            AppliedState = source.AppliedState,
            LocalRuntimeState = source.LocalRuntimeState,
            ResolvedRuntimePlan = source.ResolvedRuntimePlan,
            UpdateRequired = source.UpdateRequired,
            Health = new WorkspaceHealthSnapshot(),
        };

        return new WorkspaceSnapshot
        {
            Record = snapshot.Record,
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
            Health = WorkspaceHealthEngine.Build(snapshot),
        };
    }

    private static bool HasCurrentVolatileFailure(WorkspaceRecord record)
    {
        var failureText = string.Join(Environment.NewLine, new[]
        {
            record.LastOperationSucceeded == false ? record.LastOperationResult : null,
            record.LastProvisioningHealth?.Reason,
            record.LastProvisioningHealth?.Evidence,
        }.Where(value => !string.IsNullOrWhiteSpace(value))!);

        return failureText.Contains("already in use", StringComparison.OrdinalIgnoreCase)
            || failureText.Contains("port conflict", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildVolatileSuccessMessage(WorkspaceRecord record)
        => HasCurrentVolatileFailure(record)
            ? "Volatile runtime checks passed. Previous port conflict is no longer present."
            : "Volatile runtime checks passed.";

    private static WorkspaceProvisioningHealthRecord BuildVolatileFailureHealth(WorkspaceSnapshot snapshot, ProcessResult failure, DateTimeOffset checkedAt)
    {
        var reason = failure.StandardErrorLines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim();
        var evidenceLines = failure.StandardErrorLines
            .SkipWhile(line => string.IsNullOrWhiteSpace(line) || string.Equals(line.Trim(), reason, StringComparison.Ordinal))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        return new WorkspaceProvisioningHealthRecord
        {
            Succeeded = false,
            Stage = "Volatile environment revalidation",
            Summary = "Workspace runtime is currently blocked by a volatile host conflict.",
            Reason = string.IsNullOrWhiteSpace(reason) ? "Workspace runtime is blocked by a volatile host conflict." : reason,
            Evidence = string.Join(Environment.NewLine, evidenceLines),
            ProblemScope = "WorkspaceProblem",
            RecommendedAction = "Troubleshoot Workspace.",
            PreviousRecommendedAction = snapshot.Record.LastProvisioningHealth?.RecommendedAction ?? string.Empty,
            Confidence = "HIGH",
            Timestamp = checkedAt,
            Duration = TimeSpan.Zero,
            RawLogReference = snapshot.Paths.ComposePath,
            WorkspaceRuntimeVersion = snapshot.ResolvedRuntimePlan?.TargetPlatform ?? string.Empty,
            Repairability = WorkspaceRepairability.AutomaticRepair.ToString(),
            EstimatedEffort = "Low",
            EstimatedDuration = "1-2 minutes",
            LastDiagnosticsTimestamp = checkedAt,
            RepairHistory = snapshot.Record.LastProvisioningHealth?.RepairHistory ?? Array.Empty<WorkspaceRepairAttemptRecord>(),
        };
    }

    private static WorkspaceProvisioningHealthRecord? ExtractProvisioningHealth(Exception exception, WorkspaceProvisioningHealthRecord? fallback)
        => exception is WorkspaceProvisioningException provisioningException ? provisioningException.HealthRecord : fallback;

    private static WorkspaceProvisioningHealthRecord BuildSuccessfulProvisioningHealth(WorkspaceSnapshot snapshot, DateTimeOffset startedUtc, DateTimeOffset completedUtc)
        => new()
        {
            Succeeded = true,
            Stage = "Final verification",
            Summary = "Provisioning completed.",
            Reason = "Oracle workspace provisioning completed successfully.",
            Evidence = $"Runtime state = {snapshot.RuntimeState}",
            RecommendedAction = "Open Workspace.",
            Confidence = "HIGH",
            Timestamp = completedUtc,
            Duration = completedUtc - startedUtc,
            RawLogReference = snapshot.Paths.ProvisionScriptPath,
            ProblemScope = "Unknown",
            ApexVersion = string.Empty,
            OrdsVersion = string.Empty,
            OracleVersion = string.Empty,
            WorkspaceRuntimeVersion = snapshot.ResolvedRuntimePlan?.TargetPlatform ?? string.Empty,
        };

    private static WorkspaceProvisioningHealthRecord BuildCleanupProvisioningHealth(WorkspaceSnapshot snapshot, DateTimeOffset startedUtc, DateTimeOffset completedUtc)
    {
        var record = BuildSuccessfulProvisioningHealth(snapshot, startedUtc, completedUtc);
        return new WorkspaceProvisioningHealthRecord
        {
            Succeeded = record.Succeeded,
            Stage = record.Stage,
            Summary = record.Summary,
            Reason = record.Reason,
            Evidence = record.Evidence,
            ProblemScope = "RuntimeProblem",
            RecommendedAction = "Open Workspace.",
            Confidence = record.Confidence,
            Timestamp = record.Timestamp,
            Duration = record.Duration,
            RawLogReference = record.RawLogReference,
            ApexVersion = record.ApexVersion,
            OrdsVersion = record.OrdsVersion,
            OracleVersion = record.OracleVersion,
            WorkspaceRuntimeVersion = record.WorkspaceRuntimeVersion,
            Repairability = WorkspaceRepairability.CleanupRepair.ToString(),
            EstimatedEffort = "Medium",
            EstimatedDuration = "4-6 minutes",
            LastDiagnosticsTimestamp = completedUtc,
        };
    }

    private static WorkspaceProvisioningHealthRecord BuildFallbackFailureHealth(WorkspaceSnapshot snapshot, DateTimeOffset completedUtc, string errorMessage)
        => new()
        {
            Succeeded = false,
            Stage = "Repair execution",
            Summary = "Workspace repair failed.",
            Reason = string.IsNullOrWhiteSpace(errorMessage) ? "Workspace repair failed." : errorMessage,
            Evidence = string.Empty,
            ProblemScope = "Unknown",
            RecommendedAction = "Troubleshoot Workspace.",
            Confidence = "MEDIUM",
            Timestamp = completedUtc,
            Duration = TimeSpan.Zero,
            RawLogReference = snapshot.Paths.ProvisionScriptPath,
            WorkspaceRuntimeVersion = snapshot.ResolvedRuntimePlan?.TargetPlatform ?? string.Empty,
            Repairability = WorkspaceRepairability.Unknown.ToString(),
            EstimatedEffort = "Medium",
            EstimatedDuration = "2-4 minutes",
            LastDiagnosticsTimestamp = completedUtc,
        };

    private static void AppendRepairOutcomeTranscript(Action<OperationTranscriptLineKind, string> append, WorkspaceProvisioningHealthRecord health)
    {
        var lastAttempt = health.RepairHistory.LastOrDefault();
        if (lastAttempt is null)
        {
            return;
        }

        append(OperationTranscriptLineKind.Comment, $"Repair attempted: {lastAttempt.RepairType}");
        append(OperationTranscriptLineKind.Comment, $"Outcome: {FormatRepairOutcome(lastAttempt.Result)}");

        if (!lastAttempt.RootCauseChanged && !string.IsNullOrWhiteSpace(lastAttempt.EvidenceAfter))
        {
            append(OperationTranscriptLineKind.Comment, $"Root cause unchanged: {lastAttempt.EvidenceAfter}");
        }

        if (!string.IsNullOrWhiteSpace(lastAttempt.UpdatedRecommendation))
        {
            append(OperationTranscriptLineKind.Comment, $"Recommendation updated: {lastAttempt.UpdatedRecommendation}");
        }
    }

    private static string FormatRepairOutcome(string outcome)
        => outcome switch
        {
            nameof(WorkspaceRepairOutcome.RepairNoEffect) => "No improvement detected.",
            nameof(WorkspaceRepairOutcome.RepairImproved) => "Issue changed after repair.",
            nameof(WorkspaceRepairOutcome.RepairPartiallySucceeded) => "Repair partially succeeded.",
            nameof(WorkspaceRepairOutcome.RepairFailed) => "Repair failed.",
            _ => "Problem resolved.",
        };

    private static string BuildProvisioningTimelineDetails(WorkspaceProvisioningHealthRecord? health, string? fallback = null)
    {
        if (health is null)
        {
            return fallback ?? string.Empty;
        }

        var lines = new List<string>
        {
            health.Summary,
            $"Stage: {health.Stage}",
            $"Reason: {health.Reason}",
        };

        if (!string.IsNullOrWhiteSpace(health.Evidence))
        {
            lines.Add($"Evidence: {health.Evidence}");
        }

        if (!string.IsNullOrWhiteSpace(health.RecommendedAction))
        {
            lines.Add($"Recommended action: {health.RecommendedAction}");
        }

        if (!string.IsNullOrWhiteSpace(health.PreviousRecommendedAction))
        {
            lines.Add($"Previous recommendation: {health.PreviousRecommendedAction}");
        }

        if (health.RepairHistory.Count > 0)
        {
            var lastAttempt = health.RepairHistory[^1];
            lines.Add($"Repair attempted: {lastAttempt.RepairType}");
            lines.Add($"Repair outcome: {lastAttempt.Result}");
            if (!lastAttempt.RootCauseChanged && !string.IsNullOrWhiteSpace(lastAttempt.EvidenceAfter))
            {
                lines.Add($"Root cause unchanged: {lastAttempt.EvidenceAfter}");
            }
        }

        if (!string.IsNullOrWhiteSpace(health.Confidence))
        {
            lines.Add($"Confidence: {health.Confidence}");
        }

        return string.Join(Environment.NewLine, lines);
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
            throw new InvalidOperationException(OpenWorkspaceTerminalReadinessFailureMessage);
        }

        if (!File.Exists(snapshot.Paths.AttachWrapperScriptPath) || !File.Exists(snapshot.Paths.ComposePath))
        {
            throw new InvalidOperationException(OpenWorkspaceTerminalReadinessFailureMessage);
        }

        if (snapshot.RuntimeState != WorkspaceRuntimeState.Running)
        {
            throw new InvalidOperationException(TerminalLaunchReadinessFailureMessage);
        }
    }

    private static bool IsTerminalLaunchReadinessProblem(string message)
        => !string.IsNullOrWhiteSpace(message)
            && (message.Contains("terminal-ready state", StringComparison.OrdinalIgnoreCase)
                || message.Contains("terminal launch readiness", StringComparison.OrdinalIgnoreCase)
                || message.Contains("could not finish preparing the terminal", StringComparison.OrdinalIgnoreCase)
                || message.Contains("attach scripts and runtime state", StringComparison.OrdinalIgnoreCase)
                || message.Contains("runtime files need repair", StringComparison.OrdinalIgnoreCase)
                || message.Contains("terminal could not be prepared", StringComparison.OrdinalIgnoreCase));

    private static WorkspaceProvisioningHealthRecord BuildTerminalLaunchReadinessHealth(WorkspaceSnapshot snapshot, DateTimeOffset completedUtc, string errorMessage)
    {
        var availableApplications = snapshot.Health.Services
            .Where(item => string.Equals(item.Category, "Application", StringComparison.OrdinalIgnoreCase) && item.Status == WorkspaceHealthStatus.Healthy)
            .Select(item => item.Name)
            .ToList();
        var evidence = availableApplications.Count == 0
            ? "Attach artifacts exist, but OpenCode terminal launch could not be prepared."
            : $"Available services: {string.Join(", ", availableApplications)}. Terminal launch artifacts still failed readiness validation.";

        return new WorkspaceProvisioningHealthRecord
        {
            Succeeded = false,
            Stage = "Verify terminal launch readiness",
            Summary = availableApplications.Count == 0
                ? "Terminal launch readiness failed."
                : "Workspace services are available, but OpenCode terminal could not be prepared.",
            Reason = string.IsNullOrWhiteSpace(errorMessage) ? TerminalLaunchReadinessFailureMessage : errorMessage,
            Evidence = evidence,
            ProblemScope = "WorkspaceProblem",
            RecommendedAction = "Troubleshoot Workspace.",
            Confidence = "HIGH",
            Timestamp = completedUtc,
            Duration = TimeSpan.Zero,
            RawLogReference = snapshot.Paths.AttachDiagnosticsLogPath,
            WorkspaceRuntimeVersion = snapshot.ResolvedRuntimePlan?.TargetPlatform ?? string.Empty,
            Repairability = WorkspaceRepairability.ManualRepair.ToString(),
            EstimatedEffort = "Low",
            EstimatedDuration = "1-2 minutes",
            LastDiagnosticsTimestamp = completedUtc,
            RepairHistory = snapshot.Record.LastProvisioningHealth?.RepairHistory ?? Array.Empty<WorkspaceRepairAttemptRecord>(),
            InvestigationHistory = snapshot.Record.LastProvisioningHealth?.InvestigationHistory ?? Array.Empty<WorkspaceInvestigationRecord>(),
        };
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
