using System.IO;
using System.Diagnostics;
using System.Windows;
using OpenCode.Workspace.Manager.Services;

namespace OpenCode.Workspace.Manager;

/// <summary>
/// App bootstrap stays explicit on purpose. The MVP does not use a DI container
/// because the startup graph is still small enough to read top-to-bottom.
/// </summary>
public partial class App : Application
{
    private SingleInstanceService? _singleInstanceService;
    private StartupDiagnosticsService? _startupDiagnostics;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCode.Workspace.Manager");
        var localization = new PoLocalizationService(Path.Combine(AppContext.BaseDirectory, "Localization"), PoLocalizationService.DetectLanguageCode());
        var diagnostics = new StartupDiagnosticsService(appDataRoot);
        _startupDiagnostics = diagnostics;
        diagnostics.Log("Application startup begin.");
        DispatcherUnhandledException += (_, args) =>
        {
            _startupDiagnostics?.Log($"Unhandled UI exception: {args.Exception}");
        };

        _singleInstanceService = new SingleInstanceService("OpenCode.Workspace.Manager");
        if (!_singleInstanceService.IsPrimaryInstance)
        {
            var activated = _singleInstanceService.TryActivateExistingInstance(Process.GetCurrentProcess());
            diagnostics.Log($"Secondary instance blocked. Activated existing instance: {activated}.");
            AppDialogService.ShowOk(
                null,
                localization,
                localization.Get("dialog.singleInstance.title"),
                activated
                    ? localization.Get("dialog.singleInstance.activated")
                    : localization.Get("dialog.singleInstance.notActivated"));
            Shutdown();
            return;
        }

        var bootstrapper = new AppBootstrapper();
        var viewModel = bootstrapper.CreateMainWindowViewModel(AppContext.BaseDirectory, appDataRoot, PoLocalizationService.DetectLanguageCode());

        var mainWindow = new MainWindow
        {
            DataContext = viewModel,
        };

        mainWindow.Show();
        diagnostics.Log("Main window shown.");
        diagnostics.Log($"Main window initialized. CanStartCreateWorkspaceFlow={viewModel.CanStartCreateWorkspaceFlow}.");
        mainWindow.BeginPromptForQuickTutorialIfNeeded(diagnostics);
        _ = viewModel.InitializeBackgroundAsync(diagnostics);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceService?.Dispose();
        base.OnExit(e);
    }
}
