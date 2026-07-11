using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.ViewModels;

internal static class WorkspaceHealthAggregator
{
    public static WorkspaceAggregatedState BuildState(WorkspaceSummaryViewModel workspace, bool isOperationInProgress, string currentOperationName, string currentStatusMessage, WorkspaceReadinessSnapshot? readinessOverride = null)
    {
        if (workspace.IsLoading)
        {
            return new WorkspaceAggregatedState
            {
                Summary = "Loading current workspace state.",
                CurrentActivity = "Checking workspace",
                ActivitySummary = "Loading current workspace state.",
                DevelopmentEnvironmentSummary = string.Empty,
                ServicesSummary = string.Empty,
                RecentHistoryNote = string.Empty,
            };
        }

        if (workspace.Snapshot is null)
        {
            return new WorkspaceAggregatedState
            {
                Summary = "Workspace details could not be loaded. Run Diagnostics or Refresh to continue.",
                CurrentActivity = "None",
                ActivitySummary = "No active workspace operation.",
                DevelopmentEnvironmentSummary = string.Empty,
                ServicesSummary = string.Empty,
                RecentHistoryNote = string.Empty,
            };
        }

        var readiness = readinessOverride ?? workspace.Snapshot.Readiness;
        if (readiness is null)
        {
            throw new InvalidOperationException($"Workspace '{workspace.Name}' does not have a readiness snapshot.");
        }

        return BuildStateFromReadiness(workspace, readiness);
    }

    private static WorkspaceAggregatedState BuildStateFromReadiness(WorkspaceSummaryViewModel workspace, WorkspaceReadinessSnapshot readiness)
        => new()
        {
            Summary = readiness.Summary,
            CurrentActivity = FormatActivityLabel(readiness.CurrentActivity),
            ActivitySummary = readiness.IsOperationInProgress ? readiness.Summary : "No active workspace operation.",
            CapabilitiesSummary = BuildCapabilitiesSummary(readiness.Capabilities),
            ApplicationsSummary = BuildApplicationsSummary(readiness.Capabilities),
            DevelopmentEnvironmentSummary = BuildDevelopmentEnvironmentSummary(readiness.AttentionItems),
            ServicesSummary = string.Empty,
            RecentHistoryNote = BuildRecentHistoryNote(workspace, readiness.IsOperationInProgress),
        };

    private static string BuildRecentHistoryNote(WorkspaceSummaryViewModel workspace, bool isOperationInProgress)
    {
        if (isOperationInProgress || IsFreshWorkspace(workspace))
        {
            return string.Empty;
        }

        if (workspace.Record.LastOperationSucceeded == false && !string.IsNullOrWhiteSpace(workspace.Record.LastOperationResult))
        {
            return $"Recent issue: {ExtractHistoryReason(workspace.Record.LastOperationResult!)}";
        }

        var repair = workspace.Record.LastProvisioningHealth?.RepairHistory.LastOrDefault();
        if (repair is not null && !repair.RootCauseChanged && !string.IsNullOrWhiteSpace(repair.EvidenceAfter))
        {
            return $"Recent issue: {repair.RepairType} had no effect earlier.";
        }

        return string.Empty;
    }

    private static string FormatActivityLabel(WorkspaceActivity activity)
        => activity switch
        {
            WorkspaceActivity.Preparing => "Provisioning",
            WorkspaceActivity.OpeningTerminal => "Opening terminal",
            WorkspaceActivity.RepairingRuntime => "Repairing runtime",
            WorkspaceActivity.Investigating => "Investigating",
            WorkspaceActivity.Discovering => "Discovering",
            _ => "None",
        };

    private static string BuildCapabilitiesSummary(IReadOnlyList<WorkspaceCapabilitySnapshot> capabilities)
    {
        if (capabilities.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        AppendCapabilityLines(lines, "Available", capabilities, WorkspaceCapabilityState.Available, "- ");
        AppendCapabilityLines(lines, "Preparing", capabilities, WorkspaceCapabilityState.Preparing, "- ");
        AppendCapabilityLines(lines, "Unavailable", capabilities, WorkspaceCapabilityState.Unavailable, "- ");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildApplicationsSummary(IReadOnlyList<WorkspaceCapabilitySnapshot> capabilities)
    {
        var applications = capabilities.Where(item => !item.IsPrimaryWorkSurface).ToList();
        if (applications.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, applications.Select(item => $"{FormatCapabilityPrefix(item.State)}{item.Label}"));
    }

    private static string BuildDevelopmentEnvironmentSummary(IReadOnlyList<WorkspaceAttentionItem> attentionItems)
    {
        var issues = attentionItems
            .Where(item => item.Scope == WorkspaceAttentionScope.DevelopmentEnvironment)
            .Select(item => item.Summary)
            .ToList();
        if (issues.Count == 0)
        {
            return string.Empty;
        }

        return $"Attention: {string.Join(", ", issues)}";
    }

    private static void AppendCapabilityLines(List<string> lines, string heading, IReadOnlyList<WorkspaceCapabilitySnapshot> capabilities, WorkspaceCapabilityState state, string marker)
    {
        var matches = capabilities.Where(item => item.State == state).Select(item => item.Label).ToList();
        if (matches.Count == 0)
        {
            return;
        }

        lines.Add(heading);
        lines.AddRange(matches.Select(item => $"{marker} {item}"));
    }

    private static string FormatCapabilityPrefix(WorkspaceCapabilityState state)
        => state switch
        {
            WorkspaceCapabilityState.Available => "Available: ",
            WorkspaceCapabilityState.Preparing => "Preparing: ",
            _ => "Unavailable: ",
        };

    private static bool IsFreshWorkspace(WorkspaceSummaryViewModel workspace)
        => workspace.Record.LastPreparedUtc is null
            && workspace.Record.LastOperationSucceeded == true
            && string.Equals(workspace.Record.LastOperationName, "Create Workspace", StringComparison.Ordinal);

    private static string ExtractHistoryReason(string message)
    {
        foreach (var rawLine in message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith("Command:", StringComparison.OrdinalIgnoreCase)
                || rawLine.StartsWith("Exit code:", StringComparison.OrdinalIgnoreCase)
                || rawLine.StartsWith("Likely causes:", StringComparison.OrdinalIgnoreCase)
                || rawLine.StartsWith("Suggested actions:", StringComparison.OrdinalIgnoreCase)
                || rawLine.StartsWith("Host port details:", StringComparison.OrdinalIgnoreCase)
                || rawLine.StartsWith("This workspace docker compose ps:", StringComparison.OrdinalIgnoreCase)
                || rawLine.StartsWith("Running containers:", StringComparison.OrdinalIgnoreCase)
                || rawLine.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            return rawLine.EndsWith('.') ? rawLine : rawLine + ".";
        }

        return "Earlier workspace operation failed.";
    }
}

internal sealed class WorkspaceAggregatedState
{
    public string Summary { get; init; } = string.Empty;
    public string CurrentActivity { get; init; } = string.Empty;
    public string ActivitySummary { get; init; } = string.Empty;
    public string CapabilitiesSummary { get; init; } = string.Empty;
    public string ApplicationsSummary { get; init; } = string.Empty;
    public string DevelopmentEnvironmentSummary { get; init; } = string.Empty;
    public string ServicesSummary { get; init; } = string.Empty;
    public string RecentHistoryNote { get; init; } = string.Empty;
}
