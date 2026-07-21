using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.LocalClient;
using System.Text.Json;

namespace OpenCode.Workspace.Avalonia.Services;

public interface IDesktopWorkspaceApplicationService
{
    WorkspaceDefinition BuildWorkspaceDefinition(CreateWorkspaceDraft draft);
    OracleSoftwareNoticePrompt? BuildOracleSoftwareNotice(CreateWorkspaceDraft draft);
    Task<WorkspaceSnapshot> AcknowledgeOracleSoftwareNoticeAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<WorkspaceSnapshot> CreateWorkspaceAsync(CreateWorkspaceDraft draft, CancellationToken cancellationToken = default);
    Task<ExistingGitCheckoutPlan> InspectExistingGitCheckoutAsync(string repositoryPath, string workspaceName, CancellationToken cancellationToken = default);
    Task<GitBranchValidationResult> ValidateExistingGitCheckoutBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default);
    Task<WorkspaceSnapshot> ImportExistingGitCheckoutAsync(ExistingGitCheckoutImportRequest request, CancellationToken cancellationToken = default);
    Task<string> SuggestSavePointMessageAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> CreateSavePointAsync(WorkspaceSnapshot workspace, string message, CancellationToken cancellationToken = default);
    Task<WorkspaceCheckpointOperationResult> CreateCheckpointAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceTimeline> LoadTimelineAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InteractiveAgentSessionRecord>> LoadInteractiveSessionsAsync(string? workspaceId, CancellationToken cancellationToken = default);
    Task<InteractiveAgentSessionRecord> CreateInteractiveSessionAsync(WorkspaceSnapshot workspace, string? title, CancellationToken cancellationToken = default);
    Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(CancellationToken cancellationToken = default);
    Task<WorkspaceSnapshot?> RefreshVolatileWorkspaceStateAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceApexAssistantPlanResult> PlanOracleApexChangeAsync(WorkspaceSnapshot workspace, OracleApexAssistantRequest request, CancellationToken cancellationToken = default);
    Task<WorkspaceApexAssistantExecutionResult> ExecuteOracleApexPlanAsync(WorkspaceSnapshot workspace, OracleApexAssistantRequest request, OracleApexEditPlan plan, string planId, string contextRevision, CancellationToken cancellationToken = default);
    Task<WorkspaceApexAssistantRepairPlanResult> BuildOracleApexRepairPlanAsync(WorkspaceSnapshot workspace, OracleApexAssistantRequest request, OracleApexEditPlan sourcePlan, OracleApexValidationResult validation, string planId, string executionId, string contextRevision, CancellationToken cancellationToken = default);
    Task<WorkspaceApexAssistantExecutionResult> ExecuteOracleApexRepairPlanAsync(WorkspaceSnapshot workspace, string planId, string executionId, string repairPlanId, CancellationToken cancellationToken = default);
    Task<WorkspaceApexAssistantRollbackResult> RollbackOracleApexGeneratedChangeAsync(WorkspaceSnapshot workspace, string executionId, CancellationToken cancellationToken = default);
    Task<OracleApexApplicationDiscoveryResult> DiscoverOracleApexApplicationsAsync(WorkspaceSnapshot workspace, ConnectOracleApexApplicationDraft draft, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> ConnectExistingOracleApexApplicationAsync(WorkspaceSnapshot workspace, ConnectOracleApexApplicationDraft draft, CancellationToken cancellationToken = default);
    Task<WorkspacePublishAssessment> AssessWorkspacePublishAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceRecoveryAssessment> AssessWorkspaceRecoveryAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    WorkspaceRuntimeResetPrompt BuildRuntimeResetPrompt(WorkspaceSnapshot workspace);
    Task<WorkspaceSynchronizationStatusResult> GetSynchronizationStatusAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> ExportSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> PullSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> PushSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> ImportSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> ValidateSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> DiffSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> SynchronizeWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceApexAssistantValidationResult> ValidateOracleApexGeneratedApplicationAsync(WorkspaceSnapshot workspace, string environmentName, string? executionId, CancellationToken cancellationToken = default);
    Task<WorkspaceApexAssistantImportResult> ImportOracleApexGeneratedApplicationAsync(WorkspaceSnapshot workspace, string environmentName, bool allowNonDevelopmentDeployment, string? executionId, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> PrepareWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> OpenWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> StartWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> StopWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspacePublishResult> PublishWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceRemovalOperationResult> RemoveWorkspaceAsync(string rootPath, WorkspaceSnapshot? workspace, WorkspaceRemovalChoice choice, CancellationToken cancellationToken = default);
    Task<WorkspaceBackupResult> BackupWorkspaceAsync(WorkspaceSnapshot workspace, string destinationPath, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> RecoverWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> ResetRuntimeAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> AttachWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationResult> ReprovisionWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
}

public interface IDesktopWorkspaceProjectionMapper
{
    WorkspaceSnapshot ToWorkspaceSnapshot(WorkspaceInstanceRecord workspace, IReadOnlyCollection<WorkspaceOperationRecord> operations);
    WorkspaceLoadResult ToWorkspaceLoadResult(IReadOnlyCollection<WorkspaceInstanceRecord> workspaces, IReadOnlyCollection<WorkspaceOperationRecord> operations);
    WorkspaceOperationResult ToWorkspaceOperationResult(WorkspaceOperationRecord operation, WorkspaceInstanceRecord? workspace, OperationTranscript transcript);
}

public sealed class DesktopWorkspaceProjectionMapper : IDesktopWorkspaceProjectionMapper
{
    public static WorkspaceRuntimeResetPrompt BuildRuntimeResetPrompt(WorkspaceSnapshot snapshot)
        => new()
        {
            WorkspaceName = snapshot.Definition.Workspace.Name,
            WorkspaceRoot = snapshot.Paths.RootPath,
            Summary = "Rebuild Runtime recreates managed containers and volumes for this workspace while keeping your files.",
            Removes =
            [
                "Managed containers for this workspace",
                "Managed Docker volumes for this workspace",
                "Managed Docker network for this workspace when it is safe to remove",
                "Generated runtime state",
            ],
            Keeps =
            [
                "Workspace files",
                "Git history",
                "Documentation",
                "Downloads/cache",
                "User scripts",
                "workspace.yaml",
            ],
            ConfirmationMessage = "Rebuild Runtime and continue?",
        };

    public WorkspaceSnapshot ToWorkspaceSnapshot(WorkspaceInstanceRecord workspace, IReadOnlyCollection<WorkspaceOperationRecord> operations)
    {
        if (workspace.Workspace?.Snapshot is { } snapshot)
        {
            return snapshot;
        }

        var paths = WorkspacePathBuilder.Build(workspace.Workspace?.WorkspaceRoot ?? workspace.WorkspaceId, workspace.Workspace?.Snapshot?.ConfigurationPath ?? "workspace.yaml");
        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = workspace.WorkspaceName,
                RootPath = workspace.Workspace?.WorkspaceRoot ?? workspace.WorkspaceId,
                RepositoryPath = workspace.Workspace?.WorkspaceRoot ?? workspace.WorkspaceId,
                ConfigurationPath = "workspace.yaml",
                LastOperationName = operations.Where(item => item.WorkspaceInstanceId == workspace.WorkspaceInstanceId).OrderByDescending(item => item.LastUpdatedUtc).FirstOrDefault()?.OperationKind,
                LastOperationResult = operations.Where(item => item.WorkspaceInstanceId == workspace.WorkspaceInstanceId).OrderByDescending(item => item.LastUpdatedUtc).FirstOrDefault()?.ProgressMessage,
            },
            Definition = workspace.Workspace?.Snapshot?.Definition ?? new WorkspaceDefinition { Workspace = new WorkspaceMetadata { Id = workspace.WorkspaceId, Name = workspace.WorkspaceName, Image = "ubuntu:24.04" } },
            Paths = paths,
            ConfigurationPath = paths.WorkspaceYamlPath,
            RuntimeState = Enum.TryParse<WorkspaceRuntimeState>(workspace.RuntimeState, true, out var runtimeState) ? runtimeState : WorkspaceRuntimeState.Unknown,
            Safety = workspace.Workspace?.Snapshot?.Safety ?? new WorkspaceSafetySnapshot { OverallStatus = WorkspaceSafetyLevel.NeedsReview, Headline = workspace.Status, Message = workspace.RecoveryState, LocalRecovery = new WorkspaceLocalRecoverySnapshot(), Backup = new WorkspaceBackupSnapshot(), IgnorePolicy = new WorkspaceIgnorePolicyReview(), AdvancedGit = new WorkspaceAdvancedGitSnapshot() },
            Session = workspace.Workspace?.Snapshot?.Session ?? new WorkspaceSessionSnapshot(),
            AppliedState = workspace.Workspace?.Snapshot?.AppliedState,
            LocalRuntimeState = workspace.Workspace?.Snapshot?.LocalRuntimeState,
            ResolvedRuntimePlan = workspace.Workspace?.Snapshot?.ResolvedRuntimePlan,
            UpdateRequired = workspace.Workspace?.Snapshot?.UpdateRequired ?? false,
            Synchronization = workspace.Workspace?.Snapshot?.Synchronization ?? new WorkspaceSynchronizationSnapshot(),
            Assistant = workspace.Workspace?.Snapshot?.Assistant ?? new WorkspaceApexAssistantSnapshot(),
            Health = workspace.Workspace?.Snapshot?.Health ?? new WorkspaceHealthSnapshot { OverallStatus = WorkspaceHealthStatus.Unavailable, Summary = workspace.Status },
            Readiness = workspace.Workspace?.Snapshot?.Readiness ?? new WorkspaceReadinessSnapshot { Summary = workspace.RecoveryState },
            AvailableServices = workspace.Workspace?.Snapshot?.AvailableServices ?? Array.Empty<WorkspaceServiceInfo>(),
        };
    }

    public WorkspaceLoadResult ToWorkspaceLoadResult(IReadOnlyCollection<WorkspaceInstanceRecord> workspaces, IReadOnlyCollection<WorkspaceOperationRecord> operations)
    {
        var items = workspaces.Select(item => new WorkspaceShellItem
        {
            Record = ToWorkspaceSnapshot(item, operations).Record,
            Snapshot = ToWorkspaceSnapshot(item, operations),
        }).ToArray();
        return new WorkspaceLoadResult
        {
            Items = items,
            Report = new WorkspaceLoadReport
            {
                StartedUtc = DateTimeOffset.UtcNow,
                CompletedUtc = DateTimeOffset.UtcNow,
                RawRecordCount = items.Length,
                SnapshotAttemptCount = items.Length,
                SnapshotCount = items.Length,
                ItemsReturnedCount = items.Length,
            },
        };
    }

    public WorkspaceOperationResult ToWorkspaceOperationResult(WorkspaceOperationRecord operation, WorkspaceInstanceRecord? workspace, OperationTranscript transcript)
        => new()
        {
            Snapshot = workspace is null ? throw new InvalidOperationException($"Workspace instance was not found for operation '{operation.OperationId}'.") : ToWorkspaceSnapshot(workspace, [operation]),
            Message = string.IsNullOrWhiteSpace(operation.ProgressMessage) ? operation.CurrentPhase : operation.ProgressMessage,
            Transcript = transcript,
        };
}

public sealed class LocalHostDesktopWorkspaceApplicationService : IDesktopWorkspaceApplicationService
{
    private readonly IWorkspaceLocalHostApplicationService _localHost;
    private readonly IDesktopWorkspaceProjectionMapper _mapper;

    public LocalHostDesktopWorkspaceApplicationService(IWorkspaceLocalHostApplicationService localHost, IDesktopWorkspaceProjectionMapper mapper)
    {
        _localHost = localHost;
        _mapper = mapper;
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
        };

    public OracleSoftwareNoticePrompt? BuildOracleSoftwareNotice(CreateWorkspaceDraft draft)
        => OracleWorkspaceFamily.IsOracleWorkspace(draft.Template)
            ? new OracleSoftwareNoticePrompt
            {
                Title = "Oracle Software Notice",
                SubjectName = draft.WorkspaceName,
                Summary = "Review the Oracle software reminder before continuing with this Oracle workspace.",
                Facts = ["Oracle software is subject to Oracle licensing terms."],
                AcknowledgementLabel = "I understand the Oracle licensing reminder.",
                ConfirmLabel = "Continue",
                CancelLabel = "Cancel",
            }
            : null;

    public async Task<WorkspaceSnapshot> AcknowledgeOracleSoftwareNoticeAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
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
                LastPreparedUtc = snapshot.Record.LastPreparedUtc,
                OracleSoftwareNoticeShown = true,
                LastOperationName = snapshot.Record.LastOperationName,
                LastOperationResult = snapshot.Record.LastOperationResult,
                LastOperationSucceeded = snapshot.Record.LastOperationSucceeded,
                LastOperationUtc = snapshot.Record.LastOperationUtc,
                LastProvisioningHealth = snapshot.Record.LastProvisioningHealth,
            },
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
            Synchronization = snapshot.Synchronization,
            Assistant = snapshot.Assistant,
            Health = snapshot.Health,
            Readiness = snapshot.Readiness,
            AvailableServices = snapshot.AvailableServices,
        };
    }

    public async Task<WorkspaceSnapshot> CreateWorkspaceAsync(CreateWorkspaceDraft draft, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var workspace = await client.CreateWorkspaceCanonicalAsync(new WorkspaceCreateRequest
        {
            TemplateId = draft.Template.Id,
            WorkspaceName = draft.WorkspaceName,
            WorkspaceRootPath = draft.WorkspaceRootPath,
        }, cancellationToken);
        return workspace.Snapshot;
    }

    public async Task<ExistingGitCheckoutPlan> InspectExistingGitCheckoutAsync(string repositoryPath, string workspaceName, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        return await client.InspectExistingGitCheckoutAsync(new ExistingGitCheckoutInspectionRequest { RepositoryPath = repositoryPath, WorkspaceName = workspaceName }, cancellationToken);
    }

    public async Task<GitBranchValidationResult> ValidateExistingGitCheckoutBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        return await client.ValidateExistingGitCheckoutBranchAsync(new ExistingGitCheckoutBranchValidationRequest { RepositoryPath = repositoryPath, BranchName = branchName }, cancellationToken);
    }

    public async Task<WorkspaceSnapshot> ImportExistingGitCheckoutAsync(ExistingGitCheckoutImportRequest request, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var workspace = await client.ImportExistingGitCheckoutAsync(request, cancellationToken);
        return workspace.Snapshot;
    }

    public async Task<string> SuggestSavePointMessageAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        return await client.SuggestSavePointMessageAsync(workspace.Definition.Workspace.Id, cancellationToken);
    }

    public Task<WorkspaceOperationResult> CreateSavePointAsync(WorkspaceSnapshot workspace, string message, CancellationToken cancellationToken = default)
        => StartAndObserveAsync(workspace, client => client.StartCreateSavePointAsync(new WorkspaceSavePointCreateRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspace.Definition.Workspace.Id, Message = message, RequestedBy = new OperationInitiator { Kind = "desktop" } }, cancellationToken), cancellationToken);

    public async Task<WorkspaceCheckpointOperationResult> CreateCheckpointAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        var result = await StartAndObserveAsync(workspace, client => client.StartCreateCheckpointAsync(new WorkspaceCheckpointCreateRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspace.Definition.Workspace.Id, RequestedBy = new OperationInitiator { Kind = "desktop" } }, cancellationToken), cancellationToken);
        return new WorkspaceCheckpointOperationResult
        {
            Snapshot = result.Snapshot,
            Message = result.Message,
            Transcript = result.Transcript,
            Checkpoint = new WorkspaceCheckpointRecord { Id = result.Transcript.OperationName + "-checkpoint", CreatedUtc = result.Transcript.CompletedUtc ?? DateTimeOffset.UtcNow },
        };
    }

    public async Task<WorkspaceTimeline> LoadTimelineAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        return await client.GetWorkspaceTimelineAsync(workspace.Definition.Workspace.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<InteractiveAgentSessionRecord>> LoadInteractiveSessionsAsync(string? workspaceId, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        return await client.ListInteractiveAgentSessionsAsync(workspaceId, cancellationToken);
    }

    public async Task<InteractiveAgentSessionRecord> CreateInteractiveSessionAsync(WorkspaceSnapshot workspace, string? title, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        return await client.CreateInteractiveAgentSessionAsync(workspace.Definition.Workspace.Id, new CreateInteractiveAgentSessionRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            Title = title ?? string.Empty,
        }, cancellationToken);
    }

    public async Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        var workspaces = await _localHost.GetWorkspaceInstancesAsync(cancellationToken);
        var operations = await _localHost.GetOperationsAsync(cancellationToken);
        return _mapper.ToWorkspaceLoadResult(workspaces, operations);
    }

    public async Task<WorkspaceSnapshot?> RefreshVolatileWorkspaceStateAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        var workspaces = await _localHost.GetWorkspaceInstancesAsync(cancellationToken);
        var operations = await _localHost.GetOperationsAsync(cancellationToken);
        var match = workspaces.FirstOrDefault(item => string.Equals(item.WorkspaceId, workspace.Definition.Workspace.Id, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : _mapper.ToWorkspaceSnapshot(match, operations);
    }

    public async Task<WorkspaceApexAssistantPlanResult> PlanOracleApexChangeAsync(WorkspaceSnapshot workspace, OracleApexAssistantRequest request, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var started = await client.StartPlanOracleAssistantAsync(workspace.Definition.Workspace.Id, new OracleAssistantPlanRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            Intent = request.Prompt,
            EnvironmentName = request.EnvironmentName,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<OracleAssistantPlanOperationRecord>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Oracle Assistant planning operation '{started.OperationId}' did not return a plan payload.");
        return new WorkspaceApexAssistantPlanResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = payload.Response.Review,
            Transcript = transcript,
            PlanId = payload.PlanId,
            ContextRevision = payload.ContextRevision,
            Response = payload.Response,
        };
    }

    public async Task<WorkspaceApexAssistantExecutionResult> ExecuteOracleApexPlanAsync(WorkspaceSnapshot workspace, OracleApexAssistantRequest request, OracleApexEditPlan plan, string planId, string contextRevision, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var started = await client.StartApplyOracleAssistantAsync(workspace.Definition.Workspace.Id, new OracleAssistantApplyRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            PlanId = planId,
            ContextRevision = contextRevision,
            ConfirmPlan = request.ConfirmPlan,
            PostEditBehavior = request.PostEditBehavior,
            EnvironmentName = request.EnvironmentName,
            EnableSafeAutomaticRepair = request.EnableSafeAutomaticRepair,
            AllowNonDevelopmentDeployment = request.AllowNonDevelopmentDeployment,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<OracleAssistantApplyOperationRecord>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Oracle Assistant apply operation '{started.OperationId}' did not return an execution payload.");
        return new WorkspaceApexAssistantExecutionResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = payload.Response.Summary,
            Transcript = transcript,
            PlanId = payload.PlanId,
            ContextRevision = payload.ContextRevision,
            ExecutionId = payload.ExecutionId,
            Response = payload.Response,
        };
    }

    public async Task<WorkspaceApexAssistantRepairPlanResult> BuildOracleApexRepairPlanAsync(WorkspaceSnapshot workspace, OracleApexAssistantRequest request, OracleApexEditPlan sourcePlan, OracleApexValidationResult validation, string planId, string executionId, string contextRevision, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var started = await client.StartPlanOracleAssistantRepairAsync(workspace.Definition.Workspace.Id, new OracleAssistantRepairPlanRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            PlanId = planId,
            ExecutionId = executionId,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<OracleAssistantRepairPlanOperationRecord>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Oracle Assistant repair-plan operation '{started.OperationId}' did not return a repair plan payload.");
        return new WorkspaceApexAssistantRepairPlanResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = payload.Response.Review,
            Transcript = transcript,
            RepairPlanId = payload.RepairPlanId,
            PlanId = payload.PlanId,
            ExecutionId = payload.ExecutionId,
            ContextRevision = payload.ContextRevision,
            Response = payload.Response,
        };
    }

    public async Task<WorkspaceApexAssistantExecutionResult> ExecuteOracleApexRepairPlanAsync(WorkspaceSnapshot workspace, string planId, string executionId, string repairPlanId, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var started = await client.StartExecuteOracleAssistantRepairAsync(workspace.Definition.Workspace.Id, new OracleAssistantRepairExecutionRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            PlanId = planId,
            ExecutionId = executionId,
            RepairPlanId = repairPlanId,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<OracleAssistantRepairOperationRecord>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Oracle Assistant repair execution operation '{started.OperationId}' did not return a repair execution payload.");
        return new WorkspaceApexAssistantExecutionResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = payload.Response.Summary,
            Transcript = transcript,
            RepairPlanId = payload.RepairPlanId,
            PlanId = payload.PlanId,
            ContextRevision = payload.ContextRevision,
            ExecutionId = payload.ExecutionId,
            Response = payload.Response,
        };
    }

    public async Task<WorkspaceApexAssistantRollbackResult> RollbackOracleApexGeneratedChangeAsync(WorkspaceSnapshot workspace, string executionId, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var started = await client.StartRollbackOracleAssistantAsync(workspace.Definition.Workspace.Id, new OracleAssistantRollbackRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            ExecutionId = executionId,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<OracleAssistantRollbackOperationRecord>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Oracle Assistant rollback operation '{started.OperationId}' did not return a rollback payload.");
        return new WorkspaceApexAssistantRollbackResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = payload.Response.Summary,
            Transcript = transcript,
            ExecutionId = payload.ExecutionId,
            Response = payload.Response,
        };
    }

    public async Task<OracleApexApplicationDiscoveryResult> DiscoverOracleApexApplicationsAsync(WorkspaceSnapshot workspace, ConnectOracleApexApplicationDraft draft, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        return await client.DiscoverOracleApexApplicationsAsync(workspace.Definition.Workspace.Id, new OracleApexApplicationDiscoveryQuery
        {
            WorkspaceId = workspace.Definition.Workspace.Id,
            EnvironmentName = draft.EnvironmentName,
            WorkspaceName = draft.WorkspaceName,
            ParsingSchema = draft.ParsingSchema,
            SqlclProfile = draft.SqlclProfile,
            SourcePath = draft.SourcePath,
        }, cancellationToken);
    }

    public async Task<WorkspaceOperationResult> ConnectExistingOracleApexApplicationAsync(WorkspaceSnapshot workspace, ConnectOracleApexApplicationDraft draft, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var started = await client.StartConnectExistingOracleApexApplicationAsync(workspace.Definition.Workspace.Id, new ConnectExistingOracleApexApplicationRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            EnvironmentName = draft.EnvironmentName,
            WorkspaceName = draft.WorkspaceName,
            ParsingSchema = draft.ParsingSchema,
            SqlclProfile = draft.SqlclProfile,
            SourcePath = draft.SourcePath,
            ApplicationId = draft.ApplicationId,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<ConnectExistingOracleApexApplicationOperationRecord>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Oracle APEX connect operation '{started.OperationId}' did not return a connection payload.");
        return new WorkspaceOperationResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = payload.Message,
            Transcript = transcript,
        };
    }

    public async Task<WorkspacePublishAssessment> AssessWorkspacePublishAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var assessment = await client.GetWorkspacePublishAssessmentAsync(workspace.Definition.Workspace.Id, cancellationToken);
        return new WorkspacePublishAssessment
        {
            WorkspaceName = assessment.WorkspaceName,
            CurrentBranch = assessment.CurrentBranch,
            Summary = assessment.Summary,
            ConfirmationMessage = assessment.ConfirmationMessage,
            Findings = assessment.Findings,
            Warnings = assessment.Warnings,
            CanPublish = assessment.CanPublish,
            IsBlocked = assessment.IsBlocked,
            RequiresConfirmation = assessment.RequiresConfirmation,
            RequiresSavePoint = assessment.RequiresSavePoint,
            HasRemoteConfigured = assessment.HasRemoteConfigured,
            RemoteName = assessment.RemoteName,
            RemoteBranch = assessment.RemoteBranch,
            AheadCount = assessment.AheadCount,
            BehindCount = assessment.BehindCount,
        };
    }

    public async Task<WorkspaceRecoveryAssessment> AssessWorkspaceRecoveryAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var assessment = await client.GetWorkspaceRecoveryAssessmentAsync(workspace.Definition.Workspace.Id, cancellationToken);
        return new WorkspaceRecoveryAssessment
        {
            Title = assessment.Title,
            Summary = assessment.Summary,
            Findings = assessment.Findings,
            ConfirmationMessage = assessment.ConfirmationMessage,
            WorkspaceName = assessment.WorkspaceName,
            StatusSummary = assessment.StatusSummary,
            RecoverActions = assessment.RecoverActions,
            CurrentProblems = assessment.CurrentProblems,
            PreviousFailureContext = assessment.PreviousFailureContext,
            WillNotChange = assessment.WillNotChange,
            ManualActionSummary = assessment.ManualActionSummary,
            ManualActions = assessment.ManualActions,
            AdvancedDetails = assessment.AdvancedDetails,
            LastCheckedAt = assessment.LastCheckedAt,
        };
    }

    public WorkspaceRuntimeResetPrompt BuildRuntimeResetPrompt(WorkspaceSnapshot workspace)
        => DesktopWorkspaceProjectionMapper.BuildRuntimeResetPrompt(workspace);

    public async Task<WorkspaceSynchronizationStatusResult> GetSynchronizationStatusAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        return await client.GetSynchronizationStatusAsync(workspace.Definition.Workspace.Id, workspace.Synchronization.DefaultEnvironment?.EnvironmentName, cancellationToken);
    }

    public async Task<WorkspaceOperationResult> ValidateSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var environmentName = workspace.Synchronization.DefaultEnvironment?.EnvironmentName;
        var started = await client.StartValidateSynchronizationAsync(workspace.Definition.Workspace.Id, new WorkspaceSynchronizationValidationRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            EnvironmentName = environmentName ?? string.Empty,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<WorkspaceSynchronizationOperationResult>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Synchronization validation operation '{started.OperationId}' did not return a result payload.");
        return new WorkspaceOperationResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = payload.Message,
            Transcript = transcript,
        };
    }

    public async Task<WorkspaceOperationResult> ExportSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var environmentName = workspace.Synchronization.DefaultEnvironment?.EnvironmentName;
        var started = await client.StartExportSynchronizationAsync(workspace.Definition.Workspace.Id, new WorkspaceSynchronizationExportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            EnvironmentName = environmentName ?? string.Empty,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<WorkspaceSynchronizationOperationResult>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Synchronization export operation '{started.OperationId}' did not return a result payload.");
        return new WorkspaceOperationResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = payload.Message,
            Transcript = transcript,
        };
    }

    public async Task<WorkspaceOperationResult> PullSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var environmentName = workspace.Synchronization.DefaultEnvironment?.EnvironmentName;
        var started = await client.StartPullSynchronizationAsync(workspace.Definition.Workspace.Id, new WorkspaceSynchronizationExportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            EnvironmentName = environmentName ?? string.Empty,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<WorkspaceSynchronizationOperationResult>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Synchronization pull operation '{started.OperationId}' did not return a result payload.");
        return new WorkspaceOperationResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = payload.Message,
            Transcript = transcript,
        };
    }

    public async Task<WorkspaceOperationResult> PushSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var environmentName = workspace.Synchronization.DefaultEnvironment?.EnvironmentName;
        var deploymentProfile = workspace.Synchronization.DefaultEnvironment?.ActiveDeploymentProfile;
        var started = await client.StartPushSynchronizationAsync(workspace.Definition.Workspace.Id, new WorkspaceSynchronizationImportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            EnvironmentName = environmentName ?? string.Empty,
            DeploymentProfileOverride = string.IsNullOrWhiteSpace(deploymentProfile) ? string.Empty : deploymentProfile,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<WorkspaceSynchronizationOperationResult>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Synchronization push operation '{started.OperationId}' did not return a result payload.");
        return new WorkspaceOperationResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = payload.Message,
            Transcript = transcript,
        };
    }

    public async Task<WorkspaceOperationResult> ImportSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var environmentName = workspace.Synchronization.DefaultEnvironment?.EnvironmentName;
        var started = await client.StartImportSynchronizationAsync(workspace.Definition.Workspace.Id, new WorkspaceSynchronizationImportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            EnvironmentName = environmentName ?? string.Empty,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<WorkspaceSynchronizationOperationResult>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Synchronization import operation '{started.OperationId}' did not return a result payload.");
        return new WorkspaceOperationResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = payload.Message,
            Transcript = transcript,
        };
    }

    public async Task<WorkspaceOperationResult> DiffSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var environmentName = workspace.Synchronization.DefaultEnvironment?.EnvironmentName;
        var started = await client.StartDiffSynchronizationAsync(workspace.Definition.Workspace.Id, new WorkspaceSynchronizationDiffRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            EnvironmentName = environmentName ?? string.Empty,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<WorkspaceSynchronizationDiffResult>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Synchronization diff operation '{started.OperationId}' did not return a result payload.");
        var message = string.IsNullOrWhiteSpace(payload.DiffText)
            ? payload.Summary
            : $"{payload.Summary}{Environment.NewLine}{payload.DiffText}";
        return new WorkspaceOperationResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = message,
            Transcript = transcript,
        };
    }

    public async Task<WorkspaceOperationResult> SynchronizeWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var environmentName = workspace.Synchronization.DefaultEnvironment?.EnvironmentName;
        var started = await client.StartSynchronizeWorkspaceAsync(workspace.Definition.Workspace.Id, new WorkspaceSynchronizeRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            EnvironmentName = environmentName ?? string.Empty,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<WorkspaceSynchronizationExecutionResult>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Synchronization operation '{started.OperationId}' did not return a result payload.");
        var message = payload.OperationResult?.Message
            ?? (string.IsNullOrWhiteSpace(payload.DiffResult?.DiffText)
                ? payload.DiffResult?.Summary
                : $"{payload.DiffResult!.Summary}{Environment.NewLine}{payload.DiffResult.DiffText}")
            ?? completed.ProgressMessage;
        return new WorkspaceOperationResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = message ?? string.Empty,
            Transcript = transcript,
        };
    }

    public async Task<WorkspaceApexAssistantValidationResult> ValidateOracleApexGeneratedApplicationAsync(WorkspaceSnapshot workspace, string environmentName, string? executionId, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var started = await client.StartValidateOracleAssistantAsync(workspace.Definition.Workspace.Id, new OracleAssistantValidationRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            ExecutionId = executionId ?? string.Empty,
            EnvironmentName = environmentName,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<OracleAssistantSynchronizationOperationRecord>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Oracle Assistant validation operation '{started.OperationId}' did not return a result payload.");
        return new WorkspaceApexAssistantValidationResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = payload.Response.Message,
            Transcript = transcript,
            Response = payload.Response,
        };
    }

    public async Task<WorkspaceApexAssistantImportResult> ImportOracleApexGeneratedApplicationAsync(WorkspaceSnapshot workspace, string environmentName, bool allowNonDevelopmentDeployment, string? executionId, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var deploymentProfile = workspace.Synchronization.DefaultEnvironment?.ActiveDeploymentProfile;
        var started = await client.StartImportOracleAssistantAsync(workspace.Definition.Workspace.Id, new OracleAssistantImportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            ExecutionId = executionId ?? string.Empty,
            EnvironmentName = environmentName,
            DeploymentProfileOverride = string.IsNullOrWhiteSpace(deploymentProfile) ? string.Empty : deploymentProfile,
            AllowNonDevelopmentDeployment = allowNonDevelopmentDeployment,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var completed = await _localHost.GetOperationAsync(started.OperationId, cancellationToken: cancellationToken);
        if (completed.Status == WorkspaceOperationStatus.Failed)
        {
            ThrowIfOperationFailed(completed);
        }

        var refreshedWorkspace = await client.GetWorkspaceAsync(workspace.Definition.Workspace.Id, cancellationToken);
        var payload = completed.Result?.Deserialize<OracleAssistantSynchronizationOperationRecord>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Oracle Assistant import operation '{started.OperationId}' did not return a result payload.");
        return new WorkspaceApexAssistantImportResult
        {
            Snapshot = refreshedWorkspace.Snapshot,
            Message = payload.Response.Message,
            Transcript = transcript,
            Response = payload.Response,
        };
    }

    public Task<WorkspaceOperationResult> PrepareWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => StartAndObserveAsync(workspace, client => client.StartPrepareWorkspaceAsync(new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspace.Definition.Workspace.Id, RequestedBy = new OperationInitiator { Kind = "desktop" } }, cancellationToken), cancellationToken);

    public Task<WorkspaceOperationResult> OpenWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => PrepareWorkspaceAsync(workspace, cancellationToken);

    public Task<WorkspaceOperationResult> StartWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => StartAndObserveAsync(workspace, client => client.StartWorkspaceLifecycleAsync("start", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspace.Definition.Workspace.Id, RequestedBy = new OperationInitiator { Kind = "desktop" } }, cancellationToken), cancellationToken);

    public Task<WorkspaceOperationResult> StopWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => StartAndObserveAsync(workspace, client => client.StartWorkspaceLifecycleAsync("stop", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspace.Definition.Workspace.Id, RequestedBy = new OperationInitiator { Kind = "desktop" } }, cancellationToken), cancellationToken);

    public async Task<WorkspacePublishResult> PublishWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var started = await client.StartPublishWorkspaceAsync(workspace.Definition.Workspace.Id, new WorkspacePublishRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var workspaces = await _localHost.GetWorkspaceInstancesAsync(cancellationToken);
        var operations = await _localHost.GetOperationsAsync(cancellationToken);
        var completed = operations.First(item => item.OperationId == started.OperationId);
        ThrowIfOperationFailed(completed);
        var payload = completed.Result?.Deserialize<WorkspacePublishOperationPayload>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Publish operation '{started.OperationId}' did not return a publish result payload.");
        var updatedWorkspace = workspaces.FirstOrDefault(item => string.Equals(item.WorkspaceId, workspace.Definition.Workspace.Id, StringComparison.OrdinalIgnoreCase));
        return new WorkspacePublishResult
        {
            Snapshot = updatedWorkspace is null ? workspace : _mapper.ToWorkspaceSnapshot(updatedWorkspace, operations),
            Message = string.IsNullOrWhiteSpace(payload.Message) ? completed.ProgressMessage : payload.Message,
            Transcript = transcript,
            Review = payload.Review,
        };
    }

    public async Task<WorkspaceRemovalOperationResult> RemoveWorkspaceAsync(string rootPath, WorkspaceSnapshot? workspace, WorkspaceRemovalChoice choice, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var workspaceId = workspace?.Definition.Workspace.Id;
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            var workspaces = await _localHost.GetWorkspaceInstancesAsync(cancellationToken);
            workspaceId = workspaces.FirstOrDefault(item => string.Equals(item.Workspace?.WorkspaceRoot, rootPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Workspace?.Snapshot?.Paths.RootPath, rootPath, StringComparison.OrdinalIgnoreCase))?.WorkspaceId;
        }

        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new InvalidOperationException($"Workspace rooted at '{rootPath}' was not found before removal could start.");
        }

        var workspaceName = workspace?.Definition.Workspace.Name ?? Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var started = await client.StartRemoveWorkspaceAsync(workspaceId, new OpenCode.Workspace.LocalClient.WorkspaceRemovalRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspaceId,
            RemoveOwnedRuntimeResources = choice == WorkspaceRemovalChoice.DockerResources,
            DeleteWorkspaceFiles = choice == WorkspaceRemovalChoice.DeleteFiles,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspaceName, started.OperationKind, cancellationToken);
        var operations = await _localHost.GetOperationsAsync(cancellationToken);
        var completed = operations.First(item => item.OperationId == started.OperationId);
        ThrowIfOperationFailed(completed);
        var payload = completed.Result?.Deserialize<WorkspaceRemovalOperationPayload>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Removal operation '{started.OperationId}' did not return a removal result payload.");
        return new WorkspaceRemovalOperationResult
        {
            Message = string.IsNullOrWhiteSpace(payload.Message) ? completed.ProgressMessage : payload.Message,
            Transcript = transcript,
            Removal = new WorkspaceRemovalResult
            {
                WorkspaceName = payload.Removal.WorkspaceName,
                WorkspaceRoot = payload.Removal.WorkspaceRoot,
                FilesDeleted = payload.Removal.WorkspaceFilesDeleted,
                Warnings = payload.Removal.Warnings,
                Succeeded = payload.Removal.Succeeded,
                FailureReason = payload.Removal.FailureReason,
            },
        };
    }

    public async Task<WorkspaceBackupResult> BackupWorkspaceAsync(WorkspaceSnapshot workspace, string destinationPath, CancellationToken cancellationToken = default)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var started = await client.StartBackupWorkspaceAsync(workspace.Definition.Workspace.Id, new WorkspaceBackupRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            DestinationPath = destinationPath,
            OverwriteExisting = true,
            RequestedBy = new OperationInitiator { Kind = "desktop" },
        }, cancellationToken);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var workspaces = await _localHost.GetWorkspaceInstancesAsync(cancellationToken);
        var operations = await _localHost.GetOperationsAsync(cancellationToken);
        var completed = operations.First(item => item.OperationId == started.OperationId);
        ThrowIfOperationFailed(completed);
        var payload = completed.Result?.Deserialize<WorkspaceBackupOperationPayload>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"Backup operation '{started.OperationId}' did not return a backup result payload.");
        var updatedWorkspace = workspaces.FirstOrDefault(item => string.Equals(item.WorkspaceId, workspace.Definition.Workspace.Id, StringComparison.OrdinalIgnoreCase));
        return new WorkspaceBackupResult
        {
            Snapshot = updatedWorkspace is null ? workspace : _mapper.ToWorkspaceSnapshot(updatedWorkspace, operations),
            Message = string.IsNullOrWhiteSpace(payload.Message) ? completed.ProgressMessage : payload.Message,
            Transcript = transcript,
            Export = payload.Export,
            Manifest = payload.Manifest,
        };
    }

    public Task<WorkspaceOperationResult> RecoverWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => StartAndObserveAsync(workspace, client => client.StartWorkspaceLifecycleAsync("recover", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspace.Definition.Workspace.Id, RequestedBy = new OperationInitiator { Kind = "desktop" } }, cancellationToken), cancellationToken);

    public Task<WorkspaceOperationResult> ResetRuntimeAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => StartAndObserveAsync(workspace, client => client.StartWorkspaceLifecycleAsync("reset-runtime", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspace.Definition.Workspace.Id, RequestedBy = new OperationInitiator { Kind = "desktop" } }, cancellationToken), cancellationToken);

    public Task<WorkspaceOperationResult> AttachWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => StartAndObserveAsync(workspace, client => client.StartWorkspaceLifecycleAsync("attach", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspace.Definition.Workspace.Id, RequestedBy = new OperationInitiator { Kind = "desktop" } }, cancellationToken), cancellationToken);

    public Task<WorkspaceOperationResult> ReprovisionWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => StartAndObserveAsync(workspace, client => client.StartWorkspaceLifecycleAsync("reprovision", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspace.Definition.Workspace.Id, RequestedBy = new OperationInitiator { Kind = "desktop" } }, cancellationToken), cancellationToken);

    private async Task<WorkspaceOperationResult> StartAndObserveAsync(WorkspaceSnapshot workspace, Func<LocalHostClient, Task<WorkspaceOperationRecord>> start, CancellationToken cancellationToken)
    {
        await _localHost.ConnectAsync(cancellationToken);
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var started = await start(client);
        var transcript = await WaitForTerminalOperationAsync(started.OperationId, workspace.Definition.Workspace.Name, started.OperationKind, cancellationToken);
        var workspaces = await _localHost.GetWorkspaceInstancesAsync(cancellationToken);
        var operations = await _localHost.GetOperationsAsync(cancellationToken);
        var completed = operations.First(item => item.OperationId == started.OperationId);
        var updatedWorkspace = workspaces.FirstOrDefault(item => string.Equals(item.WorkspaceId, workspace.Definition.Workspace.Id, StringComparison.OrdinalIgnoreCase));
        return _mapper.ToWorkspaceOperationResult(completed, updatedWorkspace, transcript);
    }

    private async Task<OperationTranscript> WaitForTerminalOperationAsync(string operationId, string workspaceName, string operationName, CancellationToken cancellationToken)
    {
        var transcript = new OperationTranscript { OperationName = operationName, WorkspaceName = workspaceName, StartedUtc = DateTimeOffset.UtcNow };
        long? lastSequence = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = await _localHost.GetOperationAsync(operationId, afterSequence: lastSequence, cancellationToken: cancellationToken);
            foreach (var item in operation.RecentEvents.OrderBy(item => item.Sequence))
            {
                lastSequence = item.Sequence;
                transcript.Lines.Add(new OperationTranscriptLine
                {
                    Timestamp = item.TimestampUtc,
                    Kind = item.Level switch
                    {
                        WorkspaceOperationProgressLevel.Error => OperationTranscriptLineKind.StandardError,
                        WorkspaceOperationProgressLevel.Warning => OperationTranscriptLineKind.Comment,
                        _ => OperationTranscriptLineKind.Status,
                    },
                    Text = item.Message,
                });
            }

            if (operation.Status is WorkspaceOperationStatus.Succeeded or WorkspaceOperationStatus.Failed or WorkspaceOperationStatus.Cancelled or WorkspaceOperationStatus.Interrupted)
            {
                transcript.CompletedUtc = operation.CompletedUtc ?? DateTimeOffset.UtcNow;
                transcript.Succeeded = operation.Status == WorkspaceOperationStatus.Succeeded;
                transcript.Lines.Add(new OperationTranscriptLine { Kind = operation.Status == WorkspaceOperationStatus.Succeeded ? OperationTranscriptLineKind.Result : OperationTranscriptLineKind.StandardError, Text = string.IsNullOrWhiteSpace(operation.ProgressMessage) ? operation.CurrentPhase : operation.ProgressMessage });
                return transcript;
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private static void ThrowIfOperationFailed(WorkspaceOperationRecord operation)
    {
        if (operation.Status == WorkspaceOperationStatus.Succeeded)
        {
            return;
        }

        throw new InvalidOperationException(
            operation.OriginalFailure?.Message
            ?? operation.CleanupFailure?.Message
            ?? operation.ProgressMessage
            ?? operation.CurrentPhase);
    }

    private sealed class WorkspaceBackupOperationPayload
    {
        public string Message { get; init; } = string.Empty;
        public WorkspaceBackupExportResult Export { get; init; } = null!;
        public WorkspaceBackupManifestResult Manifest { get; init; } = null!;
    }

    private sealed class WorkspacePublishOperationPayload
    {
        public string Message { get; init; } = string.Empty;
        public WorkspacePublishReview Review { get; init; } = null!;
    }

    private sealed class WorkspaceRemovalOperationPayload
    {
        public string Message { get; init; } = string.Empty;
        public WorkspaceRemovalResultRecord Removal { get; init; } = null!;
    }
}

internal sealed class LegacyDesktopWorkspaceApplicationService : IDesktopWorkspaceApplicationService
{
    private readonly IDesktopWorkspaceService _legacy;

    public LegacyDesktopWorkspaceApplicationService(IDesktopWorkspaceService legacy)
    {
        _legacy = legacy;
    }

    public Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(CancellationToken cancellationToken = default)
        => _legacy.LoadWorkspaceItemsAsync(includeRuntimeInspection: true, cancellationToken: cancellationToken);

    public WorkspaceDefinition BuildWorkspaceDefinition(CreateWorkspaceDraft draft)
        => _legacy.BuildWorkspaceDefinition(draft);

    public OracleSoftwareNoticePrompt? BuildOracleSoftwareNotice(CreateWorkspaceDraft draft)
        => _legacy.BuildOracleSoftwareNotice(draft.Template, draft.WorkspaceName);

    public Task<WorkspaceSnapshot> AcknowledgeOracleSoftwareNoticeAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default)
        => _legacy.AcknowledgeOracleSoftwareNoticeAsync(snapshot.Paths.RootPath, snapshot, cancellationToken);

    public Task<WorkspaceSnapshot> CreateWorkspaceAsync(CreateWorkspaceDraft draft, CancellationToken cancellationToken = default)
        => _legacy.CreateWorkspaceAsync(draft.WorkspaceRootPath, BuildWorkspaceDefinition(draft), cancellationToken: cancellationToken);

    public Task<ExistingGitCheckoutPlan> InspectExistingGitCheckoutAsync(string repositoryPath, string workspaceName, CancellationToken cancellationToken = default)
        => _legacy.InspectExistingGitCheckoutAsync(repositoryPath, workspaceName, cancellationToken);

    public Task<GitBranchValidationResult> ValidateExistingGitCheckoutBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default)
        => _legacy.ValidateExistingGitCheckoutBranchAsync(repositoryPath, branchName, cancellationToken);

    public Task<WorkspaceSnapshot> ImportExistingGitCheckoutAsync(ExistingGitCheckoutImportRequest request, CancellationToken cancellationToken = default)
        => _legacy.ImportExistingGitCheckoutAsync(request, cancellationToken: cancellationToken);

    public Task<string> SuggestSavePointMessageAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => InvokeLegacyAsync<string>("SuggestSavePointMessageAsync", [workspace.Paths.RootPath, cancellationToken]);

    public Task<WorkspaceOperationResult> CreateSavePointAsync(WorkspaceSnapshot workspace, string message, CancellationToken cancellationToken = default)
        => InvokeLegacyAsync<WorkspaceOperationResult>("CreateSavePointAsync", [workspace.Paths.RootPath, message, workspace, null, cancellationToken]);

    public Task<WorkspaceCheckpointOperationResult> CreateCheckpointAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => InvokeLegacyAsync<WorkspaceCheckpointOperationResult>("CreateCheckpointAsync", [workspace.Paths.RootPath, workspace, null, cancellationToken]);

    public Task<WorkspaceTimeline> LoadTimelineAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => Task.FromResult((WorkspaceTimeline)InvokeLegacy("LoadTimeline", [workspace.Paths.TimelinePath]));

    public Task<IReadOnlyList<InteractiveAgentSessionRecord>> LoadInteractiveSessionsAsync(string? workspaceId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<InteractiveAgentSessionRecord>>([]);

    public Task<InteractiveAgentSessionRecord> CreateInteractiveSessionAsync(WorkspaceSnapshot workspace, string? title, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy interactive session creation is not available on the desktop workspace service.");

    public Task<WorkspacePublishAssessment> AssessWorkspacePublishAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy publish assessment path was removed from the desktop shell service.");

    public Task<WorkspaceRecoveryAssessment> AssessWorkspaceRecoveryAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy recovery assessment path was removed from the desktop shell service.");

    public WorkspaceRuntimeResetPrompt BuildRuntimeResetPrompt(WorkspaceSnapshot workspace)
        => DesktopWorkspaceProjectionMapper.BuildRuntimeResetPrompt(workspace);

    public Task<WorkspaceSynchronizationStatusResult> GetSynchronizationStatusAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy synchronization status path was removed from the desktop shell service.");

    public Task<WorkspaceOperationResult> ExportSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy synchronization export path was removed from the desktop shell service.");

    public Task<WorkspaceOperationResult> PullSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy synchronization pull path was removed from the desktop shell service.");

    public Task<WorkspaceOperationResult> PushSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy synchronization push path was removed from the desktop shell service.");

    public Task<WorkspaceOperationResult> ImportSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy synchronization import path was removed from the desktop shell service.");

    public Task<WorkspaceOperationResult> ValidateSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy synchronization validation path was removed from the desktop shell service.");

    public Task<WorkspaceOperationResult> DiffSynchronizationAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy synchronization diff path was removed from the desktop shell service.");

    public Task<WorkspaceOperationResult> SynchronizeWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy synchronization orchestrator path was removed from the desktop shell service.");

    public Task<WorkspaceApexAssistantValidationResult> ValidateOracleApexGeneratedApplicationAsync(WorkspaceSnapshot workspace, string environmentName, string? executionId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy Oracle Assistant validation path was removed from the desktop shell service.");

    public Task<WorkspaceApexAssistantImportResult> ImportOracleApexGeneratedApplicationAsync(WorkspaceSnapshot workspace, string environmentName, bool allowNonDevelopmentDeployment, string? executionId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy Oracle Assistant import path was removed from the desktop shell service.");

    public Task<WorkspaceApexAssistantPlanResult> PlanOracleApexChangeAsync(WorkspaceSnapshot workspace, OracleApexAssistantRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy Oracle Assistant planning path was removed from the desktop shell service.");

    public Task<WorkspaceApexAssistantExecutionResult> ExecuteOracleApexPlanAsync(WorkspaceSnapshot workspace, OracleApexAssistantRequest request, OracleApexEditPlan plan, string planId, string contextRevision, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy Oracle Assistant apply path was removed from the desktop shell service.");

    public Task<WorkspaceApexAssistantRepairPlanResult> BuildOracleApexRepairPlanAsync(WorkspaceSnapshot workspace, OracleApexAssistantRequest request, OracleApexEditPlan sourcePlan, OracleApexValidationResult validation, string planId, string executionId, string contextRevision, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy Oracle Assistant repair planning path was removed from the desktop shell service.");

    public Task<WorkspaceApexAssistantExecutionResult> ExecuteOracleApexRepairPlanAsync(WorkspaceSnapshot workspace, string planId, string executionId, string repairPlanId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy Oracle Assistant repair execution path was removed from the desktop shell service.");

    public Task<WorkspaceApexAssistantRollbackResult> RollbackOracleApexGeneratedChangeAsync(WorkspaceSnapshot workspace, string executionId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy Oracle Assistant rollback path was removed from the desktop shell service.");

    public Task<OracleApexApplicationDiscoveryResult> DiscoverOracleApexApplicationsAsync(WorkspaceSnapshot workspace, ConnectOracleApexApplicationDraft draft, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy Oracle APEX discovery path was removed from the desktop shell service.");

    public Task<WorkspaceOperationResult> ConnectExistingOracleApexApplicationAsync(WorkspaceSnapshot workspace, ConnectOracleApexApplicationDraft draft, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy Oracle APEX connection path was removed from the desktop shell service.");

    private Task<T> InvokeLegacyAsync<T>(string methodName, object?[] args)
        => (Task<T>)InvokeLegacy(methodName, args);

    private object InvokeLegacy(string methodName, object?[] args)
    {
        try
        {
            return _legacy.GetType().GetMethod(methodName)!.Invoke(_legacy, args)!;
        }
        catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    public Task<WorkspaceLoadResult> LoadWorkspaceItemsWithProgressAsync(Action<WorkspaceLoadProgressUpdate>? progress, CancellationToken cancellationToken = default)
        => _legacy.LoadWorkspaceItemsAsync(includeRuntimeInspection: true, progress, cancellationToken);

    public Task<WorkspaceSnapshot?> RefreshVolatileWorkspaceStateAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => _legacy.RefreshVolatileWorkspaceStateAsync(workspace.Paths.RootPath, workspace, cancellationToken: cancellationToken)!;

    public Task<WorkspaceOperationResult> PrepareWorkspaceWithTranscriptAsync(WorkspaceSnapshot workspace, IOperationLogSink sink, CancellationToken cancellationToken = default)
        => _legacy.PrepareWorkspaceAsync(workspace.Paths.RootPath, workspace, sink, cancellationToken);

    public Task<WorkspaceOperationResult> OpenWorkspaceWithTranscriptAsync(WorkspaceSnapshot workspace, IOperationLogSink sink, CancellationToken cancellationToken = default)
        => _legacy.OpenWorkspaceAsync(workspace.Paths.RootPath, workspace, sink, cancellationToken);

    public Task<WorkspaceOperationResult> StartWorkspaceWithTranscriptAsync(WorkspaceSnapshot workspace, IOperationLogSink sink, CancellationToken cancellationToken = default)
        => _legacy.StartWorkspaceAsync(workspace.Paths.RootPath, workspace, sink, cancellationToken);

    public Task<WorkspaceOperationResult> RecoverWorkspaceWithTranscriptAsync(WorkspaceSnapshot workspace, IOperationLogSink sink, CancellationToken cancellationToken = default)
        => _legacy.RecoverWorkspaceAsync(workspace.Paths.RootPath, workspace, sink, cancellationToken);

    public Task<WorkspaceOperationResult> ResetRuntimeWithTranscriptAsync(WorkspaceSnapshot workspace, IOperationLogSink sink, CancellationToken cancellationToken = default)
        => _legacy.ResetRuntimeAsync(workspace.Paths.RootPath, workspace, sink, cancellationToken);

    public Task<WorkspaceOperationResult> AttachWorkspaceWithTranscriptAsync(WorkspaceSnapshot workspace, IOperationLogSink sink, CancellationToken cancellationToken = default)
        => _legacy.AttachWorkspaceAsync(workspace.Paths.RootPath, workspace, sink, cancellationToken);

    public async Task<WorkspaceOperationResult> ReprovisionWorkspaceWithTranscriptAsync(WorkspaceSnapshot workspace, IOperationLogSink sink, CancellationToken cancellationToken = default)
    {
        var result = await _legacy.ReprovisionWorkspaceAsync(workspace.Paths.RootPath, workspace, sink, cancellationToken);
        return new WorkspaceOperationResult { Snapshot = result.Snapshot, Message = result.Message, Transcript = result.Transcript };
    }

    public Task<WorkspaceOperationResult> PrepareWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => _legacy.PrepareWorkspaceAsync(workspace.Paths.RootPath, workspace, cancellationToken: cancellationToken);

    public Task<WorkspaceOperationResult> OpenWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => _legacy.OpenWorkspaceAsync(workspace.Paths.RootPath, workspace, cancellationToken: cancellationToken);

    public Task<WorkspaceOperationResult> StartWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => _legacy.StartWorkspaceAsync(workspace.Paths.RootPath, workspace, cancellationToken: cancellationToken);

    public Task<WorkspaceOperationResult> StopWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => _legacy.StopWorkspaceAsync(workspace.Paths.RootPath, workspace, cancellationToken: cancellationToken);

    public Task<WorkspacePublishResult> PublishWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy publish path was removed from the desktop shell service.");

    public Task<WorkspaceRemovalOperationResult> RemoveWorkspaceAsync(string rootPath, WorkspaceSnapshot? workspace, WorkspaceRemovalChoice choice, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Legacy removal path was removed from the desktop shell service.");

    public Task<WorkspaceBackupResult> BackupWorkspaceAsync(WorkspaceSnapshot workspace, string destinationPath, CancellationToken cancellationToken = default)
        => InvokeLegacyAsync<WorkspaceBackupResult>("BackupWorkspaceAsync", [workspace.Paths.RootPath, destinationPath, workspace, null, cancellationToken]);

    public Task<WorkspaceOperationResult> RecoverWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => _legacy.RecoverWorkspaceAsync(workspace.Paths.RootPath, workspace, cancellationToken: cancellationToken);

    public Task<WorkspaceOperationResult> ResetRuntimeAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => _legacy.ResetRuntimeAsync(workspace.Paths.RootPath, workspace, cancellationToken: cancellationToken);

    public Task<WorkspaceOperationResult> AttachWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
        => _legacy.AttachWorkspaceAsync(workspace.Paths.RootPath, workspace, cancellationToken: cancellationToken);

    public async Task<WorkspaceOperationResult> ReprovisionWorkspaceAsync(WorkspaceSnapshot workspace, CancellationToken cancellationToken = default)
    {
        var result = await _legacy.ReprovisionWorkspaceAsync(workspace.Paths.RootPath, workspace, cancellationToken: cancellationToken);
        return new WorkspaceOperationResult { Snapshot = result.Snapshot, Message = result.Message, Transcript = result.Transcript };
    }
}
