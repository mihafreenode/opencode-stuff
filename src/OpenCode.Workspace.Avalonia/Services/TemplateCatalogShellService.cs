using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class TemplateCatalogShellService : ITemplateCatalogShellService
{
    private readonly BuiltInCatalogProvider _catalogProvider;

    public TemplateCatalogShellService(BuiltInCatalogProvider catalogProvider)
    {
        _catalogProvider = catalogProvider;
    }

    public IReadOnlyList<TemplateManifest> LoadTemplates() => _catalogProvider.LoadTemplates();
}
