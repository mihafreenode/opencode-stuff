using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.Services;

// LocalHost-backed application services own shared workspace state and business workflows.
// IDesktopPlatformService owns immediate native OS integration such as opening local paths,
// URLs, and resolved source locations.
public interface IDesktopPlatformService
{
    Task OpenPathAsync(string path, CancellationToken cancellationToken = default);
    Task<WorkspaceSourceNavigationResult> OpenSourceLocationAsync(string path, int line, int column, CancellationToken cancellationToken = default);
}
