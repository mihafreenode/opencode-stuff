using System.Text;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Diagnostics;

public static class PlatformValidationMarkdownReportFormatter
{
    public static string Format(PlatformValidationReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Platform Validation Report");
        builder.AppendLine();
        builder.AppendLine($"Requested Target: {report.TargetPlatform}");
        builder.AppendLine($"Resolved Platform: {report.ResolvedPlatform ?? report.ResolvedRuntimePlan?.TargetPlatform ?? "unresolved"}");
        builder.AppendLine($"Compatibility: {report.CompatibilityDisplay ?? "unresolved"}");
        builder.AppendLine();
        builder.AppendLine("## Checks");
        builder.AppendLine();
        builder.AppendLine("| Check | Status |");
        builder.AppendLine("| --- | --- |");

        foreach (var check in report.Checks)
        {
            builder.AppendLine($"| {EscapePipe(check.Name)} | {FormatStatus(check.Severity)} |");
        }

        var notes = BuildNotes(report).ToList();
        if (notes.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Notes");
            builder.AppendLine();
            foreach (var note in notes)
            {
                builder.AppendLine(note);
                builder.AppendLine();
            }
        }

        builder.AppendLine("## Result");
        builder.AppendLine();
        builder.AppendLine(report.Summary);
        return builder.ToString().TrimEnd();
    }

    public static string GetDefaultOutputPath(string workspaceRootPath, string targetPlatform)
    {
        var fileName = targetPlatform.Replace('/', '-').ToLowerInvariant() + ".md";
        return Path.Combine(workspaceRootPath, "artifacts", "platform-validation", fileName);
    }

    private static IEnumerable<string> BuildNotes(PlatformValidationReport report)
    {
        if (report.ValidatedWithFallback && !string.IsNullOrWhiteSpace(report.ResolvedPlatform))
        {
            yield return $"Requested target was validated through fallback behavior using {report.ResolvedPlatform}.";
        }

        var executionCheck = report.Checks.FirstOrDefault(check => string.Equals(check.Name, "Container execution", StringComparison.OrdinalIgnoreCase));
        if (executionCheck is not null)
        {
            var executionNote = BuildExecutionNote(executionCheck);
            if (!string.IsNullOrWhiteSpace(executionNote))
            {
                yield return executionNote;
            }
        }

        foreach (var check in report.Checks.Where(check => check.Severity != DiagnosticSeverity.Information))
        {
            if (string.IsNullOrWhiteSpace(check.Message) || string.Equals(check.Name, "Container execution", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return $"{check.Name}: {check.Message.Trim()}";
        }
    }

    private static string? BuildExecutionNote(PlatformValidationCheckResult executionCheck)
    {
        if (string.IsNullOrWhiteSpace(executionCheck.Message))
        {
            return null;
        }

        if (executionCheck.Severity == DiagnosticSeverity.Information)
        {
            const string prefix = "OK (";
            if (executionCheck.Message.StartsWith(prefix, StringComparison.Ordinal) && executionCheck.Message.EndsWith(')'))
            {
                return $"Container execution succeeded:{Environment.NewLine}{executionCheck.Message[prefix.Length..^1]}";
            }

            return $"Container execution succeeded: {executionCheck.Message.Trim()}";
        }

        return $"Container execution details:{Environment.NewLine}{executionCheck.Message.Trim()}";
    }

    private static string FormatStatus(DiagnosticSeverity severity)
        => severity switch
        {
            DiagnosticSeverity.Error => "Failed",
            DiagnosticSeverity.Warning => "Warning",
            _ => "OK",
        };

    private static string EscapePipe(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);
}
