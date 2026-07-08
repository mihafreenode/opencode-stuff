using YamlDotNet.Serialization;

namespace OpenCode.Workspace.Core.Models;

public static class WorkspaceSynchronizationModes
{
    public const string Manual = "manual";
    public const string WatchSafe = "watch-safe";
    public const string WatchLive = "watch-live";

    public static string Normalize(string? mode)
        => mode?.Trim().ToLowerInvariant() switch
        {
            WatchSafe => WatchSafe,
            WatchLive => WatchLive,
            _ => Manual,
        };
}

public enum WorkspaceSynchronizationState
{
    Unknown,
    InSync,
    GitAhead,
    DeploymentAhead,
    Diverged,
    ValidationFailed,
}

public enum WorkspaceSynchronizationDirection
{
    Validate,
    Export,
    Import,
    Pull,
    Push,
    Synchronize,
}

public sealed class WorkspaceSynchronizationSnapshot
{
    public WorkspaceSynchronizationState State { get; init; } = WorkspaceSynchronizationState.Unknown;
    public string Summary { get; init; } = string.Empty;
    public bool IsSupported { get; init; }
    public bool RequiresExplicitDecision { get; init; }
    public bool HasDrift { get; init; }
    public IReadOnlyList<WorkspaceSynchronizationEnvironmentSnapshot> Environments { get; init; } = Array.Empty<WorkspaceSynchronizationEnvironmentSnapshot>();
    public WorkspaceSynchronizationEnvironmentSnapshot? DefaultEnvironment { get; init; }
}

public sealed class WorkspaceSynchronizationEnvironmentSnapshot
{
    public string EnvironmentName { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string ParsingSchema { get; init; } = string.Empty;
    public int? ApplicationId { get; init; }
    public string SqlclProfile { get; init; } = string.Empty;
    public string SyncMode { get; init; } = WorkspaceSynchronizationModes.Manual;
    public string SourcePath { get; init; } = string.Empty;
    public WorkspaceSynchronizationState State { get; init; } = WorkspaceSynchronizationState.Unknown;
    public string Summary { get; init; } = string.Empty;
    public string DriftSummary { get; init; } = string.Empty;
    public DateTimeOffset? LastValidationUtc { get; init; }
    public DateTimeOffset? LastImportUtc { get; init; }
    public DateTimeOffset? LastExportUtc { get; init; }
    public string LastSynchronizedGitRevision { get; init; } = string.Empty;
    public string ImportedRevision { get; init; } = string.Empty;
    public string ExportedRevision { get; init; } = string.Empty;
}

public sealed class WorkspaceSynchronizationStatusResult
{
    public required WorkspaceSynchronizationSnapshot Snapshot { get; init; }
}

public sealed class WorkspaceSynchronizationOperationResult
{
    public required WorkspaceSynchronizationSnapshot Snapshot { get; init; }
    public required string Message { get; init; }
    public ProcessResult? ProcessResult { get; init; }
}

public sealed class WorkspaceSynchronizationDiffResult
{
    public required WorkspaceSynchronizationSnapshot Snapshot { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string DiffText { get; init; } = string.Empty;
}

public sealed class WorkspaceSynchronizationStateDocument
{
    [YamlMember(Alias = "defaultEnvironment")]
    public string? DefaultEnvironment { get; init; }

    [YamlMember(Alias = "environments")]
    public Dictionary<string, WorkspaceSynchronizationEnvironmentState> Environments { get; init; } = new();
}

public sealed class WorkspaceSynchronizationEnvironmentState
{
    [YamlMember(Alias = "synchronizationState")]
    public string SynchronizationState { get; init; } = nameof(WorkspaceSynchronizationState.Unknown);

    [YamlMember(Alias = "driftSummary")]
    public string DriftSummary { get; init; } = string.Empty;

    [YamlMember(Alias = "lastValidation")]
    public WorkspaceSynchronizationOperationState? LastValidation { get; init; }

    [YamlMember(Alias = "lastImport")]
    public WorkspaceSynchronizationOperationState? LastImport { get; init; }

    [YamlMember(Alias = "lastExport")]
    public WorkspaceSynchronizationOperationState? LastExport { get; init; }

    [YamlMember(Alias = "importedRevision")]
    public string ImportedRevision { get; init; } = string.Empty;

    [YamlMember(Alias = "exportedRevision")]
    public string ExportedRevision { get; init; } = string.Empty;

    [YamlMember(Alias = "lastSynchronizedGitRevision")]
    public string LastSynchronizedGitRevision { get; init; } = string.Empty;
}

public sealed class WorkspaceSynchronizationOperationState
{
    [YamlMember(Alias = "status")]
    public string Status { get; init; } = string.Empty;

    [YamlMember(Alias = "revision")]
    public string Revision { get; init; } = string.Empty;

    [YamlMember(Alias = "timestampUtc")]
    public DateTimeOffset? TimestampUtc { get; init; }

    [YamlMember(Alias = "summary")]
    public string Summary { get; init; } = string.Empty;
}

public sealed class WorkspaceSynchronizationRequest
{
    public required WorkspaceSnapshot Snapshot { get; init; }
    public string? EnvironmentName { get; init; }
}

public interface IWorkspaceSynchronizationProvider
{
    string ProviderId { get; }

    bool CanHandle(WorkspaceDefinition definition);

    Task<WorkspaceSynchronizationStatusResult> GetStatusAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default);

    Task<WorkspaceSynchronizationOperationResult> ValidateAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default);

    Task<WorkspaceSynchronizationOperationResult> ExportAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default);

    Task<WorkspaceSynchronizationOperationResult> ImportAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default);

    Task<WorkspaceSynchronizationDiffResult> DiffAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default);

    Task<WorkspaceSynchronizationOperationResult> PullAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default);

    Task<WorkspaceSynchronizationOperationResult> PushAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default);
}
