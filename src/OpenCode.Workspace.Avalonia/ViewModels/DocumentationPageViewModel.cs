using System.Collections.ObjectModel;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class DocumentationPageViewModel : PageViewModel
{
    private readonly IDocumentationShellService _documentationShellService;
    private DocumentationDocumentViewModel? _selectedDocument;

    public DocumentationPageViewModel(IDocumentationShellService documentationShellService)
        : base("Documentation", "Local docs shipped with the preview shell output.")
    {
        _documentationShellService = documentationShellService;
        foreach (var document in documentationShellService.GetDocuments())
        {
            var localDocument = document;
            Documents.Add(new DocumentationDocumentViewModel(localDocument, new AsyncRelayCommand(() => _documentationShellService.OpenDocumentAsync(localDocument.RelativePath))));
        }

        SelectedDocument = Documents.FirstOrDefault();
    }

    public ObservableCollection<DocumentationDocumentViewModel> Documents { get; } = [];

    public DocumentationDocumentViewModel? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (SetProperty(ref _selectedDocument, value))
            {
                DetailItems.Clear();
                DetailActions.Clear();
                if (value is null)
                {
                    DetailTitle = "Documentation";
                    DetailSummary = Description;
                    return;
                }

                DetailTitle = value.Name;
                DetailSummary = value.Description;
                DetailItems.Add(new DetailItemViewModel("Path", value.RelativePath));
                DetailActions.Add(new ActionItemViewModel("Open", "Open this local document with the host default application.", true, string.Empty, value.OpenCommand));
            }
        }
    }
}
