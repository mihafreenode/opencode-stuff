using OpenCode.Workspace.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceRuntimeStateService
{
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public WorkspaceRuntimeStateService()
    {
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public WorkspaceRuntimeStateRecord? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var reader = File.OpenText(path);
        var model = _deserializer.Deserialize<WorkspaceRuntimeStateYamlModel>(reader);
        return model is null
            ? null
            : new WorkspaceRuntimeStateRecord
            {
                ResolvedEngine = model.ResolvedEngine ?? string.Empty,
                ResolvedPlatform = model.ResolvedPlatform ?? string.Empty,
                CompatibilityMode = model.CompatibilityMode ?? string.Empty,
                LastSuccessfulProvision = DateTimeOffset.TryParse(model.LastSuccessfulProvision, out var provisionedAt) ? provisionedAt : null,
            };
    }

    public void Write(string path, WorkspaceRuntimeStateRecord state)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var content = _serializer.Serialize(new WorkspaceRuntimeStateYamlModel
        {
            ResolvedEngine = state.ResolvedEngine,
            ResolvedPlatform = state.ResolvedPlatform,
            CompatibilityMode = state.CompatibilityMode,
            LastSuccessfulProvision = state.LastSuccessfulProvision?.ToString("O"),
        });
        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    public WorkspaceRuntimeStateRecord CreateState(ResolvedRuntimePlan plan, DateTimeOffset? lastSuccessfulProvision = null)
    {
        return new WorkspaceRuntimeStateRecord
        {
            ResolvedEngine = plan.Runtime,
            ResolvedPlatform = plan.TargetPlatform,
            CompatibilityMode = plan.CompatibilityMode.ToString(),
            LastSuccessfulProvision = lastSuccessfulProvision,
        };
    }

    private sealed class WorkspaceRuntimeStateYamlModel
    {
        public string? ResolvedEngine { get; init; }
        public string? ResolvedPlatform { get; init; }
        public string? CompatibilityMode { get; init; }
        public string? LastSuccessfulProvision { get; init; }
    }
}
