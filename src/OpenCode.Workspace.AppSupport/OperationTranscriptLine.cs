namespace OpenCode.Workspace.AppSupport;

public sealed class OperationTranscriptLine
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public OperationTranscriptLineKind Kind { get; init; }
    public string Text { get; init; } = string.Empty;
}
