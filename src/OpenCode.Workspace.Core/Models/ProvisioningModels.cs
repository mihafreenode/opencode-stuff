namespace OpenCode.Workspace.Core.Models;

/// <summary>
/// ResolvedWorkspace turns human-friendly feature and service selections into the
/// concrete package and container plan needed to generate runtime artifacts.
/// Keeping the resolved shape explicit makes it easier to debug catalog behavior.
/// </summary>
public sealed class ResolvedWorkspace
{
    public required WorkspaceDefinition Definition { get; init; }
    public required IReadOnlyList<FeatureManifest> Features { get; init; }
    public required IReadOnlyList<CapabilityManifest> Capabilities { get; init; }
    public required IReadOnlyList<ServiceManifest> Services { get; init; }
    public required IReadOnlyList<string> AptPackages { get; init; }
    public required IReadOnlyList<string> NpmPackages { get; init; }
    public required IReadOnlyList<string> PipPackages { get; init; }
    public required IReadOnlyList<string> PostInstallCommands { get; init; }
}

public sealed class GeneratedWorkspaceArtifacts
{
    public required string WorkspaceYaml { get; init; }
    public required string ComposeYaml { get; init; }
    public required string EnvironmentFile { get; init; }
    public required string ProvisionScript { get; init; }
    public required string StarshipConfig { get; init; }
    public required string ShellInitScript { get; init; }
    public required string OpencodeWorkspaceShellScript { get; init; }
    public required string ScreenConfig { get; init; }
    public required string AttachWrapperScript { get; init; }
    public required string TerminalDiagnosticsScript { get; init; }
    public required string WorkspaceDefinitionHash { get; init; }
    public required string DesiredStateHash { get; init; }
    public required IReadOnlyDictionary<string, string> AdditionalFiles { get; init; }
}
