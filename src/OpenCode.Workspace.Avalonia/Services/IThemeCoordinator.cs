namespace OpenCode.Workspace.Avalonia.Services;

public interface IThemeCoordinator
{
    ThemeMode CurrentMode { get; }
    void SetTheme(ThemeMode mode);
}
