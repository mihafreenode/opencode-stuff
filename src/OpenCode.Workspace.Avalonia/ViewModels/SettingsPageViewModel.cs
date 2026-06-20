using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class SettingsPageViewModel : PageViewModel
{
    private readonly IThemeCoordinator _themeCoordinator;
    private ThemeMode _selectedThemeMode;

    public SettingsPageViewModel(IThemeCoordinator themeCoordinator, AppBuildInfo appBuildInfo)
        : base("Settings", "Theme selection and basic app information.")
    {
        _themeCoordinator = themeCoordinator;
        AppVersionLine = $"Version: {appBuildInfo.AssemblyVersion} ({appBuildInfo.InformationalVersion})";
        AppBuildLine = $"Build: {appBuildInfo.BuildConfiguration} | Commit: {appBuildInfo.GitCommitSha}";
        GeneratorLine = $"Workspace generator: {appBuildInfo.WorkspaceGeneratorVersion} | Schema: {appBuildInfo.GeneratedSchemaVersion}";
        RuntimeStateExplanation = ".opencode/local/ is machine-local and ignored by Git.";
        ThemeModes = Enum.GetValues<ThemeMode>();
        _selectedThemeMode = themeCoordinator.CurrentMode;

        DetailTitle = "Shell settings";
        DetailSummary = "Choose the shell theme and review app diagnostics context.";
        DetailItems.Add(new DetailItemViewModel("Runtime-state", RuntimeStateExplanation));
    }

    public ThemeMode[] ThemeModes { get; }
    public string AppVersionLine { get; }
    public string AppBuildLine { get; }
    public string GeneratorLine { get; }
    public string RuntimeStateExplanation { get; }

    public ThemeMode SelectedThemeMode
    {
        get => _selectedThemeMode;
        set
        {
            if (SetProperty(ref _selectedThemeMode, value))
            {
                _themeCoordinator.SetTheme(value);
            }
        }
    }
}
