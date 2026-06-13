using OpenCode.Workspace.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceCheckpointService
{
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public WorkspaceCheckpointService()
    {
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public WorkspaceCheckpointIndex LoadIndex(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new WorkspaceCheckpointIndex();
        }

        using var reader = File.OpenText(filePath);
        return _deserializer.Deserialize<WorkspaceCheckpointIndex>(reader) ?? new WorkspaceCheckpointIndex();
    }

    public void EnsureCreated(string filePath)
    {
        if (File.Exists(filePath))
        {
            return;
        }

        SaveIndex(filePath, new WorkspaceCheckpointIndex());
    }

    public WorkspaceCheckpointRecord? GetLatest(string filePath)
    {
        return LoadIndex(filePath).Items
            .OrderByDescending(item => item.CreatedUtc)
            .FirstOrDefault();
    }

    public void AddCheckpoint(string filePath, WorkspaceCheckpointRecord record)
    {
        var index = LoadIndex(filePath);
        index.Items.Add(record);
        SaveIndex(filePath, index);
    }

    public void SaveMetadata(string filePath, WorkspaceCheckpointRecord record)
    {
        var yaml = _serializer.Serialize(record);
        File.WriteAllText(filePath, yaml.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private void SaveIndex(string filePath, WorkspaceCheckpointIndex index)
    {
        var yaml = _serializer.Serialize(index);
        File.WriteAllText(filePath, yaml.Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
