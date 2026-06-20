using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.Services;

public interface IDesktopShellService
{
    Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(bool includeRuntimeInspection, CancellationToken cancellationToken = default);
    IReadOnlyList<WorkspaceReference> LoadWorkspaceReferences();
    WorkspaceTimeline LoadTimeline(string timelinePath);
    WorkspaceCheckpointIndex LoadCheckpointIndex(string checkpointIndexPath);
    Task OpenPathAsync(string path, CancellationToken cancellationToken = default);
}
