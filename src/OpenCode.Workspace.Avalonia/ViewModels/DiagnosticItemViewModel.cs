namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class DiagnosticItemViewModel
{
    public DiagnosticItemViewModel(string title, string statusLabel, string description, string suggestedAction, string? context = null, string? automationId = null)
    {
        Title = title;
        StatusLabel = statusLabel;
        Description = description;
        SuggestedAction = suggestedAction;
        Context = context ?? string.Empty;
        AutomationId = string.IsNullOrWhiteSpace(automationId) ? $"Diagnostic_{BuildSafeToken(title)}" : automationId;
    }

    public string Title { get; }
    public string StatusLabel { get; }
    public string Description { get; }
    public string SuggestedAction { get; }
    public string Context { get; }
    public string AutomationId { get; }
    public string AutomationName => AutomationId;
    public bool HasSuggestedAction => !string.IsNullOrWhiteSpace(SuggestedAction);
    public bool HasContext => !string.IsNullOrWhiteSpace(Context);

    public string EvidenceText
        => string.Join(Environment.NewLine, new[]
        {
            $"{AutomationId}: {Title}",
            $"Status: {StatusLabel}",
            $"Summary: {Description}",
            string.IsNullOrWhiteSpace(Context) ? string.Empty : $"Details: {Context}",
            string.IsNullOrWhiteSpace(SuggestedAction) ? string.Empty : $"Action: {SuggestedAction}",
        }.Where(line => !string.IsNullOrWhiteSpace(line)));

    private static string BuildSafeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unnamed";
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }
}
