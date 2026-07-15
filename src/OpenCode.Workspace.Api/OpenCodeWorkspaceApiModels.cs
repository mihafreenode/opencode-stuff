using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Mcp;

namespace OpenCode.Workspace.Api;

public sealed class ApiEnvelope<T>
{
    public string ContractVersion { get; init; } = OpenCodeWorkspaceMcpContract.ContractVersion;
    public T Data { get; init; } = default!;
}

public sealed class ApiErrorEnvelope
{
    public string ContractVersion { get; init; } = OpenCodeWorkspaceMcpContract.ContractVersion;
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
}

public sealed class ApiHealthResponse
{
    public string ContractVersion { get; init; } = OpenCodeWorkspaceMcpContract.ContractVersion;
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public RuntimeResourceInventory? RuntimeInventory { get; init; }
}

public sealed class CreateWorkspaceRequest
{
    public string TemplateId { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string DestinationRoot { get; init; } = string.Empty;
}

public sealed class StartSmokeRunRequest
{
    public string TemplateId { get; init; } = string.Empty;
    public string? Timeout { get; init; }
    public string? ArtifactsRoot { get; init; }
}

public sealed class StartSmokeMatrixRequest
{
    public IReadOnlyList<string> TemplateIds { get; init; } = Array.Empty<string>();
    public string? Family { get; init; }
    public bool All { get; init; }
    public string? Timeout { get; init; }
}

public sealed class CleanupSmokeRequest
{
    public bool DryRun { get; init; }
    public bool IncludeAll { get; init; }
    public string? RunId { get; init; }
}
