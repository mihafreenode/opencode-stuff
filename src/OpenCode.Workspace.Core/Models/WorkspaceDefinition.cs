using YamlDotNet.Serialization;

namespace OpenCode.Workspace.Core.Models;

/// <summary>
/// workspace.yaml is the portable source of truth for a workspace.
/// Keep this model close to the YAML shape so contributors can map file content
/// to code without hunting through abstraction layers.
/// </summary>
public sealed class WorkspaceDefinition
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
}

public sealed class WorkspaceMetadata
{
    [YamlMember(Alias = "id")]
    public string Id { get; init; } = string.Empty;

    [YamlMember(Alias = "name")]
    public string Name { get; init; } = string.Empty;

    [YamlMember(Alias = "image")]
    public string Image { get; init; } = "ubuntu:24.04";
}

public sealed class WorkspaceProviderDefinition
{
    [YamlMember(Alias = "type")]
    public string Type { get; init; } = "git";

    [YamlMember(Alias = "url")]
    public string? Url { get; init; }
}

public sealed class WorkspaceRuntimeDefinition
{
    public const int DefaultNodeMajorVersion = 22;

    [YamlMember(Alias = "default")]
    public string Default { get; init; } = "default";

    [YamlMember(Alias = "node")]
    public int Node { get; init; } = DefaultNodeMajorVersion;

    public int GetEffectiveNodeMajorVersion()
        => Node > 0 ? Node : DefaultNodeMajorVersion;
}

/// <summary>
/// Terminal preferences are part of workspace intent because the same prompt and
/// font decisions should remain portable across local and future hosted catalog
/// scenarios, even if the runtime implementation differs.
/// </summary>
public sealed class TerminalPreferences
{
    [YamlMember(Alias = "font")]
    public TerminalFontPreferences Font { get; init; } = new();

    [YamlMember(Alias = "prompt")]
    public TerminalPromptPreferences Prompt { get; init; } = new();

    [YamlMember(Alias = "installIfMissing")]
    public bool InstallIfMissing { get; init; } = true;

    [YamlMember(Alias = "utilities")]
    public TerminalUtilityPreferences Utilities { get; init; } = new();
}

public sealed class TerminalFontPreferences
{
    [YamlMember(Alias = "provider")]
    public string Provider { get; init; } = "nerd-fonts";

    [YamlMember(Alias = "family")]
    public string Family { get; init; } = "JetBrainsMono Nerd Font";
}

public sealed class TerminalPromptPreferences
{
    [YamlMember(Alias = "provider")]
    public string Provider { get; init; } = "starship";
}

public sealed class TerminalUtilityPreferences
{
    [YamlMember(Alias = "zoxide")]
    public bool Zoxide { get; init; }

    [YamlMember(Alias = "fzf")]
    public bool Fzf { get; init; }
}

/// <summary>
/// Workspaces reference agent profiles by default so the recommended OpenCode
/// defaults can evolve without forcing migrations of every workspace file.
/// Direct provider/connection/model overrides remain possible for advanced users.
/// </summary>
public sealed class AgentPreferences
{
    [YamlMember(Alias = "profile")]
    public string Profile { get; init; } = "opencode-default";

    [YamlMember(Alias = "provider")]
    public string? Provider { get; init; }

    [YamlMember(Alias = "connection")]
    public string? Connection { get; init; }

    [YamlMember(Alias = "model")]
    public string? Model { get; init; }
}

public sealed class OracleWorkspacePreferences
{
    [YamlMember(Alias = "hostPort")]
    public int? HostPort { get; init; }

    [YamlMember(Alias = "ordsPort")]
    public int? OrdsPort { get; init; }
}

public sealed class ResolvedAgentProfile
{
    public required string ProfileId { get; init; }
    public required string Provider { get; init; }
    public required string Connection { get; init; }
    public required string Model { get; init; }
    public required string ResolutionSource { get; init; }
    public bool UsesBuiltInDefault { get; init; }
}
