using System.IO;
using YamlDotNet.RepresentationModel;

namespace OpenCode.Workspace.Core.Runtime;

internal static class ComposeProjectInspector
{
    public static ComposeProjectInspection InspectFile(string composePath, string? requiredService = null)
    {
        if (string.IsNullOrWhiteSpace(composePath))
        {
            return new ComposeProjectInspection(["Compose path is missing."], [], []);
        }

        if (!File.Exists(composePath))
        {
            return new ComposeProjectInspection([$"Compose file '{composePath}' is missing."], [], []);
        }

        return Inspect(File.ReadAllText(composePath), requiredService);
    }

    public static ComposeProjectInspection Inspect(string composeText, string? requiredService = null)
    {
        if (string.IsNullOrWhiteSpace(composeText))
        {
            return new ComposeProjectInspection(["Compose file is empty."], [], []);
        }

        var errors = new List<string>();
        var serviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var declaredVolumes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var declaredNetworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var volumeReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var networkReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dependenciesByService = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var yaml = new YamlStream();
            using var reader = new StringReader(composeText);
            yaml.Load(reader);

            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            {
                return new ComposeProjectInspection(["Compose file does not contain a YAML mapping root."], [], []);
            }

            var servicesNode = GetMappingChild(root, "services");
            if (servicesNode is null || servicesNode.Children.Count == 0)
            {
                return new ComposeProjectInspection(["Compose file does not define any services."], [], []);
            }

            foreach (var child in servicesNode.Children)
            {
                if (child.Key is not YamlScalarNode serviceKey || string.IsNullOrWhiteSpace(serviceKey.Value))
                {
                    continue;
                }

                var serviceName = serviceKey.Value.Trim();
                serviceNames.Add(serviceName);
            }

            CollectTopLevelNames(GetMappingChild(root, "volumes"), declaredVolumes);
            CollectTopLevelNames(GetMappingChild(root, "networks"), declaredNetworks);

            foreach (var child in servicesNode.Children)
            {
                if (child.Key is not YamlScalarNode serviceKey || string.IsNullOrWhiteSpace(serviceKey.Value) || child.Value is not YamlMappingNode serviceMapping)
                {
                    continue;
                }

                var serviceName = serviceKey.Value.Trim();
                CollectProfiles(serviceMapping, profiles);
                dependenciesByService[serviceName] = CollectDependencies(serviceMapping);
                CollectNamedVolumeReferences(serviceMapping, volumeReferences);
                CollectNamedNetworkReferences(serviceMapping, networkReferences);
            }
        }
        catch (Exception exception)
        {
            return new ComposeProjectInspection([$"Compose file could not be parsed: {exception.Message}"], [], []);
        }

        foreach (var dependency in dependenciesByService)
        {
            foreach (var referencedService in dependency.Value)
            {
                if (!serviceNames.Contains(referencedService))
                {
                    errors.Add($"Service '{dependency.Key}' depends_on undefined service '{referencedService}'.");
                }
            }
        }

        foreach (var volume in volumeReferences.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            if (!declaredVolumes.Contains(volume))
            {
                errors.Add($"Named volume '{volume}' is referenced but not declared.");
            }
        }

        foreach (var network in networkReferences.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(network, "default", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!declaredNetworks.Contains(network))
            {
                errors.Add($"Named network '{network}' is referenced but not declared.");
            }
        }

        if (!string.IsNullOrWhiteSpace(requiredService) && !serviceNames.Contains(requiredService))
        {
            errors.Add($"Required service '{requiredService}' is missing from the compose project.");
        }

        return new ComposeProjectInspection(errors, serviceNames.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(), profiles.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static YamlMappingNode? GetMappingChild(YamlMappingNode parent, string key)
    {
        foreach (var child in parent.Children)
        {
            if (child.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                return child.Value as YamlMappingNode;
            }
        }

        return null;
    }

    private static void CollectTopLevelNames(YamlMappingNode? node, ISet<string> names)
    {
        if (node is null)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            if (child.Key is YamlScalarNode key && !string.IsNullOrWhiteSpace(key.Value))
            {
                names.Add(key.Value.Trim());
            }
        }
    }

    private static void CollectProfiles(YamlMappingNode serviceMapping, ISet<string> profiles)
    {
        if (!TryGetChild(serviceMapping, "profiles", out var profilesNode) || profilesNode is not YamlSequenceNode profileSequence)
        {
            return;
        }

        foreach (var profileNode in profileSequence.Children.OfType<YamlScalarNode>())
        {
            if (!string.IsNullOrWhiteSpace(profileNode.Value))
            {
                profiles.Add(profileNode.Value.Trim());
            }
        }
    }

    private static List<string> CollectDependencies(YamlMappingNode serviceMapping)
    {
        var dependencies = new List<string>();
        if (!TryGetChild(serviceMapping, "depends_on", out var dependsOnNode))
        {
            return dependencies;
        }

        switch (dependsOnNode)
        {
            case YamlSequenceNode sequence:
                foreach (var item in sequence.Children.OfType<YamlScalarNode>())
                {
                    if (!string.IsNullOrWhiteSpace(item.Value))
                    {
                        dependencies.Add(item.Value.Trim());
                    }
                }

                break;
            case YamlMappingNode mapping:
                foreach (var child in mapping.Children)
                {
                    if (child.Key is YamlScalarNode key && !string.IsNullOrWhiteSpace(key.Value))
                    {
                        dependencies.Add(key.Value.Trim());
                    }
                }

                break;
        }

        return dependencies;
    }

    private static void CollectNamedVolumeReferences(YamlMappingNode serviceMapping, ISet<string> references)
    {
        if (!TryGetChild(serviceMapping, "volumes", out var volumesNode) || volumesNode is not YamlSequenceNode volumeSequence)
        {
            return;
        }

        foreach (var volumeNode in volumeSequence.Children)
        {
            if (volumeNode is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
            {
                continue;
            }

            var source = scalar.Value.Split(':', 2)[0].Trim();
            if (IsNamedVolumeReference(source))
            {
                references.Add(source);
            }
        }
    }

    private static void CollectNamedNetworkReferences(YamlMappingNode serviceMapping, ISet<string> references)
    {
        if (!TryGetChild(serviceMapping, "networks", out var networksNode))
        {
            return;
        }

        switch (networksNode)
        {
            case YamlSequenceNode networkSequence:
                foreach (var networkNode in networkSequence.Children.OfType<YamlScalarNode>())
                {
                    if (!string.IsNullOrWhiteSpace(networkNode.Value))
                    {
                        references.Add(networkNode.Value.Trim());
                    }
                }

                break;
            case YamlMappingNode networkMapping:
                foreach (var child in networkMapping.Children)
                {
                    if (child.Key is YamlScalarNode key && !string.IsNullOrWhiteSpace(key.Value))
                    {
                        references.Add(key.Value.Trim());
                    }
                }

                break;
        }
    }

    private static bool TryGetChild(YamlMappingNode parent, string key, out YamlNode? value)
    {
        foreach (var child in parent.Children)
        {
            if (child.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                value = child.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool IsNamedVolumeReference(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (source.Contains('/', StringComparison.Ordinal) || source.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        if (source.StartsWith(".", StringComparison.Ordinal) || source.StartsWith("~", StringComparison.Ordinal))
        {
            return false;
        }

        if (source.StartsWith("${", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }
}

internal sealed record ComposeProjectInspection(IReadOnlyList<string> Errors, IReadOnlyList<string> Services, IReadOnlyList<string> Profiles)
{
    public bool IsValid => Errors.Count == 0;
}
