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

    [YamlMember(Alias = "capabilities")]
    public List<string> Capabilities { get; init; } = new();

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

    [YamlMember(Alias = "profiles")]
    public List<string> Profiles { get; init; } = new();

    [YamlMember(Alias = "restart")]
    public string? Restart { get; init; }

    [YamlMember(Alias = "healthcheck")]
    public ServiceHealthcheckManifest? Healthcheck { get; init; }

    [YamlMember(Alias = "volumes")]
    public List<string> Volumes { get; init; } = new();

    [YamlMember(Alias = "dependsOn")]
    public List<string> DependsOn { get; init; } = new();

    [YamlMember(Alias = "workspaceDependsOnCondition")]
    public string? WorkspaceDependsOnCondition { get; init; }
}

public sealed class ServiceHealthcheckManifest
{
    [YamlMember(Alias = "test")]
    public List<string> Test { get; init; } = new();

    [YamlMember(Alias = "interval")]
    public string? Interval { get; init; }

    [YamlMember(Alias = "timeout")]
    public string? Timeout { get; init; }

    [YamlMember(Alias = "retries")]
    public int? Retries { get; init; }

    [YamlMember(Alias = "startPeriod")]
    public string? StartPeriod { get; init; }
}

public sealed class TemplateManifest
{
    [YamlMember(Alias = "id")]
    public string Id { get; init; } = string.Empty;

    [YamlMember(Alias = "displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string Description { get; init; } = string.Empty;

    [YamlMember(Alias = "workspaceImage")]
    public string? WorkspaceImage { get; init; }

    [YamlMember(Alias = "features")]
    public List<string> Features { get; init; } = new();

    [YamlMember(Alias = "services")]
    public List<string> Services { get; init; } = new();

    [YamlMember(Alias = "skills")]
    public List<string> Skills { get; init; } = new();

    [YamlMember(Alias = "mcp")]
    public List<string> Mcp { get; init; } = new();
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

public sealed class CapabilityManifest
{
    [YamlMember(Alias = "id")]
    public string Id { get; init; } = string.Empty;

    [YamlMember(Alias = "displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string Description { get; init; } = string.Empty;

    [YamlMember(Alias = "sortOrder")]
    public int SortOrder { get; init; }

    [YamlMember(Alias = "onboardingRelevance")]
    public string OnboardingRelevance { get; init; } = string.Empty;

    [YamlMember(Alias = "whatItIs")]
    public string WhatItIs { get; init; } = string.Empty;

    [YamlMember(Alias = "whyUseIt")]
    public string WhyUseIt { get; init; } = string.Empty;

    [YamlMember(Alias = "availableTools")]
    public List<CapabilityToolManifest> AvailableTools { get; init; } = new();

    [YamlMember(Alias = "typicalTasks")]
    public List<string> TypicalTasks { get; init; } = new();

    [YamlMember(Alias = "examples")]
    public List<string> Examples { get; init; } = new();

    [YamlMember(Alias = "relatedDocumentation")]
    public List<CapabilityDocumentationLinkManifest> RelatedDocumentation { get; init; } = new();

    [YamlMember(Alias = "relatedCapabilities")]
    public List<string> RelatedCapabilities { get; init; } = new();

    [YamlMember(Alias = "agentStartHere")]
    public List<CapabilityDocumentationLinkManifest> AgentStartHere { get; init; } = new();

    [YamlMember(Alias = "learningProgression")]
    public List<string> LearningProgression { get; init; } = new();
}

public sealed class CapabilityToolManifest
{
    [YamlMember(Alias = "name")]
    public string Name { get; init; } = string.Empty;

    [YamlMember(Alias = "purpose")]
    public string Purpose { get; init; } = string.Empty;

    [YamlMember(Alias = "supportedWorkflows")]
    public List<string> SupportedWorkflows { get; init; } = new();

    [YamlMember(Alias = "commonUseCases")]
    public List<string> CommonUseCases { get; init; } = new();
}

public sealed class CapabilityDocumentationLinkManifest
{
    [YamlMember(Alias = "label")]
    public string Label { get; init; } = string.Empty;

    [YamlMember(Alias = "path")]
    public string Path { get; init; } = string.Empty;

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
