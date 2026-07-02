using OpenCode.Workspace.Core.Models;
using System.Collections;
using System.Reflection;

namespace OpenCode.Workspace.Core.Workspaces;

public static class WorkspaceDiagnosticsSessionBuilder
{
    public static WorkspaceDiagnosticsSession Build(WorkspaceDiagnosticsSessionBuildInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var transcript = ProjectTranscript(input.Transcript);
        var health = input.ProvisioningHealth;
        var readiness = input.Readiness;
        var context = input.TroubleshootingContext;
        var workspaceName = FirstNonEmpty(
            input.WorkspaceName,
            transcript?.WorkspaceName,
            context?.Snapshot.Record.Name,
            "workspace");
        var workspaceRootPath = FirstNonEmpty(
            input.WorkspaceRootPath,
            context?.Snapshot.Paths.RootPath,
            string.Empty);
        var operationName = FirstNonEmpty(
            input.OperationName,
            transcript?.OperationName,
            context?.CurrentOperationName,
            "Workspace Diagnostics");

        var entries = BuildEntries(transcript, health, context);
        var hasRunningOperation = transcript is not null && transcript.CompletedUtc is null;
        var hasFailureEvidence = HasFailureEvidence(transcript, health, entries);
        var status = DetermineStatus(hasRunningOperation, hasFailureEvidence, health, readiness);
        var mode = status == WorkspaceDiagnosticsStatus.Running
            ? WorkspaceDiagnosticsMode.Progress
            : WorkspaceDiagnosticsMode.Diagnostics;
        var startedUtc = transcript?.StartedUtc
            ?? context?.Health?.Timestamp
            ?? health?.Timestamp
            ?? entries.Select(static item => (DateTimeOffset?)item.Timestamp).OrderBy(static item => item).FirstOrDefault()
            ?? DateTimeOffset.UtcNow;
        var completedUtc = transcript?.CompletedUtc;

        return new WorkspaceDiagnosticsSession
        {
            WorkspaceName = workspaceName,
            WorkspaceRootPath = workspaceRootPath,
            OperationName = operationName,
            Mode = mode,
            Status = status,
            Summary = BuildSummary(status, transcript, health, readiness, context),
            StartedUtc = startedUtc,
            CompletedUtc = completedUtc,
            AttemptedSteps = BuildAttemptedSteps(transcript, status),
            Entries = entries,
            Readiness = readiness,
            ProvisioningHealth = health,
            FailureSummary = BuildFailureSummary(status, transcript, health, context),
            Recommendation = BuildRecommendation(readiness, health),
            BundleInfo = BuildBundleInfo(workspaceName, operationName, startedUtc, entries.Count > 0),
        };
    }

    private static IReadOnlyList<WorkspaceDiagnosticsEntry> BuildEntries(TranscriptProjection? transcript, WorkspaceProvisioningHealthRecord? health, WorkspaceTroubleshootingContext? context)
    {
        var entries = new List<WorkspaceDiagnosticsEntry>();

        if (transcript is not null)
        {
            entries.AddRange(transcript.Lines.Select(line => new WorkspaceDiagnosticsEntry
            {
                Timestamp = line.Timestamp,
                Kind = MapEntryKind(line.Kind),
                Message = line.Text,
                Source = "transcript",
                IsFailureEvidence = line.Kind == "StandardError"
                    || (line.Kind == "Result" && line.Text.Contains("fail", StringComparison.OrdinalIgnoreCase)),
            }));
        }

        if (health is not null)
        {
            AddEntryIfPresent(entries, health.Timestamp, WorkspaceDiagnosticsEntryKind.Summary, health.Summary, "health", !health.Succeeded);
            AddEntryIfPresent(entries, health.Timestamp, WorkspaceDiagnosticsEntryKind.Evidence, health.Reason, "health", !health.Succeeded);
            AddEntryIfPresent(entries, health.Timestamp, WorkspaceDiagnosticsEntryKind.Evidence, health.Evidence, "health", !health.Succeeded);

            foreach (var attempt in health.RepairHistory)
            {
                AddEntryIfPresent(entries, attempt.CompletedUtc, WorkspaceDiagnosticsEntryKind.Evidence, $"Repair attempt: {attempt.RepairType} -> {attempt.Result}", "repair-history", attempt.Result == WorkspaceRepairOutcome.RepairFailed);
            }

            foreach (var investigation in health.InvestigationHistory)
            {
                AddEntryIfPresent(entries, investigation.CompletedUtc, WorkspaceDiagnosticsEntryKind.Evidence, $"Investigation: {investigation.Title} -> {investigation.Outcome}", "investigation-history", false);
            }
        }

        if (context is not null)
        {
            AddEntryIfPresent(entries, DateTimeOffset.UtcNow, WorkspaceDiagnosticsEntryKind.Evidence, context.TranscriptExcerpt, "troubleshooting", false);
            AddEntryIfPresent(entries, DateTimeOffset.UtcNow, WorkspaceDiagnosticsEntryKind.Status, context.CurrentStatusMessage, "troubleshooting", false);
            AddEntryIfPresent(entries, DateTimeOffset.UtcNow, WorkspaceDiagnosticsEntryKind.Evidence, context.LastAttachFailureReason, "troubleshooting", true);
        }

        return entries
            .OrderBy(item => item.Timestamp)
            .ThenBy(item => item.Kind)
            .ToList();
    }

    private static void AddEntryIfPresent(List<WorkspaceDiagnosticsEntry> entries, DateTimeOffset timestamp, WorkspaceDiagnosticsEntryKind kind, string? message, string source, bool isFailureEvidence)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        entries.Add(new WorkspaceDiagnosticsEntry
        {
            Timestamp = timestamp,
            Kind = kind,
            Message = message,
            Source = source,
            IsFailureEvidence = isFailureEvidence,
        });
    }

    private static WorkspaceDiagnosticsEntryKind MapEntryKind(string kind)
        => kind switch
        {
            "Comment" => WorkspaceDiagnosticsEntryKind.Comment,
            "Command" => WorkspaceDiagnosticsEntryKind.Command,
            "StandardOutput" => WorkspaceDiagnosticsEntryKind.Output,
            "StandardError" => WorkspaceDiagnosticsEntryKind.Error,
            "Status" => WorkspaceDiagnosticsEntryKind.Status,
            "Result" => WorkspaceDiagnosticsEntryKind.Result,
            _ => WorkspaceDiagnosticsEntryKind.Status,
        };

    private static WorkspaceDiagnosticsStatus DetermineStatus(bool hasRunningOperation, bool hasFailureEvidence, WorkspaceProvisioningHealthRecord? health, WorkspaceReadinessSnapshot? readiness)
    {
        if (hasRunningOperation)
        {
            return WorkspaceDiagnosticsStatus.Running;
        }

        if (!hasFailureEvidence)
        {
            return WorkspaceDiagnosticsStatus.Succeeded;
        }

        return IsBlocked(health, readiness)
            ? WorkspaceDiagnosticsStatus.Blocked
            : WorkspaceDiagnosticsStatus.Failed;
    }

    private static bool HasFailureEvidence(TranscriptProjection? transcript, WorkspaceProvisioningHealthRecord? health, IReadOnlyList<WorkspaceDiagnosticsEntry> entries)
        => transcript?.Succeeded == false
            || health?.Succeeded == false
            || entries.Any(entry => entry.IsFailureEvidence);

    private static bool IsBlocked(WorkspaceProvisioningHealthRecord? health, WorkspaceReadinessSnapshot? readiness)
        => string.Equals(health?.ProblemScope, "HostProblem", StringComparison.Ordinal)
            || readiness?.PrimaryAction == WorkspacePrimaryAction.RunDiagnostics;

    private static string BuildSummary(WorkspaceDiagnosticsStatus status, TranscriptProjection? transcript, WorkspaceProvisioningHealthRecord? health, WorkspaceReadinessSnapshot? readiness, WorkspaceTroubleshootingContext? context)
    {
        if (status == WorkspaceDiagnosticsStatus.Running)
        {
            return FirstNonEmpty(
                transcript?.Lines.LastOrDefault(line => line.Kind == "Status")?.Text,
                context?.CurrentStatusMessage,
                readiness?.Summary,
                "Workspace operation is running.");
        }

        if (status == WorkspaceDiagnosticsStatus.Succeeded)
        {
            return FirstNonEmpty(
                transcript?.Lines.LastOrDefault(line => line.Kind == "Result")?.Text,
                health?.Summary,
                readiness?.Summary,
                "Workspace operation completed successfully.");
        }

        return FirstNonEmpty(
            health?.Summary,
            health?.Reason,
            transcript?.Lines.LastOrDefault(line => line.Kind is "StandardError" or "Result")?.Text,
            readiness?.Summary,
            "Workspace diagnostics captured failure evidence.");
    }

    private static IReadOnlyList<WorkspaceAttemptResult> BuildAttemptedSteps(TranscriptProjection? transcript, WorkspaceDiagnosticsStatus sessionStatus)
    {
        if (transcript is null)
        {
            return Array.Empty<WorkspaceAttemptResult>();
        }

        var steps = new List<WorkspaceAttemptResult>();
        var lines = transcript.Lines;
        AddAttemptStepIfDetected(steps, WorkspaceAttemptStep.SafeRepair, "safe repair", lines, transcript.OperationName, sessionStatus, MatchesSafeRepair);
        AddAttemptStepIfDetected(steps, WorkspaceAttemptStep.Provision, "provision", lines, transcript.OperationName, sessionStatus, MatchesProvision);
        AddAttemptStepIfDetected(steps, WorkspaceAttemptStep.Start, "start", lines, transcript.OperationName, sessionStatus, MatchesStart);
        AddAttemptStepIfDetected(steps, WorkspaceAttemptStep.Attach, "attach", lines, transcript.OperationName, sessionStatus, MatchesAttach);
        AddAttemptStepIfDetected(steps, WorkspaceAttemptStep.Rebuild, "rebuild", lines, transcript.OperationName, sessionStatus, MatchesRebuild);
        return steps;
    }

    private static void AddAttemptStepIfDetected(List<WorkspaceAttemptResult> steps, WorkspaceAttemptStep step, string summary, IReadOnlyList<TranscriptProjectionLine> lines, string operationName, WorkspaceDiagnosticsStatus sessionStatus, Func<string, string, bool> matcher)
    {
        var matchingLine = lines.FirstOrDefault(line => matcher(operationName, line.Text));
        if (matchingLine is null && !matcher(operationName, operationName))
        {
            return;
        }

        steps.Add(new WorkspaceAttemptResult
        {
            Step = step,
            Succeeded = sessionStatus == WorkspaceDiagnosticsStatus.Running ? null : sessionStatus == WorkspaceDiagnosticsStatus.Succeeded,
            IsInProgress = sessionStatus == WorkspaceDiagnosticsStatus.Running,
            Summary = summary,
            Timestamp = matchingLine?.Timestamp,
        });
    }

    private static bool MatchesSafeRepair(string operationName, string text)
        => ContainsAny(operationName, text, "repair", "recover");

    private static bool MatchesProvision(string operationName, string text)
        => ContainsAny(operationName, text, "provision", "installing", "prepare workspace", "generating runtime", "reprovision");

    private static bool MatchesStart(string operationName, string text)
        => ContainsAny(operationName, text, "start", "checking workspace", "ensuring workspace", "running before attach");

    private static bool MatchesAttach(string operationName, string text)
        => ContainsAny(operationName, text, "attach", "terminal");

    private static bool MatchesRebuild(string operationName, string text)
        => ContainsAny(operationName, text, "rebuild", "reset runtime", "reprovision");

    private static bool ContainsAny(string operationName, string text, params string[] needles)
        => needles.Any(needle => operationName.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static WorkspaceFailureSummary? BuildFailureSummary(WorkspaceDiagnosticsStatus status, TranscriptProjection? transcript, WorkspaceProvisioningHealthRecord? health, WorkspaceTroubleshootingContext? context)
    {
        if (status is WorkspaceDiagnosticsStatus.Running or WorkspaceDiagnosticsStatus.Succeeded)
        {
            return null;
        }

        var transcriptEvidence = transcript?.Lines
            .Where(line => line.Kind is "StandardError" or "Result")
            .Select(line => line.Text)
            .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line));

        return new WorkspaceFailureSummary
        {
            Summary = FirstNonEmpty(health?.Summary, transcriptEvidence, "Workspace diagnostics captured failure evidence."),
            Reason = FirstNonEmpty(health?.Reason, context?.LastAttachFailureReason, transcriptEvidence),
            Evidence = FirstNonEmpty(health?.Evidence, context?.TranscriptExcerpt, transcriptEvidence),
        };
    }

    private static WorkspaceNextActionRecommendation BuildRecommendation(WorkspaceReadinessSnapshot? readiness, WorkspaceProvisioningHealthRecord? health)
    {
        if (readiness is not null)
        {
            return readiness.PrimaryAction switch
            {
                WorkspacePrimaryAction.OpenWorkspace => WorkspaceNextActionRecommendation.OpenWorkspace,
                WorkspacePrimaryAction.RebuildRuntime => WorkspaceNextActionRecommendation.RebuildRuntime,
                WorkspacePrimaryAction.RunDiagnostics => WorkspaceNextActionRecommendation.RunDiagnostics,
                WorkspacePrimaryAction.OpenFolder => WorkspaceNextActionRecommendation.OpenFolder,
                _ => WorkspaceNextActionRecommendation.None,
            };
        }

        if (string.Equals(health?.RecommendedAction, "Open Workspace.", StringComparison.Ordinal))
        {
            return WorkspaceNextActionRecommendation.OpenWorkspace;
        }

        if (string.Equals(health?.RecommendedAction, "Rebuild Runtime.", StringComparison.Ordinal)
            || string.Equals(health?.RecommendedAction, "Reset Runtime.", StringComparison.Ordinal))
        {
            return WorkspaceNextActionRecommendation.RebuildRuntime;
        }

        if (string.Equals(health?.RecommendedAction, "Run Diagnostics.", StringComparison.Ordinal)
            || string.Equals(health?.RecommendedAction, "Troubleshoot Workspace.", StringComparison.Ordinal))
        {
            return WorkspaceNextActionRecommendation.RunDiagnostics;
        }

        return WorkspaceNextActionRecommendation.None;
    }

    private static WorkspaceDiagnosticsBundleInfo BuildBundleInfo(string workspaceName, string operationName, DateTimeOffset timestamp, bool hasEntries)
        => new()
        {
            SuggestedFileName = $"{Sanitize(workspaceName)}-{Sanitize(operationName)}-diagnostics-{timestamp:yyyyMMdd-HHmmss}.txt",
            CanCopyToClipboard = hasEntries,
            CanExportToFile = hasEntries,
        };

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "workspace";
        }

        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        return string.Join('-', normalized.Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static TranscriptProjection? ProjectTranscript(object? transcript)
    {
        if (transcript is null)
        {
            return null;
        }

        var type = transcript.GetType();
        var operationName = ReadProperty<string>(transcript, type, "OperationName");
        var workspaceName = ReadProperty<string>(transcript, type, "WorkspaceName");
        var startedUtc = ReadProperty<DateTimeOffset>(transcript, type, "StartedUtc");
        var completedUtc = ReadNullableProperty<DateTimeOffset>(transcript, type, "CompletedUtc");
        var succeeded = ReadNullableProperty<bool>(transcript, type, "Succeeded");
        var lines = new List<TranscriptProjectionLine>();

        if (ReadProperty<object>(transcript, type, "Lines") is IEnumerable rawLines)
        {
            foreach (var rawLine in rawLines)
            {
                if (rawLine is null)
                {
                    continue;
                }

                var lineType = rawLine.GetType();
                lines.Add(new TranscriptProjectionLine
                {
                    Timestamp = ReadProperty<DateTimeOffset>(rawLine, lineType, "Timestamp"),
                    Kind = ReadProperty<object>(rawLine, lineType, "Kind")?.ToString() ?? string.Empty,
                    Text = ReadProperty<string>(rawLine, lineType, "Text") ?? string.Empty,
                });
            }
        }

        return new TranscriptProjection
        {
            OperationName = operationName ?? string.Empty,
            WorkspaceName = workspaceName ?? string.Empty,
            StartedUtc = startedUtc,
            CompletedUtc = completedUtc,
            Succeeded = succeeded,
            Lines = lines,
        };
    }

    private static T? ReadProperty<T>(object instance, Type type, string name)
    {
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property is null)
        {
            return default;
        }

        var value = property.GetValue(instance);
        return value is T typed ? typed : default;
    }

    private static T? ReadNullableProperty<T>(object instance, Type type, string name)
        where T : struct
    {
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property is null)
        {
            return null;
        }

        var value = property.GetValue(instance);
        return value is T typed ? typed : null;
    }

    private sealed class TranscriptProjection
    {
        public string OperationName { get; init; } = string.Empty;
        public string WorkspaceName { get; init; } = string.Empty;
        public DateTimeOffset StartedUtc { get; init; }
        public DateTimeOffset? CompletedUtc { get; init; }
        public bool? Succeeded { get; init; }
        public IReadOnlyList<TranscriptProjectionLine> Lines { get; init; } = Array.Empty<TranscriptProjectionLine>();
    }

    private sealed class TranscriptProjectionLine
    {
        public DateTimeOffset Timestamp { get; init; }
        public string Kind { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
    }
}
