using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

public sealed class EnvironmentFileGenerator
{
    private const string GeneratedFileLineEnding = "\n";

    public string Generate(WorkspaceDefinition definition, GeneratedArtifactRuntimeMetadata? runtimeMetadata = null, WorkspaceRuntimeStateRecord? runtimeState = null)
    {
        var slug = WorkspacePathBuilder.Slugify(definition.Workspace.Name);
        var lines = new List<string>
        {
            GeneratedArtifactRuntimeMetadataBuilder.BuildCommentHeader(
                runtimeMetadata,
                "Source inputs: workspace.yaml and catalog manifests under catalog/.",
                "User edits to this file may be overwritten the next time artifacts are regenerated."),
            $"WORKSPACE_NAME={definition.Workspace.Name}",
            $"WORKSPACE_SLUG={slug}",
        };

        if (OracleWorkspaceFamily.IsOracleWorkspace(definition))
        {
            var oraclePort = WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(definition, runtimeState, WorkspaceRuntimeResourceCatalog.OracleDatabaseResourceId);
            var ordsPort = OracleWorkspaceFamily.HasApex(definition)
                ? WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(definition, runtimeState, WorkspaceRuntimeResourceCatalog.OracleOrdsResourceId)
                : OracleWorkspaceSettings.From(definition).OrdsPort;
            lines.AddRange(
            [
                "ORACLE_PASSWORD=change-on-first-demo",
                "ORACLE_DEMO_USERNAME=demo_user",
                "ORACLE_DEMO_PASSWORD=demo_password",
                "ORACLE_DEMO_SERVICE=FREEPDB1",
                "ORACLE_DEMO_CONNECTION=demo_user/demo_password@//oracle-demo:1521/FREEPDB1",
                $"ORACLE_HOST_PORT={oraclePort}",
                $"ORACLE_ORDS_PORT={ordsPort}",
                $"ORACLE_ORDS_BASE_URL=http://localhost:{ordsPort}/ords",
                $"ORACLE_APEX_LOGIN_URL=http://localhost:{ordsPort}/ords/apex_admin",
            ]);
        }

        if (definition.Services.Contains("postgres", StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"POSTGRES_HOST_PORT={WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(definition, runtimeState, WorkspaceRuntimeResourceCatalog.PostgresResourceId)}");
        }

        if (definition.Services.Contains("pgadmin", StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"PGADMIN_PORT={WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(definition, runtimeState, WorkspaceRuntimeResourceCatalog.PgAdminResourceId)}");
        }

        if (definition.Features.Contains("analytics-reporting", StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"MARIMO_PORT={WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(definition, runtimeState, WorkspaceRuntimeResourceCatalog.MarimoResourceId)}");
        }

        lines.Add(string.Empty);
        return string.Join(GeneratedFileLineEnding, lines);
    }
}
