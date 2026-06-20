namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class DiagnosticItemViewModel
{
    public DiagnosticItemViewModel(string title, string statusLabel, string description, string suggestedAction, string? context = null)
    {
        Title = title;
        StatusLabel = statusLabel;
        Description = description;
        SuggestedAction = suggestedAction;
        Context = context ?? string.Empty;
    }

    public string Title { get; }
    public string StatusLabel { get; }
    public string Description { get; }
    public string SuggestedAction { get; }
    public string Context { get; }
    public bool HasSuggestedAction => !string.IsNullOrWhiteSpace(SuggestedAction);
    public bool HasContext => !string.IsNullOrWhiteSpace(Context);
}
