using Microsoft.AspNetCore.SignalR.Client;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.LocalClient;

namespace OpenCode.Workspace.Avalonia.Tests;

public sealed class DesktopLifecycleCoordinatorTests
{
    [Fact]
    public void NormalClose_WithTray_HidesWindow_AndDoesNotRequestExit()
    {
        var coordinator = CreateCoordinator(trayAvailable: true);

        var outcome = coordinator.HandleMainWindowCloseRequested();

        Assert.Equal(DesktopCloseRequestOutcome.HideToTray, outcome);
        Assert.False(coordinator.ExitRequested);
        Assert.False(coordinator.IsMainWindowVisible);
    }

    [Fact]
    public void TrayUnavailable_CloseRequestsExit_InsteadOfHiding()
    {
        var coordinator = CreateCoordinator(trayAvailable: false);

        var outcome = coordinator.HandleMainWindowCloseRequested();

        Assert.Equal(DesktopCloseRequestOutcome.BeginApplicationExit, outcome);
    }

    [Fact]
    public void TrayOpen_ShowsAndActivatesExistingWindow()
    {
        var windowHost = new FakeDesktopWindowHost { IsMainWindowVisibleValue = false };
        var coordinator = CreateCoordinator(windowHost: windowHost);

        coordinator.ShowMainWindow();

        Assert.Equal(1, windowHost.ShowCallCount);
        Assert.Equal(1, windowHost.ActivateCallCount);
        Assert.True(windowHost.IsMainWindowVisible);
    }

    [Fact]
    public async Task ExplicitExit_WithExternalLocalHost_DoesNotStopIt()
    {
        var localHost = new FakeLocalHostApplicationService { Ownership = LocalHostOwnership.External };
        var trayHost = new FakeDesktopTrayHost();
        var windowHost = new FakeDesktopWindowHost();
        var lifetime = new FakeDesktopApplicationLifetime();
        var coordinator = new DesktopLifecycleCoordinator(windowHost, lifetime, trayHost, localHost);

        await coordinator.RequestExitAsync();

        Assert.True(coordinator.IsExplicitShutdownRequested);
        Assert.Equal(0, localHost.StopOwnedLocalHostCallCount);
        Assert.Equal(1, trayHost.DisposeCallCount);
        Assert.Equal(1, windowHost.CloseCallCount);
        Assert.Equal(1, lifetime.ShutdownCallCount);
    }

    [Fact]
    public async Task ExplicitExit_WithOwnedLocalHostAndNoActiveOperations_StopsIt()
    {
        var localHost = new FakeLocalHostApplicationService { Ownership = LocalHostOwnership.OwnedByDesktop };
        var coordinator = CreateCoordinator(localHost: localHost);

        await coordinator.RequestExitAsync();

        Assert.Equal(1, localHost.StopOwnedLocalHostCallCount);
    }

    [Fact]
    public async Task ExplicitExit_WithOwnedLocalHostAndActiveOperations_LeavesItRunning()
    {
        var localHost = new FakeLocalHostApplicationService
        {
            Ownership = LocalHostOwnership.OwnedByDesktop,
            Operations = [new WorkspaceOperationRecord { OperationId = "op-1", Status = WorkspaceOperationStatus.Running }],
        };
        var coordinator = CreateCoordinator(localHost: localHost);

        await coordinator.RequestExitAsync();

        Assert.Equal(0, localHost.StopOwnedLocalHostCallCount);
    }

    [Fact]
    public async Task RepeatedShutdownRequest_RunsOnce()
    {
        var localHost = new FakeLocalHostApplicationService { Ownership = LocalHostOwnership.OwnedByDesktop };
        var trayHost = new FakeDesktopTrayHost();
        var windowHost = new FakeDesktopWindowHost();
        var lifetime = new FakeDesktopApplicationLifetime();
        var coordinator = new DesktopLifecycleCoordinator(windowHost, lifetime, trayHost, localHost);

        await coordinator.RequestExitAsync();
        await coordinator.RequestExitAsync();

        Assert.Equal(1, localHost.StopOwnedLocalHostCallCount);
        Assert.Equal(1, trayHost.DisposeCallCount);
        Assert.Equal(1, windowHost.CloseCallCount);
        Assert.Equal(1, lifetime.ShutdownCallCount);
    }

    private static DesktopLifecycleCoordinator CreateCoordinator(bool trayAvailable = true, FakeDesktopWindowHost? windowHost = null, FakeDesktopTrayHost? trayHost = null, FakeDesktopApplicationLifetime? lifetime = null, FakeLocalHostApplicationService? localHost = null)
        => new(windowHost ?? new FakeDesktopWindowHost(), lifetime ?? new FakeDesktopApplicationLifetime(), trayHost ?? new FakeDesktopTrayHost { IsAvailableValue = trayAvailable }, localHost ?? new FakeLocalHostApplicationService());

    private sealed class FakeDesktopWindowHost : IDesktopWindowHost
    {
        public bool IsMainWindowVisibleValue { get; set; } = true;
        public int ShowCallCount { get; private set; }
        public int HideCallCount { get; private set; }
        public int ActivateCallCount { get; private set; }
        public int AllowCloseCallCount { get; private set; }
        public int CloseCallCount { get; private set; }
        public bool IsMainWindowVisible => IsMainWindowVisibleValue;
        public void ShowMainWindow() { ShowCallCount++; IsMainWindowVisibleValue = true; }
        public void HideMainWindow() { HideCallCount++; IsMainWindowVisibleValue = false; }
        public void ActivateMainWindow() => ActivateCallCount++;
        public void AllowMainWindowClose() => AllowCloseCallCount++;
        public void CloseMainWindow() { CloseCallCount++; IsMainWindowVisibleValue = false; }
    }

    private sealed class FakeDesktopApplicationLifetime : IDesktopApplicationLifetime
    {
        public int ShutdownCallCount { get; private set; }
        public void Shutdown() => ShutdownCallCount++;
    }

    private sealed class FakeDesktopTrayHost : IDesktopTrayHost
    {
        public bool IsAvailableValue { get; init; } = true;
        public int DisposeCallCount { get; private set; }
        public bool IsAvailable => IsAvailableValue;
        public void Dispose() => DisposeCallCount++;
    }

    private sealed class FakeLocalHostApplicationService : IWorkspaceLocalHostApplicationService
    {
        public LocalHostConnectionState ConnectionState => LocalHostConnectionState.Connected;
        public LocalHostOwnership Ownership { get; set; } = LocalHostOwnership.External;
        public string StatusMessage => string.Empty;
        public string LocalHostInstanceId => "host-1";
        public long LastObservedSequence => 0;
        public IReadOnlyList<WorkspaceOperationRecord> Operations { get; init; } = [];
        public IReadOnlyList<InteractiveAgentSessionRecord> InteractiveSessions { get; init; } = [];
        public int StopOwnedLocalHostCallCount { get; private set; }
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<WorkspaceInstanceRecord>> GetWorkspaceInstancesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkspaceInstanceRecord>>([]);
        public Task<IReadOnlyList<WorkspaceOperationRecord>> GetOperationsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Operations);
        public Task<IReadOnlyList<InteractiveAgentSessionRecord>> GetInteractiveSessionsAsync(string? workspaceId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InteractiveAgentSessionRecord>>(InteractiveSessions);
        public Task<InteractiveAgentSessionRecord> GetInteractiveSessionAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default) => Task.FromResult(InteractiveSessions.First(item => item.InteractiveAgentSessionId == interactiveAgentSessionId));
        public Task<InteractiveAgentSessionRecord> CreateInteractiveSessionAsync(string workspaceId, string? title, CancellationToken cancellationToken = default) => Task.FromResult(new InteractiveAgentSessionRecord { InteractiveAgentSessionId = "interactive-created", WorkspaceId = workspaceId, Title = title ?? "OpenCode session", Status = InteractiveAgentSessionStatus.Detached, CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow, LastActivityUtc = DateTimeOffset.UtcNow });
        public Task<WorkspaceOperationRecord> GetOperationAsync(string operationId, long? afterSequence = null, int? maxEvents = null, CancellationToken cancellationToken = default) => Task.FromResult(Operations.First(item => item.OperationId == operationId));
        public Task<WorkspaceOperationRecord> CancelOperationAsync(string operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> PrepareWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> StartWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> StopWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> RecoverWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> ResetWorkspaceRuntimeAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> AttachWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> ReprovisionWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InteractiveAgentSessionRecord>> GetInteractiveSessionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InteractiveAgentSessionRecord>>([]);
        public Task<InteractiveSessionAttachResult> AttachInteractiveSessionAsync(string interactiveAgentSessionId, string clientInstanceId, bool requestTransfer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InteractiveAgentSessionRecord> DetachInteractiveSessionAttachmentAsync(string interactiveAgentSessionId, string attachmentId, string clientInstanceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HubConnection?> StartLiveUpdatesAsync(Func<WorkspaceEventEnvelope, Task> onEvent, CancellationToken cancellationToken = default) => Task.FromResult<HubConnection?>(null);
        public Task<bool> StopOwnedLocalHostAsync(CancellationToken cancellationToken = default)
        {
            StopOwnedLocalHostCallCount++;
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
