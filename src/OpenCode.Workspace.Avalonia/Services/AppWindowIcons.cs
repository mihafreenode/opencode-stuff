using System;
using Avalonia.Controls;
using Avalonia.Platform;

namespace OpenCode.Workspace.Avalonia.Services;

public static class AppWindowIcons
{
    public const string AssetPath = "avares://OpenCode.Workspace.Avalonia/Assets/opencode-stuff-satchel-icon.ico";

    private static readonly Lazy<WindowIcon> CachedIcon = new(LoadIcon);

    public static WindowIcon GetAppIcon() => CachedIcon.Value;

    public static void Apply(Window window, Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Icon = owner?.Icon ?? GetAppIcon();
    }

    private static WindowIcon LoadIcon()
        => new(AssetLoader.Open(new Uri(AssetPath)));
}
