using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Avalonia.ViewModels;

internal static class WorkspaceHealthAggregator
{
    public static WorkspacePresentation BuildPresentation(
        WorkspaceSummaryViewModel workspace,
        bool isOperationInProgress,
        string currentOperationName,
        string currentStatusMessage,
        WorkspacePresentationActions actions)
    {
        var state = BuildState(workspace, isOperationInProgress, currentOperationName, currentStatusMessage);
        var primaryAction = ResolvePrimaryAction(state.PrimaryActionLabel, actions);
        var secondaryActions = new List<ActionItemViewModel>();

        if (!ReferenceEquals(primaryAction, actions.OpenWorkspace))
        {
            secondaryActions.Add(actions.OpenWorkspace);
        }

        if (string.Equals(state.PrimaryActionLabel, "Troubleshoot Workspace", StringComparison.Ordinal))
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
            ServicesSummary = state.ServicesSummary,
            RecentHistoryNote = state.RecentHistoryNote,
            PrimaryAction = primaryAction,
            SecondaryActions = secondaryActions,
            AdvancedActions = actions.AdvancedActions,
        };
    }

    public static WorkspaceAggregatedState BuildState(WorkspaceSummaryViewModel workspace, bool isOperationInProgress, string currentOperationName, string currentStatusMessage)
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
                ServicesSummary = string.Empty,
                RecentHistoryNote = string.Empty,
                PrimaryActionLabel = "Open Workspace",
            };
        }

        var snapshot = workspace.Snapshot;
        var health = snapshot?.Health;
        var runtimeMissing = snapshot?.LocalRuntimeState is null;
        var runtimeRunning = snapshot?.RuntimeState == WorkspaceRuntimeState.Running;
        var updateRequired = snapshot?.UpdateRequired == true || snapshot?.AppliedState is null;
        var isFreshWorkspace = IsFreshWorkspace(workspace);
        var hasTransientFailure = workspace.HasTransientOperationFailure && !isOperationInProgress;
        var hasLaunchReadinessProblem = HasLaunchReadinessProblem(workspace, snapshot, health, runtimeRunning);
        var needsRebuildRuntime = NeedsRebuildRuntime(workspace, hasLaunchReadinessProblem);
        var capabilities = BuildCapabilities(snapshot, health, runtimeMissing, updateRequired, runtimeRunning, isOperationInProgress, currentOperationName, currentStatusMessage, hasLaunchReadinessProblem);
        var servicesSummary = BuildServicesSummary(health);
        var capabilitiesSummary = BuildCapabilitiesSummary(capabilities);
        var applicationsSummary = BuildApplicationsSummary(capabilities);
        var currentStatus = BuildCurrentStatus(runtimeMissing, updateRequired, runtimeRunning, isFreshWorkspace, needsRebuildRuntime, capabilities);
        var currentActivity = isOperationInProgress
            ? DetermineActivityLabel(currentOperationName, currentStatusMessage)
            : hasTransientFailure
                ? DetermineFailureActivityLabel(workspace.FailedOperationName, hasLaunchReadinessProblem)
                : "None";
        var summary = isOperationInProgress
            ? BuildActivitySummary(currentOperationName, currentStatusMessage)
            : hasTransientFailure
                ? BuildTransientFailureSummary(workspace.TransientOperationSummary, workspace.FailedOperationName, hasLaunchReadinessProblem, servicesSummary, needsRebuildRuntime, capabilities)
            : BuildSummary(snapshot, health, runtimeMissing, updateRequired, runtimeRunning, isFreshWorkspace, servicesSummary, hasLaunchReadinessProblem, needsRebuildRuntime, capabilities);
        var primaryActionLabel = BuildPrimaryActionLabel(snapshot, health, runtimeMissing, runtimeRunning, isFreshWorkspace, hasTransientFailure, workspace.FailedOperationName, hasLaunchReadinessProblem, needsRebuildRuntime, capabilities);
        var recommendation = BuildRecommendation(primaryActionLabel, snapshot, health, capabilities, needsRebuildRuntime);

        return new WorkspaceAggregatedState
        {
            Headline = isOperationInProgress ? currentActivity : currentStatus,
            Summary = summary,
            CurrentStatus = currentStatus,
            CurrentActivity = currentActivity,
            ActivitySummary = isOperationInProgress ? summary : "No active workspace operation.",
            Recommendation = recommendation,
            CapabilitiesSummary = capabilitiesSummary,
            ApplicationsSummary = applicationsSummary,
            ServicesSummary = servicesSummary,
            RecentHistoryNote = BuildRecentHistoryNote(workspace, health, runtimeMissing, isOperationInProgress, isFreshWorkspace),
            PrimaryActionLabel = primaryActionLabel,
        };
    }

    internal static string? TryExtractRecommendedActionLabel(string recommendation)
    {
        if (string.IsNullOrWhiteSpace(recommendation))
        {
            return null;
        }

        foreach (var label in new[] { "Open Development Shell", "Open Workspace", "Troubleshoot Workspace", "Rebuild Runtime", "Run Diagnostics", "Retry" })
        {
            if (recommendation.Contains(label, StringComparison.OrdinalIgnoreCase))
            {
                return label;
            }
        }

        return null;
    }

    private static string BuildCurrentStatus(bool runtimeMissing, bool updateRequired, bool runtimeRunning, bool isFreshWorkspace, bool needsRebuildRuntime, IReadOnlyList<WorkspaceCapabilityState> capabilities)
    {
        if (isFreshWorkspace && !runtimeRunning)
        {
            return "Not Prepared";
        }

        if (runtimeMissing || updateRequired || !runtimeRunning)
        {
            return "Needs Preparation";
        }

        if (needsRebuildRuntime)
        {
            return "Unavailable";
        }

        if (capabilities.Any(item => item.Name == "Development Shell" && item.Availability == WorkspaceCapabilityAvailability.Available))
        {
            return "Workspace Ready";
        }

        if (capabilities.Any(item => item.Availability == WorkspaceCapabilityAvailability.Available))
        {
            return "Workspace Partially Ready";
        }

        return "Unavailable";
    }

    private static string BuildSummary(
        WorkspaceSnapshot? snapshot,
        WorkspaceHealthSnapshot? health,
        bool runtimeMissing,
        bool updateRequired,
        bool runtimeRunning,
        bool isFreshWorkspace,
        string servicesSummary,
        bool hasLaunchReadinessProblem,
        bool needsRebuildRuntime,
        IReadOnlyList<WorkspaceCapabilityState> capabilities)
    {
        if (isFreshWorkspace && !runtimeRunning)
        {
            return "Open Workspace will prepare the runtime and open the terminal.";
        }

        if (needsRebuildRuntime)
        {
            return "Open Workspace tried to repair the runtime automatically, but the workspace is still not ready. Rebuild Runtime will recreate managed containers and volumes while keeping your files.";
        }

        if (hasLaunchReadinessProblem)
        {
            return BuildLaunchReadinessSummary(servicesSummary);
        }

        if (runtimeMissing)
        {
            return "Open Workspace can safely regenerate runtime state.";
        }

        if (capabilities.Any(item => item.Availability == WorkspaceCapabilityAvailability.Available))
        {
            return BuildCapabilityNarrative(capabilities);
        }

        if (runtimeRunning)
        {
            var parts = new List<string> { "Workspace is running." };
            if (!string.IsNullOrWhiteSpace(servicesSummary))
            {
                parts.Add(servicesSummary);
            }

            var freshness = BuildFreshnessNote(health);
            if (!string.IsNullOrWhiteSpace(freshness))
            {
                parts.Add(freshness);
            }

            return string.Join(" ", parts);
        }

        if (updateRequired)
        {
            return "Open Workspace will fix safe runtime issues automatically before opening the terminal.";
        }

        if (!string.IsNullOrWhiteSpace(health?.Summary))
        {
            return health!.Summary;
        }

        return "Open Workspace will start what is needed and hand off to the terminal.";
    }

    private static string BuildServicesSummary(WorkspaceHealthSnapshot? health)
    {
        if (health is null)
        {
            return string.Empty;
        }

        var availableApplications = health.Services
            .Where(service => string.Equals(service.Category, "Application", StringComparison.OrdinalIgnoreCase) && service.Status == WorkspaceHealthStatus.Healthy)
            .Select(service => service.Name)
            .ToList();
        var unavailableApplications = health.Services
            .Where(service => string.Equals(service.Category, "Application", StringComparison.OrdinalIgnoreCase) && service.Status is not WorkspaceHealthStatus.Healthy)
            .Select(service => service.Name)
            .ToList();

        var parts = new List<string>();
        if (availableApplications.Count > 0)
        {
            parts.Add($"{JoinNames(availableApplications)} {(availableApplications.Count == 1 ? "is" : "are")} available.");
        }

        if (unavailableApplications.Count > 0)
        {
            parts.Add($"{JoinNames(unavailableApplications)} {(unavailableApplications.Count == 1 ? "is" : "are")} not available yet.");
        }

        return string.Join(" ", parts);
    }

    private static string BuildRecommendation(string primaryActionLabel, WorkspaceSnapshot? snapshot, WorkspaceHealthSnapshot? health, IReadOnlyList<WorkspaceCapabilityState> capabilities, bool needsRebuildRuntime)
    {
        if (string.Equals(primaryActionLabel, "Open Workspace", StringComparison.Ordinal)
            || string.Equals(primaryActionLabel, "Rebuild Runtime", StringComparison.Ordinal)
            || string.Equals(primaryActionLabel, "Run Diagnostics", StringComparison.Ordinal)
            || string.Equals(primaryActionLabel, "Troubleshoot Workspace", StringComparison.Ordinal))
        {
            return primaryActionLabel + ".";
        }

        if (needsRebuildRuntime)
        {
            return "Rebuild Runtime.";
        }

        var capabilityNeedingAttention = capabilities.FirstOrDefault(item => item.Availability is WorkspaceCapabilityAvailability.Degraded or WorkspaceCapabilityAvailability.Unavailable);
        if (capabilityNeedingAttention is not null)
        {
            return $"Investigate {capabilityNeedingAttention.Name}.";
        }

        if (!string.IsNullOrWhiteSpace(health?.Recommendation) && !string.Equals(health.Recommendation, "Open Workspace.", StringComparison.Ordinal))
        {
            return health.Recommendation;
        }

        return primaryActionLabel + ".";
    }

    private static string BuildRecentHistoryNote(WorkspaceSummaryViewModel workspace, WorkspaceHealthSnapshot? health, bool runtimeMissing, bool isOperationInProgress, bool isFreshWorkspace)
    {
        if (isOperationInProgress || isFreshWorkspace)
        {
            return string.Empty;
        }

        if (health?.OverallStatus is WorkspaceHealthStatus.Healthy or WorkspaceHealthStatus.Attention)
        {
            if (workspace.Record.LastOperationSucceeded == false && !string.IsNullOrWhiteSpace(workspace.Record.LastOperationResult) && !runtimeMissing)
            {
                return $"Recent issue: {ExtractHistoryReason(workspace.Record.LastOperationResult!)}";
            }
        }

        var repair = workspace.Record.LastProvisioningHealth?.RepairHistory.LastOrDefault();
        if (repair is not null && !repair.RootCauseChanged && !string.IsNullOrWhiteSpace(repair.EvidenceAfter))
        {
            return $"Recent issue: {repair.RepairType} had no effect earlier.";
        }

        return string.Empty;
    }

    private static string BuildActivitySummary(string operationName, string currentStatusMessage)
    {
        if (!string.IsNullOrWhiteSpace(currentStatusMessage))
        {
            return currentStatusMessage;
        }

        return string.IsNullOrWhiteSpace(operationName)
            ? "Workspace operation is in progress."
            : $"{operationName} is in progress.";
    }

    private static string BuildFreshnessNote(WorkspaceHealthSnapshot? health)
    {
        if (health is null)
        {
            return string.Empty;
        }

        var volatileTimes = health.Providers
            .Where(provider => provider.IsVolatile)
            .Select(provider => (DateTimeOffset?)provider.Timestamp)
            .Concat(health.Services.Where(service => service.RefreshInterval > TimeSpan.Zero).Select(service => (DateTimeOffset?)service.Timestamp))
            .Where(timestamp => timestamp is not null)
            .Select(timestamp => timestamp!.Value)
            .ToList();
        if (volatileTimes.Count == 0)
        {
            return string.Empty;
        }

        var lastChecked = volatileTimes.Max();
        var age = DateTimeOffset.UtcNow - lastChecked;
        if (age < TimeSpan.FromMinutes(2))
        {
            return string.Empty;
        }

        return $"Last checked {FormatAge(age)} ago.";
    }

    private static string DetermineActivityLabel(string operationName, string currentStatusMessage)
    {
        if (!string.IsNullOrWhiteSpace(currentStatusMessage))
        {
            if (currentStatusMessage.Contains("provision", StringComparison.OrdinalIgnoreCase)
                || currentStatusMessage.Contains("installing", StringComparison.OrdinalIgnoreCase)
                || currentStatusMessage.Contains("generating runtime", StringComparison.OrdinalIgnoreCase))
            {
                return "Provisioning";
            }

            if (currentStatusMessage.Contains("checking workspace", StringComparison.OrdinalIgnoreCase))
            {
                return "Investigating";
            }
        }

        return operationName switch
        {
            "Open Workspace" => "Opening terminal",
            "Start" => "Preparing",
            "Attach" => "Opening terminal",
            "Recover" => "Repairing runtime",
            "Rebuild Runtime" => "Repairing runtime",
            "Reprovision" => "Provisioning",
            _ => string.IsNullOrWhiteSpace(operationName) ? "Working" : operationName,
        };
    }

    private static string DetermineFailureActivityLabel(string? operationName, bool hasLaunchReadinessProblem)
        => hasLaunchReadinessProblem
            ? "Terminal Launch Failed"
            : operationName switch
            {
                "Reprovision" or "Recover" or "Rebuild Runtime" or "Open Workspace" => "Troubleshooting Recommended",
                "Attach" => "Attach Failed",
                "Backup" => "Backup Failed",
                "Create Save Point" => "Save Point Failed",
                "Create Checkpoint" => "Checkpoint Failed",
                _ => "Recent Failure",
            };

    private static bool IsFreshWorkspace(WorkspaceSummaryViewModel workspace)
        => workspace.Record.LastPreparedUtc is null
            && workspace.Record.LastOperationSucceeded == true
            && string.Equals(workspace.Record.LastOperationName, "Create Workspace", StringComparison.Ordinal);

    private static string BuildPrimaryActionLabel(WorkspaceSnapshot? snapshot, WorkspaceHealthSnapshot? health, bool runtimeMissing, bool runtimeRunning, bool isFreshWorkspace, bool hasTransientFailure, string? failedOperationName, bool hasLaunchReadinessProblem, bool needsRebuildRuntime, IReadOnlyList<WorkspaceCapabilityState> capabilities)
    {
        if (isFreshWorkspace || runtimeMissing || !runtimeRunning)
        {
            return "Open Workspace";
        }

        if (needsRebuildRuntime)
        {
            return "Rebuild Runtime";
        }

        if (IsHostFailure(failedOperationName, snapshot))
        {
            return "Open Workspace";
        }

        if (capabilities.Any(item => item.Name == "Development Shell" && item.Availability == WorkspaceCapabilityAvailability.Available))
        {
            return "Open Development Shell";
        }

        if (capabilities.Any(item => item.Availability == WorkspaceCapabilityAvailability.Available))
        {
            return "Open Workspace";
        }

        if (hasLaunchReadinessProblem)
        {
            return capabilities.Any(item => item.Availability == WorkspaceCapabilityAvailability.Available)
                ? "Open Workspace"
                : "Troubleshoot Workspace";
        }

        if (hasTransientFailure && failedOperationName is "Reprovision" or "Recover" or "Rebuild Runtime" or "Open Workspace")
        {
            return "Troubleshoot Workspace";
        }

        return health?.OverallStatus is WorkspaceHealthStatus.Degraded or WorkspaceHealthStatus.Unavailable or WorkspaceHealthStatus.Investigating
            ? "Troubleshoot Workspace"
            : "Open Workspace";
    }

    private static string BuildTransientFailureSummary(string summary, string? failedOperationName, bool hasLaunchReadinessProblem, string servicesSummary, bool needsRebuildRuntime, IReadOnlyList<WorkspaceCapabilityState> capabilities)
    {
        if (needsRebuildRuntime)
        {
            return "Open Workspace tried to repair the runtime automatically, but the workspace is still not ready. Rebuild Runtime will recreate managed containers and volumes while keeping your files.";
        }

        if (hasLaunchReadinessProblem)
        {
            return BuildLaunchReadinessSummary(servicesSummary);
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            return "Workspace action failed.";
        }

        return summary.Contains('\n', StringComparison.Ordinal) || summary.Contains('\r', StringComparison.Ordinal)
            ? ExtractHistoryReason(summary)
            : summary;
    }

    private static ActionItemViewModel ResolvePrimaryAction(string primaryActionLabel, WorkspacePresentationActions actions)
        => primaryActionLabel switch
        {
            "Troubleshoot Workspace" => actions.TroubleshootWorkspace,
            "Rebuild Runtime" => actions.RebuildRuntime,
            "Open Development Shell" => actions.OpenDevelopmentShell,
            _ => actions.OpenWorkspace,
        };

    private static IReadOnlyList<WorkspaceCapabilityState> BuildCapabilities(
        WorkspaceSnapshot? snapshot,
        WorkspaceHealthSnapshot? health,
        bool runtimeMissing,
        bool updateRequired,
        bool runtimeRunning,
        bool isOperationInProgress,
        string currentOperationName,
        string currentStatusMessage,
        bool hasLaunchReadinessProblem)
    {
        var capabilities = new List<WorkspaceCapabilityState>();
        capabilities.Add(new WorkspaceCapabilityState(
            "Development Shell",
            DetermineShellAvailability(runtimeMissing, updateRequired, runtimeRunning, isOperationInProgress, currentOperationName, currentStatusMessage, hasLaunchReadinessProblem),
            "Capability"));

        if (health is not null)
        {
            foreach (var service in health.Services.Where(item => string.Equals(item.Category, "Application", StringComparison.OrdinalIgnoreCase)))
            {
                capabilities.Add(new WorkspaceCapabilityState(service.Name, MapAvailability(service.Status), "Application"));
            }

            var hasApexApplication = capabilities.Any(item => item.Name.Contains("APEX", StringComparison.OrdinalIgnoreCase));
            if (!hasApexApplication)
            {
                var apexProvider = health.Providers.FirstOrDefault(item => string.Equals(item.ProviderKey, "apex", StringComparison.OrdinalIgnoreCase));
                if (apexProvider is not null)
                {
                    capabilities.Add(new WorkspaceCapabilityState("Oracle APEX", MapAvailability(apexProvider.Status), "Application"));
                }
            }
        }

        return capabilities;
    }

    private static WorkspaceCapabilityAvailability DetermineShellAvailability(bool runtimeMissing, bool updateRequired, bool runtimeRunning, bool isOperationInProgress, string currentOperationName, string currentStatusMessage, bool hasLaunchReadinessProblem)
    {
        if (isOperationInProgress
            && (string.Equals(currentOperationName, "Open Workspace", StringComparison.Ordinal)
                || string.Equals(currentOperationName, "Start", StringComparison.Ordinal)
                || string.Equals(currentOperationName, "Attach", StringComparison.Ordinal)
                || currentStatusMessage.Contains("opening terminal", StringComparison.OrdinalIgnoreCase)))
        {
            return WorkspaceCapabilityAvailability.Preparing;
        }

        if (runtimeMissing || updateRequired || !runtimeRunning)
        {
            return WorkspaceCapabilityAvailability.Unavailable;
        }

        return hasLaunchReadinessProblem
            ? WorkspaceCapabilityAvailability.Degraded
            : WorkspaceCapabilityAvailability.Available;
    }

    private static WorkspaceCapabilityAvailability MapAvailability(WorkspaceHealthStatus status)
        => status switch
        {
            WorkspaceHealthStatus.Healthy => WorkspaceCapabilityAvailability.Available,
            WorkspaceHealthStatus.Provisioning or WorkspaceHealthStatus.Investigating => WorkspaceCapabilityAvailability.Preparing,
            WorkspaceHealthStatus.Attention => WorkspaceCapabilityAvailability.Degraded,
            _ => WorkspaceCapabilityAvailability.Unavailable,
        };

    private static string BuildCapabilitiesSummary(IReadOnlyList<WorkspaceCapabilityState> capabilities)
    {
        if (capabilities.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        AppendCapabilityLines(lines, "Available", capabilities, WorkspaceCapabilityAvailability.Available, "- ");
        AppendCapabilityLines(lines, "Attention", capabilities, WorkspaceCapabilityAvailability.Degraded, "- ");
        AppendCapabilityLines(lines, "Preparing", capabilities, WorkspaceCapabilityAvailability.Preparing, "- ");
        AppendCapabilityLines(lines, "Unavailable", capabilities, WorkspaceCapabilityAvailability.Unavailable, "- ");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildApplicationsSummary(IReadOnlyList<WorkspaceCapabilityState> capabilities)
    {
        var applications = capabilities.Where(item => string.Equals(item.Kind, "Application", StringComparison.Ordinal)).ToList();
        if (applications.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        foreach (var application in applications)
        {
            lines.Add($"{FormatCapabilityPrefix(application.Availability)}{application.Name}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildCapabilityNarrative(IReadOnlyList<WorkspaceCapabilityState> capabilities)
    {
        var available = capabilities.Where(item => item.Availability == WorkspaceCapabilityAvailability.Available).Select(item => item.Name).ToList();
        var attention = capabilities.Where(item => item.Availability is WorkspaceCapabilityAvailability.Degraded or WorkspaceCapabilityAvailability.Unavailable).Select(item => item.Name).ToList();
        var parts = new List<string>();
        if (available.Count > 0)
        {
            parts.Add($"Available: {JoinNames(available)}.");
        }

        if (attention.Count > 0)
        {
            parts.Add($"Needs attention: {JoinNames(attention)}.");
        }

        return string.Join(" ", parts);
    }
    private static bool IsHostFailure(string? failedOperationName, WorkspaceSnapshot? snapshot)
        => string.Equals(snapshot?.Record.LastProvisioningHealth?.ProblemScope, "HostProblem", StringComparison.Ordinal)
            || string.Equals(failedOperationName, "Run Diagnostics", StringComparison.Ordinal);

    private static void AppendCapabilityLines(List<string> lines, string heading, IReadOnlyList<WorkspaceCapabilityState> capabilities, WorkspaceCapabilityAvailability availability, string marker)
    {
        var matches = capabilities.Where(item => item.Availability == availability).Select(item => item.Name).ToList();
        if (matches.Count == 0)
        {
            return;
        }

        lines.Add(heading);
        lines.AddRange(matches.Select(item => $"{marker} {item}"));
    }

    private static string FormatCapabilityPrefix(WorkspaceCapabilityAvailability availability)
        => availability switch
        {
            WorkspaceCapabilityAvailability.Available => "Available: ",
            WorkspaceCapabilityAvailability.Degraded => "Attention: ",
            WorkspaceCapabilityAvailability.Preparing => "Preparing: ",
            _ => "Unavailable: ",
        };

    private static bool NeedsRebuildRuntime(WorkspaceSummaryViewModel workspace, bool hasLaunchReadinessProblem)
    {
        if (workspace.Record.LastProvisioningHealth is null)
        {
            return false;
        }

        if (string.Equals(workspace.Record.LastProvisioningHealth.Repairability, WorkspaceRepairability.CleanupRepair.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (hasLaunchReadinessProblem && workspace.Record.LastProvisioningHealth.RepairHistory.LastOrDefault()?.Result == WorkspaceRepairOutcome.RepairNoEffect)
        {
            return true;
        }

        return workspace.Record.LastProvisioningHealth.RecommendedAction.Contains("Rebuild Runtime", StringComparison.OrdinalIgnoreCase)
            || workspace.Record.LastProvisioningHealth.RecommendedAction.Contains("Reset Runtime", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasLaunchReadinessProblem(WorkspaceSummaryViewModel workspace, WorkspaceSnapshot? snapshot, WorkspaceHealthSnapshot? health, bool runtimeRunning)
    {
        if (!runtimeRunning)
        {
            return false;
        }

        var message = workspace.Record.LastOperationSucceeded == false
            ? workspace.Record.LastOperationResult ?? workspace.Record.LastProvisioningHealth?.Reason ?? string.Empty
            : workspace.Record.LastProvisioningHealth?.Reason ?? string.Empty;
        if (!IsLaunchReadinessProblem(message))
        {
            return false;
        }

        return health?.Services.Any(item => item.Status == WorkspaceHealthStatus.Healthy) == true || snapshot?.RuntimeState == WorkspaceRuntimeState.Running;
    }

    private static bool IsLaunchReadinessProblem(string message)
        => !string.IsNullOrWhiteSpace(message)
            && (message.Contains("terminal-ready state", StringComparison.OrdinalIgnoreCase)
                || message.Contains("terminal launch readiness", StringComparison.OrdinalIgnoreCase)
                || message.Contains("could not finish preparing the terminal", StringComparison.OrdinalIgnoreCase)
                || message.Contains("runtime files need repair", StringComparison.OrdinalIgnoreCase)
                || message.Contains("terminal could not be prepared", StringComparison.OrdinalIgnoreCase)
                || message.Contains("attach scripts and runtime state", StringComparison.OrdinalIgnoreCase));

    private static string BuildLaunchReadinessSummary(string servicesSummary)
        => string.IsNullOrWhiteSpace(servicesSummary)
            ? "Workspace services are running, but terminal launch is not ready."
            : $"Workspace services are running, but terminal launch is not ready. {servicesSummary}";

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

            var normalized = NormalizeHistoryReason(rawLine.EndsWith('.') ? rawLine : rawLine + ".");
            return normalized;
        }

        return "Earlier workspace operation failed.";
    }

    private static string NormalizeHistoryReason(string reason)
        => IsLaunchReadinessProblem(reason)
            ? "Terminal launch readiness failed. Troubleshoot Workspace can inspect attach scripts and runtime state."
            : reason.Replace("Run Recover Workspace.", "Open Workspace will try to repair safe runtime issues automatically.", StringComparison.Ordinal);

    private static string JoinNames(IReadOnlyList<string> values)
        => values.Count == 0 ? "No applications" : string.Join(", ", values);

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
        {
            return $"{Math.Max(1, (int)Math.Round(age.TotalSeconds))} seconds";
        }

        if (age.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)Math.Round(age.TotalMinutes))} minutes";
        }

        return $"{Math.Max(1, (int)Math.Round(age.TotalHours))} hours";
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
    public string ServicesSummary { get; init; } = string.Empty;
    public string RecentHistoryNote { get; init; } = string.Empty;
    public string PrimaryActionLabel { get; init; } = string.Empty;
}

internal sealed class WorkspacePresentationActions
{
    public required ActionItemViewModel OpenWorkspace { get; init; }
    public required ActionItemViewModel OpenDevelopmentShell { get; init; }
    public required ActionItemViewModel RebuildRuntime { get; init; }
    public required ActionItemViewModel TroubleshootWorkspace { get; init; }
    public required ActionItemViewModel OpenFolder { get; init; }
    public required IReadOnlyList<ActionItemViewModel> AdvancedActions { get; init; }
}

internal enum WorkspaceCapabilityAvailability
{
    Available,
    Unavailable,
    Preparing,
    Degraded,
}

internal sealed record WorkspaceCapabilityState(string Name, WorkspaceCapabilityAvailability Availability, string Kind);
