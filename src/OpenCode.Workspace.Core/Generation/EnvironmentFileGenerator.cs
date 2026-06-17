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

        if (OracleWorkspaceFamily.IsOracleWorkspace(definition))
        {
            var oracleSettings = OracleWorkspaceSettings.From(definition);
            lines.AddRange(
            [
                "ORACLE_PASSWORD=change-on-first-demo",
                "ORACLE_DEMO_USERNAME=demo_user",
                "ORACLE_DEMO_PASSWORD=demo_password",
                "ORACLE_DEMO_SERVICE=FREEPDB1",
                "ORACLE_DEMO_CONNECTION=demo_user/demo_password@//oracle-demo:1521/FREEPDB1",
                $"ORACLE_HOST_PORT={oracleSettings.HostPort}",
                $"ORACLE_ORDS_PORT={oracleSettings.OrdsPort}",
                $"ORACLE_ORDS_BASE_URL={oracleSettings.OrdsBaseUrl}",
                $"ORACLE_APEX_LOGIN_URL={oracleSettings.ApexLoginUrl}",
            ]);
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }
}
