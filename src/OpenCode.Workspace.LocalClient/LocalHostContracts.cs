using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.Core.Workspaces;
using System.Text.Json;

namespace OpenCode.Workspace.LocalClient;

public static class LocalHostContract
{
    public const string ContractVersion = "1";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static readonly JsonSerializerOptions CompactJsonOptions = new(JsonSerializerDefaults.Web);
}

public sealed record LocalHostEnvelope<T>
{
    public string ContractVersion { get; init; } = LocalHostContract.ContractVersion;
    public T Data { get; init; } = default!;
}

public sealed record LocalHostErrorEnvelope
{
    public string ContractVersion { get; init; } = LocalHostContract.ContractVersion;
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
}

public sealed record LocalHostHealthResponse
{
    public string ContractVersion { get; init; } = LocalHostContract.ContractVersion;
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string HostInstanceId { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record LocalHostReadinessResponse
{
    public string ContractVersion { get; init; } = LocalHostContract.ContractVersion;
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public RuntimeResourceInventory? RuntimeInventory { get; init; }
}

public enum WorkspaceOperationScope
{
    Host,
    Workspace,
    InteractiveSession,
}

public enum WorkspaceOperationStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Interrupted,
}

public enum WorkspaceOperationCancellationState
{
    None,
    Requested,
    Cancelled,
}

public enum WorkspaceOperationProgressLevel
{
    Debug,
    Information,
    Warning,
    Error,
}

public sealed record WorkspaceOperationProgressEvent
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

public enum ControllerSessionStatus
{
    Connected,
    Disconnected,
    Interrupted,
}

public enum InteractiveAgentSessionStatus
{
    Created,
    Starting,
    Detached,
    Attaching,
    Attached,
    Stopping,
    Stopped,
    Failed,
    Unavailable,
}

public enum InteractiveAttachmentKind
{
    WindowsTerminal,
    WebTerminal,
    MacTerminal,
    LinuxTerminal,
}

public enum InteractiveAttachmentStatus
{
    Pending,
    Starting,
    Active,
    Detaching,
    Detached,
    Expired,
    Failed,
}

public sealed record WorkspaceOperationArtifactReference
{
    public string ArtifactId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
    public string OperationId { get; init; } = string.Empty;
    public string WorkspaceInstanceId { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long? Size { get; init; }
    public string Durability { get; init; } = string.Empty;
    public string SafeLocalReference { get; init; } = string.Empty;
}

public sealed record WorkspaceOperationFailure
{
    public string Classification { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed record WorkspaceOperationRecord
{
    public string ContractVersion { get; init; } = LocalHostContract.ContractVersion;
    public long Version { get; init; }
    public string OperationId { get; init; } = string.Empty;
    public string OperationKind { get; init; } = string.Empty;
    public WorkspaceOperationScope OperationScope { get; init; } = WorkspaceOperationScope.Workspace;
    public string WorkspaceInstanceId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public WorkspaceOperationStatus Status { get; init; }
    public string CurrentPhase { get; init; } = string.Empty;
    public string ProgressMessage { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset? StartedUtc { get; init; }
    public DateTimeOffset LastUpdatedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public OperationInitiator InitiatedBy { get; init; } = new();
    public WorkspaceOperationCancellationState CancellationState { get; init; }
    public IReadOnlyList<string> PhaseHistory { get; init; } = Array.Empty<string>();
    public IReadOnlyList<WorkspaceOperationProgressEvent> RecentEvents { get; init; } = Array.Empty<WorkspaceOperationProgressEvent>();
    public long LastEventSequence { get; init; }
    public bool EventsTruncated { get; init; }
    public WorkspaceOperationFailure? OriginalFailure { get; init; }
    public WorkspaceOperationFailure? CleanupFailure { get; init; }
    public IReadOnlyList<WorkspaceOperationArtifactReference> ArtifactReferences { get; init; } = Array.Empty<WorkspaceOperationArtifactReference>();
    public IReadOnlyList<string> RuntimeResourceReferences { get; init; } = Array.Empty<string>();
    public JsonElement? Result { get; init; }
}

public sealed record OperationInitiator
{
    public string Kind { get; init; } = string.Empty;
    public string ControllerSessionId { get; init; } = string.Empty;
    public string ClientKind { get; init; } = string.Empty;
    public string ClientInstanceId { get; init; } = string.Empty;
}

public sealed record WorkspaceInstanceRecord
{
    public string ContractVersion { get; init; } = LocalHostContract.ContractVersion;
    public long Version { get; init; }
    public string WorkspaceInstanceId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string TemplateId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset LastActivityUtc { get; init; }
    public string InitiatedBy { get; init; } = string.Empty;
    public IReadOnlyList<string> ActiveOperationIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RecentOperationIds { get; init; } = Array.Empty<string>();
    public string RuntimeState { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> RelevantServiceUrls { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<WorkspaceOperationArtifactReference> ArtifactReferences { get; init; } = Array.Empty<WorkspaceOperationArtifactReference>();
    public IReadOnlyList<string> InteractiveAgentSessionIds { get; init; } = Array.Empty<string>();
    public string RecoveryState { get; init; } = string.Empty;
    public WorkspaceRecordModel? Workspace { get; init; }
}

public sealed record WorkspaceTemplateSummaryModel
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

public sealed record WorkspaceTemplateDetailModel
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

public sealed record WorkspaceRecordModel
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

public sealed record ArtifactListItem
{
    public string RelativePath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset LastModifiedUtc { get; init; }
    public string MimeType { get; init; } = string.Empty;
    public string ResourceUri { get; init; } = string.Empty;
}

public sealed record ArtifactReadModel
{
    public ArtifactListItem Metadata { get; init; } = new();
    public bool IsTextInline { get; init; }
    public string Text { get; init; } = string.Empty;
    public bool TooLarge { get; init; }
    public string ChecksumSha256 { get; init; } = string.Empty;
}

public sealed record ArtifactResourceReadModel
{
    public ArtifactReadModel Artifact { get; init; } = new();
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
}

public sealed record ExcelProcessResultModel
{
    public string OutputPath { get; init; } = string.Empty;
    public string ResourceUri { get; init; } = string.Empty;
    public string OutputChecksumSha256 { get; init; } = string.Empty;
    public string SourceChecksumSha256 { get; init; } = string.Empty;
    public DateTimeOffset ProcessedUtc { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

public sealed record ControllerSessionRecord
{
    public string ContractVersion { get; init; } = LocalHostContract.ContractVersion;
    public long Version { get; init; }
    public string ControllerSessionId { get; init; } = string.Empty;
    public string ClientKind { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public string ClientVersion { get; init; } = string.Empty;
    public string ClientInstanceId { get; init; } = string.Empty;
    public DateTimeOffset ConnectedUtc { get; init; }
    public DateTimeOffset LastActivityUtc { get; init; }
    public DateTimeOffset? DisconnectedUtc { get; init; }
    public ControllerSessionStatus Status { get; init; }
    public IReadOnlyList<string> WorkspaceIdsTouched { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> InitiatedOperationIds { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record InteractiveAttachmentLease
{
    public string InteractiveAgentSessionId { get; init; } = string.Empty;
    public string AttachmentId { get; init; } = string.Empty;
    public string HolderKind { get; init; } = string.Empty;
    public string HolderClientInstanceId { get; init; } = string.Empty;
    public DateTimeOffset AcquiredUtc { get; init; }
    public DateTimeOffset LeaseExpiresUtc { get; init; }
    public DateTimeOffset LastHeartbeatUtc { get; init; }
    public long Version { get; init; }
    public int TokenGeneration { get; init; }
}

public enum InteractiveAttachmentControlAction
{
    None,
    Detach,
}

public enum ProviderSessionIdentitySource
{
    None,
    DirectHandshake,
    LaunchCorrelation,
    StructuredProbe,
    ExistingCanonicalIdentity,
}

public sealed record InteractiveAttachmentCapabilities
{
    public bool SupportsExclusiveControl { get; init; } = true;
    public bool SupportsTransfer { get; init; }
}

public sealed record InteractiveSessionAttachmentRecord
{
    public string ContractVersion { get; init; } = LocalHostContract.ContractVersion;
    public long Version { get; init; }
    public string AttachmentId { get; init; } = string.Empty;
    public string InteractiveAgentSessionId { get; init; } = string.Empty;
    public InteractiveAttachmentKind Kind { get; init; } = InteractiveAttachmentKind.WindowsTerminal;
    public InteractiveAttachmentStatus Status { get; init; }
    public string ClientInstanceId { get; init; } = string.Empty;
    public int? ProcessId { get; init; }
    public string WindowIdentity { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset AttachedUtc { get; init; }
    public DateTimeOffset LastActivityUtc { get; init; }
    public DateTimeOffset? LastHeartbeatUtc { get; init; }
    public DateTimeOffset? LeaseExpiresUtc { get; init; }
    public DateTimeOffset? DetachedUtc { get; init; }
    public string DetachReason { get; init; } = string.Empty;
    public WorkspaceOperationFailure? Failure { get; init; }
    public string ProviderSessionId { get; init; } = string.Empty;
    public ProviderSessionIdentitySource ProviderSessionIdentitySource { get; init; }
    public DateTimeOffset? ProviderSessionIdentityVerifiedUtc { get; init; }
    public string LaunchCorrelationId { get; init; } = string.Empty;
    public long LeaseVersion { get; init; }
    public InteractiveAttachmentCapabilities Capabilities { get; init; } = new();
}

public sealed record InteractiveAgentSessionRecord
{
    public string ContractVersion { get; init; } = LocalHostContract.ContractVersion;
    public long Version { get; init; }
    public string InteractiveAgentSessionId { get; init; } = string.Empty;
    public string WorkspaceInstanceId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string Provider { get; init; } = "OpenCode";
    public string? ProviderSessionId { get; init; }
    public ProviderSessionIdentitySource ProviderSessionIdentitySource { get; init; }
    public DateTimeOffset? ProviderSessionIdentityVerifiedUtc { get; init; }
    public string Title { get; init; } = string.Empty;
    public InteractiveAgentSessionStatus Status { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public DateTimeOffset LastActivityUtc { get; init; }
    public string ActiveAttachmentId { get; init; } = string.Empty;
    public InteractiveAttachmentLease? ActiveLease { get; init; }
    public IReadOnlyList<string> AttachmentHistory { get; init; } = Array.Empty<string>();
    public string CreateCommandId { get; init; } = string.Empty;
    public string CreatedByControllerSessionId { get; init; } = string.Empty;
    public string LastUpdatedByControllerSessionId { get; init; } = string.Empty;
    public string LastFailureSummary { get; init; } = string.Empty;
    public string RecoveryEligibleAttachmentId { get; init; } = string.Empty;
    public DateTimeOffset? RecoveryEligibleUntilUtc { get; init; }
    public bool RecoveryBlockedByCleanShutdown { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record ApprovedTerminalLaunchDescriptor
{
    public int DescriptorVersion { get; init; } = 1;
    public string LaunchKind { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string CommandText { get; init; } = string.Empty;
    public string FallbackCommandText { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
}

public sealed record ApprovedProcessLaunchDescriptor
{
    public int DescriptorVersion { get; init; } = 1;
    public string LaunchKind { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string CommandText { get; init; } = string.Empty;
    public string FallbackCommandText { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
}

public sealed record InteractiveSessionAttachResult
{
    public InteractiveAgentSessionRecord Session { get; init; } = new();
    public InteractiveSessionAttachmentRecord Attachment { get; init; } = new();
    public ApprovedTerminalLaunchDescriptor LaunchDescriptor { get; init; } = new();
}

public sealed record InteractiveSessionAttachmentActivationResult
{
    public InteractiveAgentSessionRecord Session { get; init; } = new();
    public InteractiveSessionAttachmentRecord Attachment { get; init; } = new();
    public ApprovedProcessLaunchDescriptor ProcessLaunchDescriptor { get; init; } = new();
    public ApprovedProcessLaunchDescriptor? ProviderSessionProbeDescriptor { get; init; }
    public InteractiveAttachmentControlAction RequestedAction { get; init; }
    public int HeartbeatIntervalSeconds { get; init; }
    public int TokenGeneration { get; init; }
}

public sealed record InteractiveSessionAttachmentHeartbeatResult
{
    public InteractiveAgentSessionRecord Session { get; init; } = new();
    public InteractiveSessionAttachmentRecord Attachment { get; init; } = new();
    public InteractiveAttachmentControlAction RequestedAction { get; init; }
    public int HeartbeatIntervalSeconds { get; init; }
    public int TokenGeneration { get; init; }
}

public sealed record InteractiveSessionAttachmentRecoveryResult
{
    public InteractiveAgentSessionRecord Session { get; init; } = new();
    public InteractiveSessionAttachmentRecord Attachment { get; init; } = new();
    public string AttachmentToken { get; init; } = string.Empty;
    public InteractiveAttachmentControlAction RequestedAction { get; init; }
    public int HeartbeatIntervalSeconds { get; init; }
    public int TokenGeneration { get; init; }
}

public sealed record WorkspaceEventEnvelope
{
    public string ContractVersion { get; init; } = LocalHostContract.ContractVersion;
    public string HostInstanceId { get; init; } = string.Empty;
    public long Sequence { get; init; }
    public string EventId { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
    public string EventKind { get; init; } = string.Empty;
    public string WorkspaceInstanceId { get; init; } = string.Empty;
    public string OperationId { get; init; } = string.Empty;
    public string ControllerSessionId { get; init; } = string.Empty;
    public string InteractiveAgentSessionId { get; init; } = string.Empty;
    public string AttachmentId { get; init; } = string.Empty;
    public JsonElement? Payload { get; init; }
}

public sealed record LocalHostDescriptor
{
    public string ContractVersion { get; init; } = LocalHostContract.ContractVersion;
    public string InstanceId { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string BaseUrl { get; init; } = string.Empty;
    public DateTimeOffset StartedUtc { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
}

public enum LocalHostOwnership
{
    External,
    OwnedByDesktop,
}

public sealed record ControllerSessionUpsertRequest
{
    public string ControllerSessionId { get; init; } = string.Empty;
    public string ClientKind { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public string ClientVersion { get; init; } = string.Empty;
    public string ClientInstanceId { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record OperationCommandRequest
{
    public string CommandId { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record AttachInteractiveSessionRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string CommandId { get; init; } = string.Empty;
    public string ClientInstanceId { get; init; } = string.Empty;
    public string? ControllerSessionId { get; init; }
    public InteractiveAttachmentKind AttachmentKind { get; init; } = InteractiveAttachmentKind.WindowsTerminal;
    public bool RequestTransfer { get; init; }
    public string ExpectedAttachmentId { get; init; } = string.Empty;
}

public sealed record InteractiveSessionAttachmentHeartbeatRequest
{
    public string AttachmentToken { get; init; } = string.Empty;
}

public sealed record ActivateInteractiveSessionAttachmentRequest
{
    public string AttachmentToken { get; init; } = string.Empty;
    public int HelperProcessId { get; init; }
}

public sealed record InteractiveSessionAttachmentProcessStartedRequest
{
    public string AttachmentToken { get; init; } = string.Empty;
    public int ChildProcessId { get; init; }
}

public sealed record InteractiveSessionAttachmentProviderSessionRequest
{
    public string AttachmentToken { get; init; } = string.Empty;
    public string ProviderSessionId { get; init; } = string.Empty;
    public ProviderSessionIdentitySource IdentitySource { get; init; }
}

public sealed record InteractiveSessionAttachmentProcessExitRequest
{
    public string AttachmentToken { get; init; } = string.Empty;
    public int? ChildProcessId { get; init; }
    public int? ExitCode { get; init; }
    public string Outcome { get; init; } = string.Empty;
    public string FailureMessage { get; init; } = string.Empty;
}

public sealed record InteractiveSessionAttachmentLaunchFailureRequest
{
    public string ClientInstanceId { get; init; } = string.Empty;
    public string FailureMessage { get; init; } = string.Empty;
}

public sealed record RecoverInteractiveSessionAttachmentRequest
{
    public string AttachmentRecoveryId { get; init; } = string.Empty;
    public string RecoverySecret { get; init; } = string.Empty;
    public int HelperProcessId { get; init; }
    public DateTimeOffset HelperStartedUtc { get; init; }
    public int? ChildProcessId { get; init; }
    public DateTimeOffset? ChildStartedUtc { get; init; }
    public string ProviderSessionId { get; init; } = string.Empty;
}

public sealed record DetachInteractiveSessionAttachmentRequest
{
    public string ClientInstanceId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed record CreateInteractiveAgentSessionRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string RequestedByControllerSessionId { get; init; } = string.Empty;
}

public sealed record WorkspaceProvisionRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record WorkspaceLifecycleRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record WorkspaceBackupRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string DestinationPath { get; init; } = string.Empty;
    public bool OverwriteExisting { get; init; }
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record WorkspacePublishAssessmentRecord
{
    public string WorkspaceId { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string CurrentBranch { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string ConfirmationMessage { get; init; } = string.Empty;
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool CanPublish { get; init; }
    public bool IsBlocked { get; init; }
    public bool RequiresConfirmation { get; init; }
    public bool RequiresSavePoint { get; init; }
    public bool HasRemoteConfigured { get; init; }
    public string RemoteName { get; init; } = string.Empty;
    public string RemoteBranch { get; init; } = string.Empty;
    public int AheadCount { get; init; }
    public int BehindCount { get; init; }
}

public sealed record WorkspacePublishRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record WorkspaceRemovalRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public bool RemoveOwnedRuntimeResources { get; init; }
    public bool DeleteWorkspaceFiles { get; init; }
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record WorkspaceRemovalResultRecord
{
    public string WorkspaceId { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string WorkspaceRoot { get; init; } = string.Empty;
    public bool RegistrationRemoved { get; init; }
    public bool RuntimeResourcesRemoved { get; init; }
    public bool WorkspaceFilesDeleted { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool Succeeded { get; init; }
    public string FailureReason { get; init; } = string.Empty;
}

public sealed record WorkspaceRecoveryAssessmentRecord
{
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();
    public string ConfirmationMessage { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string StatusSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> RecoverActions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CurrentProblems { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PreviousFailureContext { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> WillNotChange { get; init; } = Array.Empty<string>();
    public string ManualActionSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> ManualActions { get; init; } = Array.Empty<string>();
    public string AdvancedDetails { get; init; } = string.Empty;
    public DateTimeOffset? LastCheckedAt { get; init; }
}

public sealed record WorkspaceSynchronizationValidationRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = string.Empty;
    public string DeploymentProfileOverride { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record WorkspaceSynchronizationDiffRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record WorkspaceSynchronizationExportRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record WorkspaceSynchronizationImportRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = string.Empty;
    public string DeploymentProfileOverride { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record WorkspaceSynchronizeRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = string.Empty;
    public string DeploymentProfileOverride { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public enum WorkspaceSynchronizationExecutionAction
{
    Validate,
    ShowDiff,
    PullChanges,
    PushChanges,
}

public sealed record WorkspaceSynchronizationExecutionResult
{
    public WorkspaceSynchronizationState PreviousState { get; init; }
    public WorkspaceSynchronizationExecutionAction ActionPerformed { get; init; }
    public WorkspaceSynchronizationOperationResult? OperationResult { get; init; }
    public WorkspaceSynchronizationDiffResult? DiffResult { get; init; }
}

public sealed record OracleAssistantValidationRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string ExecutionId { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = string.Empty;
    public string DeploymentProfileOverride { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record OracleAssistantImportRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string ExecutionId { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = string.Empty;
    public string DeploymentProfileOverride { get; init; } = string.Empty;
    public bool AllowNonDevelopmentDeployment { get; init; }
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record OracleAssistantSynchronizationOperationRecord
{
    public string ExecutionId { get; init; } = string.Empty;
    public required WorkspaceSynchronizationOperationResult Response { get; init; }
}

public sealed record OracleAssistantPlanRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string Intent { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record OracleAssistantPlanOperationRecord
{
    public string PlanId { get; init; } = string.Empty;
    public string ContextRevision { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public required OracleApexAssistantPlanResponse Response { get; init; }
}

public sealed record OracleAssistantApplyRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string ContextRevision { get; init; } = string.Empty;
    public bool ConfirmPlan { get; init; }
    public OracleApexAssistantPostEditBehavior PostEditBehavior { get; init; }
    public string EnvironmentName { get; init; } = string.Empty;
    public bool EnableSafeAutomaticRepair { get; init; }
    public bool AllowNonDevelopmentDeployment { get; init; }
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record OracleAssistantApplyOperationRecord
{
    public string PlanId { get; init; } = string.Empty;
    public string ContextRevision { get; init; } = string.Empty;
    public string ExecutionId { get; init; } = string.Empty;
    public required OracleApexAssistantExecutionResponse Response { get; init; }
}

public sealed record OracleAssistantRepairPlanRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string ExecutionId { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record OracleAssistantRepairPlanOperationRecord
{
    public string RepairPlanId { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string ExecutionId { get; init; } = string.Empty;
    public string ContextRevision { get; init; } = string.Empty;
    public required OracleApexAssistantRepairPlanResponse Response { get; init; }
}

public sealed record OracleAssistantRepairExecutionRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string ExecutionId { get; init; } = string.Empty;
    public string RepairPlanId { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record OracleAssistantRepairOperationRecord
{
    public string RepairPlanId { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string ExecutionId { get; init; } = string.Empty;
    public string ContextRevision { get; init; } = string.Empty;
    public required OracleApexAssistantExecutionResponse Response { get; init; }
}

public sealed record OracleAssistantRollbackRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string ExecutionId { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record OracleAssistantRollbackOperationRecord
{
    public string ExecutionId { get; init; } = string.Empty;
    public required OracleApexAssistantRollbackResponse Response { get; init; }
}

public sealed record OracleApexApplicationDiscoveryQuery
{
    public string WorkspaceId { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = "dev";
    public string WorkspaceName { get; init; } = "TEST";
    public string ParsingSchema { get; init; } = "TESTSCHEMA";
    public string SqlclProfile { get; init; } = "local-apex-dev";
    public string SourcePath { get; init; } = "src/apex";
}

public sealed record ConnectExistingOracleApexApplicationRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = "dev";
    public string WorkspaceName { get; init; } = "TEST";
    public string ParsingSchema { get; init; } = "TESTSCHEMA";
    public string SqlclProfile { get; init; } = "local-apex-dev";
    public string SourcePath { get; init; } = "src/apex";
    public int ApplicationId { get; init; }
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record ConnectExistingOracleApexApplicationOperationRecord
{
    public string EnvironmentName { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string ParsingSchema { get; init; } = string.Empty;
    public string SqlclProfile { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public int ApplicationId { get; init; }
    public string ApplicationName { get; init; } = string.Empty;
    public string Alias { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<ProcessResult> ProcessResults { get; init; } = Array.Empty<ProcessResult>();
}

public sealed record WorkspaceCreateRequest
{
    public string TemplateId { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string WorkspaceRootPath { get; init; } = string.Empty;
}

public sealed record ExistingGitCheckoutInspectionRequest
{
    public string RepositoryPath { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
}

public sealed record ExistingGitCheckoutBranchValidationRequest
{
    public string RepositoryPath { get; init; } = string.Empty;
    public string BranchName { get; init; } = string.Empty;
}

public sealed record SavePointMessageSuggestionRequest
{
    public string WorkspaceId { get; init; } = string.Empty;
}

public sealed record WorkspaceSavePointCreateRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record WorkspaceCheckpointCreateRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record SmokeRunOperationRequest
{
    public string CommandId { get; init; } = string.Empty;
    public string TemplateId { get; init; } = string.Empty;
    public string? Timeout { get; init; }
    public string? ArtifactsRoot { get; init; }
    public OperationInitiator RequestedBy { get; init; } = new();
}

public sealed record SmokeMatrixOperationRequest
{
    public string CommandId { get; init; } = string.Empty;
    public IReadOnlyList<string> TemplateIds { get; init; } = Array.Empty<string>();
    public string? Family { get; init; }
    public bool All { get; init; }
    public string? Timeout { get; init; }
    public OperationInitiator RequestedBy { get; init; } = new();
}
