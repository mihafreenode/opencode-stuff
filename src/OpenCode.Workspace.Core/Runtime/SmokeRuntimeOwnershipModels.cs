namespace OpenCode.Workspace.Core.Runtime;

public static class SmokeRuntimeOwnershipLabels
{
    public const string Owner = "io.opencode.workspace.owner";
    public const string RunId = "io.opencode.workspace.run-id";
    public const string Template = "io.opencode.workspace.template";
    public const string CreatedBy = "io.opencode.workspace.created-by";
    public const string Project = "io.opencode.workspace.project";
    public const string WorkspaceRoot = "io.opencode.workspace.workspace-root";
    public const string ComposePath = "io.opencode.workspace.compose-path";
    public const string OwnerValue = "smoke";
    public const string CreatedByValue = "OpenCode.Workspace";
}

public sealed record SmokeCleanupOptions(bool DryRun, bool IncludeAll, string? RunId, string OutputFormat);

public sealed record SmokeOwnedResource(
    string ResourceType,
    string Identifier,
    string Project,
    string RunId,
    string Template,
    string WorkspaceRoot,
    string ComposePath);

public sealed class SmokeCleanupResult
{
    public bool Succeeded { get; init; }
    public bool DryRun { get; init; }
    public IReadOnlyList<SmokeOwnedResource> Resources { get; init; } = Array.Empty<SmokeOwnedResource>();
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed class SmokeResourcePreflight
{
    public string HostMemorySummary { get; init; } = string.Empty;
    public string DockerMemorySummary { get; init; } = string.Empty;
    public string DockerDiskUsageSummary { get; init; } = string.Empty;
    public string DockerStatsSummary { get; init; } = string.Empty;
    public IReadOnlyList<SmokeOwnedResource> ActiveSmokeResources { get; init; } = Array.Empty<SmokeOwnedResource>();
}
