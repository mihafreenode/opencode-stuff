using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.Services;

public interface ITemplateCatalogShellService
{
    IReadOnlyList<TemplateManifest> LoadTemplates();
}
