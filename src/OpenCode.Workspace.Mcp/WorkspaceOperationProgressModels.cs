using System.Text.Json.Serialization;

namespace OpenCode.Workspace.Mcp;

public enum WorkspaceOperationProgressLevel
{
    Debug,
    Information,
    Warning,
    Error,
}

public sealed class WorkspaceOperationProgressEvent
{
    public long Sequence { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public WorkspaceOperationProgressLevel Level { get; init; } = WorkspaceOperationProgressLevel.Information;
    public string Phase { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public double? Percent { get; init; }
    public int? CurrentStep { get; init; }
    public int? TotalSteps { get; init; }
    public string Source { get; init; } = string.Empty;
    public string ArtifactReference { get; init; } = string.Empty;
}

internal sealed class WorkspaceOperationProgressEnvelope
{
    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; }

    [JsonPropertyName("level")]
    public WorkspaceOperationProgressLevel Level { get; init; }

    [JsonPropertyName("phase")]
    public string Phase { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("percent")]
    public double? Percent { get; init; }

    [JsonPropertyName("currentStep")]
    public int? CurrentStep { get; init; }

    [JsonPropertyName("totalSteps")]
    public int? TotalSteps { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("artifactReference")]
    public string ArtifactReference { get; init; } = string.Empty;
}
