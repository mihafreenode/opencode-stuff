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

    [YamlMember(Alias = "category")]
    public string? Category { get; init; }

    [YamlMember(Alias = "lifecycle")]
    public string? Lifecycle { get; init; }

    [YamlMember(Alias = "alwaysEnabled")]
    public bool AlwaysEnabled { get; init; }

    [YamlMember(Alias = "requires")]
    public List<string> Requires { get; init; } = new();

    [YamlMember(Alias = "recommends")]
    public List<string> Recommends { get; init; } = new();

    [YamlMember(Alias = "knowledgePacks")]
    public List<string> KnowledgePacks { get; init; } = new();

    [YamlMember(Alias = "capabilities")]
    public List<string> Capabilities { get; init; } = new();

    [YamlMember(Alias = "dependencies")]
    public DependencySet Dependencies { get; init; } = new();

    [YamlMember(Alias = "postInstall")]
    public List<string> PostInstall { get; init; } = new();
}

public static class CatalogConventions
{
    public const string RuntimeFeatureCategory = "runtime";
    public const string KnowledgePackFeatureCategory = "knowledge-pack";
    public const string SampleDataPackFeatureCategory = "sample-data-pack";
    public const string DocumentationPackFeatureCategory = "documentation-pack";
    public const string TemplatePackFeatureCategory = "template-pack";
    public const string StableLifecycle = "stable";
    public const string PreviewLifecycle = "preview";
    public const string ExperimentalLifecycle = "experimental";

    public static readonly IReadOnlySet<string> ValidFeatureCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        RuntimeFeatureCategory,
        KnowledgePackFeatureCategory,
        SampleDataPackFeatureCategory,
        DocumentationPackFeatureCategory,
        TemplatePackFeatureCategory,
    };

    public static readonly IReadOnlySet<string> ValidLifecycles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        StableLifecycle,
        PreviewLifecycle,
        ExperimentalLifecycle,
    };
}

public sealed class KnowledgePackManifest
{
    [YamlMember(Alias = "id")]
    public string Id { get; init; } = string.Empty;

    [YamlMember(Alias = "title")]
    public string Title { get; init; } = string.Empty;

    [YamlMember(Alias = "category")]
    public string Category { get; init; } = CatalogConventions.KnowledgePackFeatureCategory;

    [YamlMember(Alias = "lifecycle")]
    public string? Lifecycle { get; init; }

    [YamlMember(Alias = "description")]
    public string Description { get; init; } = string.Empty;

    [YamlMember(Alias = "sources")]
    public List<KnowledgePackSourceManifest> Sources { get; init; } = new();

    [YamlMember(Alias = "onboarding")]
    public List<string> Onboarding { get; init; } = new();

    [YamlMember(Alias = "skillRefs")]
    public List<string> SkillRefs { get; init; } = new();

    [YamlMember(Alias = "outputAliases")]
    public List<KnowledgePackOutputAliasManifest> OutputAliases { get; init; } = new();

    [YamlMember(Alias = "workspacePaths")]
    public KnowledgePackWorkspacePathsManifest WorkspacePaths { get; init; } = new();
}

public sealed class KnowledgePackSourceManifest
{
    [YamlMember(Alias = "name")]
    public string Name { get; init; } = string.Empty;

    [YamlMember(Alias = "url")]
    public string Url { get; init; } = string.Empty;

    [YamlMember(Alias = "category")]
    public string Category { get; init; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string Description { get; init; } = string.Empty;
}

public sealed class KnowledgePackOutputAliasManifest
{
    [YamlMember(Alias = "source")]
    public string Source { get; init; } = string.Empty;

    [YamlMember(Alias = "destination")]
    public string Destination { get; init; } = string.Empty;
}

public sealed class KnowledgePackWorkspacePathsManifest
{
    [YamlMember(Alias = "knowledgeMap")]
    public string? KnowledgeMap { get; init; }

    [YamlMember(Alias = "knowledgeMapId")]
    public string? KnowledgeMapId { get; init; }

    [YamlMember(Alias = "knowledgeMapTitle")]
    public string? KnowledgeMapTitle { get; init; }

    [YamlMember(Alias = "sourceIndex")]
    public string? SourceIndex { get; init; }
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

    [YamlMember(Alias = "entrypoint")]
    public List<string> EntryPoint { get; init; } = new();

    [YamlMember(Alias = "command")]
    public List<string> Command { get; init; } = new();

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

    [YamlMember(Alias = "smoke")]
    public TemplateSmokeManifest? Smoke { get; init; }
}

public sealed class TemplateSmokeManifest
{
    [YamlMember(Alias = "supported")]
    public bool? Supported { get; init; }

    [YamlMember(Alias = "unsupportedReason")]
    public string? UnsupportedReason { get; init; }

    [YamlMember(Alias = "family")]
    public string Family { get; init; } = string.Empty;

    [YamlMember(Alias = "resourceClass")]
    public string ResourceClass { get; init; } = string.Empty;

    [YamlMember(Alias = "timeoutClass")]
    public string TimeoutClass { get; init; } = string.Empty;

    [YamlMember(Alias = "expectedServices")]
    public List<string> ExpectedServices { get; init; } = new();

    [YamlMember(Alias = "validators")]
    public List<string> Validators { get; init; } = new();
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
