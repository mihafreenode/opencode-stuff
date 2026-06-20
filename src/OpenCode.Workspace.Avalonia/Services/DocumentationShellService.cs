namespace OpenCode.Workspace.Avalonia.Services;

public sealed class DocumentationShellService : IDocumentationShellService
{
    private readonly string _applicationBasePath;
    private readonly IDesktopShellService _desktopShellService;

    public DocumentationShellService(string applicationBasePath, IDesktopShellService desktopShellService)
    {
        _applicationBasePath = applicationBasePath;
        _desktopShellService = desktopShellService;
    }

    public IReadOnlyList<DocumentationDocument> GetDocuments()
        =>
        [
            new("README", "README.md", "Project overview and current product direction."),
            new("Platform Compatibility", Path.Combine("docs", "testing", "platform-compatibility.md"), "Host and target platform validation guidance."),
            new("Workspace YAML", Path.Combine("docs", "workspace-yaml.md"), "Canonical workspace configuration format."),
            new("Architecture", Path.Combine("docs", "architecture.md"), "Current product architecture and project split."),
            new("Troubleshooting", Path.Combine("docs", "troubleshooting.md"), "Known troubleshooting and recovery guidance."),
        ];

    public Task OpenDocumentAsync(string relativePath, CancellationToken cancellationToken = default)
        => _desktopShellService.OpenPathAsync(Path.Combine(_applicationBasePath, relativePath), cancellationToken);
}
