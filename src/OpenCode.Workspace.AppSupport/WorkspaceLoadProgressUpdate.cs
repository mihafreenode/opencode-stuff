namespace OpenCode.Workspace.AppSupport;

public sealed class WorkspaceLoadProgressUpdate
{
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string ProgressLabel { get; init; } = string.Empty;
    public string CurrentWorkspaceName { get; init; } = string.Empty;
    public int CurrentWorkspaceIndex { get; init; }
    public int TotalWorkspaces { get; init; }
    public WorkspaceShellItem? LoadedItem { get; init; }
    public bool IsCompleted { get; init; }
}
