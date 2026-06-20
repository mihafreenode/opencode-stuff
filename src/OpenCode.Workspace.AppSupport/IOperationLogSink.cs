namespace OpenCode.Workspace.AppSupport;

public interface IOperationLogSink
{
    void Append(OperationTranscriptLine line);
}
