namespace OpenCode.Workspace.Core.Models;

public sealed class WorkspaceRuntimeExplorerReport
{
    public DateTimeOffset GeneratedUtc { get; init; }
    public bool UsedDockerProbe { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<WorkspaceRuntimeWorkspaceEntry> Workspaces { get; init; } = Array.Empty<WorkspaceRuntimeWorkspaceEntry>();
    public IReadOnlyList<WorkspaceRuntimeResourceEntry> Resources { get; init; } = Array.Empty<WorkspaceRuntimeResourceEntry>();
    public IReadOnlyList<WorkspaceRuntimeConflictEntry> Conflicts { get; init; } = Array.Empty<WorkspaceRuntimeConflictEntry>();
    public IReadOnlyList<WorkspaceRuntimeResourceEntry> UnusedResources { get; init; } = Array.Empty<WorkspaceRuntimeResourceEntry>();
    public IReadOnlyList<WorkspaceRuntimeResourceEntry> OrphanedResources { get; init; } = Array.Empty<WorkspaceRuntimeResourceEntry>();
    public IReadOnlyList<WorkspaceRuntimeHealthEntry> Health { get; init; } = Array.Empty<WorkspaceRuntimeHealthEntry>();
}

public sealed class WorkspaceRuntimeWorkspaceEntry
{
    public string WorkspaceName { get; init; } = string.Empty;
    public string WorkspaceRootPath { get; init; } = string.Empty;
    public string OwningRuntime { get; init; } = string.Empty;
    public string Template { get; init; } = string.Empty;
    public DateTimeOffset? CreatedUtc { get; init; }
    public DateTimeOffset? LastUsedUtc { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Health { get; init; } = string.Empty;
    public string RuntimeIdentifier { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public IReadOnlyList<string> Ports { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Containers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Volumes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Networks { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Services { get; init; } = Array.Empty<string>();
}

public sealed class WorkspaceRuntimeResourceEntry
{
    public string ResourceType { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string WorkspaceRootPath { get; init; } = string.Empty;
    public string OwningRuntime { get; init; } = string.Empty;
    public string RuntimeIdentifier { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Health { get; init; } = string.Empty;
    public bool CanCleanUpSafely { get; init; }
    public string CleanupSummary { get; init; } = string.Empty;
    public string ServiceId { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string OpenUrl { get; init; } = string.Empty;
    public int? PreferredPort { get; init; }
    public int? CurrentPort { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class WorkspaceRuntimeConflictEntry
{
    public string ConflictType { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string CurrentOwner { get; init; } = string.Empty;
    public string RequestedOwner { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
    public string WorkspaceRootPath { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public string ResourceIdentifier { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
}

public sealed class WorkspaceRuntimeHealthEntry
{
    public string Category { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
}

public sealed class WorkspaceRuntimeInspectResult
{
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
}
