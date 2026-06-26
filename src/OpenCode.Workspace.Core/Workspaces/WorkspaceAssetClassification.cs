using YamlDotNet.Serialization;

namespace OpenCode.Workspace.Core.Workspaces;

public enum WorkspaceAssetClass
{
    Durable,
    Generated,
    Ephemeral,
}

public sealed class WorkspaceAssetClassification
{
    [YamlMember(Alias = "path")]
    public string Path { get; init; } = string.Empty;

    [YamlMember(Alias = "isDirectory")]
    public bool IsDirectory { get; init; }

    [YamlMember(Alias = "assetClass")]
    public WorkspaceAssetClass AssetClass { get; init; }

    [YamlMember(Alias = "reason")]
    public string Reason { get; init; } = string.Empty;
}

public sealed class WorkspaceBackupManifest
{
    [YamlMember(Alias = "archiveFileName")]
    public string ArchiveFileName { get; init; } = string.Empty;

    [YamlMember(Alias = "exportedUtc")]
    public DateTimeOffset ExportedUtc { get; init; }

    [YamlMember(Alias = "archiveSizeBytes")]
    public long ArchiveSizeBytes { get; init; }

    [YamlMember(Alias = "workspaceName")]
    public string WorkspaceName { get; init; } = string.Empty;

    [YamlMember(Alias = "workspaceId")]
    public string WorkspaceId { get; init; } = string.Empty;

    [YamlMember(Alias = "workspaceRoot")]
    public string WorkspaceRoot { get; init; } = string.Empty;

    [YamlMember(Alias = "configurationPath")]
    public string ConfigurationPath { get; init; } = string.Empty;

    [YamlMember(Alias = "timelinePath")]
    public string TimelinePath { get; init; } = string.Empty;

    [YamlMember(Alias = "latestSavePointUtc")]
    public DateTimeOffset? LatestSavePointUtc { get; init; }

    [YamlMember(Alias = "latestCheckpointUtc")]
    public DateTimeOffset? LatestCheckpointUtc { get; init; }

    [YamlMember(Alias = "includedFileCount")]
    public int IncludedFileCount { get; init; }

    [YamlMember(Alias = "excludedFileCount")]
    public int ExcludedFileCount { get; init; }

    [YamlMember(Alias = "warnings")]
    public List<string> Warnings { get; init; } = new();

    [YamlMember(Alias = "sourceOfTruthLocations")]
    public List<string> SourceOfTruthLocations { get; init; } = new();

    [YamlMember(Alias = "durableAssetGroups")]
    public List<string> DurableAssetGroups { get; init; } = new();

    [YamlMember(Alias = "generatedAssetGroups")]
    public List<string> GeneratedAssetGroups { get; init; } = new();

    [YamlMember(Alias = "ephemeralAssetGroups")]
    public List<string> EphemeralAssetGroups { get; init; } = new();

    [YamlMember(Alias = "ownershipNotes")]
    public List<string> OwnershipNotes { get; init; } = new();

    [YamlMember(Alias = "warning")]
    public string Warning { get; init; } = string.Empty;

    [YamlMember(Alias = "items")]
    public List<WorkspaceAssetClassification> Items { get; init; } = new();
}
