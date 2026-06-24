using OpenCode.Workspace.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceTimelineService
{
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public WorkspaceTimelineService()
    {
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public WorkspaceTimeline Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new WorkspaceTimeline();
        }

        using var reader = File.OpenText(filePath);
        return _deserializer.Deserialize<WorkspaceTimeline>(reader) ?? new WorkspaceTimeline();
    }

    public void EnsureCreated(string filePath)
    {
        if (File.Exists(filePath))
        {
            return;
        }

        Save(filePath, new WorkspaceTimeline());
    }

    public void Append(string filePath, string type, string summary, string details, string branch = "", string commitSha = "", IReadOnlyList<string>? affectedPaths = null)
    {
        var timeline = Load(filePath);
        timeline.Events.Add(new WorkspaceTimelineEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            OccurredUtc = DateTimeOffset.UtcNow,
            Summary = summary,
            Details = details,
            Branch = branch,
            CommitSha = commitSha,
            AffectedPaths = affectedPaths?.ToList() ?? [],
        });

        Save(filePath, timeline);
    }

    public DateTimeOffset? GetLastPublishUtc(string filePath)
    {
        return Load(filePath).Events
            .Where(item => string.Equals(item.Type, "publish-succeeded", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.OccurredUtc)
            .Select(item => (DateTimeOffset?)item.OccurredUtc)
            .FirstOrDefault();
    }

    private void Save(string filePath, WorkspaceTimeline timeline)
    {
        var content = _serializer.Serialize(timeline);
        File.WriteAllText(filePath, content.Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
