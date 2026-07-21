using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Avalonia.ViewModels;

namespace OpenCode.Workspace.Avalonia;

public partial class App : Application
{
    private StartupLog? _startupLog;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var appDataRoot = WorkspaceAppDataPaths.GetWorkspaceManagerDataRoot();
            _startupLog = new StartupLog(appDataRoot);
            _startupLog.Write("App framework initialization started.");
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            {
                if (eventArgs.ExceptionObject is Exception exception)
                {
                    _startupLog?.WriteException("Unhandled exception", exception);
                }
            };
            TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            {
                _startupLog?.WriteException("Unobserved task exception", eventArgs.Exception);
            };

            var themeCoordinator = new ThemeCoordinator(ThemeMode.System, ApplyThemeMode);
            var bootstrapper = new AvaloniaAppBootstrapper();
            _startupLog.Write("Creating shell view model.");
            var bootstrap = bootstrapper.CreateShellViewModel(
                AppContext.BaseDirectory,
                appDataRoot,
                PoLocalizationService.DetectLanguageCode(),
                themeCoordinator);
            var shell = bootstrap.Shell;
            _startupLog.Write("Shell view model created.");

            var mainWindow = new MainWindow
            {
                DataContext = shell,
            };
            var windowHost = new AvaloniaDesktopWindowHost(mainWindow);
            var trayHost = new AvaloniaDesktopTrayHost(this, OperatingSystem.IsWindows());
            var lifecycleCoordinator = new DesktopLifecycleCoordinator(windowHost, new AvaloniaDesktopApplicationLifetime(desktop), trayHost, bootstrap.LocalHostService, message => _startupLog?.Write(message));
            var tray = new TrayViewModel(bootstrap.LocalHostService, lifecycleCoordinator);
            trayHost.Initialize(tray);
            shell.SetClipboardService(new AvaloniaClipboardService(mainWindow));
            shell.SetInteractionService(new AvaloniaWorkspaceInteractionService(mainWindow));
            mainWindow.Closing += (_, eventArgs) =>
            {
                var outcome = lifecycleCoordinator.HandleMainWindowCloseRequested();
                if (outcome == DesktopCloseRequestOutcome.HideToTray)
                {
                    eventArgs.Cancel = true;
                }
                else if (outcome == DesktopCloseRequestOutcome.BeginApplicationExit)
                {
                    eventArgs.Cancel = true;
                    _ = lifecycleCoordinator.RequestExitAsync();
                }
            };
            mainWindow.Opened += async (_, _) =>
            {
                _startupLog?.Write("MainWindow opened.");
                try
                {
                    await lifecycleCoordinator.InitializeAsync();
                    _startupLog?.Write("Workspace load start.");
                    await shell.InitializeAsync();
                    _startupLog?.Write("Workspace load end.");
                }
                catch (Exception exception)
                {
                    _startupLog?.WriteException("Workspace load failed", exception);
                }
            };
            _startupLog.Write("MainWindow created.");
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyThemeMode(ThemeMode mode)
    {
        RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

}
