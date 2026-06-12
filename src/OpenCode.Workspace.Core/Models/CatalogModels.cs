using YamlDotNet.Serialization;

namespace OpenCode.Workspace.Core.Models;

/// <summary>
/// Catalog manifests are intentionally explicit and data-first. The MVP prefers
/// readable YAML over dynamic plugin discovery so new contributors can extend the
/// system by copying one manifest and one example.
/// </summary>
public sealed class FeatureManifest
{
    [YamlMember(Alias = "id")]
    public string Id { get; init; } = string.Empty;

    [YamlMember(Alias = "displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string Description { get; init; } = string.Empty;

    [YamlMember(Alias = "alwaysEnabled")]
    public bool AlwaysEnabled { get; init; }

    [YamlMember(Alias = "dependencies")]
    public DependencySet Dependencies { get; init; } = new();

    [YamlMember(Alias = "postInstall")]
    public List<string> PostInstall { get; init; } = new();
}

public sealed class SkillManifest
{
    [YamlMember(Alias = "id")]
    public string Id { get; init; } = string.Empty;

    [YamlMember(Alias = "displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string Description { get; init; } = string.Empty;

    [YamlMember(Alias = "dependencies")]
    public SkillDependencySet Dependencies { get; init; } = new();
}

public sealed class ServiceManifest
{
    [YamlMember(Alias = "id")]
    public string Id { get; init; } = string.Empty;

    [YamlMember(Alias = "displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string Description { get; init; } = string.Empty;

    [YamlMember(Alias = "image")]
    public string Image { get; init; } = string.Empty;

    [YamlMember(Alias = "hostPorts")]
    public List<string> HostPorts { get; init; } = new();

    [YamlMember(Alias = "environment")]
    public Dictionary<string, string> Environment { get; init; } = new();

    [YamlMember(Alias = "volumes")]
    public List<string> Volumes { get; init; } = new();

    [YamlMember(Alias = "dependsOn")]
    public List<string> DependsOn { get; init; } = new();
}

public sealed class TemplateManifest
{
    [YamlMember(Alias = "id")]
    public string Id { get; init; } = string.Empty;

    [YamlMember(Alias = "displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string Description { get; init; } = string.Empty;

    [YamlMember(Alias = "features")]
    public List<string> Features { get; init; } = new();

    [YamlMember(Alias = "services")]
    public List<string> Services { get; init; } = new();
}

public sealed class McpManifest
{
    [YamlMember(Alias = "id")]
    public string Id { get; init; } = string.Empty;

    [YamlMember(Alias = "displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string Description { get; init; } = string.Empty;
}

public sealed class DependencySet
{
    [YamlMember(Alias = "apt")]
    public List<string> Apt { get; init; } = new();

    [YamlMember(Alias = "npm")]
    public List<string> Npm { get; init; } = new();

    [YamlMember(Alias = "pip")]
    public List<string> Pip { get; init; } = new();
}

public sealed class SkillDependencySet
{
    [YamlMember(Alias = "features")]
    public List<string> Features { get; init; } = new();
}
