namespace OpenCode.Workspace.Platform;

public enum PlatformKind
{
    Unknown,
    Windows,
    Linux,
    MacOS,
}

public enum HostCapabilityStatus
{
    Unknown,
    Available,
    Warning,
    Unavailable,
}

public sealed class HostCapabilityEntry
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required HostCapabilityStatus Status { get; init; }
    public required string Summary { get; init; }
    public string Details { get; init; } = string.Empty;

    public bool IsAvailable => Status == HostCapabilityStatus.Available;
}

public sealed class HostCapabilitySection
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyList<HostCapabilityEntry> Entries { get; init; }
}
