using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class TemplateItemViewModel
{
    public TemplateItemViewModel(TemplateManifest template)
    {
        Template = template;
    }

    public TemplateManifest Template { get; }
    public string Name => Template.DisplayName;
    public string Description => Template.Description;
    public string CapabilitiesSummary => $"Features: {(Template.Features.Count == 0 ? "none" : string.Join(", ", Template.Features))} | Services: {(Template.Services.Count == 0 ? "none" : string.Join(", ", Template.Services))}";
    public IReadOnlyList<string> Features => Template.Features;
    public IReadOnlyList<string> Services => Template.Services;
}
