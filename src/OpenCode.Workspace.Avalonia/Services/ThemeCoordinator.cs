namespace OpenCode.Workspace.Avalonia.Services;

public sealed class ThemeCoordinator : IThemeCoordinator
{
    private readonly Action<ThemeMode>? _applyTheme;

    public ThemeCoordinator(ThemeMode initialMode, Action<ThemeMode>? applyTheme = null)
    {
        CurrentMode = initialMode;
        _applyTheme = applyTheme;
    }

    public ThemeMode CurrentMode { get; private set; }

    public void SetTheme(ThemeMode mode)
    {
        CurrentMode = mode;
        _applyTheme?.Invoke(mode);
    }
}
