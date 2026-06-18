using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Catalog;

/// <summary>
/// Keeps built-in manifest validation explicit and readable. The goal is to fail
/// fast on malformed catalog data without hiding the rules behind reflection or
/// custom schema magic.
/// </summary>
public sealed class CatalogValidator
{
    public IReadOnlyList<string> ValidateCapabilities(IEnumerable<CapabilityManifest> capabilities)
    {
        var errors = new List<string>();
        ValidateUniqueIds(capabilities.Select(capability => capability.Id), "capability", errors);

        foreach (var capability in capabilities)
        {
            if (string.IsNullOrWhiteSpace(capability.Id))
            {
                errors.Add("Capability manifest is missing 'id'.");
            }

            if (string.IsNullOrWhiteSpace(capability.DisplayName))
            {
                errors.Add($"Capability '{capability.Id}' is missing 'displayName'.");
            }
        }

        return errors;
    }

    public IReadOnlyList<string> ValidateKnowledgePacks(IEnumerable<KnowledgePackManifest> knowledgePacks)
    {
        var errors = new List<string>();
        ValidateUniqueIds(knowledgePacks.Select(pack => pack.Id), "knowledge pack", errors);

        foreach (var pack in knowledgePacks)
        {
            if (string.IsNullOrWhiteSpace(pack.Id))
            {
                errors.Add("Knowledge pack manifest is missing 'id'.");
            }

            if (string.IsNullOrWhiteSpace(pack.Title))
            {
                errors.Add($"Knowledge pack '{pack.Id}' is missing 'title'.");
            }

            if (!string.IsNullOrWhiteSpace(pack.Category)
                && !CatalogConventions.ValidFeatureCategories.Contains(pack.Category))
            {
                errors.Add($"Knowledge pack '{pack.Id}' uses unsupported category '{pack.Category}'.");
            }

            if (!string.IsNullOrWhiteSpace(pack.Lifecycle)
                && !CatalogConventions.ValidLifecycles.Contains(pack.Lifecycle))
            {
                errors.Add($"Knowledge pack '{pack.Id}' uses unsupported lifecycle '{pack.Lifecycle}'.");
            }

            foreach (var source in pack.Sources)
            {
                if (string.IsNullOrWhiteSpace(source.Name))
                {
                    errors.Add($"Knowledge pack '{pack.Id}' contains a source without 'name'.");
                }

                if (string.IsNullOrWhiteSpace(source.Url))
                {
                    errors.Add($"Knowledge pack '{pack.Id}' source '{source.Name}' is missing 'url'.");
                }
            }
        }

        return errors;
    }

    public IReadOnlyList<string> ValidateFeatures(IEnumerable<FeatureManifest> features)
    {
        var errors = new List<string>();
        ValidateUniqueIds(features.Select(feature => feature.Id), "feature", errors);

        foreach (var feature in features)
        {
            if (string.IsNullOrWhiteSpace(feature.Id))
            {
                errors.Add("Feature manifest is missing 'id'.");
            }

            if (string.IsNullOrWhiteSpace(feature.DisplayName))
            {
                errors.Add($"Feature '{feature.Id}' is missing 'displayName'.");
            }

            if (!string.IsNullOrWhiteSpace(feature.Category)
                && !CatalogConventions.ValidFeatureCategories.Contains(feature.Category))
            {
                errors.Add($"Feature '{feature.Id}' uses unsupported category '{feature.Category}'.");
            }

            if (!string.IsNullOrWhiteSpace(feature.Lifecycle)
                && !CatalogConventions.ValidLifecycles.Contains(feature.Lifecycle))
            {
                errors.Add($"Feature '{feature.Id}' uses unsupported lifecycle '{feature.Lifecycle}'.");
            }
        }

        return errors;
    }

    public IReadOnlyList<string> ValidateServices(IEnumerable<ServiceManifest> services)
    {
        var errors = new List<string>();
        ValidateUniqueIds(services.Select(service => service.Id), "service", errors);

        foreach (var service in services)
        {
            if (string.IsNullOrWhiteSpace(service.Id))
            {
                errors.Add("Service manifest is missing 'id'.");
            }

            if (string.IsNullOrWhiteSpace(service.DisplayName))
            {
                errors.Add($"Service '{service.Id}' is missing 'displayName'.");
            }

            if (string.IsNullOrWhiteSpace(service.Image))
            {
                errors.Add($"Service '{service.Id}' is missing 'image'.");
            }
        }

        return errors;
    }

    public IReadOnlyList<string> ValidateFeatures(IEnumerable<FeatureManifest> features, IEnumerable<CapabilityManifest> capabilities)
    {
        var errors = ValidateFeatures(features).ToList();
        var capabilityIds = capabilities.Select(capability => capability.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in features)
        {
            foreach (var capabilityId in feature.Capabilities)
            {
                if (!capabilityIds.Contains(capabilityId))
                {
                    errors.Add($"Feature '{feature.Id}' references unknown capability '{capabilityId}'.");
                }
            }
        }

        return errors;
    }

    public IReadOnlyList<string> ValidateFeatures(IEnumerable<FeatureManifest> features, IEnumerable<CapabilityManifest> capabilities, IEnumerable<KnowledgePackManifest> knowledgePacks)
    {
        var errors = ValidateFeatures(features, capabilities).ToList();
        var featureIds = features.Select(feature => feature.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knowledgePackIds = knowledgePacks.Select(pack => pack.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in features)
        {
            foreach (var featureId in feature.Requires)
            {
                if (!featureIds.Contains(featureId))
                {
                    errors.Add($"Feature '{feature.Id}' requires unknown feature '{featureId}'.");
                }
            }

            foreach (var featureId in feature.Recommends)
            {
                if (!featureIds.Contains(featureId))
                {
                    errors.Add($"Feature '{feature.Id}' recommends unknown feature '{featureId}'.");
                }
            }

            foreach (var knowledgePackId in feature.KnowledgePacks)
            {
                if (!knowledgePackIds.Contains(knowledgePackId))
                {
                    errors.Add($"Feature '{feature.Id}' references unknown knowledge pack '{knowledgePackId}'.");
                }
            }
        }

        return errors;
    }

    public IReadOnlyList<string> ValidateTemplates(IEnumerable<TemplateManifest> templates, IEnumerable<FeatureManifest> features, IEnumerable<ServiceManifest> services)
    {
        var errors = new List<string>();
        var featureIds = features.Select(feature => feature.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var serviceIds = services.Select(service => service.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        ValidateUniqueIds(templates.Select(template => template.Id), "template", errors);

        foreach (var template in templates)
        {
            if (string.IsNullOrWhiteSpace(template.Id))
            {
                errors.Add("Template manifest is missing 'id'.");
            }

            if (string.IsNullOrWhiteSpace(template.DisplayName))
            {
                errors.Add($"Template '{template.Id}' is missing 'displayName'.");
            }

            foreach (var featureId in template.Features)
            {
                if (!featureIds.Contains(featureId))
                {
                    errors.Add($"Template '{template.Id}' references unknown feature '{featureId}'.");
                }
            }

            foreach (var serviceId in template.Services)
            {
                if (!serviceIds.Contains(serviceId))
                {
                    errors.Add($"Template '{template.Id}' references unknown service '{serviceId}'.");
                }
            }

            if (template.Skills.Any(skillId => string.IsNullOrWhiteSpace(skillId)))
            {
                errors.Add($"Template '{template.Id}' contains an empty skill id.");
            }

            if (template.Mcp.Any(mcpId => string.IsNullOrWhiteSpace(mcpId)))
            {
                errors.Add($"Template '{template.Id}' contains an empty MCP id.");
            }
        }

        return errors;
    }

    private static void ValidateUniqueIds(IEnumerable<string> ids, string manifestType, ICollection<string> errors)
    {
        var duplicates = ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var duplicate in duplicates)
        {
            errors.Add($"Duplicate {manifestType} id '{duplicate}'.");
        }
    }
}
