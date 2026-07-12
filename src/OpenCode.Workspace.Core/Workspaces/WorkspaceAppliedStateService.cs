using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenCode.Workspace.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceAppliedStateService
{
    private static readonly Regex GeneratedTimestampLine = new(@"(?m)^# Generated: .+$", RegexOptions.Compiled);

    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public WorkspaceAppliedStateService()
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

    public WorkspaceAppliedState? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var reader = File.OpenText(path);
        return _deserializer.Deserialize<WorkspaceAppliedState>(reader);
    }

    public void Write(string path, WorkspaceAppliedState state)
    {
        var content = _serializer.Serialize(state);
        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    public WorkspaceAppliedState CreateState(GeneratedWorkspaceArtifacts artifacts)
    {
        return new WorkspaceAppliedState
        {
            DesiredStateHash = artifacts.DesiredStateHash,
            WorkspaceDefinitionHash = artifacts.WorkspaceDefinitionHash,
            WorkspaceImageTag = artifacts.WorkspaceImageTag,
            WorkspaceImageInputHash = artifacts.WorkspaceImageInputHash,
            AppliedUtc = DateTimeOffset.UtcNow,
            AppVersion = typeof(WorkspaceAppliedStateService).Assembly.GetName().Version?.ToString(),
        };
    }

    public static string ComputeHash(params string[] parts)
    {
        using var sha256 = SHA256.Create();
        var payload = string.Join("\n---\n", parts.Select(NormalizeGeneratedMetadata));
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }

    private static string NormalizeGeneratedMetadata(string content)
        => GeneratedTimestampLine.Replace(content, "# Generated: <normalized>");
}
