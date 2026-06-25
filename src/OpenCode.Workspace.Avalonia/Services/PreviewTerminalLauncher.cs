using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class PreviewTerminalLauncher : ITerminalLauncher
{
    public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Attach is not available from this shell yet.");
    }
}
