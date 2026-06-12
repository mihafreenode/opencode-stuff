using OpenCode.Workspace.Core.Models;
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

    private static WorkspaceDefinition Normalize(WorkspaceDefinition definition)
    {
        return new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Name = definition.Workspace.Name.Trim(),
                Image = string.IsNullOrWhiteSpace(definition.Workspace.Image) ? "ubuntu:24.04" : definition.Workspace.Image.Trim(),
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
