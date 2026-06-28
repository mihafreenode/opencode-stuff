using OpenCode.Workspace.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenCode.Workspace.Core.Catalog;

/// <summary>
/// The built-in catalog is loaded from plain YAML files copied beside the app.
/// That keeps catalog behavior inspectable in the shipped output and makes the
/// first extension story obvious: copy an existing manifest and edit it.
/// </summary>
public sealed class BuiltInCatalogProvider
{
    private readonly string _catalogRootPath;
    private readonly IDeserializer _deserializer;
    private readonly Dictionary<string, object> _manifestCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    public BuiltInCatalogProvider(string catalogRootPath)
    {
        _catalogRootPath = catalogRootPath;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public string CatalogRootPath => _catalogRootPath;

    public IReadOnlyList<FeatureManifest> LoadFeatures() => LoadAll<FeatureManifest>("features");
    public IReadOnlyList<CapabilityManifest> LoadCapabilities() => LoadAll<CapabilityManifest>("capabilities");
    public IReadOnlyList<KnowledgePackManifest> LoadKnowledgePacks() => LoadAll<KnowledgePackManifest>("knowledge-packs");
    public IReadOnlyList<ServiceManifest> LoadServices() => LoadAll<ServiceManifest>("services");
    public IReadOnlyList<SkillManifest> LoadSkills() => LoadAll<SkillManifest>("skills");
    public IReadOnlyList<McpManifest> LoadMcpModules() => LoadAll<McpManifest>("mcp");
    public IReadOnlyList<TemplateManifest> LoadTemplates() => LoadAll<TemplateManifest>("templates");

    private IReadOnlyList<TManifest> LoadAll<TManifest>(string folderName)
    {
        lock (_cacheLock)
        {
            if (_manifestCache.TryGetValue(folderName, out var cached))
            {
                return (IReadOnlyList<TManifest>)cached;
            }
        }

        var folderPath = Path.Combine(_catalogRootPath, folderName);
        if (!Directory.Exists(folderPath))
        {
            return Array.Empty<TManifest>();
        }

        var manifests = Directory.EnumerateFiles(folderPath, "*.yaml", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(ReadManifest<TManifest>)
            .ToList();

        lock (_cacheLock)
        {
            _manifestCache[folderName] = manifests;
        }

        return manifests;
    }

    private TManifest ReadManifest<TManifest>(string filePath)
    {
        using var reader = File.OpenText(filePath);
        return _deserializer.Deserialize<TManifest>(reader);
    }
}
