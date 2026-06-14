using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

public sealed class EnvironmentFileGenerator
{
    public string Generate(WorkspaceDefinition definition)
    {
        var slug = WorkspacePathBuilder.Slugify(definition.Workspace.Name);
        var lines = new List<string>
        {
            "# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES",
            "# Source inputs: workspace.yaml and catalog manifests under catalog/.",
            "# User edits to this file may be overwritten the next time artifacts are regenerated.",
            $"WORKSPACE_NAME={definition.Workspace.Name}",
            $"WORKSPACE_SLUG={slug}",
        };

        if (definition.Services.Contains("oracle-demo", StringComparer.OrdinalIgnoreCase))
        {
            lines.AddRange(
            [
                "ORACLE_PASSWORD=change-on-first-demo",
                "ORACLE_DEMO_USERNAME=demo_user",
                "ORACLE_DEMO_PASSWORD=demo_password",
                "ORACLE_DEMO_SERVICE=FREEPDB1",
            ]);
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }
}
