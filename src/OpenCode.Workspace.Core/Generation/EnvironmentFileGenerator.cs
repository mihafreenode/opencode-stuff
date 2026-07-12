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
            var oracle = OracleRuntimeConfiguration.From(definition, runtimeState);
            foreach (var pair in oracle.ToEnvironmentVariables().OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"{pair.Key}={pair.Value}");
            }
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
