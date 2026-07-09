using OpenCode.Workspace.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceSynchronizationStateService
{
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public WorkspaceSynchronizationStateService()
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

    public WorkspaceSynchronizationStateDocument? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var reader = File.OpenText(path);
            var document = _deserializer.Deserialize<WorkspaceSynchronizationStateDocument>(reader);
            return Normalize(document);
        }
        catch (Exception exception) when (exception is YamlException or InvalidCastException or FormatException)
        {
            throw new InvalidOperationException($"Workspace synchronization metadata at '{path}' is invalid. {exception.Message}".Trim(), exception);
        }
    }

    public void Write(string path, WorkspaceSynchronizationStateDocument document)
    {
        var normalized = Normalize(document);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var yaml = _serializer.Serialize(normalized).Replace("\r\n", "\n", StringComparison.Ordinal);
        File.WriteAllText(path, yaml);
    }

    private static WorkspaceSynchronizationStateDocument Normalize(WorkspaceSynchronizationStateDocument? document)
    {
        document ??= new WorkspaceSynchronizationStateDocument();
        var environments = new Dictionary<string, WorkspaceSynchronizationEnvironmentState>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in document.Environments)
        {
            var key = pair.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            environments[key] = NormalizeEnvironmentState(pair.Value);
        }

        var defaultEnvironment = string.IsNullOrWhiteSpace(document.DefaultEnvironment)
            ? environments.Keys.FirstOrDefault()
            : document.DefaultEnvironment.Trim();

        return new WorkspaceSynchronizationStateDocument
        {
            DefaultEnvironment = string.IsNullOrWhiteSpace(defaultEnvironment) ? null : defaultEnvironment,
            Environments = environments,
        };
    }

    private static WorkspaceSynchronizationEnvironmentState NormalizeEnvironmentState(WorkspaceSynchronizationEnvironmentState? state)
    {
        state ??= new WorkspaceSynchronizationEnvironmentState();
        return new WorkspaceSynchronizationEnvironmentState
        {
            SynchronizationState = NormalizeStateName(state.SynchronizationState),
            DriftSummary = state.DriftSummary?.Trim() ?? string.Empty,
            LastValidation = NormalizeOperationState(state.LastValidation),
            LastImport = NormalizeOperationState(state.LastImport),
            LastExport = NormalizeOperationState(state.LastExport),
            LastPull = NormalizeOperationState(state.LastPull),
            ImportedRevision = state.ImportedRevision?.Trim() ?? string.Empty,
            ExportedRevision = state.ExportedRevision?.Trim() ?? string.Empty,
            LastSynchronizedGitRevision = state.LastSynchronizedGitRevision?.Trim() ?? string.Empty,
            SynchronizedSourceSignature = state.SynchronizedSourceSignature?.Trim() ?? string.Empty,
            WorkspaceSourceSignature = state.WorkspaceSourceSignature?.Trim() ?? string.Empty,
            RemoteSourceSignature = state.RemoteSourceSignature?.Trim() ?? string.Empty,
        };
    }

    private static WorkspaceSynchronizationOperationState? NormalizeOperationState(WorkspaceSynchronizationOperationState? state)
    {
        if (state is null)
        {
            return null;
        }

        return new WorkspaceSynchronizationOperationState
        {
            Status = state.Status?.Trim() ?? string.Empty,
            Revision = state.Revision?.Trim() ?? string.Empty,
            TimestampUtc = state.TimestampUtc,
            Summary = state.Summary?.Trim() ?? string.Empty,
        };
    }

    private static string NormalizeStateName(string? state)
        => Enum.TryParse<WorkspaceSynchronizationState>(state, ignoreCase: true, out var parsed)
            ? parsed.ToString()
            : WorkspaceSynchronizationState.Unknown.ToString();
}
