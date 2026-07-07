namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class AvailableWorkspaceServiceRowViewModel
{
    public AvailableWorkspaceServiceRowViewModel(
        string service,
        string category,
        string description,
        string status,
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
}
