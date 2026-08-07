using Microsoft.AspNetCore.SignalR.Client;
using OpenCode.Workspace.LocalClient;

namespace OpenCode.Workspace.Avalonia.Services;

public enum LocalHostConnectionState
{
    Disconnected,
    Discovering,
    StartingLocalHost,
    WaitingForReadiness,
    Connected,
    DegradedPolling,
    Reconnecting,
    Failed,
}

public interface IWorkspaceLocalHostApplicationService : IAsyncDisposable
{
    LocalHostConnectionState ConnectionState { get; }
    LocalHostOwnership Ownership { get; }
    string StatusMessage { get; }
    string LocalHostInstanceId { get; }
    long LastObservedSequence { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkspaceInstanceRecord>> GetWorkspaceInstancesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkspaceOperationRecord>> GetOperationsAsync(CancellationToken cancellationToken = default);
    Task<WorkspaceOperationRecord> GetOperationAsync(string operationId, long? afterSequence = null, int? maxEvents = null, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationRecord> CancelOperationAsync(string operationId, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationRecord> PrepareWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationRecord> StartWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationRecord> StopWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationRecord> RecoverWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationRecord> ResetWorkspaceRuntimeAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationRecord> AttachWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceOperationRecord> ReprovisionWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InteractiveAgentSessionRecord>> GetInteractiveSessionsAsync(string? workspaceId = null, CancellationToken cancellationToken = default);
    Task<InteractiveAgentSessionRecord> GetInteractiveSessionAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default);
    Task<InteractiveAgentSessionRecord> CreateInteractiveSessionAsync(string workspaceId, string? title, CancellationToken cancellationToken = default);
    Task<InteractiveSessionAttachResult> AttachInteractiveSessionAsync(string interactiveAgentSessionId, string clientInstanceId, bool requestTransfer, CancellationToken cancellationToken = default);
    Task<InteractiveAgentSessionRecord> DetachInteractiveSessionAttachmentAsync(string interactiveAgentSessionId, string attachmentId, string clientInstanceId, CancellationToken cancellationToken = default);
    Task<HubConnection?> StartLiveUpdatesAsync(Func<WorkspaceEventEnvelope, Task> onEvent, CancellationToken cancellationToken = default);
    Task<bool> StopOwnedLocalHostAsync(CancellationToken cancellationToken = default);
}

public sealed class WorkspaceLocalHostApplicationService : IWorkspaceLocalHostApplicationService
{
    private readonly LocalHostClientOptions _options;
    private LocalHostClient? _client;
    private LocalHostDiscoveryResult? _discovery;

    public WorkspaceLocalHostApplicationService(LocalHostClientOptions? options = null)
    {
        _options = options ?? new LocalHostClientOptions();
    }

    public LocalHostConnectionState ConnectionState { get; private set; } = LocalHostConnectionState.Disconnected;
    public LocalHostOwnership Ownership { get; private set; } = LocalHostOwnership.External;
    public string StatusMessage { get; private set; } = string.Empty;
    public string LocalHostInstanceId => _discovery?.Descriptor.InstanceId ?? string.Empty;
    public long LastObservedSequence { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
        {
            return;
        }

        ConnectionState = LocalHostConnectionState.Discovering;
        StatusMessage = "Discovering LocalHost.";
        _discovery = await LocalHostDiscovery.EnsureLocalHostWithOwnershipAsync(_options, cancellationToken);
        Ownership = _discovery.Ownership;
        var httpClient = new HttpClient { BaseAddress = new Uri(_discovery.Descriptor.BaseUrl, UriKind.Absolute) };
        _client = new LocalHostClient(httpClient, _discovery.Descriptor.BaseUrl);
        ConnectionState = LocalHostConnectionState.WaitingForReadiness;
        StatusMessage = "Waiting for LocalHost readiness.";
        await _client.GetReadinessAsync(cancellationToken);
        ConnectionState = LocalHostConnectionState.Connected;
        StatusMessage = Ownership == LocalHostOwnership.OwnedByDesktop
            ? "Connected to LocalHost started by desktop."
            : "Connected to externally owned LocalHost.";
    }

    public async Task<IReadOnlyList<WorkspaceInstanceRecord>> GetWorkspaceInstancesAsync(CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).ListWorkspaceInstancesAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkspaceOperationRecord>> GetOperationsAsync(CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).ListOperationsAsync(cancellationToken);

    public async Task<WorkspaceOperationRecord> GetOperationAsync(string operationId, long? afterSequence = null, int? maxEvents = null, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).GetOperationAsync(operationId, afterSequence, maxEvents, cancellationToken);

    public async Task<WorkspaceOperationRecord> CancelOperationAsync(string operationId, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).CancelOperationAsync(operationId, new OperationCommandRequest { CommandId = Guid.NewGuid().ToString("n"), RequestedBy = new OperationInitiator { Kind = "avalonia" } }, cancellationToken);

    public async Task<WorkspaceOperationRecord> StartSmokeRunAsync(string templateId, string? timeout = null, string? artifactsRoot = null, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).StartSmokeRunAsync(new SmokeRunOperationRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            TemplateId = templateId,
            Timeout = timeout,
            ArtifactsRoot = artifactsRoot,
            RequestedBy = new OperationInitiator { Kind = "avalonia" },
        }, cancellationToken);

    public async Task<WorkspaceOperationRecord> PrepareWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).StartPrepareWorkspaceAsync(new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = new OperationInitiator { Kind = "avalonia" } }, cancellationToken);

    public async Task<WorkspaceOperationRecord> StartWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).StartWorkspaceLifecycleAsync("start", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = new OperationInitiator { Kind = "avalonia" } }, cancellationToken);

    public async Task<WorkspaceOperationRecord> StopWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).StartWorkspaceLifecycleAsync("stop", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = new OperationInitiator { Kind = "avalonia" } }, cancellationToken);

    public async Task<WorkspaceOperationRecord> RecoverWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).StartWorkspaceLifecycleAsync("recover", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = new OperationInitiator { Kind = "avalonia" } }, cancellationToken);

    public async Task<WorkspaceOperationRecord> ResetWorkspaceRuntimeAsync(string workspaceId, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).StartWorkspaceLifecycleAsync("reset-runtime", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = new OperationInitiator { Kind = "avalonia" } }, cancellationToken);

    public async Task<WorkspaceOperationRecord> AttachWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).StartWorkspaceLifecycleAsync("attach", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = new OperationInitiator { Kind = "avalonia" } }, cancellationToken);

    public async Task<WorkspaceOperationRecord> ReprovisionWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).StartWorkspaceLifecycleAsync("reprovision", new WorkspaceLifecycleRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, RequestedBy = new OperationInitiator { Kind = "avalonia" } }, cancellationToken);

    public async Task<IReadOnlyList<InteractiveAgentSessionRecord>> GetInteractiveSessionsAsync(string? workspaceId = null, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).ListInteractiveAgentSessionsAsync(workspaceId, cancellationToken);

    public async Task<InteractiveAgentSessionRecord> GetInteractiveSessionAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).GetInteractiveAgentSessionAsync(interactiveAgentSessionId, cancellationToken);

    public async Task<InteractiveAgentSessionRecord> CreateInteractiveSessionAsync(string workspaceId, string? title, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).CreateInteractiveAgentSessionAsync(workspaceId, new CreateInteractiveAgentSessionRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = workspaceId, Title = title ?? string.Empty }, cancellationToken);

    public async Task<InteractiveSessionAttachResult> AttachInteractiveSessionAsync(string interactiveAgentSessionId, string clientInstanceId, bool requestTransfer, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).AttachInteractiveSessionAsync(interactiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = interactiveAgentSessionId, CommandId = Guid.NewGuid().ToString("n"), ClientInstanceId = clientInstanceId, AttachmentKind = InteractiveAttachmentKind.WindowsTerminal, RequestTransfer = requestTransfer }, cancellationToken);

    public async Task<InteractiveAgentSessionRecord> DetachInteractiveSessionAttachmentAsync(string interactiveAgentSessionId, string attachmentId, string clientInstanceId, CancellationToken cancellationToken = default)
        => await (await RequireClientAsync(cancellationToken)).DetachInteractiveSessionAttachmentAsync(interactiveAgentSessionId, attachmentId, new DetachInteractiveSessionAttachmentRequest { ClientInstanceId = clientInstanceId, Reason = "desktop_detach" }, cancellationToken);

    public async Task<HubConnection?> StartLiveUpdatesAsync(Func<WorkspaceEventEnvelope, Task> onEvent, CancellationToken cancellationToken = default)
    {
        var client = await RequireClientAsync(cancellationToken);
        try
        {
            var connection = await client.ConnectEventsAsync(async envelope =>
            {
                if (envelope.Sequence <= LastObservedSequence)
                {
                    return;
                }

                LastObservedSequence = envelope.Sequence;
                await onEvent(envelope);
            });
            return connection;
        }
        catch
        {
            ConnectionState = LocalHostConnectionState.DegradedPolling;
            StatusMessage = "Connected to LocalHost with polling fallback.";
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (_discovery?.Ownership == LocalHostOwnership.OwnedByDesktop && _discovery.OwnedProcess is not null && !_discovery.OwnedProcess.HasExited)
        {
            await StopOwnedLocalHostAsync();
        }
    }

    public async Task<bool> StopOwnedLocalHostAsync(CancellationToken cancellationToken = default)
    {
        if (_discovery?.Ownership != LocalHostOwnership.OwnedByDesktop || _discovery.OwnedProcess is null)
        {
            return false;
        }

        var process = _discovery.OwnedProcess;
        if (process.HasExited)
        {
            return false;
        }

        process.StandardInput.Close();
        var startedAt = DateTimeOffset.UtcNow;
        while (!process.HasExited && DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(10))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(100, cancellationToken);
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);
        }

        Ownership = LocalHostOwnership.External;
        _discovery = _discovery with { Ownership = LocalHostOwnership.External, OwnedProcess = null };
        return true;
    }

    private async Task<LocalHostClient> RequireClientAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            await ConnectAsync(cancellationToken);
        }

        return _client!;
    }
}
