using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using System.Text.Json;

namespace OpenCode.Workspace.Mcp;

public static class OpenCodeWorkspaceMcpContract
{
    public const string ContractVersion = "1";

    public static readonly JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}

public sealed class OpenCodeWorkspaceMcpOptions
{
    public string Transport { get; init; } = "stdio";
    public string CatalogRoot { get; init; } = string.Empty;
    public string WorkspaceStateRoot { get; init; } = string.Empty;
    public string SmokeArtifactsRoot { get; init; } = string.Empty;
    public OpenCodeWorkspaceMcpHttpOptions Http { get; init; } = new();
    public OpenCodeWorkspaceMcpOperationOptions Operations { get; init; } = new();
    public OpenCodeWorkspaceMcpArtifactOptions Artifacts { get; init; } = new();
}

public sealed class OpenCodeWorkspaceMcpHttpOptions
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; }
}

public sealed class OpenCodeWorkspaceMcpOperationOptions
{
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan Retention { get; init; } = TimeSpan.FromHours(1);
}

public sealed class OpenCodeWorkspaceMcpArtifactOptions
{
    public long MaxReadBytes { get; init; } = 10 * 1024 * 1024;
}

public sealed class McpToolEnvelope<T>
{
    public string ContractVersion { get; init; } = OpenCodeWorkspaceMcpContract.ContractVersion;
    public string CorrelationId { get; init; } = string.Empty;
    public T Data { get; init; } = default!;
}

public sealed class McpErrorEnvelope
{
    public string ContractVersion { get; init; } = OpenCodeWorkspaceMcpContract.ContractVersion;
    public string CorrelationId { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string FailureClassification { get; init; } = string.Empty;
    public string CleanupFailureClassification { get; init; } = string.Empty;
}

public sealed class WorkspaceTemplateSummaryModel
{
    public string TemplateId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Family { get; init; } = string.Empty;
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
    public bool Provisionable { get; init; }
    public bool SmokeSupported { get; init; }
    public string ResourceClass { get; init; } = string.Empty;
    public IReadOnlyList<string> ExpectedServices { get; init; } = Array.Empty<string>();
}

public sealed class WorkspaceTemplateDetailModel
{
    public WorkspaceTemplateSummaryModel Summary { get; init; } = new();
    public string WorkspaceImage { get; init; } = string.Empty;
    public IReadOnlyList<string> Services { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Skills { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> McpModules { get; init; } = Array.Empty<string>();
    public TemplateManifest Template { get; init; } = new();
    public IReadOnlyList<FeatureManifest> ResolvedFeatures { get; init; } = Array.Empty<FeatureManifest>();
    public IReadOnlyList<CapabilityManifest> ResolvedCapabilities { get; init; } = Array.Empty<CapabilityManifest>();
    public IReadOnlyList<ServiceManifest> ResolvedServices { get; init; } = Array.Empty<ServiceManifest>();
}

public sealed class WorkspaceRecordModel
{
    public string WorkspaceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Template { get; init; } = string.Empty;
    public string WorkspaceRoot { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Readiness { get; init; } = string.Empty;
    public string RuntimeState { get; init; } = string.Empty;
    public IReadOnlyList<string> AvailableServices { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DocumentationPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public WorkspaceSnapshot Snapshot { get; init; } = null!;
}

public sealed class ArtifactListItem
{
    public string RelativePath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset LastModifiedUtc { get; init; }
    public string MimeType { get; init; } = string.Empty;
    public string ResourceUri { get; init; } = string.Empty;
}

public sealed class ArtifactReadModel
{
    public ArtifactListItem Metadata { get; init; } = new();
    public bool IsTextInline { get; init; }
    public string Text { get; init; } = string.Empty;
    public bool TooLarge { get; init; }
    public string ChecksumSha256 { get; init; } = string.Empty;
}

public sealed class ArtifactResourceReadModel
{
    public ArtifactReadModel Artifact { get; init; } = new();
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
}

public sealed class ExcelProcessResultModel
{
    public string OutputPath { get; init; } = string.Empty;
    public string ResourceUri { get; init; } = string.Empty;
    public string OutputChecksumSha256 { get; init; } = string.Empty;
    public string SourceChecksumSha256 { get; init; } = string.Empty;
    public DateTimeOffset ProcessedUtc { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

public enum McpOperationStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

public sealed class McpOperationModel
{
    public string ContractVersion { get; init; } = OpenCodeWorkspaceMcpContract.ContractVersion;
    public string OperationId { get; init; } = string.Empty;
    public string OperationResourceUri { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public McpOperationStatus Status { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset? StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public string CurrentPhase { get; init; } = string.Empty;
    public IReadOnlyList<string> PhaseHistory { get; init; } = Array.Empty<string>();
    public string ProgressMessage { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string SmokeRunId { get; init; } = string.Empty;
    public string SmokeMatrixRunId { get; init; } = string.Empty;
    public string ArtifactDirectory { get; init; } = string.Empty;
    public string FailureClassification { get; init; } = string.Empty;
    public string FailureMessage { get; init; } = string.Empty;
    public string CleanupFailureClassification { get; init; } = string.Empty;
    public string CleanupFailureMessage { get; init; } = string.Empty;
    public bool CancellationRequested { get; init; }
    public JsonElement? Result { get; init; }
}

public sealed class ServerHealthModel
{
    public string ContractVersion { get; init; } = OpenCodeWorkspaceMcpContract.ContractVersion;
    public string Transport { get; init; } = string.Empty;
    public string CatalogRoot { get; init; } = string.Empty;
    public string WorkspaceStateRoot { get; init; } = string.Empty;
    public string SmokeArtifactsRoot { get; init; } = string.Empty;
    public bool HttpEnabled { get; init; }
    public string HttpBinding { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
}
