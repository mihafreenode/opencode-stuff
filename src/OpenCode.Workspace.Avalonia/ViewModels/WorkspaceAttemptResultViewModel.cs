using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspaceAttemptResultViewModel
{
    public WorkspaceAttemptResultViewModel(WorkspaceAttemptResult result)
    {
        StepLabel = result.Step switch
        {
            WorkspaceAttemptStep.SafeRepair => "Safe Repair",
            WorkspaceAttemptStep.Provision => "Provision",
            WorkspaceAttemptStep.Start => "Start",
            WorkspaceAttemptStep.Attach => "Attach",
            WorkspaceAttemptStep.Rebuild => "Rebuild",
            _ => "Unknown",
        };
        StatusLabel = result.IsInProgress
            ? "Running"
            : result.Succeeded == true
                ? "Succeeded"
                : result.Succeeded == false
                    ? "Failed"
                    : "Unknown";
        Summary = result.Summary;
        TimestampText = result.Timestamp?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
    }

    public string StepLabel { get; }
    public string StatusLabel { get; }
    public string Summary { get; }
    public string TimestampText { get; }
    public bool HasTimestamp => !string.IsNullOrWhiteSpace(TimestampText);
}
