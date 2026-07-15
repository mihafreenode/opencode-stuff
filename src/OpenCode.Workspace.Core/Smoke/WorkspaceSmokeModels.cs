using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Smoke;

public enum WorkspaceSmokeStatus
{
    Passed,
    Failed,
    Skipped,
}

public enum WorkspaceSmokePhase
{
    Discovery,
    Preflight,
    Creation,
    Provisioning,
    Validation,
    Diagnostics,
    Cleanup,
    Completed,
}

public enum WorkspaceSmokeFailureClassification
{
    None,
    ValidationToolingFailure,
    EnvironmentFailure,
    ProductFailure,
    OracleRuntimeFailure,
    ApexPrerequisiteFailure,
    RuntimeResourceExhaustion,
    WorkspaceCreationFailure,
    ComposeValidationFailure,
    RuntimeStartupFailure,
    SmokeValidationFailure,
    CleanupFailure,
    UnsupportedSmokeTemplate,
    LockAcquisitionFailure,
}

public enum WorkspaceSmokeResourceClass
{
    Lightweight,
    DocumentProcessing,
    Analytics,
    Database,
    OracleExclusive,
}

public enum WorkspaceSmokeTimeoutClass
{
    Short,
    Medium,
    Long,
    Extended,
}

public sealed class WorkspaceSmokeDefinition
{
    public required string TemplateId { get; init; }
    public required string DisplayName { get; init; }
    public required string Family { get; init; }
    public bool Supported { get; init; }
    public string UnsupportedReason { get; init; } = string.Empty;
    public WorkspaceSmokeResourceClass ResourceClass { get; init; }
    public WorkspaceSmokeTimeoutClass TimeoutClass { get; init; }
    public IReadOnlyList<string> ExpectedServices { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ValidatorIds { get; init; } = Array.Empty<string>();
    public TemplateManifest Template { get; init; } = new();
}

public sealed class WorkspaceSmokeCommandResult
{
    public string Command { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
}

public sealed class WorkspaceSmokeValidatorResult
{
    public required string ValidatorId { get; init; }
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public WorkspaceSmokeCommandResult? Command { get; init; }
    public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class WorkspaceSmokeResourceCounts
{
    public int Containers { get; init; }
    public int Networks { get; init; }
    public int Volumes { get; init; }
    public int Projects { get; init; }

    public static WorkspaceSmokeResourceCounts FromInventory(RuntimeResourceInventory inventory)
        => new()
        {
            Containers = inventory.Resources.Count(item => item.Type == RuntimeResourceType.Container),
            Networks = inventory.Resources.Count(item => item.Type == RuntimeResourceType.Network),
            Volumes = inventory.Resources.Count(item => item.Type == RuntimeResourceType.Volume),
            Projects = inventory.Projects.Count,
        };
}

public sealed class WorkspaceSmokeResult
{
    public string TemplateId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = string.Empty;
    public string ComposeProject { get; init; } = string.Empty;
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset FinishedUtc { get; init; }
    public TimeSpan Duration { get; init; }
    public WorkspaceSmokeStatus Status { get; init; }
    public WorkspaceSmokePhase Phase { get; init; }
    public WorkspaceSmokeFailureClassification FailureClassification { get; init; }
    public string FailureMessage { get; init; } = string.Empty;
    public IReadOnlyList<WorkspaceSmokeValidatorResult> Validators { get; init; } = Array.Empty<WorkspaceSmokeValidatorResult>();
    public WorkspaceSmokeResourceCounts ResourceCountsBefore { get; init; } = new();
    public WorkspaceSmokeResourceCounts ResourceCountsActive { get; init; } = new();
    public WorkspaceSmokeResourceCounts ResourceCountsAfter { get; init; } = new();
    public SmokeCleanupResult? CleanupResult { get; init; }
    public bool CleanupVerificationSucceeded { get; init; }
    public string ArtifactDirectory { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class WorkspaceSmokeMatrixResult
{
    public string MatrixRunId { get; init; } = string.Empty;
    public IReadOnlyList<string> SelectedTemplates { get; init; } = Array.Empty<string>();
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset FinishedUtc { get; init; }
    public IReadOnlyList<WorkspaceSmokeResult> Results { get; init; } = Array.Empty<WorkspaceSmokeResult>();
    public int PassedCount { get; init; }
    public int FailedCount { get; init; }
    public int SkippedCount { get; init; }
    public SmokeCleanupResult? FinalHostCleanupResult { get; init; }
    public RuntimeResourceInventory? FinalRuntimeInventory { get; init; }
    public WorkspaceSmokeStatus Status { get; init; }
    public string ArtifactDirectory { get; init; } = string.Empty;
}

public sealed class WorkspaceSmokeRunnerOptions
{
    public string MatrixRunId { get; init; } = string.Empty;
    public string ArtifactsRoot { get; init; } = string.Empty;
    public string? WorkspaceRoot { get; init; }
    public bool DryRun { get; init; }
    public bool OracleLockAlreadyHeld { get; init; }
    public bool KeepWorkspace { get; init; }
    public bool KeepRuntimeOnFailure { get; init; }
}

public sealed class WorkspaceSmokeMatrixRunnerOptions
{
    public string ArtifactsRoot { get; init; } = string.Empty;
    public int ParallelCount { get; init; } = 1;
    public bool KeepWorkspace { get; init; }
    public bool KeepRuntimeOnFailure { get; init; }
}

public sealed class WorkspaceSmokeSingleRunRequest
{
    public string TemplateId { get; init; } = string.Empty;
    public string ArtifactsRoot { get; init; } = string.Empty;
    public string? WorkspaceRoot { get; init; }
    public bool DryRun { get; init; }
    public bool KeepWorkspace { get; init; }
    public bool KeepRuntimeOnFailure { get; init; }
}

public sealed class WorkspaceSmokeMatrixRunRequest
{
    public IReadOnlyList<string> TemplateIds { get; init; } = Array.Empty<string>();
    public string ArtifactsRoot { get; init; } = string.Empty;
    public int ParallelCount { get; init; } = 1;
    public bool KeepWorkspace { get; init; }
    public bool KeepRuntimeOnFailure { get; init; }
}
