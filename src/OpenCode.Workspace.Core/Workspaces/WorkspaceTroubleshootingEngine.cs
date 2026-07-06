using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceTroubleshootingContext
{
    public required WorkspaceSnapshot Snapshot { get; init; }
    public WorkspaceProvisioningHealthRecord? Health { get; init; }
    public bool IsProvisioningInProgress { get; init; }
    public string CurrentOperationName { get; init; } = string.Empty;
    public string CurrentStatusMessage { get; init; } = string.Empty;
    public string TranscriptFilePath { get; init; } = string.Empty;
    public string TranscriptExcerpt { get; init; } = string.Empty;
    public ProcessResult? VolatileValidation { get; init; }
    public WorkspaceTimelineEvent? LastTimelineEvent { get; init; }
    public WorkspaceLaunchPlan LaunchPlan { get; init; } = new();
    public IReadOnlyList<WorkspaceTroubleshootingCheck> TerminalReadinessChecks { get; init; } = Array.Empty<WorkspaceTroubleshootingCheck>();
    public string LastAttachFailureReason { get; init; } = string.Empty;
}

public sealed class WorkspaceTroubleshootingCheck
{
    public required string Label { get; init; }
    public required string Value { get; init; }
}

public sealed class WorkspaceInvestigationDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string EstimatedDuration { get; init; }
    public required string ProviderName { get; init; }
}

public sealed class WorkspaceInvestigationExecutionResult
{
    public required WorkspaceProvisioningHealthRecord UpdatedHealth { get; init; }
    public required WorkspaceInvestigationRecord Investigation { get; init; }
}

public static class WorkspaceTroubleshootingEngine
{
    private static readonly IWorkspaceTroubleshootingProvider[] Providers =
    [
        new OracleWorkspaceTroubleshootingProvider(),
        new PostgreSqlWorkspaceTroubleshootingProvider(),
        new PythonWorkspaceTroubleshootingProvider(),
        new GenericWorkspaceTroubleshootingProvider(),
    ];

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
            investigationHistory: previousHealth?.InvestigationHistory,
            lastDiagnosticsTimestamp: diagnosis.LastDiagnosticsTimestamp ?? diagnosis.Timestamp);
    }

    public static IReadOnlyList<WorkspaceInvestigationDefinition> GetAvailableInvestigations(WorkspaceTroubleshootingContext context)
        => Providers
            .Where(provider => provider.CanHandle(context))
            .SelectMany(provider => provider.GetInvestigations(context))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

    public static WorkspaceInvestigationExecutionResult ExecuteInvestigation(WorkspaceTroubleshootingContext context, string investigationId)
    {
        foreach (var provider in Providers)
        {
            if (provider.CanHandle(context) && provider.TryExecute(context, investigationId, out var execution))
            {
                return execution;
            }
        }

        throw new InvalidOperationException($"Unknown workspace investigation '{investigationId}'.");
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
        return CloneHealth(diagnosis, history: history, investigationHistory: previousHealth?.InvestigationHistory);
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
        IReadOnlyList<WorkspaceInvestigationRecord>? investigationHistory = null,
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
            InvestigationHistory = investigationHistory ?? source.InvestigationHistory,
        };

    private static WorkspaceProvisioningHealthRecord RecordInvestigation(WorkspaceProvisioningHealthRecord source, WorkspaceInvestigationRecord investigation, string recommendation, string evidence, string confidence)
    {
        var history = source.InvestigationHistory.ToList();
        history.Add(investigation);
        return CloneHealth(
            source,
            evidence: string.IsNullOrWhiteSpace(evidence) ? source.Evidence : evidence,
            recommendedAction: recommendation,
            confidence: string.IsNullOrWhiteSpace(confidence) ? source.Confidence : confidence,
            investigationHistory: history,
            lastDiagnosticsTimestamp: investigation.CompletedUtc);
    }

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

    private static bool IsOracleWorkspace(WorkspaceSnapshot snapshot)
        => snapshot.Definition.Services.Any(service => service.Contains("oracle", StringComparison.OrdinalIgnoreCase) || service.Contains("ords", StringComparison.OrdinalIgnoreCase))
            || snapshot.Definition.Features.Any(feature => feature.Contains("oracle", StringComparison.OrdinalIgnoreCase) || feature.Contains("apex", StringComparison.OrdinalIgnoreCase));

    private static bool IsPostgreSqlWorkspace(WorkspaceSnapshot snapshot)
        => snapshot.Definition.Services.Any(service => service.Contains("postgres", StringComparison.OrdinalIgnoreCase));

    private static bool IsPythonWorkspace(WorkspaceSnapshot snapshot)
        => snapshot.Definition.Features.Any(feature => feature.Contains("python", StringComparison.OrdinalIgnoreCase));

    private static bool HasRepairAttemptWithNoEffect(WorkspaceProvisioningHealthRecord? health, string repairType)
        => health?.RepairHistory.Any(attempt => string.Equals(attempt.RepairType, repairType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(attempt.Result, WorkspaceRepairOutcome.RepairNoEffect, StringComparison.Ordinal)) == true;

    private static string ResolveRecommendationAfterInvestigation(WorkspaceTroubleshootingContext context, string recommendation, string fallback)
    {
        if (string.IsNullOrWhiteSpace(recommendation))
        {
            return fallback;
        }

        if (string.Equals(recommendation, "Reset Runtime.", StringComparison.Ordinal)
            && HasRepairAttemptWithNoEffect(context.Health, "Reset Runtime"))
        {
            return "Manual intervention required.";
        }

        if (string.Equals(recommendation, "Recover Workspace.", StringComparison.Ordinal)
            && HasRepairAttemptWithNoEffect(context.Health, "Recover Workspace"))
        {
            return "Manual intervention required.";
        }

        return recommendation;
    }

    private static WorkspaceProvisioningHealthRecord CreateInvestigationHealth(WorkspaceTroubleshootingContext context, string stage, string summary, string reason, string evidence, string recommendation, string confidence, string estimatedDuration)
        => new()
        {
            Succeeded = context.Health?.Succeeded ?? false,
            Stage = stage,
            Summary = summary,
            Reason = reason,
            Evidence = evidence,
            ProblemScope = context.Health?.ProblemScope ?? ClassifyProblemScope(context.Health ?? new WorkspaceProvisioningHealthRecord { RecommendedAction = recommendation }, WorkspaceRepairabilityAnalyzer.Analyze(context.Snapshot, context.Health)),
            RecommendedAction = recommendation,
            PreviousRecommendedAction = context.Health?.RecommendedAction ?? string.Empty,
            Confidence = confidence,
            Timestamp = DateTimeOffset.UtcNow,
            Duration = TimeSpan.Zero,
            RawLogReference = string.IsNullOrWhiteSpace(context.TranscriptFilePath) ? context.Snapshot.Paths.ProvisionScriptPath : context.TranscriptFilePath,
            OracleVersion = context.Health?.OracleVersion ?? string.Empty,
            ApexVersion = context.Health?.ApexVersion ?? string.Empty,
            OrdsVersion = context.Health?.OrdsVersion ?? string.Empty,
            WorkspaceRuntimeVersion = context.Snapshot.ResolvedRuntimePlan?.TargetPlatform ?? context.Health?.WorkspaceRuntimeVersion ?? string.Empty,
            Repairability = context.Health?.Repairability ?? WorkspaceRepairability.Unknown.ToString(),
            EstimatedEffort = context.Health?.EstimatedEffort ?? "Medium",
            EstimatedDuration = estimatedDuration,
            LastDiagnosticsTimestamp = DateTimeOffset.UtcNow,
            RepairHistory = context.Health?.RepairHistory ?? Array.Empty<WorkspaceRepairAttemptRecord>(),
            InvestigationHistory = context.Health?.InvestigationHistory ?? Array.Empty<WorkspaceInvestigationRecord>(),
        };

    private static WorkspaceInvestigationExecutionResult CompleteInvestigation(WorkspaceTroubleshootingContext context, string investigationId, string title, string providerName, string summary, string evidence, string recommendation, string confidence, string estimatedDuration, string outcome)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var completedUtc = DateTimeOffset.UtcNow;
        var effectiveRecommendation = ResolveRecommendationAfterInvestigation(context, recommendation, context.Health?.RecommendedAction ?? "Open Workspace.");
        var diagnosis = CreateInvestigationHealth(context, title, summary, summary, evidence, effectiveRecommendation, confidence, estimatedDuration);
        var investigation = new WorkspaceInvestigationRecord
        {
            InvestigationId = investigationId,
            Title = title,
            Summary = summary,
            Evidence = evidence,
            Recommendation = effectiveRecommendation,
            Outcome = outcome,
            Confidence = confidence,
            EstimatedDuration = estimatedDuration,
            StartedUtc = startedUtc,
            CompletedUtc = completedUtc,
            Duration = completedUtc - startedUtc,
            ProviderName = providerName,
            RelevantLogReference = string.IsNullOrWhiteSpace(context.TranscriptFilePath) ? context.Snapshot.Paths.ProvisionScriptPath : context.TranscriptFilePath,
        };

        return new WorkspaceInvestigationExecutionResult
        {
            Investigation = investigation,
            UpdatedHealth = RecordInvestigation(diagnosis, investigation, effectiveRecommendation, evidence, confidence),
        };
    }

    private interface IWorkspaceTroubleshootingProvider
    {
        bool CanHandle(WorkspaceTroubleshootingContext context);
        IReadOnlyList<WorkspaceInvestigationDefinition> GetInvestigations(WorkspaceTroubleshootingContext context);
        bool TryExecute(WorkspaceTroubleshootingContext context, string investigationId, out WorkspaceInvestigationExecutionResult execution);
    }

    private sealed class OracleWorkspaceTroubleshootingProvider : IWorkspaceTroubleshootingProvider
    {
        public bool CanHandle(WorkspaceTroubleshootingContext context) => IsOracleWorkspace(context.Snapshot);

        public IReadOnlyList<WorkspaceInvestigationDefinition> GetInvestigations(WorkspaceTroubleshootingContext context)
            =>
            [
                new WorkspaceInvestigationDefinition { Id = "inspect-oracle-runtime", Title = "Inspect Oracle runtime", Description = "Inspect Oracle runtime evidence such as XDB, SYSDBA, and pluggable database readiness.", EstimatedDuration = "15-30 seconds", ProviderName = "Oracle" },
                new WorkspaceInvestigationDefinition { Id = "inspect-apex", Title = "Inspect APEX", Description = "Inspect APEX installation progress, media availability, and APEX login readiness.", EstimatedDuration = "15-30 seconds", ProviderName = "Oracle" },
                new WorkspaceInvestigationDefinition { Id = "inspect-ords", Title = "Inspect ORDS", Description = "Inspect ORDS reachability and configuration evidence for this workspace.", EstimatedDuration = "15-30 seconds", ProviderName = "Oracle" },
            ];

        public bool TryExecute(WorkspaceTroubleshootingContext context, string investigationId, out WorkspaceInvestigationExecutionResult execution)
        {
            var evidenceSource = $"{context.Health?.Reason}\n{context.Health?.Evidence}\n{context.TranscriptExcerpt}\n{context.CurrentStatusMessage}";

            switch (investigationId)
            {
                case "inspect-oracle-runtime":
                {
                    var xdbInvalid = evidenceSource.Contains("XDB", StringComparison.OrdinalIgnoreCase)
                        && evidenceSource.Contains("INVALID", StringComparison.OrdinalIgnoreCase);
                    var pdbClosed = evidenceSource.Contains("not open for writes", StringComparison.OrdinalIgnoreCase)
                        || evidenceSource.Contains("pluggable database", StringComparison.OrdinalIgnoreCase);
                    var summary = xdbInvalid
                        ? "Oracle prerequisite validation failed."
                        : pdbClosed
                            ? "Oracle runtime is not fully open for writes."
                            : "Oracle runtime inspection completed.";
                    var evidence = xdbInvalid
                        ? "XDB status = INVALID"
                        : pdbClosed
                            ? "Pluggable database is not open for writes."
                            : string.IsNullOrWhiteSpace(context.Health?.Evidence) ? "No Oracle runtime failure evidence was recorded." : context.Health.Evidence;
                    var recommendation = xdbInvalid || pdbClosed
                        ? "Reset Runtime."
                        : "Inspect ORDS.";
                    execution = CompleteInvestigation(context, investigationId, "Inspect Oracle runtime", "Oracle", summary, evidence, recommendation, xdbInvalid || pdbClosed ? "HIGH" : "MEDIUM", "15-30 seconds", xdbInvalid || pdbClosed ? "Oracle runtime issue confirmed." : "Oracle runtime evidence collected.");
                    return true;
                }

                case "inspect-apex":
                {
                    var mediaMissing = evidenceSource.Contains("APEX installation media missing", StringComparison.OrdinalIgnoreCase);
                    var stillInstalling = context.IsProvisioningInProgress || evidenceSource.Contains("Installing APEX", StringComparison.OrdinalIgnoreCase);
                    var summary = mediaMissing
                        ? "APEX media is missing for this workspace."
                        : stillInstalling
                            ? "APEX installation is still running."
                            : "APEX inspection completed.";
                    var evidence = mediaMissing
                        ? string.IsNullOrWhiteSpace(context.Health?.Evidence) ? "Oracle APEX media was not available in any configured search location." : context.Health.Evidence
                        : stillInstalling
                            ? "APEX installation output is still active."
                            : string.IsNullOrWhiteSpace(context.TranscriptExcerpt) ? "No recent APEX transcript lines were available." : context.TranscriptExcerpt;
                    var recommendation = mediaMissing
                        ? "Provide Oracle APEX media."
                        : stillInstalling
                            ? "Keep Waiting."
                            : "Inspect ORDS.";
                    execution = CompleteInvestigation(context, investigationId, "Inspect APEX", "Oracle", summary, evidence, recommendation, stillInstalling || mediaMissing ? "HIGH" : "MEDIUM", "15-30 seconds", stillInstalling ? "Provisioning is still active." : "APEX evidence collected.");
                    return true;
                }

                case "inspect-ords":
                {
                    var ordsUnreachable = evidenceSource.Contains("ORDS", StringComparison.OrdinalIgnoreCase)
                        && evidenceSource.Contains("reachable", StringComparison.OrdinalIgnoreCase);
                    var summary = ordsUnreachable ? "ORDS did not become reachable." : "ORDS inspection completed.";
                    var evidence = ordsUnreachable ? "ORDS endpoint did not become reachable during provisioning." : (string.IsNullOrWhiteSpace(context.Health?.Evidence) ? "No ORDS-specific failure evidence was recorded." : context.Health.Evidence);
                    var recommendation = ordsUnreachable && context.IsProvisioningInProgress
                        ? "Keep Waiting."
                        : ordsUnreachable
                            ? "Reset Runtime."
                            : "Inspect provisioning transcript.";
                    execution = CompleteInvestigation(context, investigationId, "Inspect ORDS", "Oracle", summary, evidence, recommendation, ordsUnreachable ? "HIGH" : "MEDIUM", "15-30 seconds", ordsUnreachable ? "ORDS reachability issue confirmed." : "ORDS evidence collected.");
                    return true;
                }
            }

            execution = null!;
            return false;
        }
    }

    private sealed class PostgreSqlWorkspaceTroubleshootingProvider : IWorkspaceTroubleshootingProvider
    {
        public bool CanHandle(WorkspaceTroubleshootingContext context) => IsPostgreSqlWorkspace(context.Snapshot);

        public IReadOnlyList<WorkspaceInvestigationDefinition> GetInvestigations(WorkspaceTroubleshootingContext context)
            => [new WorkspaceInvestigationDefinition { Id = "inspect-postgres-runtime", Title = "Inspect PostgreSQL runtime", Description = "Inspect PostgreSQL runtime and migration evidence for this workspace.", EstimatedDuration = "10-20 seconds", ProviderName = "PostgreSQL" }];

        public bool TryExecute(WorkspaceTroubleshootingContext context, string investigationId, out WorkspaceInvestigationExecutionResult execution)
        {
            if (!string.Equals(investigationId, "inspect-postgres-runtime", StringComparison.Ordinal))
            {
                execution = null!;
                return false;
            }

            var evidence = string.IsNullOrWhiteSpace(context.Health?.Evidence)
                ? "No PostgreSQL-specific failure evidence was recorded. Review Docker resources and the provisioning transcript."
                : context.Health.Evidence;
            execution = CompleteInvestigation(context, investigationId, "Inspect PostgreSQL runtime", "PostgreSQL", "PostgreSQL runtime inspection completed.", evidence, "Inspect Docker resources.", "MEDIUM", "10-20 seconds", "PostgreSQL runtime evidence collected.");
            return true;
        }
    }

    private sealed class PythonWorkspaceTroubleshootingProvider : IWorkspaceTroubleshootingProvider
    {
        public bool CanHandle(WorkspaceTroubleshootingContext context) => IsPythonWorkspace(context.Snapshot);

        public IReadOnlyList<WorkspaceInvestigationDefinition> GetInvestigations(WorkspaceTroubleshootingContext context)
            => [new WorkspaceInvestigationDefinition { Id = "inspect-python-runtime", Title = "Inspect Python runtime", Description = "Inspect Python interpreter and environment setup evidence for this workspace.", EstimatedDuration = "10-20 seconds", ProviderName = "Python" }];

        public bool TryExecute(WorkspaceTroubleshootingContext context, string investigationId, out WorkspaceInvestigationExecutionResult execution)
        {
            if (!string.Equals(investigationId, "inspect-python-runtime", StringComparison.Ordinal))
            {
                execution = null!;
                return false;
            }

            var evidence = string.IsNullOrWhiteSpace(context.Health?.Evidence)
                ? "No Python-specific failure evidence was recorded. Review generated configuration and the provisioning transcript."
                : context.Health.Evidence;
            execution = CompleteInvestigation(context, investigationId, "Inspect Python runtime", "Python", "Python runtime inspection completed.", evidence, "Inspect generated configuration.", "MEDIUM", "10-20 seconds", "Python runtime evidence collected.");
            return true;
        }
    }

    private sealed class GenericWorkspaceTroubleshootingProvider : IWorkspaceTroubleshootingProvider
    {
        public bool CanHandle(WorkspaceTroubleshootingContext context) => true;

        public IReadOnlyList<WorkspaceInvestigationDefinition> GetInvestigations(WorkspaceTroubleshootingContext context)
            =>
            [
                new WorkspaceInvestigationDefinition { Id = "inspect-workspace-runtime-files", Title = "Inspect workspace runtime files", Description = "Inspect runtime-state, applied-state, and attach artifacts for this workspace.", EstimatedDuration = "10-20 seconds", ProviderName = "Generic" },
                new WorkspaceInvestigationDefinition { Id = "inspect-terminal-readiness", Title = "Inspect terminal readiness", Description = "Inspect attach scripts, runtime-state, container exec readiness, and terminal launch evidence.", EstimatedDuration = "10-20 seconds", ProviderName = "Generic" },
                new WorkspaceInvestigationDefinition { Id = "inspect-generated-configuration", Title = "Inspect generated configuration", Description = "Inspect compose, environment, and generated provisioning artifacts.", EstimatedDuration = "10-20 seconds", ProviderName = "Generic" },
                new WorkspaceInvestigationDefinition { Id = "inspect-docker-resources", Title = "Inspect Docker resources", Description = "Inspect current Docker and compose evidence for this workspace.", EstimatedDuration = "10-20 seconds", ProviderName = "Generic" },
                new WorkspaceInvestigationDefinition { Id = "inspect-provisioning-transcript", Title = "Inspect provisioning transcript", Description = "Inspect the latest provisioning transcript and link the recommendation to relevant log evidence.", EstimatedDuration = "10-20 seconds", ProviderName = "Generic" },
                new WorkspaceInvestigationDefinition { Id = "compare-last-successful-provisioning", Title = "Compare with last successful provisioning", Description = "Compare the current failure with the last successful timeline evidence for this workspace.", EstimatedDuration = "10-20 seconds", ProviderName = "Generic" },
            ];

        public bool TryExecute(WorkspaceTroubleshootingContext context, string investigationId, out WorkspaceInvestigationExecutionResult execution)
        {
            switch (investigationId)
            {
                case "inspect-workspace-runtime-files":
                {
                    var missingFiles = new List<string>();
                    if (context.Snapshot.LocalRuntimeState is null)
                    {
                        missingFiles.Add("runtime-state.yaml");
                    }

                    if (context.Snapshot.AppliedState is null)
                    {
                        missingFiles.Add("applied-state.yaml");
                    }

                    if (!File.Exists(context.Snapshot.Paths.AttachWrapperScriptPath))
                    {
                        missingFiles.Add("attach wrapper script");
                    }

                    var evidence = missingFiles.Count == 0
                        ? "Managed runtime files are present."
                        : $"Missing or stale managed files: {string.Join(", ", missingFiles)}.";
                    var recommendation = missingFiles.Count == 0 ? "Inspect provisioning transcript." : "Open Workspace.";
                    execution = CompleteInvestigation(context, investigationId, "Inspect workspace runtime files", "Generic", "Managed runtime files inspected.", evidence, recommendation, missingFiles.Count == 0 ? "MEDIUM" : "HIGH", "10-20 seconds", missingFiles.Count == 0 ? "No missing managed runtime files detected." : "Missing managed runtime files confirmed.");
                    return true;
                }

                case "inspect-generated-configuration":
                {
                    var missing = new List<string>();
                    if (!File.Exists(context.Snapshot.Paths.ComposePath)) missing.Add("compose.yaml");
                    if (!File.Exists(context.Snapshot.Paths.EnvironmentFilePath)) missing.Add(".env");
                    if (!File.Exists(context.Snapshot.Paths.ProvisionScriptPath)) missing.Add("provision.sh");
                    var evidence = missing.Count == 0 ? "Generated configuration files are present." : $"Missing generated configuration files: {string.Join(", ", missing)}.";
                    var recommendation = missing.Count == 0 ? "Inspect Docker resources." : "Recover Workspace.";
                    execution = CompleteInvestigation(context, investigationId, "Inspect generated configuration", "Generic", "Generated configuration inspected.", evidence, recommendation, missing.Count == 0 ? "MEDIUM" : "HIGH", "10-20 seconds", missing.Count == 0 ? "Generated configuration looks complete." : "Generated configuration gap confirmed.");
                    return true;
                }

                case "inspect-terminal-readiness":
                {
                    var evidence = context.TerminalReadinessChecks.Count == 0
                        ? "No terminal readiness evidence was collected."
                        : string.Join(Environment.NewLine, context.TerminalReadinessChecks.Select(item => $"{item.Label}: {item.Value}"));
                    if (!string.IsNullOrWhiteSpace(context.LastAttachFailureReason))
                    {
                        evidence = string.IsNullOrWhiteSpace(evidence)
                            ? $"Last attach failure: {context.LastAttachFailureReason}"
                            : evidence + Environment.NewLine + $"Last attach failure: {context.LastAttachFailureReason}";
                    }

                    execution = CompleteInvestigation(context, investigationId, "Inspect terminal readiness", "Generic", "Terminal readiness inspection completed.", evidence, "Troubleshoot Workspace.", "HIGH", "10-20 seconds", "Terminal readiness evidence collected.");
                    return true;
                }

                case "inspect-docker-resources":
                {
                    var evidence = context.VolatileValidation is null
                        ? "No Docker validation output was captured for this workspace."
                        : string.Join(Environment.NewLine, context.VolatileValidation.StandardErrorLines.Concat(context.VolatileValidation.StandardOutputLines).Where(line => !string.IsNullOrWhiteSpace(line)).Take(12));
                    var recommendation = context.VolatileValidation?.FailureClassification == WorkspaceFailureClassification.EnvironmentPortConflict
                        ? "Stop conflicting workspace and Retry."
                        : "Inspect provisioning transcript.";
                    execution = CompleteInvestigation(context, investigationId, "Inspect Docker resources", "Generic", "Docker resource inspection completed.", evidence, recommendation, context.VolatileValidation?.FailureClassification == WorkspaceFailureClassification.EnvironmentPortConflict ? "HIGH" : "MEDIUM", "10-20 seconds", context.VolatileValidation is null ? "No Docker evidence was available." : "Docker evidence collected.");
                    return true;
                }

                case "inspect-provisioning-transcript":
                {
                    var evidence = string.IsNullOrWhiteSpace(context.TranscriptExcerpt)
                        ? (string.IsNullOrWhiteSpace(context.Health?.Evidence) ? "No transcript excerpt was available." : context.Health.Evidence)
                        : context.TranscriptExcerpt;
                    var recommendation = context.IsProvisioningInProgress
                        ? "Keep Waiting."
                        : !string.IsNullOrWhiteSpace(context.Health?.RecommendedAction)
                            ? context.Health.RecommendedAction
                            : "Open Workspace.";
                    execution = CompleteInvestigation(context, investigationId, "Inspect provisioning transcript", "Generic", "Provisioning transcript inspected.", evidence, recommendation, context.IsProvisioningInProgress ? "HIGH" : "MEDIUM", "10-20 seconds", "Provisioning transcript evidence collected.");
                    return true;
                }

                case "compare-last-successful-provisioning":
                {
                    var evidence = context.LastTimelineEvent is null
                        ? "No successful provisioning event was found in the workspace timeline."
                        : $"Last timeline event: {context.LastTimelineEvent.Summary}. {context.LastTimelineEvent.Details}";
                    var recommendation = context.LastTimelineEvent is null ? "Inspect provisioning transcript." : "Inspect Docker resources.";
                    execution = CompleteInvestigation(context, investigationId, "Compare with last successful provisioning", "Generic", "Timeline comparison completed.", evidence, recommendation, "MEDIUM", "10-20 seconds", "Timeline comparison evidence collected.");
                    return true;
                }
            }

            execution = null!;
            return false;
        }
    }
}
