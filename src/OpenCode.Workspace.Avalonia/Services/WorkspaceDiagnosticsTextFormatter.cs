using System.Text;
using OpenCode.Workspace.Avalonia.ViewModels;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.Services;

public static class WorkspaceDiagnosticsTextFormatter
{
    public static string BuildSummaryText(WorkspaceDiagnosticsSession session)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Workspace Diagnostics");
        builder.AppendLine($"Workspace: {session.WorkspaceName}");

        if (!string.IsNullOrWhiteSpace(session.WorkspaceRootPath))
        {
            builder.AppendLine($"Root path: {session.WorkspaceRootPath}");
        }

        builder.AppendLine($"Operation: {session.OperationName}");
        builder.AppendLine($"Mode: {session.Mode}");
        builder.AppendLine($"Status: {session.Status}");
        builder.AppendLine($"Started: {session.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

        if (session.CompletedUtc is not null)
        {
            builder.AppendLine($"Completed: {session.CompletedUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        }

        builder.AppendLine();
        builder.AppendLine(session.Summary);

        var recommendation = FormatRecommendation(session.Recommendation);
        if (!string.IsNullOrWhiteSpace(recommendation))
        {
            builder.AppendLine();
            builder.AppendLine($"Next: {recommendation}");
        }

        if (session.FailureSummary is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Failure Summary");
            if (!string.IsNullOrWhiteSpace(session.FailureSummary.Summary))
            {
                builder.AppendLine($"Summary: {session.FailureSummary.Summary}");
            }

            if (!string.IsNullOrWhiteSpace(session.FailureSummary.Reason))
            {
                builder.AppendLine($"Reason: {session.FailureSummary.Reason}");
            }

            if (!string.IsNullOrWhiteSpace(session.FailureSummary.Evidence))
            {
                builder.AppendLine($"Evidence: {session.FailureSummary.Evidence}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static string BuildFullLogText(WorkspaceDiagnosticsSession session)
    {
        var builder = new StringBuilder();
        builder.AppendLine(BuildSummaryText(session));

        if (session.AttemptedSteps.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Attempted Steps");
            foreach (var step in session.AttemptedSteps.Select(static item => new WorkspaceAttemptResultViewModel(item)))
            {
                builder.Append("- ");
                builder.Append(step.StepLabel);
                builder.Append(": ");
                builder.Append(step.StatusLabel);
                if (!string.IsNullOrWhiteSpace(step.Summary))
                {
                    builder.Append(" - ");
                    builder.Append(step.Summary);
                }

                if (step.HasTimestamp)
                {
                    builder.Append(" (");
                    builder.Append(step.TimestampText);
                    builder.Append(')');
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine();
        builder.AppendLine("Timeline / Entries");
        foreach (var entry in session.Entries.Select(static item => new WorkspaceDiagnosticsEntryViewModel(item)))
        {
            builder.Append('[');
            builder.Append(entry.TimestampText);
            builder.Append("] ");
            builder.Append(entry.Kind);
            if (!string.IsNullOrWhiteSpace(entry.Source))
            {
                builder.Append(" / ");
                builder.Append(entry.Source);
            }

            builder.Append(": ");
            builder.AppendLine(entry.Message);
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatRecommendation(WorkspaceNextActionRecommendation recommendation)
        => recommendation switch
        {
            WorkspaceNextActionRecommendation.OpenWorkspace => "Open Workspace",
            WorkspaceNextActionRecommendation.RebuildRuntime => "Rebuild Runtime",
            WorkspaceNextActionRecommendation.RunDiagnostics => "Run Diagnostics",
            WorkspaceNextActionRecommendation.OpenFolder => "Open Folder",
            _ => string.Empty,
        };
}
