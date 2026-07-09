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
    public string ApplicationName { get; init; } = string.Empty;
    public string SqlclProfile { get; init; } = string.Empty;
    public string SyncMode { get; init; } = WorkspaceSynchronizationModes.Manual;
    public string SourcePath { get; init; } = string.Empty;
    public WorkspaceSynchronizationState State { get; init; } = WorkspaceSynchronizationState.Unknown;
    public string Summary { get; init; } = string.Empty;
    public string DriftSummary { get; init; } = string.Empty;
    public DateTimeOffset? LastValidationUtc { get; init; }
    public DateTimeOffset? LastImportUtc { get; init; }
    public DateTimeOffset? LastExportUtc { get; init; }
    public DateTimeOffset? LastPullUtc { get; init; }
    public DateTimeOffset? LastPushUtc { get; init; }
    public string LastSynchronizedGitRevision { get; init; } = string.Empty;
    public string ImportedRevision { get; init; } = string.Empty;
    public string ExportedRevision { get; init; } = string.Empty;
    public string LastImportedRevision { get; init; } = string.Empty;
    public string LastExportedRevision { get; init; } = string.Empty;
    public string LastPushResult { get; init; } = string.Empty;
    public string SynchronizedSourceSignature { get; init; } = string.Empty;
    public string WorkspaceSourceSignature { get; init; } = string.Empty;
    public string RemoteSourceSignature { get; init; } = string.Empty;
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

public sealed class OracleApexApplicationInfo
{
    public int ApplicationId { get; init; }
    public string ApplicationName { get; init; } = string.Empty;
    public string Alias { get; init; } = string.Empty;
}

public sealed class OracleApexApplicationDiscoveryRequest
{
    public required WorkspaceSnapshot Snapshot { get; init; }
    public string EnvironmentName { get; init; } = "dev";
    public string WorkspaceName { get; init; } = "TEST";
    public string ParsingSchema { get; init; } = "TESTSCHEMA";
    public string SqlclProfile { get; init; } = "local-apex-dev";
    public string SourcePath { get; init; } = "src/apex";
}

public sealed class OracleApexApplicationDiscoveryResult
{
    public string EnvironmentName { get; init; } = "dev";
    public string WorkspaceName { get; init; } = string.Empty;
    public string ParsingSchema { get; init; } = string.Empty;
    public string SqlclProfile { get; init; } = string.Empty;
    public string SourcePath { get; init; } = "src/apex";
    public IReadOnlyList<OracleApexApplicationInfo> Applications { get; init; } = Array.Empty<OracleApexApplicationInfo>();
    public string Summary { get; init; } = string.Empty;
}

public sealed class OracleApexConnectExistingApplicationRequest
{
    public required WorkspaceSnapshot Snapshot { get; init; }
    public string EnvironmentName { get; init; } = "dev";
    public string WorkspaceName { get; init; } = string.Empty;
    public string ParsingSchema { get; init; } = string.Empty;
    public int ApplicationId { get; init; }
    public string ApplicationName { get; init; } = string.Empty;
    public string Alias { get; init; } = string.Empty;
    public string SqlclProfile { get; init; } = string.Empty;
    public string SourcePath { get; init; } = "src/apex";
}

public sealed class OracleApexConnectExistingApplicationResult
{
    public required WorkspaceSnapshot Snapshot { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<ProcessResult> ProcessResults { get; init; } = Array.Empty<ProcessResult>();
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

    [YamlMember(Alias = "lastPull")]
    public WorkspaceSynchronizationOperationState? LastPull { get; init; }

    [YamlMember(Alias = "lastPush")]
    public WorkspaceSynchronizationOperationState? LastPush { get; init; }

    [YamlMember(Alias = "importedRevision")]
    public string ImportedRevision { get; init; } = string.Empty;

    [YamlMember(Alias = "exportedRevision")]
    public string ExportedRevision { get; init; } = string.Empty;

    [YamlMember(Alias = "lastSynchronizedGitRevision")]
    public string LastSynchronizedGitRevision { get; init; } = string.Empty;

    [YamlMember(Alias = "applicationName")]
    public string ApplicationName { get; init; } = string.Empty;

    [YamlMember(Alias = "lastPushResult")]
    public string LastPushResult { get; init; } = string.Empty;

    [YamlMember(Alias = "lastImportedRevision")]
    public string LastImportedRevision { get; init; } = string.Empty;

    [YamlMember(Alias = "lastExportedRevision")]
    public string LastExportedRevision { get; init; } = string.Empty;

    [YamlMember(Alias = "synchronizedSourceSignature")]
    public string SynchronizedSourceSignature { get; init; } = string.Empty;

    [YamlMember(Alias = "workspaceSourceSignature")]
    public string WorkspaceSourceSignature { get; init; } = string.Empty;

    [YamlMember(Alias = "remoteSourceSignature")]
    public string RemoteSourceSignature { get; init; } = string.Empty;

    [YamlMember(Alias = "operationHistory")]
    public List<WorkspaceSynchronizationHistoryEntry> OperationHistory { get; init; } = [];
}

public sealed class WorkspaceSynchronizationHistoryEntry
{
    [YamlMember(Alias = "operation")]
    public string Operation { get; init; } = string.Empty;

    [YamlMember(Alias = "result")]
    public string Result { get; init; } = string.Empty;

    [YamlMember(Alias = "state")]
    public string State { get; init; } = string.Empty;

    [YamlMember(Alias = "revision")]
    public string Revision { get; init; } = string.Empty;

    [YamlMember(Alias = "contentRevision")]
    public string ContentRevision { get; init; } = string.Empty;

    [YamlMember(Alias = "timestampUtc")]
    public DateTimeOffset? TimestampUtc { get; init; }

    [YamlMember(Alias = "summary")]
    public string Summary { get; init; } = string.Empty;
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

public interface IOracleApexWorkspaceConnectionProvider
{
    bool CanHandle(WorkspaceDefinition definition);

    Task<OracleApexApplicationDiscoveryResult> DiscoverApplicationsAsync(OracleApexApplicationDiscoveryRequest request, CancellationToken cancellationToken = default);

    Task<OracleApexConnectExistingApplicationResult> ConnectExistingApplicationAsync(OracleApexConnectExistingApplicationRequest request, CancellationToken cancellationToken = default);
}
