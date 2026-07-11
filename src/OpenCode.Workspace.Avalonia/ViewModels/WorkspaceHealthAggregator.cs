using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.ViewModels;

internal static class WorkspaceHealthAggregator
{
    public static WorkspacePresentation BuildPresentation(
        WorkspaceSummaryViewModel workspace,
        bool isOperationInProgress,
        string currentOperationName,
        string currentStatusMessage,
        WorkspacePresentationActions actions,
        WorkspaceReadinessSnapshot? readinessOverride = null)
    {
        var state = BuildState(workspace, isOperationInProgress, currentOperationName, currentStatusMessage, readinessOverride);
        var primaryAction = ResolvePrimaryAction(state.PrimaryActionLabel, actions);
        var secondaryActions = new List<ActionItemViewModel>();

        if (!ReferenceEquals(primaryAction, actions.OpenWorkspace)
            && !string.Equals(state.PrimaryActionLabel, "View Progress", StringComparison.Ordinal))
        {
            secondaryActions.Add(actions.OpenWorkspace);
        }

        if (state.IsOperationInProgress)
        {
            secondaryActions.Add(actions.OpenFolder);
        }
        else if (string.Equals(state.PrimaryActionLabel, "Troubleshoot Workspace", StringComparison.Ordinal))
        {
            secondaryActions.Add(actions.OpenFolder);
        }
        else if (!ReferenceEquals(primaryAction, actions.RebuildRuntime))
        {
            secondaryActions.Add(actions.OpenFolder);
        }

        if (string.Equals(state.PrimaryActionLabel, "Rebuild Runtime", StringComparison.Ordinal))
        {
            secondaryActions.Add(actions.OpenFolder);
        }

        return new WorkspacePresentation
        {
            Headline = state.Headline,
            Summary = state.Summary,
            CurrentStatus = state.CurrentStatus,
            CurrentActivity = state.CurrentActivity,
            ActivitySummary = state.ActivitySummary,
            Recommendation = state.Recommendation,
            CapabilitiesSummary = state.CapabilitiesSummary,
            ApplicationsSummary = state.ApplicationsSummary,
            DevelopmentEnvironmentSummary = state.DevelopmentEnvironmentSummary,
            ServicesSummary = state.ServicesSummary,
            RecentHistoryNote = state.RecentHistoryNote,
            PrimaryAction = primaryAction,
            SecondaryActions = secondaryActions,
            AdvancedActions = actions.AdvancedActions,
        };
    }

    public static WorkspaceAggregatedState BuildState(WorkspaceSummaryViewModel workspace, bool isOperationInProgress, string currentOperationName, string currentStatusMessage, WorkspaceReadinessSnapshot? readinessOverride = null)
    {
        if (workspace.IsLoading)
        {
            return new WorkspaceAggregatedState
            {
                Headline = "Checking workspace",
                Summary = "Loading current workspace state.",
                CurrentStatus = "Checking",
                CurrentActivity = "Checking workspace",
                ActivitySummary = "Loading current workspace state.",
                Recommendation = "Open Workspace.",
                DevelopmentEnvironmentSummary = string.Empty,
                ServicesSummary = string.Empty,
                RecentHistoryNote = string.Empty,
                IsOperationInProgress = false,
                PrimaryActionLabel = "Open Workspace",
            };
        }

        if (workspace.Snapshot is null)
        {
            return new WorkspaceAggregatedState
            {
                Headline = "Discovery Failed",
                Summary = "Workspace details could not be loaded. Run Diagnostics or Refresh to continue.",
                CurrentStatus = "Discovery Failed",
                CurrentActivity = "None",
                ActivitySummary = "No active workspace operation.",
                Recommendation = "Run Diagnostics.",
                DevelopmentEnvironmentSummary = string.Empty,
                ServicesSummary = string.Empty,
                RecentHistoryNote = string.Empty,
                IsOperationInProgress = false,
                PrimaryActionLabel = "Run Diagnostics",
            };
        }

        var readiness = readinessOverride ?? workspace.Snapshot.Readiness;
        if (readiness is null)
        {
            throw new InvalidOperationException($"Workspace '{workspace.Name}' does not have a readiness snapshot.");
        }

        return BuildStateFromReadiness(workspace, readiness);
    }

    internal static string? TryExtractRecommendedActionLabel(string recommendation)
    {
        if (string.IsNullOrWhiteSpace(recommendation))
        {
            return null;
        }

        foreach (var label in new[] { "Open Development Shell", "Open Workspace", "Troubleshoot Workspace", "Retry Provisioning", "Rebuild Runtime", "Run Diagnostics", "View Progress", "Retry" })
        {
            if (recommendation.Contains(label, StringComparison.OrdinalIgnoreCase))
            {
                return label;
            }
        }

        return null;
    }

    private static WorkspaceAggregatedState BuildStateFromReadiness(WorkspaceSummaryViewModel workspace, WorkspaceReadinessSnapshot readiness)
    {
        var primaryActionLabel = WorkspaceReadinessPresentationFormatter.FormatPrimaryActionLabel(readiness);
        return new WorkspaceAggregatedState
        {
            Headline = WorkspaceReadinessPresentationFormatter.FormatHeadline(readiness, workspace),
            Summary = readiness.Summary,
            CurrentStatus = WorkspaceReadinessPresentationFormatter.FormatStatusLabel(readiness, workspace),
            CurrentActivity = WorkspaceReadinessPresentationFormatter.FormatActivityLabel(readiness.CurrentActivity),
            ActivitySummary = readiness.IsOperationInProgress ? readiness.Summary : "No active workspace operation.",
            Recommendation = BuildRecommendation(readiness, primaryActionLabel),
            CapabilitiesSummary = BuildCapabilitiesSummary(readiness.Capabilities),
            ApplicationsSummary = BuildApplicationsSummary(readiness.Capabilities),
            DevelopmentEnvironmentSummary = BuildDevelopmentEnvironmentSummary(readiness.AttentionItems),
            ServicesSummary = string.Empty,
            RecentHistoryNote = BuildRecentHistoryNote(workspace, readiness.IsOperationInProgress),
            IsOperationInProgress = readiness.IsOperationInProgress,
            PrimaryActionLabel = primaryActionLabel,
        };
    }

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

    private static string BuildRecommendation(WorkspaceReadinessSnapshot readiness, string primaryActionLabel)
    {
        var attention = readiness.AttentionItems
            .Where(item => !string.IsNullOrWhiteSpace(item.RecommendedActionLabel))
            .OrderByDescending(item => item.Severity)
            .FirstOrDefault();
        if (attention is not null)
        {
            var label = attention.RecommendedActionLabel.TrimEnd('.');
            return label + ".";
        }

        return primaryActionLabel + ".";
    }

    private static ActionItemViewModel ResolvePrimaryAction(string primaryActionLabel, WorkspacePresentationActions actions)
        => primaryActionLabel switch
        {
            "Troubleshoot Workspace" => actions.TroubleshootWorkspace,
            "View Progress" => CloneAction(actions.OpenWorkspace, "View Progress"),
            "Run Diagnostics" => CloneAction(actions.TroubleshootWorkspace, "Run Diagnostics"),
            "Retry Provisioning" => actions.RetryProvisioning,
            "Rebuild Runtime" => actions.RebuildRuntime,
            "Open Development Shell" => actions.OpenDevelopmentShell,
            _ => actions.OpenWorkspace,
        };

    private static ActionItemViewModel CloneAction(ActionItemViewModel source, string label)
        => new(label, source.Description, source.IsEnabled, source.DisabledReason, source.Command);

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
    public string Headline { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string CurrentStatus { get; init; } = string.Empty;
    public string CurrentActivity { get; init; } = string.Empty;
    public string ActivitySummary { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string CapabilitiesSummary { get; init; } = string.Empty;
    public string ApplicationsSummary { get; init; } = string.Empty;
    public string DevelopmentEnvironmentSummary { get; init; } = string.Empty;
    public string ServicesSummary { get; init; } = string.Empty;
    public string RecentHistoryNote { get; init; } = string.Empty;
    public bool IsOperationInProgress { get; init; }
    public string PrimaryActionLabel { get; init; } = string.Empty;
}

internal sealed class WorkspacePresentationActions
{
    public required ActionItemViewModel OpenWorkspace { get; init; }
    public required ActionItemViewModel OpenDevelopmentShell { get; init; }
    public required ActionItemViewModel RetryProvisioning { get; init; }
    public required ActionItemViewModel RebuildRuntime { get; init; }
    public required ActionItemViewModel TroubleshootWorkspace { get; init; }
    public required ActionItemViewModel OpenFolder { get; init; }
    public required IReadOnlyList<ActionItemViewModel> AdvancedActions { get; init; }
}
