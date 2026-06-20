namespace OpenCode.Workspace.AppSupport;

public sealed class WorkspaceLoadResult
{
    public IReadOnlyList<WorkspaceShellItem> Items { get; init; } = Array.Empty<WorkspaceShellItem>();
    public WorkspaceLoadReport Report { get; init; } = new();
}
