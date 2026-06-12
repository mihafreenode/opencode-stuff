using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Catalog;

/// <summary>
/// Expands a template into a concrete workspace definition without involving any
/// WPF view model code. This keeps the template model portable and testable.
/// </summary>
public sealed class TemplateExpander
{
    public WorkspaceDefinition Expand(string workspaceName, TemplateManifest template)
    {
        return new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Name = workspaceName,
                Image = "ubuntu:24.04",
            },
            Features = template.Features.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Services = template.Services.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Skills = new List<string>(),
            Mcp = new List<string>(),
        };
    }
}
