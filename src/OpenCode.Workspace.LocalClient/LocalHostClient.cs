using Microsoft.AspNetCore.SignalR.Client;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.Core.Workspaces;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json;

namespace OpenCode.Workspace.LocalClient;

public sealed class LocalHostClient : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly LocalHostDiscoveryResult? _discovery;

    public LocalHostClient(HttpClient httpClient, string baseUrl, LocalHostDiscoveryResult? discovery = null)
    {
        _httpClient = httpClient;
        BaseUrl = baseUrl.TrimEnd('/');
        _discovery = discovery;
    }

    public string BaseUrl { get; }

    public static string GetDescriptorPath(LocalHostClientOptions? options = null)
        => ResolveStatePathProvider(options).DescriptorPath;

    public static async Task<LocalHostClient> ConnectAsync(CancellationToken cancellationToken = default)
        => await ConnectAsync(null, cancellationToken);

    public static async Task<LocalHostClient> ConnectAsync(LocalHostClientOptions? options, CancellationToken cancellationToken = default)
    {
        var discovery = await LocalHostDiscovery.EnsureLocalHostWithOwnershipAsync(options, cancellationToken);
        var httpClient = new HttpClient { BaseAddress = new Uri(discovery.Descriptor.BaseUrl, UriKind.Absolute) };
        return new LocalHostClient(httpClient, discovery.Descriptor.BaseUrl, discovery);
    }

    public Task<LocalHostHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default)
        => GetAsync<LocalHostHealthResponse>("/api/v1/local-host/health", cancellationToken);

    public Task<LocalHostReadinessResponse> GetReadinessAsync(CancellationToken cancellationToken = default)
        => GetAsync<LocalHostReadinessResponse>("/api/v1/local-host/readiness", cancellationToken);

    public Task<IReadOnlyList<WorkspaceInstanceRecord>> ListWorkspaceInstancesAsync(CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<WorkspaceInstanceRecord>>("/api/v1/workspace-instances", cancellationToken);

    public Task<WorkspaceInstanceRecord> GetWorkspaceInstanceAsync(string workspaceInstanceId, CancellationToken cancellationToken = default)
        => GetAsync<WorkspaceInstanceRecord>($"/api/v1/workspace-instances/{Uri.EscapeDataString(workspaceInstanceId)}", cancellationToken);

    public Task<IReadOnlyList<WorkspaceOperationRecord>> ListOperationsAsync(CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<WorkspaceOperationRecord>>("/api/v1/local-host/operations", cancellationToken);

    public Task<WorkspaceOperationRecord> GetOperationAsync(string operationId, long? afterSequence = null, int? maxEvents = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (afterSequence.HasValue)
        {
            query.Add($"afterSequence={afterSequence.Value}");
        }

        if (maxEvents.HasValue)
        {
            query.Add($"maxEvents={maxEvents.Value}");
        }

        var suffix = query.Count == 0 ? string.Empty : $"?{string.Join("&", query)}";
        return GetAsync<WorkspaceOperationRecord>($"/api/v1/local-host/operations/{Uri.EscapeDataString(operationId)}{suffix}", cancellationToken);
    }

    public Task<WorkspaceOperationRecord> CancelOperationAsync(string operationId, OperationCommandRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/operations/{Uri.EscapeDataString(operationId)}/cancel", request, cancellationToken);

    public Task<IReadOnlyList<ControllerSessionRecord>> ListControllerSessionsAsync(CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<ControllerSessionRecord>>("/api/v1/controller-sessions", cancellationToken);

    public Task<ControllerSessionRecord> UpsertControllerSessionAsync(ControllerSessionUpsertRequest request, CancellationToken cancellationToken = default)
        => PostAsync<ControllerSessionRecord>("/api/v1/controller-sessions", request, cancellationToken);

    public Task<ControllerSessionRecord> DisconnectControllerSessionAsync(string controllerSessionId, ControllerSessionUpsertRequest request, CancellationToken cancellationToken = default)
        => PostAsync<ControllerSessionRecord>($"/api/v1/controller-sessions/{Uri.EscapeDataString(controllerSessionId)}/disconnect", request, cancellationToken);

    public Task<IReadOnlyList<InteractiveAgentSessionRecord>> ListInteractiveAgentSessionsAsync(string? workspaceId = null, CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<InteractiveAgentSessionRecord>>($"/api/v1/interactive-agent-sessions{BuildQuery(("workspaceId", workspaceId))}", cancellationToken);

    public Task<InteractiveAgentSessionRecord> GetInteractiveAgentSessionAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default)
        => GetAsync<InteractiveAgentSessionRecord>($"/api/v1/interactive-agent-sessions/{Uri.EscapeDataString(interactiveAgentSessionId)}", cancellationToken);

    public Task<InteractiveAgentSessionRecord> CreateInteractiveAgentSessionAsync(string workspaceId, CreateInteractiveAgentSessionRequest request, CancellationToken cancellationToken = default)
        => PostAsync<InteractiveAgentSessionRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/interactive-sessions", request, cancellationToken);

    public Task<IReadOnlyList<InteractiveSessionAttachmentRecord>> GetInteractiveAttachmentsAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<InteractiveSessionAttachmentRecord>>($"/api/v1/interactive-agent-sessions/{Uri.EscapeDataString(interactiveAgentSessionId)}/attachments", cancellationToken);

    public Task<InteractiveSessionAttachResult> AttachInteractiveSessionAsync(string interactiveAgentSessionId, AttachInteractiveSessionRequest request, CancellationToken cancellationToken = default)
        => PostAsync<InteractiveSessionAttachResult>($"/api/v1/local-host/interactive-agent-sessions/{Uri.EscapeDataString(interactiveAgentSessionId)}/attachments", request, cancellationToken);

    public Task<InteractiveSessionAttachmentActivationResult> ActivateInteractiveSessionAttachmentAsync(string interactiveAgentSessionId, string attachmentId, ActivateInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken = default)
        => PostAsync<InteractiveSessionAttachmentActivationResult>($"/api/v1/local-host/interactive-agent-sessions/{Uri.EscapeDataString(interactiveAgentSessionId)}/attachments/{Uri.EscapeDataString(attachmentId)}/activate", request, cancellationToken);

    public Task<InteractiveSessionAttachmentRecoveryResult> RecoverInteractiveSessionAttachmentAsync(string interactiveAgentSessionId, string attachmentId, RecoverInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken = default)
        => PostAsync<InteractiveSessionAttachmentRecoveryResult>($"/api/v1/local-host/interactive-agent-sessions/{Uri.EscapeDataString(interactiveAgentSessionId)}/attachments/{Uri.EscapeDataString(attachmentId)}/recover", request, cancellationToken);

    public Task<InteractiveSessionAttachmentRecord> ReportInteractiveSessionAttachmentProcessStartedAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProcessStartedRequest request, CancellationToken cancellationToken = default)
        => PostAsync<InteractiveSessionAttachmentRecord>($"/api/v1/local-host/interactive-agent-sessions/{Uri.EscapeDataString(interactiveAgentSessionId)}/attachments/{Uri.EscapeDataString(attachmentId)}/process-started", request, cancellationToken);

    public Task<InteractiveSessionAttachmentHeartbeatResult> HeartbeatInteractiveSessionAttachmentAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentHeartbeatRequest request, CancellationToken cancellationToken = default)
        => PostAsync<InteractiveSessionAttachmentHeartbeatResult>($"/api/v1/local-host/interactive-agent-sessions/{Uri.EscapeDataString(interactiveAgentSessionId)}/attachments/{Uri.EscapeDataString(attachmentId)}/heartbeat", request, cancellationToken);

    public Task<InteractiveAgentSessionRecord> ReportInteractiveSessionProviderSessionAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProviderSessionRequest request, CancellationToken cancellationToken = default)
        => PostAsync<InteractiveAgentSessionRecord>($"/api/v1/local-host/interactive-agent-sessions/{Uri.EscapeDataString(interactiveAgentSessionId)}/attachments/{Uri.EscapeDataString(attachmentId)}/provider-session", request, cancellationToken);

    public Task<InteractiveAgentSessionRecord> ReportInteractiveSessionAttachmentProcessExitAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentProcessExitRequest request, CancellationToken cancellationToken = default)
        => PostAsync<InteractiveAgentSessionRecord>($"/api/v1/local-host/interactive-agent-sessions/{Uri.EscapeDataString(interactiveAgentSessionId)}/attachments/{Uri.EscapeDataString(attachmentId)}/process-exit", request, cancellationToken);

    public Task<InteractiveAgentSessionRecord> ReportInteractiveSessionAttachmentLaunchFailureAsync(string interactiveAgentSessionId, string attachmentId, InteractiveSessionAttachmentLaunchFailureRequest request, CancellationToken cancellationToken = default)
        => PostAsync<InteractiveAgentSessionRecord>($"/api/v1/local-host/interactive-agent-sessions/{Uri.EscapeDataString(interactiveAgentSessionId)}/attachments/{Uri.EscapeDataString(attachmentId)}/launch-failed", request, cancellationToken);

    public Task<InteractiveAgentSessionRecord> DetachInteractiveSessionAttachmentAsync(string interactiveAgentSessionId, string attachmentId, DetachInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken = default)
        => PostAsync<InteractiveAgentSessionRecord>($"/api/v1/local-host/interactive-agent-sessions/{Uri.EscapeDataString(interactiveAgentSessionId)}/attachments/{Uri.EscapeDataString(attachmentId)}/detach", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartProvisionWorkspaceAsync(WorkspaceProvisionRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(request.WorkspaceId)}/provision", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartPrepareWorkspaceAsync(WorkspaceLifecycleRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(request.WorkspaceId)}/prepare", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartWorkspaceLifecycleAsync(string action, WorkspaceLifecycleRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(request.WorkspaceId)}/{Uri.EscapeDataString(action)}", request, cancellationToken);

    public Task<WorkspaceRecordModel> CreateWorkspaceCanonicalAsync(WorkspaceCreateRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceRecordModel>("/api/v1/local-host/workspaces/create", request, cancellationToken);

    public Task<ExistingGitCheckoutPlan> InspectExistingGitCheckoutAsync(ExistingGitCheckoutInspectionRequest request, CancellationToken cancellationToken = default)
        => PostAsync<ExistingGitCheckoutPlan>("/api/v1/local-host/workspaces/import/inspect-git-checkout", request, cancellationToken);

    public Task<GitBranchValidationResult> ValidateExistingGitCheckoutBranchAsync(ExistingGitCheckoutBranchValidationRequest request, CancellationToken cancellationToken = default)
        => PostAsync<GitBranchValidationResult>("/api/v1/local-host/workspaces/import/validate-branch", request, cancellationToken);

    public Task<WorkspaceRecordModel> ImportExistingGitCheckoutAsync(ExistingGitCheckoutImportRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceRecordModel>("/api/v1/local-host/workspaces/import", request, cancellationToken);

    public Task<string> SuggestSavePointMessageAsync(string workspaceId, CancellationToken cancellationToken = default)
        => PostAsync<string>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/save-points/suggest-message", new SavePointMessageSuggestionRequest { WorkspaceId = workspaceId }, cancellationToken);

    public Task<WorkspaceOperationRecord> StartCreateSavePointAsync(WorkspaceSavePointCreateRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(request.WorkspaceId)}/save-points", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartCreateCheckpointAsync(WorkspaceCheckpointCreateRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(request.WorkspaceId)}/checkpoints", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartBackupWorkspaceAsync(string workspaceId, WorkspaceBackupRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/backups", request, cancellationToken);

    public Task<WorkspacePublishAssessmentRecord> GetWorkspacePublishAssessmentAsync(string workspaceId, CancellationToken cancellationToken = default)
        => GetAsync<WorkspacePublishAssessmentRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/publish-assessment", cancellationToken);

    public Task<WorkspaceOperationRecord> StartPublishWorkspaceAsync(string workspaceId, WorkspacePublishRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/publish", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartRemoveWorkspaceAsync(string workspaceId, WorkspaceRemovalRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/remove", request, cancellationToken);

    public Task<WorkspaceRecoveryAssessmentRecord> GetWorkspaceRecoveryAssessmentAsync(string workspaceId, CancellationToken cancellationToken = default)
        => GetAsync<WorkspaceRecoveryAssessmentRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/recovery-assessment", cancellationToken);

    public Task<WorkspaceSynchronizationStatusResult> GetSynchronizationStatusAsync(string workspaceId, string? environmentName = null, CancellationToken cancellationToken = default)
        => GetAsync<WorkspaceSynchronizationStatusResult>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/synchronization/status{BuildQuery(("environmentName", environmentName))}", cancellationToken);

    public Task<OracleApexApplicationDiscoveryResult> DiscoverOracleApexApplicationsAsync(string workspaceId, OracleApexApplicationDiscoveryQuery request, CancellationToken cancellationToken = default)
        => PostAsync<OracleApexApplicationDiscoveryResult>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/oracle-apex/discover-applications", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartValidateSynchronizationAsync(string workspaceId, WorkspaceSynchronizationValidationRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/synchronization/validate", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartDiffSynchronizationAsync(string workspaceId, WorkspaceSynchronizationDiffRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/synchronization/diff", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartExportSynchronizationAsync(string workspaceId, WorkspaceSynchronizationExportRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/synchronization/export", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartPullSynchronizationAsync(string workspaceId, WorkspaceSynchronizationExportRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/synchronization/pull", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartPushSynchronizationAsync(string workspaceId, WorkspaceSynchronizationImportRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/synchronization/push", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartImportSynchronizationAsync(string workspaceId, WorkspaceSynchronizationImportRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/synchronization/import", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartSynchronizeWorkspaceAsync(string workspaceId, WorkspaceSynchronizeRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/synchronization/synchronize", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartPlanOracleAssistantAsync(string workspaceId, OracleAssistantPlanRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/oracle-assistant/plan", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartApplyOracleAssistantAsync(string workspaceId, OracleAssistantApplyRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/oracle-assistant/apply", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartPlanOracleAssistantRepairAsync(string workspaceId, OracleAssistantRepairPlanRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/oracle-assistant/repair-plan", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartExecuteOracleAssistantRepairAsync(string workspaceId, OracleAssistantRepairExecutionRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/oracle-assistant/repair", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartRollbackOracleAssistantAsync(string workspaceId, OracleAssistantRollbackRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/oracle-assistant/rollback", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartConnectExistingOracleApexApplicationAsync(string workspaceId, ConnectExistingOracleApexApplicationRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/oracle-apex/connect-existing-application", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartValidateOracleAssistantAsync(string workspaceId, OracleAssistantValidationRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/oracle-assistant/validate", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartImportOracleAssistantAsync(string workspaceId, OracleAssistantImportRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/oracle-assistant/import", request, cancellationToken);

    public Task<WorkspaceTimeline> GetWorkspaceTimelineAsync(string workspaceId, CancellationToken cancellationToken = default)
        => GetAsync<WorkspaceTimeline>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/save-points", cancellationToken);

    public Task<WorkspaceCheckpointIndex> GetWorkspaceCheckpointsAsync(string workspaceId, CancellationToken cancellationToken = default)
        => GetAsync<WorkspaceCheckpointIndex>($"/api/v1/local-host/workspaces/{Uri.EscapeDataString(workspaceId)}/checkpoints", cancellationToken);

    public Task<WorkspaceOperationRecord> StartSmokeRunAsync(SmokeRunOperationRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>("/api/v1/local-host/smoke/runs", request, cancellationToken);

    public Task<WorkspaceOperationRecord> StartSmokeMatrixAsync(SmokeMatrixOperationRequest request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceOperationRecord>("/api/v1/local-host/smoke/matrices", request, cancellationToken);

    public Task<IReadOnlyList<WorkspaceTemplateSummaryModel>> ListWorkspaceTemplatesAsync(CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<WorkspaceTemplateSummaryModel>>("/api/v1/templates", cancellationToken);

    public Task<WorkspaceTemplateDetailModel> GetWorkspaceTemplateAsync(string templateId, CancellationToken cancellationToken = default)
        => GetAsync<WorkspaceTemplateDetailModel>($"/api/v1/templates/{Uri.EscapeDataString(templateId)}", cancellationToken);

    public Task<IReadOnlyList<WorkspaceRecordModel>> ListWorkspacesAsync(CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<WorkspaceRecordModel>>("/api/v1/workspaces", cancellationToken);

    public Task<WorkspaceRecordModel> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
        => GetAsync<WorkspaceRecordModel>($"/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}", cancellationToken);

    public Task<WorkspaceRecordModel> CreateWorkspaceAsync(object request, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceRecordModel>("/api/v1/workspaces", request, cancellationToken);

    public Task<WorkspaceRecordModel> ValidateWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceRecordModel>($"/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/validate", new { }, cancellationToken);

    public Task<WorkspaceRecordModel> StopWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceRecordModel>($"/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/stop", new { }, cancellationToken);

    public Task<WorkspaceRecordModel> RemoveWorkspaceRuntimeAsync(string workspaceId, CancellationToken cancellationToken = default)
        => PostAsync<WorkspaceRecordModel>($"/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/remove-runtime", new { }, cancellationToken);

    public Task<WorkspaceSmokeDefinitionCatalogResult> ListSmokeDefinitionsAsync(CancellationToken cancellationToken = default)
        => GetAsync<WorkspaceSmokeDefinitionCatalogResult>("/api/v1/smoke/definitions", cancellationToken);

    public Task<RuntimeResourceInventory> ListRuntimeResourcesAsync(string? owner, string? runId, string? project, string? workspaceRoot, CancellationToken cancellationToken = default)
        => GetAsync<RuntimeResourceInventory>($"/api/v1/runtime/resources?owner={Uri.EscapeDataString(owner ?? string.Empty)}&runId={Uri.EscapeDataString(runId ?? string.Empty)}&project={Uri.EscapeDataString(project ?? string.Empty)}&workspaceRoot={Uri.EscapeDataString(workspaceRoot ?? string.Empty)}", cancellationToken);

    public Task<RuntimeResourceInventory> RunRuntimeDoctorAsync(string? owner, string? runId, string? project, string? workspaceRoot, CancellationToken cancellationToken = default)
        => GetAsync<RuntimeResourceInventory>($"/api/v1/runtime/doctor?owner={Uri.EscapeDataString(owner ?? string.Empty)}&runId={Uri.EscapeDataString(runId ?? string.Empty)}&project={Uri.EscapeDataString(project ?? string.Empty)}&workspaceRoot={Uri.EscapeDataString(workspaceRoot ?? string.Empty)}", cancellationToken);

    public async Task<HubConnection> ConnectEventsAsync(Func<WorkspaceEventEnvelope, Task> onEvent)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl($"{BaseUrl}/hubs/events")
            .WithAutomaticReconnect()
            .Build();
        connection.On<WorkspaceEventEnvelope>("event", onEvent);
        await connection.StartAsync();
        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        if (_discovery?.Ownership == LocalHostOwnership.OwnedByDesktop && _discovery.OwnedProcess is not null && !_discovery.OwnedProcess.HasExited)
        {
            try
            {
                _discovery.OwnedProcess.StandardInput.Close();
            }
            catch
            {
            }
        }

        await ValueTask.CompletedTask;
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        return await ReadEnvelopeAsync<T>(response, cancellationToken);
    }

    private async Task<T> PostAsync<T>(string path, object request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, request, LocalHostContract.JsonOptions, cancellationToken);
        return await ReadEnvelopeAsync<T>(response, cancellationToken);
    }

    private static string BuildQuery(params (string Key, string? Value)[] values)
    {
        var entries = values
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}")
            .ToArray();
        return entries.Length == 0 ? string.Empty : $"?{string.Join("&", entries)}";
    }

    private static async Task<T> ReadEnvelopeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("data", out var data))
            {
                return data.Deserialize<T>(LocalHostContract.JsonOptions)
                    ?? throw new InvalidOperationException("The LocalHost response did not contain a valid payload.");
            }

            return document.RootElement.Deserialize<T>(LocalHostContract.JsonOptions)
                ?? throw new InvalidOperationException("The LocalHost response did not contain a valid payload.");
        }

        await using var errorStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var errorDocument = await JsonDocument.ParseAsync(errorStream, cancellationToken: cancellationToken);
        var code = errorDocument.RootElement.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
        var message = errorDocument.RootElement.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
        var recommendation = errorDocument.RootElement.TryGetProperty("recommendation", out var recommendationElement) ? recommendationElement.GetString() : null;
        throw new LocalHostClientException(code ?? "local_host_error", message ?? $"LocalHost request failed with status {(int)response.StatusCode}.", recommendation ?? "Inspect LocalHost diagnostics and retry.");
    }

    internal static ILocalHostStatePathProvider ResolveStatePathProvider(LocalHostClientOptions? options)
        => WorkspaceAppDataPaths.CreateLocalHostStatePathProvider(options?.StateRoot);
}

public sealed class LocalHostClientException(string code, string message, string recommendation) : Exception(message)
{
    public string Code { get; } = code;
    public string Recommendation { get; } = recommendation;
}

public static class LocalHostDiscovery
{
    private static readonly JsonSerializerOptions JsonOptions = LocalHostContract.JsonOptions;

    public static async Task<LocalHostDescriptor> EnsureLocalHostAsync(CancellationToken cancellationToken = default)
        => await EnsureLocalHostAsync(null, cancellationToken);

    public static async Task<LocalHostDescriptor> EnsureLocalHostAsync(LocalHostClientOptions? options, CancellationToken cancellationToken = default)
        => (await EnsureLocalHostWithOwnershipAsync(options, cancellationToken)).Descriptor;

    public static async Task<LocalHostDiscoveryResult> EnsureLocalHostWithOwnershipAsync(LocalHostClientOptions? options, CancellationToken cancellationToken = default)
    {
        var descriptor = await TryReadHealthyDescriptorAsync(options, cancellationToken);
        if (descriptor is not null)
        {
            return new LocalHostDiscoveryResult { Descriptor = descriptor, Ownership = LocalHostOwnership.External };
        }

        await using var startupGate = await AcquireStartupGateAsync(options, cancellationToken);
        descriptor = await TryReadHealthyDescriptorAsync(options, cancellationToken);
        if (descriptor is not null)
        {
            return new LocalHostDiscoveryResult { Descriptor = descriptor, Ownership = LocalHostOwnership.External };
        }

        var ownedProcess = StartLocalHostProcess(options);

        var startedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(30))
        {
            cancellationToken.ThrowIfCancellationRequested();
            descriptor = await TryReadHealthyDescriptorAsync(options, cancellationToken);
            if (descriptor is not null)
            {
                if (descriptor.ProcessId == ownedProcess.Id && !ownedProcess.HasExited)
                {
                    return new LocalHostDiscoveryResult { Descriptor = descriptor, Ownership = LocalHostOwnership.OwnedByDesktop, OwnedProcess = ownedProcess };
                }

                TryRequestOwnedProcessShutdown(ownedProcess);
                return new LocalHostDiscoveryResult { Descriptor = descriptor, Ownership = LocalHostOwnership.External };
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new InvalidOperationException("LocalHost did not become healthy within 30 seconds.");
    }

    private static Process StartLocalHostProcess(LocalHostClientOptions? options)
    {
        var installationLayout = ResolveInstallationLayout(options);
        string[] candidateNames = OperatingSystem.IsWindows()
            ? ["OpenCode.Workspace.LocalHost.exe", "OpenCode.Workspace.LocalHost.dll"]
            : ["OpenCode.Workspace.LocalHost", "OpenCode.Workspace.LocalHost.dll"];
        var hostDirectory = ResolveHostDirectory(installationLayout, options);
        var executablePath = candidateNames.Select(name => Path.Combine(hostDirectory, name)).FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException($"Could not locate the packaged LocalHost executable under '{hostDirectory}'.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : executablePath,
            UseShellExecute = false,
            WorkingDirectory = hostDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (executablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(executablePath);
        }

        startInfo.ArgumentList.Add("--shutdown-on-stdin-eof");

        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{AllocateLoopbackPort()}";
        var stateRoot = LocalHostClient.ResolveStatePathProvider(options).StateRoot;
        startInfo.Environment["localHost__stateRoot"] = stateRoot;
        startInfo.Environment["mcp__workspaceStateRoot"] = stateRoot;
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start LocalHost process.");
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        return process;
    }

    private static void TryRequestOwnedProcessShutdown(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
            }
        }
        catch
        {
        }
    }

    private static async Task<LocalHostDescriptor?> TryReadHealthyDescriptorAsync(LocalHostClientOptions? options, CancellationToken cancellationToken)
    {
        var descriptorPath = LocalHostClient.GetDescriptorPath(options);
        if (!File.Exists(descriptorPath))
        {
            return null;
        }

        try
        {
            var descriptor = JsonSerializer.Deserialize<LocalHostDescriptor>(await File.ReadAllTextAsync(descriptorPath, cancellationToken), JsonOptions);
            if (descriptor is null || string.IsNullOrWhiteSpace(descriptor.BaseUrl))
            {
                return null;
            }

            if (!string.Equals(descriptor.ContractVersion, LocalHostContract.ContractVersion, StringComparison.Ordinal)
                || !Uri.TryCreate(descriptor.BaseUrl, UriKind.Absolute, out var baseUri)
                || !IPAddress.TryParse(baseUri.Host, out var address)
                || !IPAddress.IsLoopback(address))
            {
                TryRemoveStaleDescriptor(descriptorPath);
                return null;
            }

            using var httpClient = new HttpClient { BaseAddress = baseUri };
            using var response = await httpClient.GetAsync("/api/v1/local-host/health", cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                TryRemoveStaleDescriptor(descriptorPath);
                return null;
            }

            var envelope = await response.Content.ReadFromJsonAsync<LocalHostEnvelope<LocalHostHealthResponse>>(JsonOptions, cancellationToken);
            if (envelope?.Data is null
                || !string.Equals(envelope.Data.ContractVersion, descriptor.ContractVersion, StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(envelope.Data.HostInstanceId)
                    && !string.Equals(envelope.Data.HostInstanceId, descriptor.InstanceId, StringComparison.Ordinal)))
            {
                TryRemoveStaleDescriptor(descriptorPath);
                return null;
            }

            return descriptor;
        }
        catch
        {
            return null;
        }
    }

    private static OpenCodeWorkspaceInstallationLayout ResolveInstallationLayout(LocalHostClientOptions? options)
        => string.IsNullOrWhiteSpace(options?.DistributionRoot)
            ? OpenCodeWorkspaceInstallationLayout.Resolve(AppContext.BaseDirectory)
            : OpenCodeWorkspaceInstallationLayout.Resolve(Path.GetFullPath(options.DistributionRoot!));

    private static string ResolveHostDirectory(OpenCodeWorkspaceInstallationLayout layout, LocalHostClientOptions? options)
    {
        if (!string.IsNullOrWhiteSpace(options?.LocalHostExecutableDirectory))
        {
            return Path.GetFullPath(options.LocalHostExecutableDirectory!);
        }

        var localHostDirectory = Path.Combine(layout.DistributionRoot, "bin", "local-host");
        if (Directory.Exists(localHostDirectory))
        {
            return localHostDirectory;
        }

        return localHostDirectory;
    }

    private static void TryRemoveStaleDescriptor(string descriptorPath)
    {
        try { File.Delete(descriptorPath); } catch { }
    }

    private static async Task<FileStream> AcquireStartupGateAsync(LocalHostClientOptions? options, CancellationToken cancellationToken)
    {
        var stateRoot = LocalHostClient.ResolveStatePathProvider(options).StateRoot;
        var path = Path.Combine(stateRoot, "local-host", "startup.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var started = Stopwatch.StartNew();
        while (started.Elapsed < TimeSpan.FromSeconds(30))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        throw new TimeoutException($"Timed out waiting for the LocalHost startup gate '{path}'.");
    }


    private static int AllocateLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}

public sealed record LocalHostDiscoveryResult
{
    public required LocalHostDescriptor Descriptor { get; init; }
    public LocalHostOwnership Ownership { get; init; } = LocalHostOwnership.External;
    public Process? OwnedProcess { get; init; }
}

public sealed class LocalHostClientOptions
{
    public string StateRoot { get; init; } = string.Empty;
    public string DistributionRoot { get; init; } = string.Empty;
    public string LocalHostExecutableDirectory { get; init; } = string.Empty;

    public static LocalHostClientOptions FromEnvironment()
        => new()
        {
            StateRoot = Environment.GetEnvironmentVariable("localHost__stateRoot") ?? string.Empty,
            DistributionRoot = Environment.GetEnvironmentVariable("localHost__distributionRoot") ?? string.Empty,
            LocalHostExecutableDirectory = Environment.GetEnvironmentVariable("localHost__executableDirectory") ?? string.Empty,
        };
}
