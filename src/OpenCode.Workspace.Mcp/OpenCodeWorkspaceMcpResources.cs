using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace OpenCode.Workspace.Mcp;

[McpServerResourceType]
public sealed class OpenCodeWorkspaceMcpResources
{
    [McpServerResource(UriTemplate = "opencode://server/health", Name = "Server Health", MimeType = "application/json")]
    [Description("Read local MCP server health and binding information.")]
    public static string GetServerHealth(IOpenCodeWorkspaceMcpService service)
        => JsonSerializer.Serialize(service.GetServerHealth(), new JsonSerializerOptions { WriteIndented = true });

    [McpServerResource(UriTemplate = "opencode://templates/{templateId}", Name = "Workspace Template", MimeType = "application/json")]
    [Description("Read a resolved workspace template definition.")]
    public static async Task<TextResourceContents> GetTemplate(string templateId, IOpenCodeWorkspaceMcpService service)
        => new()
        {
            Uri = $"opencode://templates/{templateId}",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(await service.GetWorkspaceTemplateAsync(templateId), new JsonSerializerOptions { WriteIndented = true }),
        };

    [McpServerResource(UriTemplate = "opencode://workspaces/{workspaceId}", Name = "Workspace Snapshot", MimeType = "application/json")]
    [Description("Read a local workspace snapshot.")]
    public static async Task<TextResourceContents> GetWorkspace(string workspaceId, IOpenCodeWorkspaceMcpService service)
        => new()
        {
            Uri = $"opencode://workspaces/{workspaceId}",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(await service.GetWorkspaceAsync(workspaceId), new JsonSerializerOptions { WriteIndented = true }),
        };

    [McpServerResource(UriTemplate = "opencode://operations/{operationId}", Name = "Operation", MimeType = "application/json")]
    [Description("Read a local in-memory operation snapshot.")]
    public static TextResourceContents GetOperation(string operationId, McpOperationStore operations)
        => new()
        {
            Uri = $"opencode://operations/{operationId}",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(operations.Get(operationId), new JsonSerializerOptions { WriteIndented = true }),
        };

    [McpServerResource(UriTemplate = "opencode://smoke/{runId}/summary", Name = "Smoke Summary", MimeType = "application/json")]
    [Description("Read smoke summary JSON for a run id.")]
    public static async Task<TextResourceContents> GetSmokeSummary(string runId, IOpenCodeWorkspaceMcpService service)
    {
        var artifact = await service.GetSmokeArtifactAsync(runId, "summary.json");
        return new TextResourceContents { Uri = $"opencode://smoke/{runId}/summary", MimeType = "application/json", Text = artifact.Text };
    }

    [McpServerResource(UriTemplate = "opencode://smoke-matrices/{matrixRunId}/summary", Name = "Smoke Matrix Summary", MimeType = "application/json")]
    [Description("Read smoke matrix summary JSON for a matrix run id.")]
    public static async Task<TextResourceContents> GetSmokeMatrixSummary(string matrixRunId, IOpenCodeWorkspaceMcpService service)
    {
        var artifact = await service.GetSmokeArtifactAsync(matrixRunId, "matrix-summary.json");
        return new TextResourceContents { Uri = $"opencode://smoke-matrices/{matrixRunId}/summary", MimeType = "application/json", Text = artifact.Text };
    }

    [McpServerResource(UriTemplate = "opencode://runtime/inventory", Name = "Runtime Inventory", MimeType = "application/json")]
    [Description("Read runtime inventory for all owned OpenCode resources.")]
    public static async Task<TextResourceContents> GetRuntimeInventory(IOpenCodeWorkspaceMcpService service)
        => new()
        {
            Uri = "opencode://runtime/inventory",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(await service.ListRuntimeResourcesAsync(new OpenCode.Workspace.Core.Runtime.RuntimeOwnershipQuery()), new JsonSerializerOptions { WriteIndented = true }),
        };

    [McpServerResource(UriTemplate = "opencode://artifacts/{artifactId}", Name = "Artifact", MimeType = "application/octet-stream")]
    [Description("Read a validated workspace or smoke artifact.")]
    public static async Task<ResourceContents> GetArtifact(string artifactId, IOpenCodeWorkspaceMcpService service)
    {
        var artifact = await service.ReadArtifactResourceAsync($"opencode://artifacts/{artifactId}");
        if (artifact.Artifact.IsTextInline)
        {
            return new TextResourceContents
            {
                Uri = artifact.Artifact.Metadata.ResourceUri,
                MimeType = artifact.Artifact.Metadata.MimeType,
                Text = artifact.Artifact.Text,
            };
        }

        return BlobResourceContents.FromBytes(artifact.Bytes, artifact.Artifact.Metadata.ResourceUri, artifact.Artifact.Metadata.MimeType);
    }
}
