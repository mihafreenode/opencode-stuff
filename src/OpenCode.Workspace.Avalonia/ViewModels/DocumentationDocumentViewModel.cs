using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class DocumentationDocumentViewModel
{
    public DocumentationDocumentViewModel(DocumentationDocument document, AsyncRelayCommand openCommand)
    {
        Name = document.Name;
        RelativePath = document.RelativePath;
        Description = document.Description;
        OpenCommand = openCommand;
    }

    public string Name { get; }
    public string RelativePath { get; }
    public string Description { get; }
    public AsyncRelayCommand OpenCommand { get; }
}
