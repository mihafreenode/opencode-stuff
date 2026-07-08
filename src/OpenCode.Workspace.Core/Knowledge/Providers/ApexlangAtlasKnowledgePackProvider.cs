using System.Text.Json;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using YamlDotNet.RepresentationModel;

namespace OpenCode.Workspace.Core.Knowledge.Providers;

public sealed class ApexlangAtlasKnowledgePackProvider : IKnowledgePackProvider
{
    private const string MetadataFileName = "apexlang_meta_data.json";
    private const string BuiltinCatalogFileName = "builtin_catalog.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IKnowledgePackRemoteSourceFetcher _remoteSourceFetcher;

    public ApexlangAtlasKnowledgePackProvider(IKnowledgePackRemoteSourceFetcher? remoteSourceFetcher = null)
    {
        _remoteSourceFetcher = remoteSourceFetcher ?? new HttpKnowledgePackRemoteSourceFetcher();
    }

    public string ProviderId => "apexlang-atlas";

    public string Version => "1";

    public bool IsApplicable(WorkspaceDefinition definition, WorkspaceKnowledgePackDefinition configuration)
        => string.Equals(configuration.Provider, ProviderId, StringComparison.OrdinalIgnoreCase);

    public async Task<ProvisionedKnowledgePackContent> GenerateAsync(KnowledgePackContext context, CancellationToken cancellationToken = default)
    {
        var settings = ParseSettings(context.Configuration.Settings);
        var metadataSource = await ResolveSourceAsync(context, settings.BuildId, MetadataFileName, settings.MetadataUrl, cancellationToken);
        var builtinSource = await ResolveSourceAsync(context, settings.BuildId, BuiltinCatalogFileName, settings.BuiltinCatalogUrl, cancellationToken);

        using var metadataDocument = JsonDocument.Parse(metadataSource.Content);
        using var builtinDocument = JsonDocument.Parse(builtinSource.Content);

        var model = BuildModel(metadataDocument.RootElement, builtinDocument.RootElement, settings.BuildId);
        var generatedFiles = BuildGeneratedFiles(model);

        return new ProvisionedKnowledgePackContent
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["buildId"] = model.BuildId,
                ["schemaVersion"] = model.SchemaVersion,
            },
            SourceHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataFileName] = metadataSource.Hash,
                [BuiltinCatalogFileName] = builtinSource.Hash,
            },
            SourceLocations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataFileName] = metadataSource.Location,
                [BuiltinCatalogFileName] = builtinSource.Location,
            },
            GeneratedFiles = generatedFiles,
        };
    }

    private static ApexlangAtlasSettings ParseSettings(YamlNode? settingsNode)
    {
        if (settingsNode is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException("Knowledge Pack provider 'apexlang-atlas' requires mapping settings with buildId, metadataUrl, or builtinCatalogUrl.");
        }

        return new ApexlangAtlasSettings(
            ReadSetting(mapping, "buildId"),
            ReadSetting(mapping, "metadataUrl"),
            ReadSetting(mapping, "builtinCatalogUrl"));
    }

    private async Task<ResolvedSource> ResolveSourceAsync(KnowledgePackContext context, string? buildId, string fileName, string? downloadUrl, CancellationToken cancellationToken)
    {
        var workspaceLocalPath = Path.Combine(context.Paths.OpencodePath, "apexlang", "source", fileName);
        if (File.Exists(workspaceLocalPath))
        {
            var content = await File.ReadAllTextAsync(workspaceLocalPath, cancellationToken);
            return new ResolvedSource(workspaceLocalPath, content, WorkspaceAppliedStateService.ComputeHash(content.Replace("\r\n", "\n", StringComparison.Ordinal)));
        }

        var cacheDirectory = Path.Combine(context.Paths.OpencodePath, "cache", "apexlang", string.IsNullOrWhiteSpace(buildId) ? "default" : buildId);
        var cachePath = Path.Combine(cacheDirectory, fileName);
        if (File.Exists(cachePath))
        {
            var content = await File.ReadAllTextAsync(cachePath, cancellationToken);
            return new ResolvedSource(cachePath, content, WorkspaceAppliedStateService.ComputeHash(content.Replace("\r\n", "\n", StringComparison.Ordinal)));
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidOperationException($"Atlas source '{fileName}' was not found locally or in cache, and no download URL is configured.");
        }

        var downloadedContent = await _remoteSourceFetcher.FetchAsync(downloadUrl, cancellationToken);
        Directory.CreateDirectory(cacheDirectory);
        await File.WriteAllTextAsync(cachePath, downloadedContent.Replace("\r\n", "\n", StringComparison.Ordinal), cancellationToken);
        return new ResolvedSource(downloadUrl, downloadedContent, WorkspaceAppliedStateService.ComputeHash(downloadedContent.Replace("\r\n", "\n", StringComparison.Ordinal)));
    }

    private static ApexlangAtlasModel BuildModel(JsonElement metadataRoot, JsonElement builtinRoot, string? configuredBuildId)
    {
        var properties = ReadProperties(metadataRoot).ToList();
        var dependencyRules = ReadDependencyRules(metadataRoot).ToList();
        var components = ReadComponents(builtinRoot).ToList();

        foreach (var component in components)
        {
            foreach (var attribute in component.CustomAttributes.Where(attribute => !string.IsNullOrWhiteSpace(attribute.DependingOn)))
            {
                dependencyRules.Add(new AtlasDependencyRule
                {
                    ComponentId = component.Id,
                    SupportedUi = component.SupportedUi,
                    PropertyId = attribute.PropertyId,
                    DependingOn = attribute.DependingOn!,
                });
            }
        }

        return new ApexlangAtlasModel
        {
            BuildId = ReadString(metadataRoot, "buildID") ?? ReadString(metadataRoot, "buildId") ?? configuredBuildId ?? "unknown",
            SchemaVersion = ReadString(builtinRoot, "schemaVersion") ?? "unknown",
            PropertyTypes = ReadNamedValues(metadataRoot, "propertyTypes"),
            Groups = ReadNamedValues(metadataRoot, "groups"),
            LovValues = ReadLovValues(metadataRoot),
            ValidationRules = ReadValidationRules(metadataRoot),
            Properties = properties,
            Components = components,
            DependencyRules = dependencyRules
                .GroupBy(rule => $"{rule.ComponentId}|{rule.SupportedUi}|{rule.PropertyId}|{rule.DependingOn}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(rule => rule.ComponentId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(rule => rule.PropertyId, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static Dictionary<string, string> BuildGeneratedFiles(ApexlangAtlasModel model)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["generated/catalog-model.json"] = SerializeJson(model),
            ["indexes/components-index.json"] = SerializeJson(model.Components.Select(component => new
            {
                component.Id,
                component.Name,
                component.DisplayName,
                component.SupportedUi,
                component.PluginType,
                component.HelpText,
                component.SupportedFeatures,
                RequiredProperties = component.CustomAttributes.Where(attribute => attribute.Required).Select(attribute => attribute.PropertyId).ToList(),
            })),
            ["indexes/properties-index.json"] = SerializeJson(model.Properties.Select(property => new
            {
                property.Id,
                property.Name,
                property.DisplayName,
                property.Type,
                property.Group,
                property.PropertyTypes,
                property.LovValues,
                property.ValidationRules,
            })),
            ["indexes/dependency-rules.json"] = SerializeJson(model.DependencyRules),
            ["indexes/required-properties.json"] = SerializeJson(model.Components.Select(component => new
            {
                component.Id,
                component.SupportedUi,
                RequiredProperties = component.CustomAttributes.Where(attribute => attribute.Required).Select(attribute => new
                {
                    attribute.PropertyId,
                    attribute.DefaultValue,
                    attribute.DependingOn,
                }).ToList(),
            })),
            ["prompts/apexlang-context.md"] = BuildPrompt(model),
        };

        foreach (var component in model.Components)
        {
            files[$"docs/components/{WorkspacePathBuilder.Slugify(component.Id)}.md"] = BuildComponentDoc(component);
        }

        return files;
    }

    private static string BuildPrompt(ApexlangAtlasModel model)
    {
        var componentLines = model.Components
            .Take(12)
            .Select(component => $"- {component.DisplayName} [{component.SupportedUi}] required: {string.Join(", ", component.CustomAttributes.Where(attribute => attribute.Required).Select(attribute => attribute.PropertyId))}");
        var dependencyLines = model.DependencyRules
            .Take(12)
            .Select(rule => $"- {rule.ComponentId}.{rule.PropertyId} depends on {rule.DependingOn}");

        return string.Join("\n", new[]
        {
            "# APEXlang Atlas Context",
            $"Build: {model.BuildId}",
            $"Schema: {model.SchemaVersion}",
            "",
            "## Components",
            componentLines.Any() ? string.Join("\n", componentLines) : "- none",
            "",
            "## Dependency Rules",
            dependencyLines.Any() ? string.Join("\n", dependencyLines) : "- none",
        }) + "\n";
    }

    private static string BuildComponentDoc(AtlasComponent component)
    {
        var required = component.CustomAttributes.Where(attribute => attribute.Required).Select(attribute => attribute.PropertyId).ToList();
        var optional = component.CustomAttributes.Where(attribute => !attribute.Required).Take(8).Select(attribute => attribute.PropertyId).ToList();

        return string.Join("\n", new[]
        {
            $"# {component.DisplayName}",
            $"- id: {component.Id}",
            $"- ui: {component.SupportedUi}",
            $"- pluginType: {component.PluginType}",
            $"- required: {(required.Count == 0 ? "none" : string.Join(", ", required))}",
            $"- optional-sample: {(optional.Count == 0 ? "none" : string.Join(", ", optional))}",
            $"- action-templates: {(component.ActionTemplates.Count == 0 ? "none" : string.Join(", ", component.ActionTemplates))}",
            $"- action-positions: {(component.ActionPositions.Count == 0 ? "none" : string.Join(", ", component.ActionPositions))}",
            $"- supported-features: {(component.SupportedFeatures.Count == 0 ? "none" : string.Join(", ", component.SupportedFeatures))}",
            string.IsNullOrWhiteSpace(component.HelpText) ? string.Empty : $"- help: {component.HelpText}",
        }.Where(line => !string.IsNullOrWhiteSpace(line))) + "\n";
    }

    private static IEnumerable<AtlasProperty> ReadProperties(JsonElement metadataRoot)
    {
        foreach (var property in EnumerateArray(metadataRoot, "properties"))
        {
            var id = ReadString(property, "id") ?? ReadString(property, "propertyId") ?? ReadString(property, "name");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            yield return new AtlasProperty
            {
                Id = id,
                Name = ReadString(property, "name") ?? id,
                DisplayName = ReadString(property, "displayName") ?? ReadString(property, "label") ?? ReadString(property, "name") ?? id,
                Type = ReadString(property, "type") ?? ReadString(property, "propertyType") ?? string.Empty,
                Group = ReadString(property, "group") ?? ReadString(property, "groupId") ?? string.Empty,
                PropertyTypes = ReadStringList(property, "propertyTypes"),
                LovValues = ReadStringList(property, "lovValues"),
                ValidationRules = ReadValidationRuleStrings(property),
            };
        }
    }

    private static IEnumerable<AtlasDependencyRule> ReadDependencyRules(JsonElement metadataRoot)
    {
        foreach (var rule in EnumerateArray(metadataRoot, "dependencyRules"))
        {
            var propertyId = ReadString(rule, "propertyId") ?? ReadString(rule, "property");
            var dependingOn = ReadString(rule, "dependingOn") ?? ReadString(rule, "dependsOn");
            if (string.IsNullOrWhiteSpace(propertyId) || string.IsNullOrWhiteSpace(dependingOn))
            {
                continue;
            }

            yield return new AtlasDependencyRule
            {
                ComponentId = ReadString(rule, "componentId") ?? ReadString(rule, "componentType") ?? string.Empty,
                SupportedUi = ReadString(rule, "supportedUi") ?? ReadString(rule, "ui") ?? string.Empty,
                PropertyId = propertyId,
                DependingOn = dependingOn,
            };
        }
    }

    private static IEnumerable<AtlasComponent> ReadComponents(JsonElement builtinRoot)
    {
        if (TryGetProperty(builtinRoot, "componentsBySupportedUi", out var byUi) && byUi.ValueKind == JsonValueKind.Object)
        {
            foreach (var uiProperty in byUi.EnumerateObject())
            {
                foreach (var component in EnumerateComponentNodes(uiProperty.Value))
                {
                    var parsed = ParseComponent(component, uiProperty.Name);
                    if (parsed is not null)
                    {
                        yield return parsed;
                    }
                }
            }

            yield break;
        }

        foreach (var component in EnumerateComponentNodes(TryGetProperty(builtinRoot, "builtInComponents", out var builtInComponents) ? builtInComponents : builtinRoot))
        {
            var supportedUiValues = ReadStringList(component, "supportedUi");
            if (supportedUiValues.Count == 0)
            {
                supportedUiValues.Add(ReadString(component, "supportedUi") ?? "default");
            }

            foreach (var supportedUi in supportedUiValues.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var parsed = ParseComponent(component, supportedUi);
                if (parsed is not null)
                {
                    yield return parsed;
                }
            }
        }
    }

    private static AtlasComponent? ParseComponent(JsonElement component, string supportedUi)
    {
        var id = ReadString(component, "id") ?? ReadString(component, "componentId") ?? ReadString(component, "name");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return new AtlasComponent
        {
            Id = id,
            Name = ReadString(component, "name") ?? id,
            DisplayName = ReadString(component, "displayName") ?? ReadString(component, "name") ?? id,
            SupportedUi = supportedUi,
            PluginType = ReadString(component, "pluginType") ?? string.Empty,
            HelpText = ReadString(component, "helpText") ?? string.Empty,
            SupportedFeatures = ReadStringList(component, "supportedFeatures"),
            ActionTemplates = ReadStringList(component, "actionTemplates"),
            ActionPositions = ReadStringList(component, "actionPositions"),
            CustomAttributes = EnumerateArray(component, "customAttributes")
                .Select(ParseAttribute)
                .Where(attribute => attribute is not null)
                .Cast<AtlasComponentAttribute>()
                .ToList(),
        };
    }

    private static AtlasComponentAttribute? ParseAttribute(JsonElement attribute)
    {
        var propertyId = ReadString(attribute, "propertyId") ?? ReadString(attribute, "id") ?? ReadString(attribute, "name");
        if (string.IsNullOrWhiteSpace(propertyId))
        {
            return null;
        }

        return new AtlasComponentAttribute
        {
            PropertyId = propertyId,
            Required = ReadBool(attribute, "required"),
            DefaultValue = ReadString(attribute, "default") ?? ReadString(attribute, "defaultValue") ?? string.Empty,
            HelpText = ReadString(attribute, "helpText") ?? string.Empty,
            DependingOn = ReadString(attribute, "dependingOn") ?? ReadString(attribute, "dependsOn"),
        };
    }

    private static List<string> ReadNamedValues(JsonElement root, string propertyName)
        => EnumerateArray(root, propertyName)
            .Select(item => ReadString(item, "id") ?? ReadString(item, "name") ?? ReadString(item, "value"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList();

    private static List<string> ReadLovValues(JsonElement root)
        => EnumerateArray(root, "lovValues")
            .SelectMany(item => ReadStringList(item, "values").DefaultIfEmpty(ReadString(item, "value") ?? string.Empty))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> ReadValidationRules(JsonElement root)
        => EnumerateArray(root, "validationRules")
            .Select(item => ReadString(item, "id") ?? ReadString(item, "rule") ?? ReadString(item, "name"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList();

    private static List<string> ReadValidationRuleStrings(JsonElement property)
        => EnumerateArray(property, "validationRules")
            .Select(item => ReadString(item, "id") ?? ReadString(item, "rule") ?? ReadString(item, "name") ?? item.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList();

    private static List<string> ReadStringList(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return new List<string>();
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            return property.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : ReadString(item, "id") ?? ReadString(item, "name") ?? item.ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToList();
        }

        var scalar = property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
        return string.IsNullOrWhiteSpace(scalar) ? new List<string>() : new List<string> { scalar };
    }

    private static IEnumerable<JsonElement> EnumerateArray(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static IEnumerable<JsonElement> EnumerateComponentNodes(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray();
        }

        if (element.ValueKind == JsonValueKind.Object && TryGetProperty(element, "components", out var components) && components.ValueKind == JsonValueKind.Array)
        {
            return components.EnumerateArray();
        }

        return Enumerable.Empty<JsonElement>();
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        if (TryGetProperty(element, propertyName, out var property)
            && (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False))
        {
            return property.GetBoolean();
        }

        return bool.TryParse(ReadString(element, propertyName), out var value) && value;
    }

    private static string ReadSetting(YamlMappingNode mapping, string key)
    {
        foreach (var child in mapping.Children)
        {
            if (child.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal) && child.Value is YamlScalarNode value)
            {
                return value.Value ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string SerializeJson<T>(T value)
        => JsonSerializer.Serialize(value, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed record ApexlangAtlasSettings(string BuildId, string MetadataUrl, string BuiltinCatalogUrl);

    private sealed record ResolvedSource(string Location, string Content, string Hash);

    private sealed class ApexlangAtlasModel
    {
        public required string BuildId { get; init; }

        public required string SchemaVersion { get; init; }

        public required List<string> PropertyTypes { get; init; }

        public required List<string> Groups { get; init; }

        public required List<string> LovValues { get; init; }

        public required List<string> ValidationRules { get; init; }

        public required List<AtlasProperty> Properties { get; init; }

        public required List<AtlasComponent> Components { get; init; }

        public required List<AtlasDependencyRule> DependencyRules { get; init; }
    }

    private sealed class AtlasProperty
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string DisplayName { get; init; }
        public required string Type { get; init; }
        public required string Group { get; init; }
        public required List<string> PropertyTypes { get; init; }
        public required List<string> LovValues { get; init; }
        public required List<string> ValidationRules { get; init; }
    }

    private sealed class AtlasComponent
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string DisplayName { get; init; }
        public required string SupportedUi { get; init; }
        public required string PluginType { get; init; }
        public required string HelpText { get; init; }
        public required List<string> SupportedFeatures { get; init; }
        public required List<string> ActionTemplates { get; init; }
        public required List<string> ActionPositions { get; init; }
        public required List<AtlasComponentAttribute> CustomAttributes { get; init; }
    }

    private sealed class AtlasComponentAttribute
    {
        public required string PropertyId { get; init; }
        public bool Required { get; init; }
        public required string DefaultValue { get; init; }
        public required string HelpText { get; init; }
        public string? DependingOn { get; init; }
    }

    private sealed class AtlasDependencyRule
    {
        public string ComponentId { get; init; } = string.Empty;
        public string SupportedUi { get; init; } = string.Empty;
        public required string PropertyId { get; init; }
        public required string DependingOn { get; init; }
    }
}
