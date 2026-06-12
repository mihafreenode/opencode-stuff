using System.IO;
using System.Windows;
using OpenCode.Workspace.Manager.Services;

namespace OpenCode.Workspace.Manager;

/// <summary>
/// App bootstrap stays explicit on purpose. The MVP does not use a DI container
/// because the startup graph is still small enough to read top-to-bottom.
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCode.Workspace.Manager");
        var bootstrapper = new AppBootstrapper();
        var viewModel = bootstrapper.CreateMainWindowViewModel(AppContext.BaseDirectory, appDataRoot, PoLocalizationService.DetectLanguageCode());

        var mainWindow = new MainWindow
        {
            DataContext = viewModel,
        };

        mainWindow.Show();
        await viewModel.InitializeAsync();
    }
}
