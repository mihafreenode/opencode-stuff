namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class AvailableWorkspaceServiceRowViewModel
{
    public AvailableWorkspaceServiceRowViewModel(
        WorkspacePresentedService service,
        IReadOnlyList<ActionItemViewModel> actions)
    {
        PresentedService = service;
        Actions = actions;
    }

    public WorkspacePresentedService PresentedService { get; }
    public string Service => PresentedService.Service;
    public string Category => PresentedService.Category;
    public string Description => PresentedService.Description;
    public string Status => PresentedService.Status;
    public string ServiceGlyph => ResolveServiceGlyph(Service, Category);
    public bool IsReadyStatus => PresentedService.Tone == WorkspacePresentationTone.Ready;
    public bool IsWarningStatus => PresentedService.Tone == WorkspacePresentationTone.Warning;
    public bool IsUnavailableStatus => PresentedService.Tone == WorkspacePresentationTone.Unavailable;
    public string OpenOrCommand => PresentedService.OpenOrCommand;
    public string Credentials => PresentedService.Credentials;
    public string DocsPath => PresentedService.DocsPath;
    public IReadOnlyList<ActionItemViewModel> Actions { get; }
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public bool HasCredentials => !string.IsNullOrWhiteSpace(Credentials);
    public bool HasDocsPath => !string.IsNullOrWhiteSpace(DocsPath);

    private static string ResolveServiceGlyph(string service, string category)
    {
        var value = string.Join(' ', new[] { service, category }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (value.Contains("APEX", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE8B7";
        }

        if (value.Contains("REST", StringComparison.OrdinalIgnoreCase) || value.Contains("API", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE774";
        }

        if (value.Contains("Shell", StringComparison.OrdinalIgnoreCase) || value.Contains("Terminal", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE756";
        }

        if (value.Contains("Database", StringComparison.OrdinalIgnoreCase) || value.Contains("SQL", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE9F9";
        }

        if (value.Contains("Docs", StringComparison.OrdinalIgnoreCase) || value.Contains("Documentation", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE8A5";
        }

        return "\uE9CE";
    }
}
