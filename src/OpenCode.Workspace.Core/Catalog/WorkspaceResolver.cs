using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Catalog;

/// <summary>
/// Resolves the small, human-owned workspace.yaml selections into the concrete
/// package and service plan used by compose and provisioning generation.
/// </summary>
public sealed class WorkspaceResolver
{
    private readonly IReadOnlyDictionary<string, FeatureManifest> _featuresById;
    private readonly IReadOnlyDictionary<string, CapabilityManifest> _capabilitiesById;
    private readonly IReadOnlyDictionary<string, KnowledgePackManifest> _knowledgePacksById;
    private readonly IReadOnlyDictionary<string, ServiceManifest> _servicesById;

    public WorkspaceResolver(IEnumerable<FeatureManifest> features, IEnumerable<ServiceManifest> services)
        : this(features, services, Array.Empty<CapabilityManifest>(), Array.Empty<KnowledgePackManifest>())
    {
    }

    public WorkspaceResolver(IEnumerable<FeatureManifest> features, IEnumerable<ServiceManifest> services, IEnumerable<CapabilityManifest> capabilities)
        : this(features, services, capabilities, Array.Empty<KnowledgePackManifest>())
    {
    }

    public WorkspaceResolver(IEnumerable<FeatureManifest> features, IEnumerable<ServiceManifest> services, IEnumerable<CapabilityManifest> capabilities, IEnumerable<KnowledgePackManifest> knowledgePacks)
    {
        _featuresById = features.ToDictionary(feature => feature.Id, StringComparer.OrdinalIgnoreCase);
        _capabilitiesById = capabilities.ToDictionary(capability => capability.Id, StringComparer.OrdinalIgnoreCase);
        _knowledgePacksById = knowledgePacks.ToDictionary(pack => pack.Id, StringComparer.OrdinalIgnoreCase);
        _servicesById = services.ToDictionary(service => service.Id, StringComparer.OrdinalIgnoreCase);
    }

    public ResolvedWorkspace Resolve(WorkspaceDefinition definition)
    {
        var selectedFeatures = new List<FeatureManifest>();

        foreach (var feature in _featuresById.Values.Where(feature => feature.AlwaysEnabled).OrderBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase))
        {
            selectedFeatures.Add(feature);
        }

        foreach (var featureId in definition.Features)
        {
            if (!_featuresById.TryGetValue(featureId, out var feature))
            {
                throw new InvalidOperationException($"Unknown feature '{featureId}'. Add a built-in manifest or fix workspace.yaml.");
            }

            if (selectedFeatures.All(existing => !string.Equals(existing.Id, feature.Id, StringComparison.OrdinalIgnoreCase)))
            {
                selectedFeatures.Add(feature);
            }
        }

        foreach (var feature in selectedFeatures.ToList())
        {
            foreach (var requiredFeatureId in feature.Requires)
            {
                if (!_featuresById.TryGetValue(requiredFeatureId, out var requiredFeature))
                {
                    throw new InvalidOperationException($"Feature '{feature.Id}' requires unknown feature '{requiredFeatureId}'.");
                }

                if (selectedFeatures.All(existing => !string.Equals(existing.Id, requiredFeature.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    selectedFeatures.Add(requiredFeature);
                }
            }
        }

        var selectedCapabilities = new List<CapabilityManifest>();
        foreach (var capabilityId in selectedFeatures.SelectMany(feature => feature.Capabilities))
        {
            if (!_capabilitiesById.TryGetValue(capabilityId, out var capability))
            {
                throw new InvalidOperationException($"Feature-selected capability '{capabilityId}' is missing from the built-in catalog.");
            }

            if (selectedCapabilities.All(existing => !string.Equals(existing.Id, capability.Id, StringComparison.OrdinalIgnoreCase)))
            {
                selectedCapabilities.Add(capability);
            }
        }

        selectedCapabilities = selectedCapabilities
            .OrderBy(capability => capability.SortOrder)
            .ThenBy(capability => capability.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selectedKnowledgePacks = new List<KnowledgePackManifest>();
        foreach (var packId in selectedFeatures.SelectMany(feature => feature.KnowledgePacks))
        {
            if (!_knowledgePacksById.TryGetValue(packId, out var knowledgePack))
            {
                throw new InvalidOperationException($"Feature-selected knowledge pack '{packId}' is missing from the built-in catalog.");
            }

            if (selectedKnowledgePacks.All(existing => !string.Equals(existing.Id, knowledgePack.Id, StringComparison.OrdinalIgnoreCase)))
            {
                selectedKnowledgePacks.Add(knowledgePack);
            }
        }

        selectedKnowledgePacks = selectedKnowledgePacks
            .OrderBy(pack => pack.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selectedServices = new List<ServiceManifest>();
        foreach (var serviceId in definition.Services)
        {
            if (!_servicesById.TryGetValue(serviceId, out var service))
            {
                throw new InvalidOperationException($"Unknown service '{serviceId}'. Add a built-in manifest or fix workspace.yaml.");
            }

            if (selectedServices.All(existing => !string.Equals(existing.Id, service.Id, StringComparison.OrdinalIgnoreCase)))
            {
                selectedServices.Add(ApplyWorkspaceServiceOverrides(definition, service));
            }
        }

        return new ResolvedWorkspace
        {
            Definition = definition,
            Features = selectedFeatures,
            Capabilities = selectedCapabilities,
            KnowledgePacks = selectedKnowledgePacks,
            Services = selectedServices,
            AptPackages = selectedFeatures.SelectMany(feature => feature.Dependencies.Apt).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(packageName => packageName, StringComparer.OrdinalIgnoreCase).ToList(),
            NpmPackages = selectedFeatures.SelectMany(feature => feature.Dependencies.Npm).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(packageName => packageName, StringComparer.OrdinalIgnoreCase).ToList(),
            PipPackages = selectedFeatures.SelectMany(feature => feature.Dependencies.Pip).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(packageName => packageName, StringComparer.OrdinalIgnoreCase).ToList(),
            PostInstallCommands = selectedFeatures.SelectMany(feature => feature.PostInstall).Distinct(StringComparer.Ordinal).ToList(),
        };
    }

    private static ServiceManifest ApplyWorkspaceServiceOverrides(WorkspaceDefinition definition, ServiceManifest service)
    {
        if (!string.Equals(service.Id, OracleWorkspaceFamily.OracleDatabaseServiceId, StringComparison.OrdinalIgnoreCase))
        {
            return service;
        }

        var resolvedImage = OracleDatabaseImageCatalog.ResolveDatabaseImage(definition);
        if (string.Equals(service.Image, resolvedImage, StringComparison.OrdinalIgnoreCase))
        {
            return service;
        }

        return new ServiceManifest
        {
            Id = service.Id,
            DisplayName = service.DisplayName,
            Description = service.Description,
            Image = resolvedImage,
            HostPorts = service.HostPorts.ToList(),
            Environment = new Dictionary<string, string>(service.Environment, StringComparer.Ordinal),
            Profiles = service.Profiles.ToList(),
            Restart = service.Restart,
            Healthcheck = service.Healthcheck,
            Volumes = service.Volumes.ToList(),
            EntryPoint = service.EntryPoint.ToList(),
            Command = service.Command.ToList(),
            DependsOn = service.DependsOn.ToList(),
            WorkspaceDependsOnCondition = service.WorkspaceDependsOnCondition,
        };
    }
}
