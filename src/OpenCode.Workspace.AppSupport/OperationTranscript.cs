namespace OpenCode.Workspace.AppSupport;

public sealed class OperationTranscript
{
    public string OperationName { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedUtc { get; set; }
    public bool? Succeeded { get; set; }
    public List<OperationTranscriptLine> Lines { get; } = [];
}
