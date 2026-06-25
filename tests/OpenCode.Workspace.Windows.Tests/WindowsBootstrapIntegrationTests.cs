using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using OpenCode.Workspace.Manager;
using OpenCode.Workspace.Manager.Services;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class WindowsBootstrapIntegrationTests
{
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
            App? application = null;
            MainWindow? window = null;
            try
            {
                application = Application.Current as App;
                if (application is null)
                {
                    application = new App
                    {
                        ShutdownMode = ShutdownMode.OnExplicitShutdown,
                    };
                    application.InitializeComponent();
                }

                window = new MainWindow();
                Assert.NotNull(window);
            }
            catch (Exception exception)
            {
                captured = exception;
            }
            finally
            {
                try
                {
                    if (window is not null)
                    {
                        window.Close();
                    }

                    if (application is not null)
                    {
                        application.Shutdown();
                    }

                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
                catch (Exception exception)
                {
                    captured ??= exception;
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        Assert.Null(captured);
    }

}
