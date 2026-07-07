namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspaceRecentActivityItemViewModel
{
    public WorkspaceRecentActivityItemViewModel(string title, string summary, string timeLabel)
    {
        Title = title;
        Summary = summary;
        TimeLabel = timeLabel;
    }

    public string Title { get; }
    public string Summary { get; }
    public string TimeLabel { get; }
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
}
