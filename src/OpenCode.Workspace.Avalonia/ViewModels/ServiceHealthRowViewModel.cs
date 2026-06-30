namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class ServiceHealthRowViewModel
{
    public ServiceHealthRowViewModel(string name, string status, string summary, string applications, string primaryUrl, string highlights, string details, string actionLabel, string openUrl, AsyncRelayCommand? openCommand)
    {
        Name = name;
        Status = status;
        Summary = summary;
        Applications = applications;
        PrimaryUrl = primaryUrl;
        Highlights = highlights;
        Details = details;
        ActionLabel = actionLabel;
        OpenUrl = openUrl;
        OpenCommand = openCommand;
    }

    public string Name { get; }
    public string Status { get; }
    public string Summary { get; }
    public string Applications { get; }
    public string PrimaryUrl { get; }
    public string Highlights { get; }
    public string Details { get; }
    public string ActionLabel { get; }
    public string OpenUrl { get; }
    public AsyncRelayCommand? OpenCommand { get; }
    public bool HasApplications => !string.IsNullOrWhiteSpace(Applications);
    public bool HasPrimaryUrl => !string.IsNullOrWhiteSpace(PrimaryUrl);
    public bool HasHighlights => !string.IsNullOrWhiteSpace(Highlights);
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    public bool CanOpen => OpenCommand is not null && !string.IsNullOrWhiteSpace(OpenUrl);
}
