using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Markup;
using OpenCode.Workspace.AppSupport;
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
            _startupDiagnostics?.Log(FormatStartupException("Unhandled UI exception", args.Exception));
        };
        AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
        {
            if (args.Exception is XamlParseException)
            {
                _startupDiagnostics?.Log(FormatStartupException("First-chance XAML exception", args.Exception));
            }
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

        try
        {
            var bootstrapper = new AppBootstrapper();
            var viewModel = bootstrapper.CreateMainWindowViewModel(AppContext.BaseDirectory, appDataRoot, PoLocalizationService.DetectLanguageCode());

            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            mainWindow.Show();
            diagnostics.Log("Main window shown.");
            diagnostics.Log($"Main window initialized. CanStartCreateWorkspaceFlow={viewModel.CanStartCreateWorkspaceFlow}.");
            diagnostics.Log($"App executable path: {viewModel.AppExecutablePath}");
            diagnostics.Log($"App build configuration: {viewModel.AppBuildConfiguration}");
            diagnostics.Log($"App assembly version: {viewModel.AppAssemblyVersion}");
            diagnostics.Log($"App informational version: {viewModel.AppInformationalVersion}");
            diagnostics.Log($"App git commit SHA: {viewModel.AppGitCommitSha}");
            diagnostics.Log($"App build timestamp: {viewModel.AppBuildTimestamp}");
            diagnostics.Log($"Workspace generator version: {viewModel.WorkspaceGeneratorVersion}");
            diagnostics.Log($"Generated schema version: {viewModel.GeneratedSchemaVersion}");
            mainWindow.BeginPromptForQuickTutorialIfNeeded(diagnostics);
            _ = viewModel.InitializeBackgroundAsync(diagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.Log(FormatStartupException("Startup failure before main window became usable", exception));
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceService?.Dispose();
        base.OnExit(e);
    }

    private static string FormatStartupException(string heading, Exception exception)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"{heading}: {exception}");

        if (exception is XamlParseException xamlException)
        {
            builder.AppendLine($"XAML LineNumber={xamlException.LineNumber}, LinePosition={xamlException.LinePosition}, BaseUri='{xamlException.BaseUri}'.");
            if (xamlException.InnerException is not null)
            {
                builder.AppendLine("XAML InnerException:");
                builder.AppendLine(xamlException.InnerException.ToString());
            }
        }
        else if (exception.InnerException is not null)
        {
            builder.AppendLine("InnerException:");
            builder.AppendLine(exception.InnerException.ToString());
        }

        return builder.ToString();
    }
}
