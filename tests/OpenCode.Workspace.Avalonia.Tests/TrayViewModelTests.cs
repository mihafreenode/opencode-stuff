using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Avalonia.ViewModels;
using OpenCode.Workspace.LocalClient;

namespace OpenCode.Workspace.Avalonia.Tests;

public sealed class TrayViewModelTests
{
    [Fact]
    public void ArchitectureGuard_TrayViewModel_DoesNotOwnProcessOrWindowTermination()
    {
        var repoRoot = GetRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "ViewModels", "TrayViewModel.cs"));
        var appSourceCount = Directory.GetFiles(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Count(text => text.Contains("new WorkspaceLocalHostApplicationService()", StringComparison.Ordinal));

        Assert.DoesNotContain("StopOwnedLocalHostAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.Exit", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new MainWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InteractiveAgentSession", source, StringComparison.Ordinal);
        Assert.Equal(1, appSourceCount);
    }

    [Fact]
    public async Task Refresh_ProjectsCanonicalWorkspaceAndOperationState()
    {
        var service = new FakeLocalHostApplicationService
        {
            WorkspaceInstances =
            [
                new WorkspaceInstanceRecord
                {
                    WorkspaceInstanceId = "workspace-alpha",
                    WorkspaceName = "Alpha",
                    Status = "Ready",
                    ActiveOperationIds = ["op-1"],
                },
            ],
            Operations =
            [
                new WorkspaceOperationRecord
                {
                    OperationId = "op-1",
                    CurrentPhase = "provisioning",
                    ProgressMessage = "Installing services.",
                },
            ],
        };
        var tray = new TrayViewModel(service, new FakeLifecycleCoordinator { IsTrayAvailableValue = true });

        await tray.RefreshAsync();

        var item = Assert.Single(tray.Workspaces);
        Assert.Equal("Alpha", item.WorkspaceName);
        Assert.Equal("op-1", item.ActiveOperationId);
        Assert.Equal("provisioning", item.ActiveOperationPhase);
        Assert.Equal("Installing services.", item.ProgressMessage);
    }

    [Fact]
    public void ShowMainWindowCommand_UsesLifecycleCoordinatorAvailability()
    {
        var coordinator = new FakeLifecycleCoordinator { IsTrayAvailableValue = true };
        var tray = new TrayViewModel(new FakeLocalHostApplicationService(), coordinator);

        tray.ShowMainWindowCommand.Execute(null);

        Assert.Equal(1, coordinator.ShowMainWindowCallCount);
    }

    [Fact]
    public async Task ExitApplicationCommand_DelegatesToLifecycleCoordinator()
    {
        var coordinator = new FakeLifecycleCoordinator { IsTrayAvailableValue = true };
        var tray = new TrayViewModel(new FakeLocalHostApplicationService(), coordinator);

        await tray.ExitApplicationCommand.ExecuteAsync();

        Assert.Equal(1, coordinator.RequestExitCallCount);
    }

    private sealed class FakeLocalHostApplicationService : IWorkspaceLocalHostApplicationService
    {
        public LocalHostConnectionState ConnectionState => LocalHostConnectionState.Connected;
        public LocalHostOwnership Ownership => LocalHostOwnership.External;
        public string StatusMessage => string.Empty;
        public string LocalHostInstanceId => "host-1";
        public long LastObservedSequence => 0;
        public IReadOnlyList<WorkspaceInstanceRecord> WorkspaceInstances { get; init; } = Array.Empty<WorkspaceInstanceRecord>();
        public IReadOnlyList<WorkspaceOperationRecord> Operations { get; init; } = Array.Empty<WorkspaceOperationRecord>();
        public IReadOnlyList<InteractiveAgentSessionRecord> InteractiveSessions { get; init; } = Array.Empty<InteractiveAgentSessionRecord>();
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<WorkspaceInstanceRecord>> GetWorkspaceInstancesAsync(CancellationToken cancellationToken = default) => Task.FromResult(WorkspaceInstances);
        public Task<IReadOnlyList<WorkspaceOperationRecord>> GetOperationsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Operations);
        public Task<IReadOnlyList<InteractiveAgentSessionRecord>> GetInteractiveSessionsAsync(string? workspaceId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InteractiveAgentSessionRecord>>(string.IsNullOrWhiteSpace(workspaceId) ? InteractiveSessions : InteractiveSessions.Where(item => string.Equals(item.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase)).ToArray());
        public Task<InteractiveAgentSessionRecord> GetInteractiveSessionAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default) => Task.FromResult(InteractiveSessions.First(item => item.InteractiveAgentSessionId == interactiveAgentSessionId));
        public Task<InteractiveAgentSessionRecord> CreateInteractiveSessionAsync(string workspaceId, string? title, CancellationToken cancellationToken = default) => Task.FromResult(new InteractiveAgentSessionRecord { InteractiveAgentSessionId = "interactive-created", WorkspaceId = workspaceId, Title = title ?? "OpenCode session", Status = InteractiveAgentSessionStatus.Detached, CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow, LastActivityUtc = DateTimeOffset.UtcNow });
        public Task<WorkspaceOperationRecord> GetOperationAsync(string operationId, long? afterSequence = null, int? maxEvents = null, CancellationToken cancellationToken = default) => Task.FromResult(Operations.First(item => item.OperationId == operationId));
        public Task<WorkspaceOperationRecord> CancelOperationAsync(string operationId, CancellationToken cancellationToken = default) => Task.FromResult(Operations.First(item => item.OperationId == operationId));
        public Task<WorkspaceOperationRecord> PrepareWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> StartWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> StopWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> RecoverWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> ResetWorkspaceRuntimeAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> AttachWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> ReprovisionWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InteractiveAgentSessionRecord>> GetInteractiveSessionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InteractiveAgentSessionRecord>>(Array.Empty<InteractiveAgentSessionRecord>());
        public Task<InteractiveSessionAttachResult> AttachInteractiveSessionAsync(string interactiveAgentSessionId, string clientInstanceId, bool requestTransfer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InteractiveAgentSessionRecord> DetachInteractiveSessionAttachmentAsync(string interactiveAgentSessionId, string attachmentId, string clientInstanceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Microsoft.AspNetCore.SignalR.Client.HubConnection?> StartLiveUpdatesAsync(Func<WorkspaceEventEnvelope, Task> onEvent, CancellationToken cancellationToken = default) => Task.FromResult<Microsoft.AspNetCore.SignalR.Client.HubConnection?>(null);
        public Task<bool> StopOwnedLocalHostAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeLifecycleCoordinator : IDesktopLifecycleCoordinator
    {
        public bool IsTrayAvailableValue { get; init; }
        public bool IsTrayAvailable => IsTrayAvailableValue;
        public bool IsExplicitShutdownRequested { get; private set; }
        public bool IsMainWindowVisible => true;
        public bool ExitRequested { get; private set; }
        public LocalHostOwnership LocalHostOwnership => LocalHostOwnership.External;
        public int ShowMainWindowCallCount { get; private set; }
        public int RequestExitCallCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public DesktopCloseRequestOutcome HandleMainWindowCloseRequested() => DesktopCloseRequestOutcome.HideToTray;
        public void ShowMainWindow() => ShowMainWindowCallCount++;
        public Task RequestExitAsync(CancellationToken cancellationToken = default)
        {
            RequestExitCallCount++;
            ExitRequested = true;
            IsExplicitShutdownRequested = true;
            return Task.CompletedTask;
        }
    }

    private static string GetRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));
}
