namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class TranscriptEntryViewModel
{
    public TranscriptEntryViewModel(string action, string workspace, string result, DateTimeOffset timestamp, string transcriptLink)
    {
        Action = action;
        Workspace = workspace;
        Result = result;
        Timestamp = timestamp;
        TranscriptLink = transcriptLink;
    }

    public string Action { get; }
    public string Workspace { get; }
    public string Result { get; }
    public DateTimeOffset Timestamp { get; }
    public string TranscriptLink { get; }
    public string TimestampLabel => Timestamp.ToString("u");
}
