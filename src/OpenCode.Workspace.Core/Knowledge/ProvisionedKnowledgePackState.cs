namespace OpenCode.Workspace.Core.Knowledge;

public sealed class ProvisionedKnowledgePackState
{
    public string ProviderVersion { get; init; } = string.Empty;

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> SourceHashes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> SourceLocations { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset ImportTimestamp { get; init; }

    public Dictionary<string, string> GeneratedFileHashes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Warnings { get; init; } = new();

    public List<string> SkippedFiles { get; init; } = new();
}

public sealed class ProvisionedKnowledgePackContent
{
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> SourceHashes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> SourceLocations { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> GeneratedFiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Warnings { get; init; } = new();
}
