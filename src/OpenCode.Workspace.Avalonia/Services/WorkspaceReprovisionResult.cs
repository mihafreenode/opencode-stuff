using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.AppSupport;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class WorkspaceReprovisionResult
{
    public required WorkspaceSnapshot Snapshot { get; init; }
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public OperationTranscript Transcript { get; init; } = new();
}
