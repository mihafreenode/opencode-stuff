using OpenCode.Workspace.Core.Models;
using YamlDotNet.Core;
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
        => ReadWithStatus(path).State;

    public WorkspaceRuntimeStateReadResult ReadWithStatus(string path)
    {
        if (!File.Exists(path))
        {
            return new WorkspaceRuntimeStateReadResult
            {
                Status = WorkspaceRuntimeStateReadStatus.Missing,
            };
        }

        try
        {
            using var reader = File.OpenText(path);
            var model = _deserializer.Deserialize<WorkspaceRuntimeStateYamlModel>(reader);
            return new WorkspaceRuntimeStateReadResult
            {
                Status = WorkspaceRuntimeStateReadStatus.Loaded,
                State = model is null
                    ? null
                    : new WorkspaceRuntimeStateRecord
                    {
                        ResolvedEngine = model.ResolvedEngine ?? string.Empty,
                        ResolvedPlatform = model.ResolvedPlatform ?? string.Empty,
                        CompatibilityMode = model.CompatibilityMode ?? string.Empty,
                        LastSuccessfulProvision = DateTimeOffset.TryParse(model.LastSuccessfulProvision, out var provisionedAt) ? provisionedAt : null,
                        Resources = model.Resources ?? new WorkspaceManagedRuntimeResources(),
                    },
            };
        }
        catch (Exception exception) when (exception is YamlException or InvalidCastException or FormatException)
        {
            // Runtime state is machine-local cache data. If it becomes corrupt, the
            // workspace should continue loading and regenerate it after a known-good
            // runtime operation instead of blocking repository access.
            return new WorkspaceRuntimeStateReadResult
            {
                Status = WorkspaceRuntimeStateReadStatus.Corrupted,
            };
        }
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
            Resources = state.Resources,
        });
        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    public WorkspaceRuntimeStateRecord CreateState(ResolvedRuntimePlan plan, DateTimeOffset? lastSuccessfulProvision = null, WorkspaceManagedRuntimeResources? resources = null)
    {
        return new WorkspaceRuntimeStateRecord
        {
            ResolvedEngine = plan.Runtime,
            ResolvedPlatform = plan.TargetPlatform,
            CompatibilityMode = plan.CompatibilityMode.ToString(),
            LastSuccessfulProvision = lastSuccessfulProvision,
            Resources = resources ?? new WorkspaceManagedRuntimeResources(),
        };
    }

    private sealed class WorkspaceRuntimeStateYamlModel
    {
        public string? ResolvedEngine { get; init; }
        public string? ResolvedPlatform { get; init; }
        public string? CompatibilityMode { get; init; }
        public string? LastSuccessfulProvision { get; init; }
        public WorkspaceManagedRuntimeResources? Resources { get; init; }
    }
}
