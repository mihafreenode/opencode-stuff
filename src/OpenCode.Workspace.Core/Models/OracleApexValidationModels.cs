namespace OpenCode.Workspace.Core.Models;

public sealed class OracleApexCompilerDiagnostic
{
    public string FilePath { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }
    public string Component { get; init; } = string.Empty;
    public string Property { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string CompilerCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
}

public sealed class OracleApexDiagnosticMapping
{
    public required OracleApexCompilerDiagnostic Diagnostic { get; init; }
    public string SemanticNodeId { get; init; } = string.Empty;
    public string WorkspaceIdentifier { get; init; } = string.Empty;
    public string WorkspaceSemanticType { get; init; } = string.Empty;
    public int PlannedOperationSequence { get; init; }
    public string PlannedOperationTitle { get; init; } = string.Empty;
    public string BlueprintModule { get; init; } = string.Empty;
    public string BlueprintEntity { get; init; } = string.Empty;
}

public sealed class OracleApexValidationResult
{
    public bool IsSuccess { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<OracleApexCompilerDiagnostic> Diagnostics { get; init; } = Array.Empty<OracleApexCompilerDiagnostic>();
    public IReadOnlyList<OracleApexDiagnosticMapping> Mappings { get; init; } = Array.Empty<OracleApexDiagnosticMapping>();
}

public sealed class OracleApexAssistantWorkspaceEvidenceState
{
    public Dictionary<string, OracleApexComponentValidationEvidence> ValidationByComponentType { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> MissingProperties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> FailedBlueprintOperations { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> AppliedRepairActions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<OracleApexAssistantEvidenceEntry> Entries { get; init; } = [];
}

public sealed class OracleApexComponentValidationEvidence
{
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
}

public sealed class OracleApexAssistantWorkspaceSettings
{
    public bool SafeAutomaticRepairEnabled { get; init; }
}

public sealed class OracleApexAssistantEvidenceEntry
{
    public string ExecutionId { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
    public string ValidationResult { get; init; } = string.Empty;
    public string RepairResult { get; init; } = string.Empty;
    public string ImportResult { get; init; } = string.Empty;
    public string RollbackAvailability { get; init; } = string.Empty;
    public string RollbackResult { get; init; } = string.Empty;
    public IReadOnlyList<string> AffectedFiles { get; init; } = Array.Empty<string>();
}

public sealed class OracleApexAssistantRollbackManifest
{
    public string ExecutionId { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
    public string EnvironmentName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public OracleApexAssistantRollbackState RollbackState { get; init; } = OracleApexAssistantRollbackState.NotAvailable;
    public string RollbackBlockedReason { get; init; } = string.Empty;
    public string RollbackResult { get; init; } = string.Empty;
    public IReadOnlyList<OracleApexAssistantRollbackFile> Files { get; init; } = Array.Empty<OracleApexAssistantRollbackFile>();
}

public sealed class OracleApexAssistantRollbackFile
{
    public string RelativePath { get; init; } = string.Empty;
    public string OriginalHash { get; init; } = string.Empty;
    public string PostExecutionHash { get; init; } = string.Empty;
    public string BackupRelativePath { get; init; } = string.Empty;
    public bool ExistedBeforeExecution { get; init; }
}

public enum OracleApexAssistantRollbackState
{
    NotAvailable,
    Available,
    ConfirmationRequired,
    Running,
    Completed,
    Blocked,
    Failed,
}
