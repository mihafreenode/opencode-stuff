using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenCode.Workspace.Core.Workspaces;

/// <summary>
/// The MVP does not yet extract inbox archives automatically, but it already
/// tracks import state in a portable YAML file so the future archive importer can
/// remain deterministic and avoid re-importing the same archive repeatedly.
/// </summary>
public sealed class ArchiveImportStateStore
{
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public ArchiveImportStateStore()
    {
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public ArchiveImportState Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new ArchiveImportState();
        }

        using var reader = File.OpenText(filePath);
        return _deserializer.Deserialize<ArchiveImportState>(reader) ?? new ArchiveImportState();
    }

    public void Save(string filePath, ArchiveImportState state)
    {
        var yaml = _serializer.Serialize(state);
        File.WriteAllText(filePath, yaml);
    }

    public void MarkImported(string filePath, string archiveName, string checksum, string extractionTarget, DateTimeOffset importedUtc)
    {
        var state = Load(filePath);
        var entry = state.Items.FirstOrDefault(item => string.Equals(item.ArchiveName, archiveName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            state.Items.Add(new ArchiveImportEntry
            {
                ArchiveName = archiveName,
                Checksum = checksum,
                ExtractionTarget = extractionTarget,
                ImportedUtc = importedUtc,
            });
        }
        else
        {
            entry.Checksum = checksum;
            entry.ExtractionTarget = extractionTarget;
            entry.ImportedUtc = importedUtc;
        }

        Save(filePath, state);
    }
}

public sealed class ArchiveImportState
{
    [YamlMember(Alias = "items")]
    public List<ArchiveImportEntry> Items { get; init; } = new();
}

public sealed class ArchiveImportEntry
{
    [YamlMember(Alias = "archiveName")]
    public string ArchiveName { get; set; } = string.Empty;

    [YamlMember(Alias = "checksum")]
    public string Checksum { get; set; } = string.Empty;

    [YamlMember(Alias = "extractionTarget")]
    public string ExtractionTarget { get; set; } = string.Empty;

    [YamlMember(Alias = "importedUtc")]
    public DateTimeOffset ImportedUtc { get; set; }
}
