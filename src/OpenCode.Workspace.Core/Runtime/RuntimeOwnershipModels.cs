namespace OpenCode.Workspace.Core.Runtime;

public static class RuntimeOwnershipLabels
{
    public const string Owner = "io.opencode.workspace.owner";
    public const string RunId = "io.opencode.workspace.run-id";
    public const string Template = "io.opencode.workspace.template";
    public const string CreatedBy = "io.opencode.workspace.created-by";
    public const string Project = "io.opencode.workspace.project";
    public const string WorkspaceRoot = "io.opencode.workspace.workspace-root";
    public const string ComposePath = "io.opencode.workspace.compose-path";
    public const string CreatedAt = "io.opencode.workspace.created-at";
    public const string CreatedByValue = "OpenCode.Workspace";
}

public static class SmokeRuntimeOwnershipLabels
{
    public const string Owner = RuntimeOwnershipLabels.Owner;
    public const string RunId = RuntimeOwnershipLabels.RunId;
    public const string Template = RuntimeOwnershipLabels.Template;
    public const string CreatedBy = RuntimeOwnershipLabels.CreatedBy;
    public const string Project = RuntimeOwnershipLabels.Project;
    public const string WorkspaceRoot = RuntimeOwnershipLabels.WorkspaceRoot;
    public const string ComposePath = RuntimeOwnershipLabels.ComposePath;
    public const string CreatedAt = RuntimeOwnershipLabels.CreatedAt;
    public const string OwnerValue = "smoke";
    public const string CreatedByValue = RuntimeOwnershipLabels.CreatedByValue;
}

public enum RuntimeResourceType
{
    Container,
    Network,
    Volume,
}

public sealed class RuntimeOwnershipQuery
{
    public string? OwnerKind { get; init; }
    public string? RunId { get; init; }
    public string? WorkspaceRoot { get; init; }
    public string? Project { get; init; }
}

public sealed class RuntimeCleanupOptions
{
    public bool DryRun { get; init; }
    public string OutputFormat { get; init; } = "text";
    public string? OwnerKind { get; init; }
    public string? RunId { get; init; }
    public string? WorkspaceRoot { get; init; }
    public string? Project { get; init; }
}

public sealed class RuntimeOwnedResource
{
    public required string ResourceId { get; init; }
    public required string Name { get; init; }
    public required RuntimeResourceType Type { get; init; }
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public string OwnerKind { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public string Template { get; init; } = string.Empty;
    public string WorkspaceRoot { get; init; } = string.Empty;
    public string ComposePath { get; init; } = string.Empty;
    public string CreatedAt { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool IsOrphaned { get; init; }
    public bool IsStale { get; init; }
    public IReadOnlyList<string> MissingLabels { get; init; } = Array.Empty<string>();
}

public sealed class RuntimeProjectInventory
{
    public string Project { get; init; } = string.Empty;
    public string OwnerKind { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string Template { get; init; } = string.Empty;
    public string WorkspaceRoot { get; init; } = string.Empty;
    public string ComposePath { get; init; } = string.Empty;
    public string CreatedAt { get; init; } = string.Empty;
    public IReadOnlyList<RuntimeOwnedResource> Resources { get; init; } = Array.Empty<RuntimeOwnedResource>();
}

public sealed class RuntimeInventoryIssue
{
    public required string Kind { get; init; }
    public required string Message { get; init; }
    public string Project { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string ResourceName { get; init; } = string.Empty;
}

public sealed class RuntimeResourceInventory
{
    public IReadOnlyList<RuntimeOwnedResource> Resources { get; init; } = Array.Empty<RuntimeOwnedResource>();
    public IReadOnlyList<RuntimeProjectInventory> Projects { get; init; } = Array.Empty<RuntimeProjectInventory>();
    public IReadOnlyList<RuntimeInventoryIssue> Orphans { get; init; } = Array.Empty<RuntimeInventoryIssue>();
    public IReadOnlyList<RuntimeInventoryIssue> StaleRuntimes { get; init; } = Array.Empty<RuntimeInventoryIssue>();
    public IReadOnlyList<RuntimeInventoryIssue> DuplicateRunIds { get; init; } = Array.Empty<RuntimeInventoryIssue>();
    public IReadOnlyList<RuntimeInventoryIssue> MissingRequiredLabels { get; init; } = Array.Empty<RuntimeInventoryIssue>();
    public IReadOnlyList<RuntimeInventoryIssue> MissingComposeFiles { get; init; } = Array.Empty<RuntimeInventoryIssue>();
    public IReadOnlyList<RuntimeInventoryIssue> MissingWorkspaceDirectories { get; init; } = Array.Empty<RuntimeInventoryIssue>();
}

public sealed class RuntimeCleanupResult
{
    public bool Succeeded { get; init; }
    public bool DryRun { get; init; }
    public RuntimeOwnershipQuery Filter { get; init; } = new();
    public IReadOnlyList<RuntimeOwnedResource> Resources { get; init; } = Array.Empty<RuntimeOwnedResource>();
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed class RuntimeResourcePreflight
{
    public string HostMemorySummary { get; init; } = string.Empty;
    public string DockerMemorySummary { get; init; } = string.Empty;
    public string DockerDiskUsageSummary { get; init; } = string.Empty;
    public string DockerStatsSummary { get; init; } = string.Empty;
    public IReadOnlyList<RuntimeOwnedResource> ActiveOwnedResources { get; init; } = Array.Empty<RuntimeOwnedResource>();
}

public sealed record SmokeCleanupOptions(bool DryRun, bool IncludeAll, string? RunId, string OutputFormat);

public sealed class SmokeCleanupResult
{
    public bool Succeeded { get; init; }
    public bool DryRun { get; init; }
    public IReadOnlyList<RuntimeOwnedResource> Resources { get; init; } = Array.Empty<RuntimeOwnedResource>();
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed class SmokeResourcePreflight
{
    public string HostMemorySummary { get; init; } = string.Empty;
    public string DockerMemorySummary { get; init; } = string.Empty;
    public string DockerDiskUsageSummary { get; init; } = string.Empty;
    public string DockerStatsSummary { get; init; } = string.Empty;
    public IReadOnlyList<RuntimeOwnedResource> ActiveSmokeResources { get; init; } = Array.Empty<RuntimeOwnedResource>();
}
