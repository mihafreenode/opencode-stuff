using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.AppSupport;

public sealed class WorkspaceShellItem
{
    public required WorkspaceRecord Record { get; init; }
    public WorkspaceSnapshot? Snapshot { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public bool IsLoading { get; init; }
    public string LoadingStatusMessage { get; init; } = string.Empty;
    public bool HasSnapshot => Snapshot is not null;
}
