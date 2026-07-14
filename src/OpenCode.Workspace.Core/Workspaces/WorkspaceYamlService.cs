using OpenCode.Workspace.Core.Models;
using YamlDotNet.Core;
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
        try
        {
            var yaml = File.ReadAllText(filePath);
            var definition = _deserializer.Deserialize<WorkspaceDefinition>(yaml);
            var rootMapping = ParseMapping(yaml);
            var knowledgePacks = ReadKnowledgePacks(rootMapping);
            definition = CopyDefinition(definition, knowledgePacks);
            return Normalize(definition);
        }
        catch (Exception exception) when (exception is YamlException or InvalidCastException or FormatException)
        {
            throw new InvalidOperationException($"Workspace configuration at '{filePath}' is invalid. {exception.Message}".Trim(), exception);
        }
    }

    public string Write(WorkspaceDefinition definition)
    {
        var normalizedDefinition = Normalize(definition);
        var rootMapping = ParseMapping(_serializer.Serialize(WriteableWorkspaceDefinition.From(normalizedDefinition)));
        WriteKnowledgePacks(rootMapping, normalizedDefinition.KnowledgePacks);
        return SaveYaml(rootMapping);
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
            Oracle = new OracleWorkspacePreferences
            {
                DatabaseImage = definition.Oracle.DatabaseImage is null ? null : OracleDatabaseImageCatalog.ResolveDatabaseImage(definition),
                HostPort = definition.Oracle.HostPort is > 0 ? definition.Oracle.HostPort.Value : null,
                OrdsPort = definition.Oracle.OrdsPort is > 0 ? definition.Oracle.OrdsPort.Value : null,
                Apex = NormalizeOracleApexPreferences(definition.Oracle.Apex),
            },
            Analytics = new AnalyticsWorkspacePreferences
            {
                MarimoPort = definition.Analytics.MarimoPort is > 0 ? definition.Analytics.MarimoPort.Value : null,
            },
            KnowledgePacks = definition.KnowledgePacks
                .Where(pack => !string.IsNullOrWhiteSpace(pack.Provider))
                .Select(pack => new WorkspaceKnowledgePackDefinition
                {
                    Provider = pack.Provider.Trim(),
                    Enabled = pack.Enabled,
                    Mode = WorkspaceKnowledgePackModes.Normalize(pack.Mode),
                    Settings = CloneYamlNode(pack.Settings),
                })
                .ToList(),
        };
    }

    private static WorkspaceDefinition CopyDefinition(WorkspaceDefinition definition, List<WorkspaceKnowledgePackDefinition> knowledgePacks)
    {
        return new WorkspaceDefinition
        {
            Workspace = definition.Workspace,
            Provider = definition.Provider,
            Runtime = definition.Runtime,
            Features = definition.Features,
            Skills = definition.Skills,
            Services = definition.Services,
            Mcp = definition.Mcp,
            Terminal = definition.Terminal,
            Agent = definition.Agent,
            Oracle = definition.Oracle,
            Analytics = definition.Analytics,
            KnowledgePacks = knowledgePacks,
        };
    }

    private static OracleApexWorkspacePreferences NormalizeOracleApexPreferences(OracleApexWorkspacePreferences? apex)
    {
        apex ??= new OracleApexWorkspacePreferences();
        var environments = new Dictionary<string, OracleApexEnvironmentPreferences>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in apex.Environments)
        {
            var key = pair.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            environments[key] = NormalizeOracleApexEnvironmentPreferences(pair.Value);
        }

        var defaultEnvironment = string.IsNullOrWhiteSpace(apex.DefaultEnvironment)
            ? environments.Keys.FirstOrDefault()
            : apex.DefaultEnvironment.Trim();

        return new OracleApexWorkspacePreferences
        {
            DefaultEnvironment = string.IsNullOrWhiteSpace(defaultEnvironment) ? null : defaultEnvironment,
            Environments = environments,
        };
    }

    private static OracleApexEnvironmentPreferences NormalizeOracleApexEnvironmentPreferences(OracleApexEnvironmentPreferences? environment)
    {
        environment ??= new OracleApexEnvironmentPreferences();
        return new OracleApexEnvironmentPreferences
        {
            Workspace = NormalizeOptionalValue(environment.Workspace),
            ParsingSchema = NormalizeOptionalValue(environment.ParsingSchema),
            ApplicationId = environment.ApplicationId is > 0 ? environment.ApplicationId.Value : null,
            SqlclProfile = NormalizeOptionalValue(environment.SqlclProfile),
            SyncMode = NormalizeOptionalValue(environment.SyncMode),
            SourcePath = NormalizeOptionalValue(environment.SourcePath),
            DeploymentProfile = NormalizeOptionalValue(environment.DeploymentProfile),
        };
    }

    private static string? NormalizeOptionalValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<WorkspaceKnowledgePackDefinition> ReadKnowledgePacks(YamlMappingNode rootMapping)
    {
        if (!TryGetChild(rootMapping, "knowledgePacks", out var packsNode) || packsNode is not YamlSequenceNode sequence)
        {
            return new List<WorkspaceKnowledgePackDefinition>();
        }

        var results = new List<WorkspaceKnowledgePackDefinition>();
        foreach (var child in sequence.Children.OfType<YamlMappingNode>())
        {
            var provider = ReadScalar(child, "provider");
            if (string.IsNullOrWhiteSpace(provider))
            {
                continue;
            }

            var enabled = ReadBool(child, "enabled") ?? true;
            var mode = WorkspaceKnowledgePackModes.Normalize(ReadScalar(child, "mode"));
            TryGetChild(child, "settings", out var settingsNode);

            results.Add(new WorkspaceKnowledgePackDefinition
            {
                Provider = provider,
                Enabled = enabled,
                Mode = mode,
                Settings = CloneYamlNode(settingsNode),
            });
        }

        return results;
    }

    private static void WriteKnowledgePacks(YamlMappingNode rootMapping, IReadOnlyList<WorkspaceKnowledgePackDefinition> knowledgePacks)
    {
        if (knowledgePacks.Count == 0)
        {
            RemoveChild(rootMapping, "knowledgePacks");
            return;
        }

        var sequence = new YamlSequenceNode();
        foreach (var pack in knowledgePacks)
        {
            if (string.IsNullOrWhiteSpace(pack.Provider))
            {
                continue;
            }

            var item = new YamlMappingNode
            {
                { "provider", pack.Provider },
                { "enabled", pack.Enabled ? "true" : "false" },
                { "mode", WorkspaceKnowledgePackModes.Normalize(pack.Mode) },
            };

            if (pack.Settings is not null)
            {
                var settings = CloneYamlNode(pack.Settings);
                if (settings is not null)
                {
                    item.Add("settings", settings);
                }
            }

            sequence.Add(item);
        }

        rootMapping.Children[new YamlScalarNode("knowledgePacks")] = sequence;
    }

    private static string SaveYaml(YamlMappingNode rootMapping)
    {
        var stream = new YamlStream(new YamlDocument(rootMapping));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        var yaml = writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (yaml.StartsWith("---\n", StringComparison.Ordinal))
        {
            yaml = yaml[4..];
        }

        if (yaml.EndsWith("...\n", StringComparison.Ordinal))
        {
            yaml = yaml[..^4];
        }

        return yaml;
    }

    private static bool TryGetChild(YamlMappingNode mapping, string key, out YamlNode? value)
    {
        foreach (var child in mapping.Children)
        {
            if (child.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal))
            {
                value = child.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static void RemoveChild(YamlMappingNode mapping, string key)
    {
        var match = mapping.Children.Keys
            .OfType<YamlScalarNode>()
            .FirstOrDefault(candidate => string.Equals(candidate.Value, key, StringComparison.Ordinal));
        if (match is not null)
        {
            mapping.Children.Remove(match);
        }
    }

    private static string? ReadScalar(YamlMappingNode mapping, string key)
        => TryGetChild(mapping, key, out var value) && value is YamlScalarNode scalar
            ? scalar.Value
            : null;

    private static bool? ReadBool(YamlMappingNode mapping, string key)
        => bool.TryParse(ReadScalar(mapping, key), out var value) ? value : null;

    private static YamlNode? CloneYamlNode(YamlNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var stream = new YamlStream(new YamlDocument(node));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);

        var cloneStream = new YamlStream();
        using var reader = new StringReader(writer.ToString());
        cloneStream.Load(reader);
        return cloneStream.Documents[0].RootNode;
    }

    private sealed class WriteableWorkspaceDefinition
    {
        [YamlMember(Alias = "workspace")]
        public WorkspaceMetadata Workspace { get; init; } = new();

        [YamlMember(Alias = "provider")]
        public WorkspaceProviderDefinition Provider { get; init; } = new();

        [YamlMember(Alias = "runtime")]
        public WorkspaceRuntimeDefinition Runtime { get; init; } = new();

        [YamlMember(Alias = "features")]
        public List<string> Features { get; init; } = new();

        [YamlMember(Alias = "skills")]
        public List<string> Skills { get; init; } = new();

        [YamlMember(Alias = "services")]
        public List<string> Services { get; init; } = new();

        [YamlMember(Alias = "mcp")]
        public List<string> Mcp { get; init; } = new();

        [YamlMember(Alias = "terminal")]
        public TerminalPreferences Terminal { get; init; } = new();

        [YamlMember(Alias = "agent")]
        public AgentPreferences Agent { get; init; } = new();

        [YamlMember(Alias = "oracle")]
        public OracleWorkspacePreferences Oracle { get; init; } = new();

        [YamlMember(Alias = "analytics")]
        public AnalyticsWorkspacePreferences Analytics { get; init; } = new();

        public static WriteableWorkspaceDefinition From(WorkspaceDefinition definition)
        {
            return new WriteableWorkspaceDefinition
            {
                Workspace = definition.Workspace,
                Provider = definition.Provider,
                Runtime = definition.Runtime,
                Features = definition.Features.ToList(),
                Skills = definition.Skills.ToList(),
                Services = definition.Services.ToList(),
                Mcp = definition.Mcp.ToList(),
                Terminal = definition.Terminal,
                Agent = definition.Agent,
                Oracle = definition.Oracle,
                Analytics = definition.Analytics,
            };
        }
    }
}
