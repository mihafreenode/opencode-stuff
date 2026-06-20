namespace OpenCode.Workspace.Avalonia.Services;

public interface IDocumentationShellService
{
    IReadOnlyList<DocumentationDocument> GetDocuments();
    Task OpenDocumentAsync(string relativePath, CancellationToken cancellationToken = default);
}
