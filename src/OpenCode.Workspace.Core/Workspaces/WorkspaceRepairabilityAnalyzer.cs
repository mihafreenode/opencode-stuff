using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public enum WorkspaceRepairability
{
    AutomaticRepair,
    CleanupRepair,
    ManualRepair,
    Unknown,
}

public sealed class WorkspaceRepairabilityAssessment
{
    public WorkspaceRepairability Classification { get; init; } = WorkspaceRepairability.Unknown;
    public string Confidence { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public string RecommendedNextAction { get; init; } = string.Empty;
    public string EstimatedEffort { get; init; } = string.Empty;
    public string EstimatedDuration { get; init; } = string.Empty;
}

public static class WorkspaceRepairabilityAnalyzer
{
    public static WorkspaceRepairabilityAssessment Analyze(WorkspaceSnapshot? snapshot, WorkspaceProvisioningHealthRecord? health)
    {
        if (health is not null)
        {
            var reason = health.Reason;
            var evidence = string.IsNullOrWhiteSpace(health.Evidence) ? reason : health.Evidence;

            if (reason.Contains("already in use", StringComparison.OrdinalIgnoreCase))
            {
                return Create(WorkspaceRepairability.AutomaticRepair, "HIGH", evidence, "Stop conflicting workspace and Retry.", "Low", "1-2 minutes");
            }

            if (reason.Contains("Docker", StringComparison.OrdinalIgnoreCase) && reason.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                return Create(WorkspaceRepairability.AutomaticRepair, "MEDIUM", evidence, "Run Diagnostics.", "Low", "1-2 minutes");
            }

            if (reason.Contains("XDB is invalid", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("SYSDBA connection", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("pluggable database", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("not open for writes", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("ORDS) did not become reachable", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("APEX login route is not reachable", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("APEX installation media missing", StringComparison.OrdinalIgnoreCase))
            {
                return Create(WorkspaceRepairability.CleanupRepair, "HIGH", evidence, "Reset Runtime.", "Medium", "4-6 minutes");
            }

            if (reason.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
                && reason.Contains("Oracle", StringComparison.OrdinalIgnoreCase))
            {
                return Create(WorkspaceRepairability.ManualRepair, "HIGH", evidence, "Upgrade runtime image.", "High", "10-15 minutes");
            }

            if (IsOracleWorkspace(snapshot))
            {
                return Create(WorkspaceRepairability.Unknown, "MEDIUM", evidence, "Run Diagnostics.", "Medium", "2-4 minutes");
            }
        }

        if (snapshot?.LocalRuntimeState is null || snapshot?.AppliedState is null || snapshot?.UpdateRequired == true)
        {
            return Create(WorkspaceRepairability.AutomaticRepair, "HIGH", "Runtime state is missing or stale.", "Run Recover Workspace.", "Low", "1-2 minutes");
        }

        if (snapshot?.RuntimeState == WorkspaceRuntimeState.Unknown)
        {
            return Create(WorkspaceRepairability.AutomaticRepair, "MEDIUM", "Runtime availability could not be confirmed.", "Run Diagnostics.", "Low", "1-2 minutes");
        }

        return Create(WorkspaceRepairability.Unknown, "LOW", string.Empty, "Run Diagnostics.", "Medium", "2-4 minutes");
    }

    private static bool IsOracleWorkspace(WorkspaceSnapshot? snapshot)
        => snapshot?.Definition.Services.Any(service => string.Equals(service, "oracle-demo", StringComparison.OrdinalIgnoreCase) || string.Equals(service, "oracle-ords", StringComparison.OrdinalIgnoreCase)) == true;

    private static WorkspaceRepairabilityAssessment Create(WorkspaceRepairability classification, string confidence, string evidence, string action, string effort, string duration)
        => new()
        {
            Classification = classification,
            Confidence = confidence,
            Evidence = evidence,
            RecommendedNextAction = action,
            EstimatedEffort = effort,
            EstimatedDuration = duration,
        };
}
