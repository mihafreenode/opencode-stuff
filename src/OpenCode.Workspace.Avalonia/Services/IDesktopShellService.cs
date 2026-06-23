using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.Services;

public interface IDesktopShellService
{
    Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(bool includeRuntimeInspection, Action<WorkspaceLoadProgressUpdate>? progress = null, CancellationToken cancellationToken = default);
    IReadOnlyList<WorkspaceReference> LoadWorkspaceReferences();
    WorkspaceTimeline LoadTimeline(string timelinePath);
    WorkspaceCheckpointIndex LoadCheckpointIndex(string checkpointIndexPath);
    Task<WorkspaceReprovisionResult> ReprovisionWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default);
    Task OpenPathAsync(string path, CancellationToken cancellationToken = default);
}
