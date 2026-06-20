using OpenCode.Workspace.AppSupport;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class OperationTranscriptLineViewModel
{
    public OperationTranscriptLineViewModel(OperationTranscriptLine line)
    {
        Line = line;
    }

    public OperationTranscriptLine Line { get; }
    public string Text => $"[{Line.Timestamp:HH:mm:ss}] {KindLabel(Line.Kind)} {Line.Text}";

    private static string KindLabel(OperationTranscriptLineKind kind)
        => kind switch
        {
            OperationTranscriptLineKind.Command => "cmd ",
            OperationTranscriptLineKind.StandardOutput => "out ",
            OperationTranscriptLineKind.StandardError => "err ",
            OperationTranscriptLineKind.Status => "stat",
            OperationTranscriptLineKind.Result => "res ",
            _ => "info",
        };
}
