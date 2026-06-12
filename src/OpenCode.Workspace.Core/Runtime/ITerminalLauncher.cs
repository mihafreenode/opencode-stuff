using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Runtime;

public interface ITerminalLauncher
{
    Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default);
}
