using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var appDataRoot = WorkspaceAppDataPaths.GetWorkspaceManagerDataRoot();

            var themeCoordinator = new ThemeCoordinator(ThemeMode.System, ApplyThemeMode);
            var bootstrapper = new AvaloniaAppBootstrapper();
            var shell = bootstrapper.CreateShellViewModel(
                AppContext.BaseDirectory,
                appDataRoot,
                PoLocalizationService.DetectLanguageCode(),
                themeCoordinator);

            desktop.MainWindow = new MainWindow
            {
                DataContext = shell,
            };
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
