using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

public sealed class EnvironmentFileGenerator
{
    public string Generate(WorkspaceDefinition definition)
    {
        var slug = WorkspacePathBuilder.Slugify(definition.Workspace.Name);
        return string.Join(Environment.NewLine, new[]
        {
            "# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES",
            "# Source inputs: workspace.yaml and catalog manifests under catalog/.",
            "# User edits to this file may be overwritten the next time artifacts are regenerated.",
            $"WORKSPACE_NAME={definition.Workspace.Name}",
            $"WORKSPACE_SLUG={slug}",
            string.Empty,
        });
    }
}
