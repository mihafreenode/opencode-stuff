namespace OpenCode.Workspace.Core.Models;

public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>
/// Diagnostics keep the technical probe result separate from the localized user
/// explanation. That split lets the UI stay friendly without losing actionable
/// details for contributors and troubleshooting docs.
/// </summary>
public sealed class DiagnosticResult
{
    public required string Code { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required DiagnosticSeverity Severity { get; init; }
    public bool IsSuccess { get; init; }
    public string? TechnicalDetails { get; init; }
}

public sealed class ProcessResult
{
    public required string Command { get; init; }
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required IReadOnlyList<string> StandardOutputLines { get; init; }
    public required IReadOnlyList<string> StandardErrorLines { get; init; }
    public required TimeSpan Duration { get; init; }
    public bool IsSuccess => ExitCode == 0;
}

public sealed class CommandLogEntry
{
    public required string Source { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class WorkspaceCommandResult
{
    public required string Description { get; init; }
    public required ProcessResult Result { get; init; }
}
