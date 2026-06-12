namespace OpenCode.Workspace.Manager.Services;

/// <summary>
/// Maps the user-facing recommended Nerd Font names to the actual Windows font
/// face names used by Terminal and to the archive names used by the installer.
/// </summary>
public static class NerdFontCatalog
{
    public static IReadOnlyList<NerdFontDefinition> SupportedFonts { get; } =
    [
        new("JetBrainsMono Nerd Font", "JetBrainsMono", ["JetBrainsMono NFM", "JetBrainsMono NF", "JetBrainsMono Nerd Font"]),
        new("CaskaydiaCove Nerd Font", "CascadiaCode", ["CaskaydiaCove NFM", "CaskaydiaCove NF", "CaskaydiaCove Nerd Font"]),
        new("FiraCode Nerd Font", "FiraCode", ["FiraCode Nerd Font Mono", "FiraCode Nerd Font"]),
    ];

    public static NerdFontDefinition? FindByDisplayName(string displayName)
        => SupportedFonts.FirstOrDefault(font => string.Equals(font.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
}

public sealed record NerdFontDefinition(string DisplayName, string ArchiveName, IReadOnlyList<string> CandidateFaceNames);
