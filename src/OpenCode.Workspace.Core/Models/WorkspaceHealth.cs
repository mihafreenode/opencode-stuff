namespace OpenCode.Workspace.Core.Models;

public enum WorkspaceHealthStatus
{
    Healthy,
    Attention,
    Degraded,
    Unavailable,
    Provisioning,
    Investigating,
}

public sealed class WorkspaceHealthFact
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public sealed class WorkspaceProviderHealthSnapshot
{
    public string ProviderKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public WorkspaceHealthStatus Status { get; init; } = WorkspaceHealthStatus.Healthy;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<WorkspaceHealthFact> Evidence { get; init; } = Array.Empty<WorkspaceHealthFact>();
    public string Confidence { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public TimeSpan RefreshInterval { get; init; }
    public string Repairability { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
    public bool IsVolatile { get; init; }
    public string WorkspaceImpact { get; init; } = string.Empty;
}

public sealed class WorkspaceHealthSnapshot
{
    public WorkspaceHealthStatus OverallStatus { get; init; } = WorkspaceHealthStatus.Healthy;
    public string Summary { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public IReadOnlyList<WorkspaceProviderHealthSnapshot> Providers { get; init; } = Array.Empty<WorkspaceProviderHealthSnapshot>();
    public IReadOnlyList<WorkspaceServiceHealthSnapshot> Services { get; init; } = Array.Empty<WorkspaceServiceHealthSnapshot>();
}

public enum WorkspaceServiceProbeType
{
    Tcp,
    Http,
    Https,
    Database,
    Custom,
}

public sealed class WorkspaceServiceHealthSnapshot
{
    public string ServiceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public WorkspaceServiceProbeType ProbeType { get; init; } = WorkspaceServiceProbeType.Custom;
    public WorkspaceHealthStatus Status { get; init; } = WorkspaceHealthStatus.Attention;
    public TimeSpan? Latency { get; init; }
    public IReadOnlyList<WorkspaceHealthFact> Evidence { get; init; } = Array.Empty<WorkspaceHealthFact>();
    public string Confidence { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public string Recommendation { get; init; } = string.Empty;
    public string OpenUrl { get; init; } = string.Empty;
    public TimeSpan RefreshInterval { get; init; }
    public string ProviderKey { get; init; } = string.Empty;
}
