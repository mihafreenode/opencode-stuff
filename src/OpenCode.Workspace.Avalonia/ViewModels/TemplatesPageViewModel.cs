using System.Collections.ObjectModel;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class TemplatesPageViewModel : PageViewModel
{
    private TemplateItemViewModel? _selectedTemplate;

    public TemplatesPageViewModel(ITemplateCatalogShellService templateCatalogShellService)
        : base("Templates", "Built-in repository templates and service bundles.")
    {
        foreach (var template in templateCatalogShellService.LoadTemplates())
        {
            Templates.Add(new TemplateItemViewModel(template));
        }

        SelectedTemplate = Templates.FirstOrDefault();
    }

    public ObservableCollection<TemplateItemViewModel> Templates { get; } = [];

    public TemplateItemViewModel? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetProperty(ref _selectedTemplate, value))
            {
                DetailItems.Clear();
                DetailActions.Clear();
                if (value is null)
                {
                    DetailTitle = "Templates";
                    DetailSummary = Description;
                    return;
                }

                DetailTitle = value.Name;
                DetailSummary = value.Description;
                DetailItems.Add(new DetailItemViewModel("Required services", value.Template.Services.Count == 0 ? "None" : string.Join(", ", value.Template.Services)));
                DetailItems.Add(new DetailItemViewModel("Capabilities", value.Template.Features.Count == 0 ? "Core" : string.Join(", ", value.Template.Features)));
                DetailItems.Add(new DetailItemViewModel("Platform notes", string.IsNullOrWhiteSpace(value.Template.WorkspaceImage) ? "Uses default workspace image." : value.Template.WorkspaceImage!));
            }
        }
    }
}
