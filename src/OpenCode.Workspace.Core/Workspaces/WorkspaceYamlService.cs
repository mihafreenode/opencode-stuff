using OpenCode.Workspace.Core.Models;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenCode.Workspace.Core.Workspaces;

/// <summary>
/// Handles the user-owned workspace.yaml file. This service stays intentionally
/// small because the YAML shape itself should remain obvious and contributor
/// friendly.
/// </summary>
public sealed class WorkspaceYamlService
{
    public const string SchemaVersion = "workspace-yaml-v1";

    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public WorkspaceYamlService()
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

    public WorkspaceDefinition Read(string filePath)
    {
        using var reader = File.OpenText(filePath);
        var definition = _deserializer.Deserialize<WorkspaceDefinition>(reader);
        return Normalize(definition);
    }

    public string Write(WorkspaceDefinition definition)
    {
        return _serializer.Serialize(Normalize(definition));
    }

    public void WriteToFile(string filePath, WorkspaceDefinition definition)
    {
        var normalizedDefinition = Normalize(definition);
        if (!File.Exists(filePath))
        {
            WriteNewFile(filePath, Write(normalizedDefinition));
            return;
        }

        var stream = new YamlStream();
        using (var reader = File.OpenText(filePath))
        {
            stream.Load(reader);
        }
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode rootMapping)
        {
            throw new InvalidOperationException($"Workspace configuration at '{filePath}' is not a YAML mapping document and cannot be updated safely.");
        }

        var updatedDocument = ParseMapping(Write(normalizedDefinition));
        foreach (var child in updatedDocument.Children)
        {
            rootMapping.Children[child.Key] = child.Value;
        }

        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        File.WriteAllText(filePath, writer.ToString());
    }

    private static YamlMappingNode ParseMapping(string yaml)
    {
        using var reader = new StringReader(yaml);
        var stream = new YamlStream();
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static void WriteNewFile(string filePath, string yaml)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, yaml);
    }

    private static WorkspaceDefinition Normalize(WorkspaceDefinition definition)
    {
        return new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Id = string.IsNullOrWhiteSpace(definition.Workspace.Id) ? WorkspacePathBuilder.Slugify(definition.Workspace.Name) : WorkspacePathBuilder.Slugify(definition.Workspace.Id),
                Name = definition.Workspace.Name.Trim(),
                Image = string.IsNullOrWhiteSpace(definition.Workspace.Image) ? "ubuntu:24.04" : definition.Workspace.Image.Trim(),
            },
            Provider = new WorkspaceProviderDefinition
            {
                Type = string.IsNullOrWhiteSpace(definition.Provider.Type) ? "git" : definition.Provider.Type.Trim(),
                Url = string.IsNullOrWhiteSpace(definition.Provider.Url) ? null : definition.Provider.Url.Trim(),
            },
            Runtime = new WorkspaceRuntimeDefinition
            {
                Default = string.IsNullOrWhiteSpace(definition.Runtime.Default) ? "default" : definition.Runtime.Default.Trim(),
                Node = definition.Runtime.GetEffectiveNodeMajorVersion(),
            },
            Features = definition.Features.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Skills = definition.Skills.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Services = definition.Services.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Mcp = definition.Mcp.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Terminal = new TerminalPreferences
            {
                InstallIfMissing = definition.Terminal.InstallIfMissing,
                Font = new TerminalFontPreferences
                {
                    Provider = string.IsNullOrWhiteSpace(definition.Terminal.Font.Provider) ? "nerd-fonts" : definition.Terminal.Font.Provider.Trim(),
                    Family = string.IsNullOrWhiteSpace(definition.Terminal.Font.Family) ? "JetBrainsMono Nerd Font" : definition.Terminal.Font.Family.Trim(),
                },
                Prompt = new TerminalPromptPreferences
                {
                    Provider = string.IsNullOrWhiteSpace(definition.Terminal.Prompt.Provider) ? "starship" : definition.Terminal.Prompt.Provider.Trim(),
                },
                Utilities = new TerminalUtilityPreferences
                {
                    Zoxide = definition.Terminal.Utilities.Zoxide,
                    Fzf = definition.Terminal.Utilities.Fzf,
                },
            },
            Agent = new AgentPreferences
            {
                Profile = string.IsNullOrWhiteSpace(definition.Agent.Profile) ? AgentProfileResolver.BuiltInDefault.ProfileId : definition.Agent.Profile.Trim(),
                Provider = string.IsNullOrWhiteSpace(definition.Agent.Provider) ? null : definition.Agent.Provider.Trim(),
                Connection = string.IsNullOrWhiteSpace(definition.Agent.Connection) ? null : definition.Agent.Connection.Trim(),
                Model = string.IsNullOrWhiteSpace(definition.Agent.Model) ? null : definition.Agent.Model.Trim(),
            },
        };
    }
}
