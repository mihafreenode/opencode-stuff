namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class SavePointEntryViewModel
{
    public SavePointEntryViewModel(string title, string summary, DateTimeOffset timestamp, string workspaceName)
    {
        Title = title;
        Summary = summary;
        Timestamp = timestamp;
        WorkspaceName = workspaceName;
    }

    public string Title { get; }
    public string Summary { get; }
    public DateTimeOffset Timestamp { get; }
    public string WorkspaceName { get; }
    public string TimestampLabel => Timestamp.ToString("u");
}
