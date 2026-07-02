using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspaceDiagnosticsEntryViewModel
{
    public WorkspaceDiagnosticsEntryViewModel(WorkspaceDiagnosticsEntry entry)
    {
        TimestampText = entry.Timestamp == default ? string.Empty : entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        Kind = entry.Kind.ToString();
        Message = entry.Message;
        Source = entry.Source;
        IsFailureEvidence = entry.IsFailureEvidence;
    }

    public string TimestampText { get; }
    public string Kind { get; }
    public string Message { get; }
    public string Source { get; }
    public bool IsFailureEvidence { get; }
}
