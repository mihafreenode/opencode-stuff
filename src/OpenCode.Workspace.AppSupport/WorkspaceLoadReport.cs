namespace OpenCode.Workspace.AppSupport;

public sealed class WorkspaceLoadReport
{
    public string AppDataRoot { get; init; } = string.Empty;
    public string IndexFilePath { get; init; } = string.Empty;
    public bool IndexFileExists { get; init; }
    public int RawRecordCount { get; init; }
    public int SnapshotAttemptCount { get; init; }
    public int SnapshotCount { get; init; }
    public IReadOnlyList<WorkspaceLoadFailure> Failures { get; init; } = Array.Empty<WorkspaceLoadFailure>();
    public int ItemsReturnedCount { get; init; }
    public int FailureCount => Failures.Count;
}
