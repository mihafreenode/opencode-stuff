using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public static class WorkspaceTroubleshootingEngine
{
    public static WorkspaceProvisioningHealthRecord ApplyDiagnosis(WorkspaceSnapshot? snapshot, WorkspaceProvisioningHealthRecord diagnosis, WorkspaceProvisioningHealthRecord? previousHealth)
    {
        var assessment = WorkspaceRepairabilityAnalyzer.Analyze(snapshot, diagnosis);
        var scope = ClassifyProblemScope(diagnosis, assessment);
        var recommendedAction = string.IsNullOrWhiteSpace(diagnosis.RecommendedAction)
            ? assessment.RecommendedNextAction
            : diagnosis.RecommendedAction;
        var previousRecommendedAction = previousHealth?.RecommendedAction ?? string.Empty;
        var repairability = string.IsNullOrWhiteSpace(diagnosis.Repairability)
            ? assessment.Classification.ToString()
            : diagnosis.Repairability;
        var confidence = string.IsNullOrWhiteSpace(diagnosis.Confidence) ? assessment.Confidence : diagnosis.Confidence;
        var history = previousHealth?.RepairHistory?.ToList() ?? [];

        if (history.Count > 0 && !diagnosis.Succeeded)
        {
            var lastAttempt = history[^1];
            if (ShouldCompareAgainstLastRepair(lastAttempt))
            {
                var outcome = DetermineRepairOutcome(lastAttempt, diagnosis, snapshot);
                history[^1] = outcome.Attempt;

                if (string.Equals(outcome.Attempt.Result, WorkspaceRepairOutcome.RepairNoEffect, StringComparison.Ordinal)
                    && CanRepeatRepair(lastAttempt, recommendedAction))
                {
                    previousRecommendedAction = recommendedAction;
                    recommendedAction = BuildFallbackRecommendation(lastAttempt.RepairType, scope);
                    repairability = BuildFallbackRepairability(lastAttempt.RepairType, repairability);
                    confidence = string.IsNullOrWhiteSpace(confidence) ? "HIGH" : confidence;
                }
            }
        }

        return CloneHealth(
            diagnosis,
            evidence: string.IsNullOrWhiteSpace(diagnosis.Evidence) ? assessment.Evidence : diagnosis.Evidence,
            problemScope: scope,
            recommendedAction: recommendedAction,
            previousRecommendedAction: previousRecommendedAction,
            confidence: confidence,
            repairability: repairability,
            estimatedEffort: string.IsNullOrWhiteSpace(diagnosis.EstimatedEffort) ? assessment.EstimatedEffort : diagnosis.EstimatedEffort,
            estimatedDuration: string.IsNullOrWhiteSpace(diagnosis.EstimatedDuration) ? assessment.EstimatedDuration : diagnosis.EstimatedDuration,
            history: history,
            lastDiagnosticsTimestamp: diagnosis.LastDiagnosticsTimestamp ?? diagnosis.Timestamp);
    }

    public static WorkspaceProvisioningHealthRecord RecordRepairAttempt(
        WorkspaceProvisioningHealthRecord? previousHealth,
        string repairType,
        DateTimeOffset startedUtc,
        DateTimeOffset completedUtc,
        WorkspaceSnapshot? beforeSnapshot,
        WorkspaceSnapshot? afterSnapshot,
        WorkspaceProvisioningHealthRecord diagnosis,
        string? forcedOutcome = null)
    {
        var history = previousHealth?.RepairHistory?.ToList() ?? [];
        var attempt = new WorkspaceRepairAttemptRecord
        {
            RepairType = repairType,
            StartedUtc = startedUtc,
            CompletedUtc = completedUtc,
            Duration = completedUtc - startedUtc,
            Result = forcedOutcome ?? DetermineImmediateOutcome(diagnosis),
            EvidenceBefore = previousHealth?.Evidence ?? string.Empty,
            EvidenceAfter = diagnosis.Evidence,
            RootCauseBefore = previousHealth?.Reason ?? string.Empty,
            RootCauseAfter = diagnosis.Reason,
            RootCauseChanged = !IsSameRootCause(previousHealth?.Reason ?? string.Empty, previousHealth?.Evidence ?? string.Empty, diagnosis.Reason, diagnosis.Evidence),
            WorkspaceStateBefore = DescribeWorkspaceState(beforeSnapshot),
            WorkspaceStateAfter = DescribeWorkspaceState(afterSnapshot),
            WorkspaceStateChanged = !string.Equals(DescribeWorkspaceState(beforeSnapshot), DescribeWorkspaceState(afterSnapshot), StringComparison.Ordinal),
            Confidence = diagnosis.Confidence,
            PreviousRecommendation = previousHealth?.RecommendedAction ?? string.Empty,
            UpdatedRecommendation = diagnosis.RecommendedAction,
        };

        history.Add(attempt);
        return CloneHealth(diagnosis, history: history);
    }

    private static WorkspaceProvisioningHealthRecord CloneHealth(
        WorkspaceProvisioningHealthRecord source,
        string? evidence = null,
        string? problemScope = null,
        string? recommendedAction = null,
        string? previousRecommendedAction = null,
        string? confidence = null,
        string? repairability = null,
        string? estimatedEffort = null,
        string? estimatedDuration = null,
        IReadOnlyList<WorkspaceRepairAttemptRecord>? history = null,
        DateTimeOffset? lastDiagnosticsTimestamp = null)
        => new()
        {
            Succeeded = source.Succeeded,
            Stage = source.Stage,
            Summary = source.Summary,
            Reason = source.Reason,
            Evidence = evidence ?? source.Evidence,
            ProblemScope = problemScope ?? source.ProblemScope,
            RecommendedAction = recommendedAction ?? source.RecommendedAction,
            PreviousRecommendedAction = previousRecommendedAction ?? source.PreviousRecommendedAction,
            Confidence = confidence ?? source.Confidence,
            Timestamp = source.Timestamp,
            Duration = source.Duration,
            RawLogReference = source.RawLogReference,
            OracleVersion = source.OracleVersion,
            ApexVersion = source.ApexVersion,
            OrdsVersion = source.OrdsVersion,
            WorkspaceRuntimeVersion = source.WorkspaceRuntimeVersion,
            Repairability = repairability ?? source.Repairability,
            EstimatedEffort = estimatedEffort ?? source.EstimatedEffort,
            EstimatedDuration = estimatedDuration ?? source.EstimatedDuration,
            LastDiagnosticsTimestamp = lastDiagnosticsTimestamp ?? source.LastDiagnosticsTimestamp,
            RepairHistory = history ?? source.RepairHistory,
        };

    private static bool ShouldCompareAgainstLastRepair(WorkspaceRepairAttemptRecord attempt)
        => !string.IsNullOrWhiteSpace(attempt.RepairType)
            && !string.Equals(attempt.Result, WorkspaceRepairOutcome.RepairFailed, StringComparison.Ordinal);

    private static bool CanRepeatRepair(WorkspaceRepairAttemptRecord attempt, string recommendedAction)
        => !string.IsNullOrWhiteSpace(attempt.RepairType)
            && string.Equals(NormalizeAction(attempt.RepairType), NormalizeAction(recommendedAction), StringComparison.Ordinal);

    private static (WorkspaceRepairAttemptRecord Attempt, bool RootCauseUnchanged) DetermineRepairOutcome(WorkspaceRepairAttemptRecord attempt, WorkspaceProvisioningHealthRecord diagnosis, WorkspaceSnapshot? snapshot)
    {
        var rootCauseUnchanged = IsSameRootCause(attempt.RootCauseBefore, attempt.EvidenceBefore, diagnosis.Reason, diagnosis.Evidence);
        var result = rootCauseUnchanged
            ? WorkspaceRepairOutcome.RepairNoEffect
            : diagnosis.Succeeded
                ? WorkspaceRepairOutcome.RepairSucceeded
                : WorkspaceRepairOutcome.RepairImproved;

        return (new WorkspaceRepairAttemptRecord
        {
            RepairType = attempt.RepairType,
            StartedUtc = attempt.StartedUtc,
            CompletedUtc = attempt.CompletedUtc,
            Duration = attempt.Duration,
            Result = result,
            EvidenceBefore = attempt.EvidenceBefore,
            EvidenceAfter = diagnosis.Evidence,
            RootCauseBefore = attempt.RootCauseBefore,
            RootCauseAfter = diagnosis.Reason,
            RootCauseChanged = !rootCauseUnchanged,
            WorkspaceStateBefore = attempt.WorkspaceStateBefore,
            WorkspaceStateAfter = DescribeWorkspaceState(snapshot),
            WorkspaceStateChanged = !string.Equals(attempt.WorkspaceStateBefore, DescribeWorkspaceState(snapshot), StringComparison.Ordinal),
            Confidence = diagnosis.Confidence,
            PreviousRecommendation = attempt.PreviousRecommendation,
            UpdatedRecommendation = diagnosis.RecommendedAction,
        }, rootCauseUnchanged);
    }

    private static string DetermineImmediateOutcome(WorkspaceProvisioningHealthRecord diagnosis)
        => diagnosis.Succeeded ? WorkspaceRepairOutcome.RepairSucceeded : WorkspaceRepairOutcome.RepairFailed;

    private static string DescribeWorkspaceState(WorkspaceSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return "Unavailable";
        }

        return $"runtime={snapshot.RuntimeState}; runtime-state={(snapshot.LocalRuntimeState is null ? "missing" : "loaded")}; update-required={snapshot.UpdateRequired}";
    }

    private static string BuildFallbackRecommendation(string repairType, string problemScope)
        => NormalizeAction(repairType) switch
        {
            "Reset Runtime" => "Troubleshoot Workspace.",
            "Recover Workspace" => "Troubleshoot Workspace.",
            "Run Diagnostics" when string.Equals(problemScope, "HostProblem", StringComparison.Ordinal) => "Troubleshoot Workspace.",
            _ => "Troubleshoot Workspace.",
        };

    private static string BuildFallbackRepairability(string repairType, string currentRepairability)
        => NormalizeAction(repairType) switch
        {
            "Reset Runtime" or "Recover Workspace" => WorkspaceRepairability.ManualRepair.ToString(),
            _ => currentRepairability,
        };

    private static string ClassifyProblemScope(WorkspaceProvisioningHealthRecord diagnosis, WorkspaceRepairabilityAssessment assessment)
    {
        var normalizedAction = NormalizeAction(string.IsNullOrWhiteSpace(diagnosis.RecommendedAction) ? assessment.RecommendedNextAction : diagnosis.RecommendedAction);
        return normalizedAction switch
        {
            "Run Diagnostics" => "HostProblem",
            "Reset Runtime" or "Upgrade runtime image" => "RuntimeProblem",
            "Recover Workspace" or "Troubleshoot Workspace" => "WorkspaceProblem",
            _ => "Unknown",
        };
    }

    private static string NormalizeAction(string action)
        => action.Trim().TrimEnd('.');

    private static bool IsSameRootCause(string beforeReason, string beforeEvidence, string afterReason, string afterEvidence)
    {
        var normalizedBeforeReason = NormalizeText(beforeReason);
        var normalizedBeforeEvidence = NormalizeText(beforeEvidence);
        var normalizedAfterReason = NormalizeText(afterReason);
        var normalizedAfterEvidence = NormalizeText(afterEvidence);

        if (!string.IsNullOrWhiteSpace(normalizedBeforeEvidence) && normalizedBeforeEvidence == normalizedAfterEvidence)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(normalizedBeforeReason) && normalizedBeforeReason == normalizedAfterReason)
        {
            return true;
        }

        return string.Equals($"{normalizedBeforeReason}|{normalizedBeforeEvidence}", $"{normalizedAfterReason}|{normalizedAfterEvidence}", StringComparison.Ordinal);
    }

    private static string NormalizeText(string value)
        => string.Join(' ', value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Trim()
            .ToUpperInvariant();
}
