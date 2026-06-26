namespace OpenCode.Workspace.Platform;

public sealed class HostCapabilityReport
{
    public required PlatformKind Platform { get; init; }
    public required string Architecture { get; init; }
    public required IReadOnlyList<HostCapabilitySection> Sections { get; init; }

    public HostCapabilityEntry? FindEntry(string id)
        => Sections.SelectMany(section => section.Entries)
            .FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));

    public static HostCapabilityReport Empty(PlatformKind platform, string architecture)
        => new()
        {
            Platform = platform,
            Architecture = architecture,
            Sections = [],
        };
}
