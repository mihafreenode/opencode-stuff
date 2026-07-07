namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class AvailableWorkspaceServiceRowViewModel
{
    public AvailableWorkspaceServiceRowViewModel(
        string service,
        string category,
        string description,
        string status,
        bool actionsEnabled,
        string openOrCommand,
        string credentials,
        string docsPath,
        AsyncRelayCommand? openServiceCommand,
        AsyncRelayCommand? copyUrlCommand,
        AsyncRelayCommand? copyCredentialsCommand,
        AsyncRelayCommand? copyCommandCommand,
        AsyncRelayCommand? openDocsCommand)
    {
        Service = service;
        Category = category;
        Description = description;
        Status = status;
        ActionsEnabled = actionsEnabled;
        OpenOrCommand = openOrCommand;
        Credentials = credentials;
        DocsPath = docsPath;
        OpenServiceCommand = openServiceCommand;
        CopyUrlCommand = copyUrlCommand;
        CopyCredentialsCommand = copyCredentialsCommand;
        CopyCommandCommand = copyCommandCommand;
        OpenDocsCommand = openDocsCommand;
    }

    public string Service { get; }
    public string Category { get; }
    public string Description { get; }
    public string Status { get; }
    public bool ActionsEnabled { get; }
    public string ServiceGlyph => ResolveServiceGlyph(Service, Category);
    public bool IsReadyStatus => Status.Contains("Ready", StringComparison.OrdinalIgnoreCase);
    public bool IsWarningStatus => Status.Contains("Rebuild", StringComparison.OrdinalIgnoreCase)
        || Status.Contains("Update", StringComparison.OrdinalIgnoreCase)
        || Status.Contains("Pending", StringComparison.OrdinalIgnoreCase);
    public bool IsUnavailableStatus => Status.Contains("Unavailable", StringComparison.OrdinalIgnoreCase)
        || Status.Contains("Error", StringComparison.OrdinalIgnoreCase)
        || Status.Contains("Stopped", StringComparison.OrdinalIgnoreCase);
    public string OpenOrCommand { get; }
    public string Credentials { get; }
    public string DocsPath { get; }
    public AsyncRelayCommand? OpenServiceCommand { get; }
    public AsyncRelayCommand? CopyUrlCommand { get; }
    public AsyncRelayCommand? CopyCredentialsCommand { get; }
    public AsyncRelayCommand? CopyCommandCommand { get; }
    public AsyncRelayCommand? OpenDocsCommand { get; }
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public bool HasCredentials => !string.IsNullOrWhiteSpace(Credentials);
    public bool HasDocsPath => !string.IsNullOrWhiteSpace(DocsPath);
    public bool CanOpenService => OpenServiceCommand is not null;
    public bool CanCopyUrl => CopyUrlCommand is not null;
    public bool CanCopyCredentials => CopyCredentialsCommand is not null;
    public bool CanCopyCommand => CopyCommandCommand is not null;
    public bool CanOpenDocs => OpenDocsCommand is not null;

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
