using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class WorkspaceReprovisionResult
{
    public required WorkspaceSnapshot Snapshot { get; init; }
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
}
