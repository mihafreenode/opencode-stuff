namespace OpenCode.Workspace.Core.Models;

public sealed class WorkspaceLoadTiming
{
    public string StageKey { get; init; } = string.Empty;
    public string StageLabel { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public TimeSpan Duration { get; init; }
    public bool Succeeded { get; init; }
    public string FailureMessage { get; init; } = string.Empty;
}

public sealed class WorkspaceLoadStageProgress
{
    public string StageKey { get; init; } = string.Empty;
    public string StageLabel { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
}
