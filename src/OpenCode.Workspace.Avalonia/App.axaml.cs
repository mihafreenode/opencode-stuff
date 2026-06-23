using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;

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
            var shell = bootstrapper.CreateShellViewModel(
                AppContext.BaseDirectory,
                appDataRoot,
                PoLocalizationService.DetectLanguageCode(),
                themeCoordinator);
            _startupLog.Write("Shell view model created.");

            var mainWindow = new MainWindow
            {
                DataContext = shell,
            };
            shell.SetClipboardService(new AvaloniaClipboardService(mainWindow));
            shell.SetInteractionService(new AvaloniaWorkspaceInteractionService(mainWindow));
            mainWindow.Opened += async (_, _) =>
            {
                _startupLog?.Write("MainWindow opened.");
                try
                {
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
