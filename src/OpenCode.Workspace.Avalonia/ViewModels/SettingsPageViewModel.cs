using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Platform.Windows;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class SettingsPageViewModel : PageViewModel
{
    private readonly IThemeCoordinator _themeCoordinator;
    private readonly IDesktopShellService _desktopShellService;
    private readonly Func<WorkspaceSummaryViewModel?> _selectedWorkspaceProvider;
    private ThemeMode _selectedThemeMode;
    private string _terminalProfileStatus = string.Empty;

    public SettingsPageViewModel(IThemeCoordinator themeCoordinator, AppBuildInfo appBuildInfo, IDesktopShellService desktopShellService, Func<WorkspaceSummaryViewModel?> selectedWorkspaceProvider)
        : base("Settings", "Theme selection and basic app information.")
    {
        _themeCoordinator = themeCoordinator;
        _desktopShellService = desktopShellService;
        _selectedWorkspaceProvider = selectedWorkspaceProvider;
        AppVersionLine = $"Version: {appBuildInfo.AssemblyVersion} ({appBuildInfo.InformationalVersion})";
        AppBuildLine = $"Build: {appBuildInfo.BuildConfiguration} | Commit: {appBuildInfo.GitCommitSha}";
        GeneratorLine = $"Workspace generator: {appBuildInfo.WorkspaceGeneratorVersion} | Schema: {appBuildInfo.GeneratedSchemaVersion}";
        RuntimeStateExplanation = ".opencode/local/ is machine-local and ignored by Git.";
        ThemeModes = Enum.GetValues<ThemeMode>();
        _selectedThemeMode = themeCoordinator.CurrentMode;
        SetupWindowsTerminalProfileCommand = new AsyncRelayCommand(SetupWindowsTerminalProfileAsync, CanSetupWindowsTerminalProfile);

        DetailTitle = "Shell settings";
        DetailSummary = "Choose the shell theme, review app diagnostics context, and configure the managed Windows Terminal profile for the selected workspace.";
        DetailItems.Add(new DetailItemViewModel("Runtime-state", RuntimeStateExplanation));
        RefreshTerminalProfileDetails();
    }

    public ThemeMode[] ThemeModes { get; }
    public string AppVersionLine { get; }
    public string AppBuildLine { get; }
    public string GeneratorLine { get; }
    public string RuntimeStateExplanation { get; }
    public AsyncRelayCommand SetupWindowsTerminalProfileCommand { get; }
    public string TerminalProfileStatus
    {
        get => _terminalProfileStatus;
        private set => SetProperty(ref _terminalProfileStatus, value);
    }

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

    public void RefreshWorkspaceContext()
    {
        SetupWindowsTerminalProfileCommand.RaiseCanExecuteChanged();
        RefreshTerminalProfileDetails();
    }

    private bool CanSetupWindowsTerminalProfile()
        => _selectedWorkspaceProvider() is { HasSnapshot: true };

    private async Task SetupWindowsTerminalProfileAsync()
    {
        var workspace = _selectedWorkspaceProvider();
        if (workspace?.Snapshot is null)
        {
            return;
        }

        var result = await _desktopShellService.EnsureWindowsTerminalProfileAsync(workspace.RootPath, workspace.Snapshot);
        TerminalProfileStatus = result.Message;
        RefreshTerminalProfileDetails(result.Setup);
    }

    private void RefreshTerminalProfileDetails(WindowsTerminalProfileSetupResult? result = null)
    {
        DetailItems.Clear();
        DetailItems.Add(new DetailItemViewModel("Runtime-state", RuntimeStateExplanation));
        var workspace = _selectedWorkspaceProvider();
        DetailItems.Add(new DetailItemViewModel("Selected workspace", workspace?.Name ?? "No workspace selected"));
        DetailItems.Add(new DetailItemViewModel("Windows Terminal profile", string.IsNullOrWhiteSpace(TerminalProfileStatus) ? "Not run yet" : TerminalProfileStatus));
        if (result is { } setup)
        {
            if (!string.IsNullOrWhiteSpace(setup.ProfileName))
            {
                DetailItems.Add(new DetailItemViewModel("Profile name", setup.ProfileName));
            }

            if (!string.IsNullOrWhiteSpace(setup.ResolvedFontFace))
            {
                DetailItems.Add(new DetailItemViewModel("Resolved font", setup.ResolvedFontFace));
            }

            if (!string.IsNullOrWhiteSpace(setup.FragmentPath))
            {
                DetailItems.Add(new DetailItemViewModel("Fragment path", setup.FragmentPath));
            }

            if (setup.Status == WindowsTerminalProfileSetupStatus.Failed || setup.Status == WindowsTerminalProfileSetupStatus.Unavailable)
            {
                DetailItems.Add(new DetailItemViewModel("Failure", string.IsNullOrWhiteSpace(setup.FailureReason) ? setup.Summary : setup.FailureReason));
            }
        }

        DetailActions.Clear();
        DetailActions.Add(new ActionItemViewModel("Configure Windows Terminal profile", "Create or update the managed OpenCode Stuff Windows Terminal profile for the selected workspace.", CanSetupWindowsTerminalProfile(), CanSetupWindowsTerminalProfile() ? string.Empty : "Select a workspace with loaded details first.", SetupWindowsTerminalProfileCommand));
    }
}
