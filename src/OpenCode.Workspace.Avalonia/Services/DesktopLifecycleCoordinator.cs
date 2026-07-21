using OpenCode.Workspace.LocalClient;

namespace OpenCode.Workspace.Avalonia.Services;

public interface IDesktopWindowHost
{
    bool IsMainWindowVisible { get; }
    void ShowMainWindow();
    void HideMainWindow();
    void ActivateMainWindow();
    void AllowMainWindowClose();
    void CloseMainWindow();
}

public interface IDesktopApplicationLifetime
{
    void Shutdown();
}

public interface IDesktopTrayHost : IDisposable
{
    bool IsAvailable { get; }
}

public enum DesktopCloseRequestOutcome
{
    AllowClose,
    HideToTray,
    BeginApplicationExit,
}

public interface IDesktopLifecycleCoordinator
{
    bool IsTrayAvailable { get; }
    bool IsExplicitShutdownRequested { get; }
    bool IsMainWindowVisible { get; }
    bool ExitRequested { get; }
    LocalHostOwnership LocalHostOwnership { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    DesktopCloseRequestOutcome HandleMainWindowCloseRequested();
    void ShowMainWindow();
    Task RequestExitAsync(CancellationToken cancellationToken = default);
}

public sealed class DesktopLifecycleCoordinator : IDesktopLifecycleCoordinator
{
    private readonly IDesktopWindowHost _windowHost;
    private readonly IDesktopApplicationLifetime _applicationLifetime;
    private readonly IDesktopTrayHost _trayHost;
    private readonly IWorkspaceLocalHostApplicationService _localHostService;
    private readonly Action<string>? _log;
    private bool _initialized;
    private bool _shutdownSequenceStarted;

    public DesktopLifecycleCoordinator(IDesktopWindowHost windowHost, IDesktopApplicationLifetime applicationLifetime, IDesktopTrayHost trayHost, IWorkspaceLocalHostApplicationService localHostService, Action<string>? log = null)
    {
        _windowHost = windowHost;
        _applicationLifetime = applicationLifetime;
        _trayHost = trayHost;
        _localHostService = localHostService;
        _log = log;
    }

    public bool IsTrayAvailable => _trayHost.IsAvailable;
    public bool IsExplicitShutdownRequested { get; private set; }
    public bool IsMainWindowVisible => _windowHost.IsMainWindowVisible;
    public bool ExitRequested { get; private set; }
    public LocalHostOwnership LocalHostOwnership => _localHostService.Ownership;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await _localHostService.ConnectAsync(cancellationToken);
        _log?.Invoke(_localHostService.Ownership == LocalHostOwnership.OwnedByDesktop
            ? "LocalHost started and owned by desktop."
            : "LocalHost discovered externally.");
    }

    public DesktopCloseRequestOutcome HandleMainWindowCloseRequested()
    {
        if (IsExplicitShutdownRequested)
        {
            return DesktopCloseRequestOutcome.AllowClose;
        }

        if (IsTrayAvailable)
        {
            _windowHost.HideMainWindow();
            _log?.Invoke("Main window hidden to tray.");
            return DesktopCloseRequestOutcome.HideToTray;
        }

        _log?.Invoke("Tray unavailable. Close request will exit application.");
        return DesktopCloseRequestOutcome.BeginApplicationExit;
    }

    public void ShowMainWindow()
    {
        _windowHost.ShowMainWindow();
        _windowHost.ActivateMainWindow();
        _log?.Invoke("Main window restored from tray.");
    }

    public async Task RequestExitAsync(CancellationToken cancellationToken = default)
    {
        if (_shutdownSequenceStarted)
        {
            return;
        }

        _shutdownSequenceStarted = true;
        ExitRequested = true;
        IsExplicitShutdownRequested = true;
        _log?.Invoke("Explicit shutdown requested.");

        var shouldStopOwnedLocalHost = false;
        if (_localHostService.Ownership == LocalHostOwnership.OwnedByDesktop)
        {
            var activeOperations = await _localHostService.GetOperationsAsync(cancellationToken);
            var hasActiveOperations = activeOperations.Any(operation => operation.Status is WorkspaceOperationStatus.Pending or WorkspaceOperationStatus.Running || operation.CancellationState == WorkspaceOperationCancellationState.Requested);
            if (hasActiveOperations)
            {
                _log?.Invoke("Active operation prevented LocalHost shutdown. Leaving owned LocalHost running.");
            }
            else
            {
                shouldStopOwnedLocalHost = true;
            }
        }
        else
        {
            _log?.Invoke("External LocalHost left running.");
        }

        _trayHost.Dispose();
        _log?.Invoke("Tray disposed.");
        _windowHost.AllowMainWindowClose();

        if (shouldStopOwnedLocalHost)
        {
            await _localHostService.StopOwnedLocalHostAsync(cancellationToken);
            _log?.Invoke("Owned LocalHost stopped.");
        }

        _windowHost.CloseMainWindow();
        _applicationLifetime.Shutdown();
    }
}
