namespace OpenCode.Workspace.Core.Models;

public enum WorkspaceConfigurationStatus
{
    NotFound,
    Found,
    Invalid,
}

public enum WorkspaceRuntimeStateReadStatus
{
    Missing,
    Loaded,
    Corrupted,
}

public sealed class WorkspaceRuntimeStateReadResult
{
    public WorkspaceRuntimeStateReadStatus Status { get; init; }
    public WorkspaceRuntimeStateRecord? State { get; init; }
}

public sealed class WorkspaceDoctorResult
{
    public required string WorkspaceRootPath { get; init; }
    public required string RuntimeStatePath { get; init; }
    public HostPlatformInfo? HostPlatform { get; init; }
    public WorkspaceConfigurationStatus WorkspaceConfigurationStatus { get; init; }
    public string? WorkspaceConfigurationPath { get; init; }
    public string? WorkspaceConfigurationError { get; init; }
    public WorkspaceRuntimeStateReadStatus RuntimeStateStatus { get; init; }
    public WorkspaceRuntimeStateRecord? RuntimeState { get; init; }
    public ResolvedRuntimePlan? ResolvedRuntimePlan { get; init; }
    public bool CanRun { get; init; }
    public required string Recommendation { get; init; }
}

public sealed class PlatformValidationRequest
{
    public required string WorkspacePath { get; init; }
    public required string TargetPlatform { get; init; }
}

public sealed class PlatformValidationCheckResult
{
    public required string Name { get; init; }
    public required DiagnosticSeverity Severity { get; init; }
    public required string Message { get; init; }
}

public sealed class PlatformValidationReport
{
    public required string WorkspaceRootPath { get; init; }
    public required string TargetPlatform { get; init; }
    public string? WorkspaceConfigurationPath { get; init; }
    public ResolvedRuntimePlan? ResolvedRuntimePlan { get; init; }
    public string? ResolvedPlatform { get; init; }
    public string? CompatibilityDisplay { get; init; }
    public bool ValidatedWithFallback { get; init; }
    public IReadOnlyList<PlatformValidationCheckResult> Checks { get; init; } = Array.Empty<PlatformValidationCheckResult>();
    public bool IsSuccess { get; init; }
    public bool HasWarnings { get; init; }
    public required string Summary { get; init; }
}
