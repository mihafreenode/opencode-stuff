using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using OpenCode.Workspace.Avalonia.ViewModels;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class AvaloniaDesktopWindowHost(Window mainWindow) : IDesktopWindowHost
{
    private bool _allowClose;

    public bool IsMainWindowVisible => mainWindow.IsVisible;

    public void ShowMainWindow()
    {
        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }

        mainWindow.Show();
    }

    public void HideMainWindow() => mainWindow.Hide();

    public void ActivateMainWindow()
    {
        mainWindow.Activate();
        mainWindow.Topmost = true;
        mainWindow.Topmost = false;
    }

    public void AllowMainWindowClose() => _allowClose = true;

    public void CloseMainWindow()
    {
        _allowClose = true;
        mainWindow.Close();
    }

    public bool CanCloseWindow() => _allowClose;
}

public sealed class AvaloniaDesktopApplicationLifetime(IClassicDesktopStyleApplicationLifetime desktopLifetime) : IDesktopApplicationLifetime
{
    public void Shutdown() => desktopLifetime.Shutdown();
}

public sealed class AvaloniaDesktopTrayHost : IDesktopTrayHost
{
    private readonly Application _application;
    private TrayIcon? _trayIcon;
    private TrayIcons? _trayIcons;

    public AvaloniaDesktopTrayHost(Application application, bool isAvailable)
    {
        _application = application;
        IsAvailable = isAvailable;
    }

    public bool IsAvailable { get; }

    public void Initialize(TrayViewModel trayViewModel)
    {
        if (!IsAvailable || _trayIcon is not null)
        {
            return;
        }

        var menu = new NativeMenu();
        menu.Add(new NativeMenuItem("Open OpenCode Workspace") { Command = trayViewModel.ShowMainWindowCommand });
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(new NativeMenuItem("Exit") { Command = trayViewModel.ExitApplicationCommand });

        _trayIcon = new TrayIcon
        {
            ToolTipText = "OpenCode Workspace",
            Icon = AppWindowIcons.GetAppIcon(),
            Menu = menu,
            IsVisible = true,
        };
        _trayIcons = new TrayIcons { _trayIcon };
        TrayIcon.SetIcons(_application, _trayIcons);
    }

    public void Dispose()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.IsVisible = false;
        TrayIcon.SetIcons(_application, null);
        _trayIcon.Dispose();
        _trayIcon = null;
        _trayIcons = null;
    }
}
