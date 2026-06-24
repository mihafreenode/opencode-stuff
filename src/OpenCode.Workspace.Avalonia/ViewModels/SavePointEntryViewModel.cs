namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class SavePointEntryViewModel
{
    public SavePointEntryViewModel(
        string id,
        string title,
        string summary,
        string eventType,
        string message,
        DateTimeOffset timestamp,
        string workspaceName,
        string timelinePath,
        string historyPath,
        string branch = "",
        string commitSha = "",
        IReadOnlyList<string>? affectedPaths = null)
    {
        Id = id;
        Title = title;
        Summary = summary;
        EventType = eventType;
        Message = message;
        Timestamp = timestamp;
        WorkspaceName = workspaceName;
        TimelinePath = timelinePath;
        HistoryPath = historyPath;
        Branch = branch;
        CommitSha = commitSha;
        AffectedPaths = affectedPaths ?? [];
    }

    public string Id { get; }
    public string Title { get; }
    public string Summary { get; }
    public string EventType { get; }
    public string Message { get; }
    public DateTimeOffset Timestamp { get; }
    public string WorkspaceName { get; }
    public string TimelinePath { get; }
    public string HistoryPath { get; }
    public string Branch { get; }
    public string CommitSha { get; }
    public IReadOnlyList<string> AffectedPaths { get; }
    public bool HasBranch => !string.IsNullOrWhiteSpace(Branch);
    public bool HasCommitSha => !string.IsNullOrWhiteSpace(CommitSha);
    public bool HasAffectedPaths => AffectedPaths.Count > 0;
    public int AffectedPathCount => AffectedPaths.Count;
    public string EventTypeLabel => EventType.Replace('-', ' ');
    public string TimestampLabel => Timestamp.ToString("u");
}
