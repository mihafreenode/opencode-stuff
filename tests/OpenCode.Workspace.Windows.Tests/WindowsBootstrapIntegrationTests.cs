using System.IO;
using System.Threading;
using System.Windows;
using OpenCode.Workspace.Manager;
using OpenCode.Workspace.Manager.Services;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class WindowsBootstrapIntegrationTests
{
    [Fact]
    public void LocalizationLoading_ReturnsEnglishAndSlovenianStrings()
    {
        var root = TestPaths.RepositoryRoot;
        var localizationRoot = Path.Combine(root, "Localization");

        var english = new PoLocalizationService(localizationRoot, "en");
        var slovenian = new PoLocalizationService(localizationRoot, "sl");

        Assert.Equal("OpenCode Workspace Manager", english.Get("app.title"));
        Assert.Equal("Nadzorna plošča delovnih prostorov", slovenian.Get("dashboard.title"));
    }

    [Fact]
    public void AppBootstrapper_CreatesMainWindowViewModel()
    {
        var bootstrapper = new AppBootstrapper();
        var viewModel = bootstrapper.CreateMainWindowViewModel(
            TestPaths.RepositoryRoot,
            Path.Combine(Path.GetTempPath(), $"ocwm-bootstrap-{Guid.NewGuid():N}"),
            "en");

        Assert.Equal("OpenCode Workspace Manager", viewModel.Title);
        Assert.NotEmpty(viewModel.AvailableFeatures);
    }

    [Fact]
    public void MainWindow_CanBeConstructedOnStaThread()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = Application.Current ?? new App
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };

                var window = new MainWindow();
                Assert.NotNull(window);
                application.Shutdown();
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(captured);
    }

    [Fact]
    public async Task WindowsHostBuildIntegration_DotNetHostIsAvailable()
    {
        var runner = new OpenCode.Workspace.Core.Runtime.ProcessRunner();
        var result = await runner.RunAsync("cmd.exe", ["/c", "dotnet", "--info"]);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.StandardOutputLines, line => line.Contains(".NET SDK", StringComparison.OrdinalIgnoreCase));
    }
}
