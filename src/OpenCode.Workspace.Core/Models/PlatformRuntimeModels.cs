using YamlDotNet.Serialization;

namespace OpenCode.Workspace.Core.Models;

public enum HostOperatingSystem
{
    Unknown,
    Windows,
    Linux,
    MacOS,
}

public enum HostArchitecture
{
    Unknown,
    X64,
    Arm64,
}

public enum RuntimeCompatibilityMode
{
    Native,
    MultiArchitecture,
    Emulated,
    Unavailable,
}

public enum SupportLevel
{
    NativeTested,
    EmulatedTested,
    CommunityTested,
    Experimental,
    Unavailable,
}

public sealed class ContainerRuntimeAvailability
{
    public string EngineId { get; init; } = string.Empty;
    public bool CliAvailable { get; init; }
    public bool EngineReachable { get; init; }
    public bool BuildxAvailable { get; init; }
    public IReadOnlyList<string> SupportedPlatforms { get; init; } = Array.Empty<string>();
    public string DiagnosticSummary { get; init; } = string.Empty;
}

public sealed class HostPlatformInfo
{
    public HostOperatingSystem OperatingSystem { get; init; }
    public HostArchitecture Architecture { get; init; }
    public string HostDescription { get; init; } = string.Empty;
    public string NativeContainerPlatform { get; init; } = string.Empty;
    public ContainerRuntimeAvailability Docker { get; init; } = new() { EngineId = "docker" };
}

public sealed class ResolvedRuntimePlan
{
    public string Runtime { get; init; } = string.Empty;
    public string TargetPlatform { get; init; } = string.Empty;
    public RuntimeCompatibilityMode CompatibilityMode { get; init; }
    public SupportLevel SupportLevel { get; init; } = SupportLevel.Experimental;
    public bool IsAvailable { get; init; }
    public string DiagnosticExplanation { get; init; } = string.Empty;
    public HostPlatformInfo HostPlatform { get; init; } = new();
}

public sealed class WorkspaceRuntimeStateRecord
{
    [YamlMember(Alias = "resolvedEngine")]
    public string ResolvedEngine { get; init; } = string.Empty;

    [YamlMember(Alias = "resolvedPlatform")]
    public string ResolvedPlatform { get; init; } = string.Empty;

    [YamlMember(Alias = "compatibilityMode")]
    public string CompatibilityMode { get; init; } = string.Empty;

    [YamlMember(Alias = "lastSuccessfulProvision")]
    public DateTimeOffset? LastSuccessfulProvision { get; init; }

    [YamlMember(Alias = "workspaceImageTag")]
    public string WorkspaceImageTag { get; init; } = string.Empty;

    [YamlMember(Alias = "workspaceImageInputHash")]
    public string WorkspaceImageInputHash { get; init; } = string.Empty;

    [YamlMember(Alias = "workspaceImageInputCategories")]
    public Dictionary<string, string> WorkspaceImageInputCategories { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [YamlMember(Alias = "generatedArtifactsUtc")]
    public DateTimeOffset? GeneratedArtifactsUtc { get; init; }

    [YamlMember(Alias = "resources")]
    public WorkspaceManagedRuntimeResources Resources { get; init; } = new();
}
