namespace OpenCode.Workspace.Core.Models;

public sealed class WorkspaceAiRuntimeContext
{
    public WorkspaceAiContextMetadata Metadata { get; init; } = new();
    public WorkspaceAiInspectionPolicy InspectionPolicy { get; init; } = new();
    public WorkspaceAiWorkspaceState Workspace { get; init; } = new();
    public WorkspaceAiRuntimeState Runtime { get; init; } = new();
    public IReadOnlyList<WorkspaceAiApplicationAvailability> Applications { get; init; } = Array.Empty<WorkspaceAiApplicationAvailability>();
    public IReadOnlyList<WorkspaceAiServiceContext> Services { get; init; } = Array.Empty<WorkspaceAiServiceContext>();
    public WorkspaceAiRuntimeResources Resources { get; init; } = new();
    public IReadOnlyList<WorkspaceAiProviderContext> Providers { get; init; } = Array.Empty<WorkspaceAiProviderContext>();
}

public sealed class WorkspaceAiContextMetadata
{
    public string GeneratedBy { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string UserEdits { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
}

public sealed class WorkspaceAiInspectionPolicy
{
    public string Authority { get; init; } = string.Empty;
    public string DefaultBehavior { get; init; } = string.Empty;
    public string ManualOverridePhrase { get; init; } = string.Empty;
    public IReadOnlyList<string> AvoidShellConclusions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ProviderAwareChecks { get; init; } = Array.Empty<string>();
}

public sealed class WorkspaceAiWorkspaceState
{
    public string Name { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
    public string ConfigurationPath { get; init; } = string.Empty;
    public string CurrentBranch { get; init; } = string.Empty;
    public string RuntimeState { get; init; } = string.Empty;
    public string SessionState { get; init; } = string.Empty;
    public bool UpdateRequired { get; init; }
}

public sealed class WorkspaceAiRuntimeState
{
    public string Engine { get; init; } = string.Empty;
    public string TargetPlatform { get; init; } = string.Empty;
    public string HealthStatus { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
}

public sealed class WorkspaceAiApplicationAvailability
{
    public string Name { get; init; } = string.Empty;
    public string ServiceId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
}

public sealed class WorkspaceAiServiceContext
{
    public string ServiceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string PrimaryUrl { get; init; } = string.Empty;
    public string OpenUrl { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public DateTimeOffset LastProbeUtc { get; init; }
    public IReadOnlyList<WorkspaceHealthFact> Highlights { get; init; } = Array.Empty<WorkspaceHealthFact>();
    public IReadOnlyList<WorkspaceHealthFact> Evidence { get; init; } = Array.Empty<WorkspaceHealthFact>();
}

public sealed class WorkspaceAiRuntimeResources
{
    public IReadOnlyList<WorkspacePortAllocationRecord> Ports { get; init; } = Array.Empty<WorkspacePortAllocationRecord>();
    public IReadOnlyList<WorkspaceServiceEndpointRecord> ServiceEndpoints { get; init; } = Array.Empty<WorkspaceServiceEndpointRecord>();
    public IReadOnlyList<WorkspaceRuntimeIdentifierRecord> RuntimeIdentifiers { get; init; } = Array.Empty<WorkspaceRuntimeIdentifierRecord>();
    public IReadOnlyList<WorkspaceResourceConflictRecord> Conflicts { get; init; } = Array.Empty<WorkspaceResourceConflictRecord>();
}

public sealed class WorkspaceAiProviderContext
{
    public string ProviderKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string WorkspaceImpact { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public DateTimeOffset LastProbeUtc { get; init; }
    public IReadOnlyList<WorkspaceHealthFact> Evidence { get; init; } = Array.Empty<WorkspaceHealthFact>();
}
