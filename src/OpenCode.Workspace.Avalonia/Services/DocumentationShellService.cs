namespace OpenCode.Workspace.Avalonia.Services;

public sealed class DocumentationShellService : IDocumentationShellService
{
    private readonly string _applicationBasePath;
    private readonly IDesktopPlatformService _desktopPlatformService;

    public DocumentationShellService(string applicationBasePath, IDesktopPlatformService desktopPlatformService)
    {
        _applicationBasePath = applicationBasePath;
        _desktopPlatformService = desktopPlatformService;
    }

    public IReadOnlyList<DocumentationDocument> GetDocuments()
        =>
        [
            new("Getting Started", Path.Combine("docs", "getting-started.md"), "Install the package and create a first workspace."),
            new("README", "README.md", "Project overview and current product direction."),
            new("Package Layout", Path.Combine("docs", "reference", "package-layout.md"), "Supported platforms and installed host layout."),
            new("Workspace YAML", Path.Combine("docs", "reference", "workspace-yaml.md"), "Canonical workspace configuration format."),
            new("Configuration", Path.Combine("docs", "reference", "configuration.md"), "Workspace manager and host configuration reference."),
            new("Troubleshooting", Path.Combine("docs", "user", "troubleshooting.md"), "Known troubleshooting and recovery guidance."),
        ];

    public Task OpenDocumentAsync(string relativePath, CancellationToken cancellationToken = default)
        => _desktopPlatformService.OpenPathAsync(Path.Combine(_applicationBasePath, relativePath), cancellationToken);
}
