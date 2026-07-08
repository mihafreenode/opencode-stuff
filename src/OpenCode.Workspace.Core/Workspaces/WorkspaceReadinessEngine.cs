using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public static class WorkspaceReadinessEngine
{
    public static WorkspaceReadinessSnapshot Build(WorkspaceReadinessInput input)
    {
        var snapshot = input.Snapshot;
        var health = input.Health ?? snapshot?.Health;
        var operation = input.Operation ?? new WorkspaceOperationState();
        var activity = DetermineActivity(operation);
        var launchReadinessProblem = HasLaunchReadinessProblem(snapshot, health);
        var isFreshWorkspace = IsFreshWorkspace(snapshot);
        var hostBlocked = IsHostBlocked(snapshot, health);
        var needsRebuild = NeedsRebuild(snapshot, health);
        var canOpenWorkspace = CanOpenWorkspace(snapshot);
        var capabilities = BuildCapabilities(snapshot, health, operation, launchReadinessProblem);
        var attentionItems = BuildAttentionItems(snapshot, health, hostBlocked, needsRebuild, launchReadinessProblem, isFreshWorkspace);

        var status = DetermineStatus(snapshot, operation, hostBlocked, needsRebuild, capabilities);
        var primaryAction = DeterminePrimaryAction(status, operation, hostBlocked, canOpenWorkspace);

        return new WorkspaceReadinessSnapshot
        {
            Status = status,
            CurrentActivity = activity,
            PrimaryAction = primaryAction,
            Summary = BuildSummary(status, activity, snapshot, health, capabilities, hostBlocked, needsRebuild, launchReadinessProblem, isFreshWorkspace),
            Capabilities = capabilities,
            AttentionItems = attentionItems,
            Evidence = BuildEvidenceSections(snapshot, health),
            CanOpenWorkspace = canOpenWorkspace,
            CanRebuildRuntime = needsRebuild,
            IsOperationInProgress = operation.IsInProgress,
        };
    }

    private static WorkspaceReadinessStatus DetermineStatus(
        WorkspaceSnapshot? snapshot,
        WorkspaceOperationState operation,
        bool hostBlocked,
        bool needsRebuild,
        IReadOnlyList<WorkspaceCapabilitySnapshot> capabilities)
    {
        if (operation.IsInProgress)
        {
            return WorkspaceReadinessStatus.Preparing;
        }

        if (needsRebuild)
        {
            return WorkspaceReadinessStatus.NeedsRebuild;
        }

        if (hostBlocked)
        {
            return WorkspaceReadinessStatus.Unavailable;
        }

        return IsPrimaryWorkSurfaceUsable(capabilities)
            ? WorkspaceReadinessStatus.Ready
            : WorkspaceReadinessStatus.Unavailable;
    }

    private static WorkspacePrimaryAction DeterminePrimaryAction(WorkspaceReadinessStatus status, WorkspaceOperationState operation, bool hostBlocked, bool canOpenWorkspace)
    {
        if (status == WorkspaceReadinessStatus.Preparing && operation.IsInProgress)
        {
            return WorkspacePrimaryAction.ViewProgress;
        }

        if (status == WorkspaceReadinessStatus.Ready)
        {
            return WorkspacePrimaryAction.OpenWorkspace;
        }

        if (status == WorkspaceReadinessStatus.NeedsRebuild)
        {
            return WorkspacePrimaryAction.RebuildRuntime;
        }

        if (hostBlocked)
        {
            return WorkspacePrimaryAction.RunDiagnostics;
        }

        return canOpenWorkspace
            ? WorkspacePrimaryAction.OpenWorkspace
            : WorkspacePrimaryAction.RunDiagnostics;
    }

    private static IReadOnlyList<WorkspaceCapabilitySnapshot> BuildCapabilities(WorkspaceSnapshot? snapshot, WorkspaceHealthSnapshot? health, WorkspaceOperationState operation, bool launchReadinessProblem)
    {
        var capabilities = new List<WorkspaceCapabilitySnapshot>
        {
            BuildDevelopmentShellCapability(snapshot, operation, launchReadinessProblem),
        };

        if (snapshot?.Synchronization.IsSupported == true)
        {
            capabilities.Add(BuildSynchronizationCapability(snapshot.Synchronization));
        }

        foreach (var service in health?.Services.Where(item => string.Equals(item.Category, "Application", StringComparison.OrdinalIgnoreCase)) ?? [])
        {
            capabilities.Add(new WorkspaceCapabilitySnapshot
            {
                Key = string.IsNullOrWhiteSpace(service.ServiceId) ? service.Name : service.ServiceId,
                Label = service.Name,
                State = MapCapabilityState(service.Status),
                Summary = service.Summary,
                IsPrimaryWorkSurface = false,
            });
        }

        return capabilities;
    }

    private static WorkspaceCapabilitySnapshot BuildDevelopmentShellCapability(WorkspaceSnapshot? snapshot, WorkspaceOperationState operation, bool launchReadinessProblem)
    {
        var state = operation.IsInProgress
            ? WorkspaceCapabilityState.Preparing
            : launchReadinessProblem
                ? WorkspaceCapabilityState.Unavailable
            : snapshot?.RuntimeState == WorkspaceRuntimeState.Running
                && snapshot.LocalRuntimeState is not null
                && snapshot.AppliedState is not null
                && !snapshot.UpdateRequired
                    ? WorkspaceCapabilityState.Available
                    : WorkspaceCapabilityState.Unavailable;

        return new WorkspaceCapabilitySnapshot
        {
            Key = "development-shell",
            Label = "Development Shell",
            State = state,
            Summary = state switch
            {
                WorkspaceCapabilityState.Available => "Development shell is available.",
                WorkspaceCapabilityState.Preparing => "Development shell is being prepared.",
                _ => "Development shell is not ready yet.",
            },
            IsPrimaryWorkSurface = true,
        };
    }

    private static WorkspaceCapabilitySnapshot BuildSynchronizationCapability(WorkspaceSynchronizationSnapshot synchronization)
    {
        var state = synchronization.State == WorkspaceSynchronizationState.InSync
            ? WorkspaceCapabilityState.Available
            : synchronization.State == WorkspaceSynchronizationState.Unknown
                ? WorkspaceCapabilityState.Preparing
                : WorkspaceCapabilityState.Unavailable;

        return new WorkspaceCapabilitySnapshot
        {
            Key = "oracle-apex-sync",
            Label = "Oracle APEX Sync",
            State = state,
            Summary = synchronization.Summary,
            IsPrimaryWorkSurface = false,
        };
    }

    private static IReadOnlyList<WorkspaceAttentionItem> BuildAttentionItems(WorkspaceSnapshot? snapshot, WorkspaceHealthSnapshot? health, bool hostBlocked, bool needsRebuild, bool launchReadinessProblem, bool isFreshWorkspace)
    {
        var items = new List<WorkspaceAttentionItem>();

        if (health?.DevelopmentEnvironment?.Status is WorkspaceHealthStatus.Attention or WorkspaceHealthStatus.Degraded or WorkspaceHealthStatus.Unavailable)
        {
            items.Add(new WorkspaceAttentionItem
            {
                Key = "development-environment",
                Label = "Development Environment",
                Severity = WorkspaceAttentionSeverity.Attention,
                Summary = health.DevelopmentEnvironment.Summary,
                RecommendedActionLabel = string.IsNullOrWhiteSpace(health.DevelopmentEnvironment.Recommendation) ? "Inspect Development Environment" : health.DevelopmentEnvironment.Recommendation.TrimEnd('.'),
                Scope = WorkspaceAttentionScope.DevelopmentEnvironment,
            });
        }

        if (snapshot?.Synchronization.IsSupported == true
            && snapshot.Synchronization.State is WorkspaceSynchronizationState.GitAhead or WorkspaceSynchronizationState.DeploymentAhead or WorkspaceSynchronizationState.Diverged or WorkspaceSynchronizationState.ValidationFailed)
        {
            items.Add(new WorkspaceAttentionItem
            {
                Key = "oracle-apex-sync",
                Label = "Oracle APEX Sync",
                Severity = snapshot.Synchronization.State is WorkspaceSynchronizationState.Diverged or WorkspaceSynchronizationState.ValidationFailed
                    ? WorkspaceAttentionSeverity.Blocking
                    : WorkspaceAttentionSeverity.Attention,
                Summary = snapshot.Synchronization.Summary,
                RecommendedActionLabel = snapshot.Synchronization.State switch
                {
                    WorkspaceSynchronizationState.GitAhead => "Push Changes",
                    WorkspaceSynchronizationState.DeploymentAhead => "Pull Changes",
                    WorkspaceSynchronizationState.ValidationFailed => "Validate",
                    _ => "Show Diff",
                },
                Scope = WorkspaceAttentionScope.Capability,
            });
        }

        foreach (var service in health?.Services.Where(item => string.Equals(item.Category, "Application", StringComparison.OrdinalIgnoreCase) && item.Status is not WorkspaceHealthStatus.Healthy) ?? [])
        {
            items.Add(new WorkspaceAttentionItem
            {
                Key = string.IsNullOrWhiteSpace(service.ServiceId) ? service.Name : service.ServiceId,
                Label = service.Name,
                Severity = WorkspaceAttentionSeverity.Attention,
                Summary = service.Summary,
                RecommendedActionLabel = $"Investigate {service.Name}",
                Scope = WorkspaceAttentionScope.Capability,
            });
        }

        if (needsRebuild)
        {
            items.Add(new WorkspaceAttentionItem
            {
                Key = "runtime-rebuild",
                Label = "Runtime",
                Severity = WorkspaceAttentionSeverity.Blocking,
                Summary = "Safe open and repair cannot reach a usable workspace state.",
                RecommendedActionLabel = "Rebuild Runtime",
                Scope = WorkspaceAttentionScope.Runtime,
            });
        }
        else if (launchReadinessProblem)
        {
            items.Add(new WorkspaceAttentionItem
            {
                Key = "terminal-readiness",
                Label = "Development Shell",
                Severity = WorkspaceAttentionSeverity.Attention,
                Summary = "Terminal launch is not ready yet even though workspace services are running.",
                RecommendedActionLabel = "Open Workspace",
                Scope = WorkspaceAttentionScope.Capability,
            });
        }
        else if (snapshot?.LocalRuntimeState is null || snapshot?.AppliedState is null || snapshot?.UpdateRequired == true)
        {
            items.Add(new WorkspaceAttentionItem
            {
                Key = "runtime-preparation",
                Label = "Runtime",
                Severity = WorkspaceAttentionSeverity.Info,
                Summary = isFreshWorkspace
                    ? "Open Workspace will prepare the runtime and open the terminal."
                    : snapshot?.LocalRuntimeState is null
                        ? "Open Workspace can safely regenerate runtime state."
                        : "Open Workspace will need to prepare runtime artifacts before work can continue.",
                RecommendedActionLabel = "Open Workspace",
                Scope = WorkspaceAttentionScope.Runtime,
            });
        }

        if (hostBlocked)
        {
            items.Add(new WorkspaceAttentionItem
            {
                Key = "host-blocker",
                Label = "Host",
                Severity = WorkspaceAttentionSeverity.Blocking,
                Summary = "Host prerequisites are blocking workspace readiness.",
                RecommendedActionLabel = "Run Diagnostics",
                Scope = WorkspaceAttentionScope.Host,
            });
        }

        return items;
    }

    private static IReadOnlyList<WorkspaceEvidenceSection> BuildEvidenceSections(WorkspaceSnapshot? snapshot, WorkspaceHealthSnapshot? health)
    {
        var sections = new List<WorkspaceEvidenceSection>();

        if (snapshot is not null)
        {
            sections.Add(new WorkspaceEvidenceSection
            {
                Label = "Workspace",
                Items =
                [
                    new WorkspaceEvidenceItem { Label = "Root path", Value = snapshot.Paths.RootPath },
                    new WorkspaceEvidenceItem { Label = "Runtime state", Value = snapshot.RuntimeState.ToString() },
                    new WorkspaceEvidenceItem { Label = "Runtime state file", Value = snapshot.LocalRuntimeState is null ? "Missing" : "Present" },
                    new WorkspaceEvidenceItem { Label = "Applied state", Value = snapshot.AppliedState is null ? "Missing" : "Present" },
                ],
            });

            if (snapshot.Synchronization.IsSupported)
            {
                sections.Add(new WorkspaceEvidenceSection
                {
                    Label = "Synchronization",
                    Items =
                    [
                        new WorkspaceEvidenceItem { Label = "State", Value = snapshot.Synchronization.State.ToString() },
                        new WorkspaceEvidenceItem { Label = "Environment", Value = snapshot.Synchronization.DefaultEnvironment?.EnvironmentName ?? "default" },
                        new WorkspaceEvidenceItem { Label = "Mode", Value = snapshot.Synchronization.DefaultEnvironment?.SyncMode ?? WorkspaceSynchronizationModes.Manual },
                        new WorkspaceEvidenceItem { Label = "Source", Value = snapshot.Synchronization.DefaultEnvironment?.SourcePath ?? string.Empty },
                        new WorkspaceEvidenceItem { Label = "Sync metadata", Value = snapshot.Paths.ApexMetadataPath },
                    ],
                });
            }
        }

        if (health is not null)
        {
            sections.Add(new WorkspaceEvidenceSection
            {
                Label = "Providers",
                Items = health.Providers.Select(provider => new WorkspaceEvidenceItem
                {
                    Label = provider.DisplayName,
                    Value = provider.Status.ToString(),
                }).ToList(),
            });

            sections.Add(new WorkspaceEvidenceSection
            {
                Label = "Applications",
                Items = health.Services
                    .Where(item => string.Equals(item.Category, "Application", StringComparison.OrdinalIgnoreCase))
                    .Select(service => new WorkspaceEvidenceItem
                    {
                        Label = service.Name,
                        Value = service.Status.ToString(),
                    })
                    .ToList(),
            });

            if (health.DevelopmentEnvironment is not null)
            {
                sections.Add(new WorkspaceEvidenceSection
                {
                    Label = "Development Environment",
                    Items = health.DevelopmentEnvironment.Checks.Select(check => new WorkspaceEvidenceItem
                    {
                        Label = check.Name,
                        Value = check.Status,
                    }).ToList(),
                });
            }
        }

        return sections;
    }

    private static string BuildSummary(WorkspaceReadinessStatus status, WorkspaceActivity activity, WorkspaceSnapshot? snapshot, WorkspaceHealthSnapshot? health, IReadOnlyList<WorkspaceCapabilitySnapshot> capabilities, bool hostBlocked, bool needsRebuild, bool launchReadinessProblem, bool isFreshWorkspace)
        => status switch
        {
            WorkspaceReadinessStatus.Ready => launchReadinessProblem
                ? BuildLaunchReadinessSummary(health)
                : BuildReadySummary(health, capabilities),
            WorkspaceReadinessStatus.Preparing => activity switch
            {
                WorkspaceActivity.RepairingRuntime => "Repairing runtime.",
                WorkspaceActivity.OpeningTerminal => "Opening terminal.",
                _ => "Preparing workspace. This may take several minutes.",
            },
            WorkspaceReadinessStatus.NeedsRebuild => "Rebuild Runtime will recreate managed containers and volumes while keeping your files.",
            _ => hostBlocked
                ? "Host prerequisites are blocking this workspace. Run Diagnostics to continue."
                : launchReadinessProblem
                    ? BuildLaunchReadinessSummary(health)
                : isFreshWorkspace
                    ? "Open Workspace will prepare the runtime and open the terminal."
                    : snapshot?.LocalRuntimeState is null
                        ? "Open Workspace can safely regenerate runtime state."
                        : "Open Workspace can prepare and open this workspace.",
        };

    private static string BuildReadySummary(WorkspaceHealthSnapshot? health, IReadOnlyList<WorkspaceCapabilitySnapshot> capabilities)
    {
        var available = capabilities.Where(item => item.State == WorkspaceCapabilityState.Available).Select(item => item.Label).ToList();
        var summary = available.Count == 0 ? "Workspace is ready." : $"Available: {string.Join(", ", available)}.";

        if (health?.DevelopmentEnvironment?.Status is WorkspaceHealthStatus.Attention or WorkspaceHealthStatus.Degraded or WorkspaceHealthStatus.Unavailable)
        {
            return string.Concat(summary, " Development environment needs attention.");
        }

        return summary;
    }

    private static string BuildLaunchReadinessSummary(WorkspaceHealthSnapshot? health)
    {
        var availableApplications = health?.Services
            .Where(service => string.Equals(service.Category, "Application", StringComparison.OrdinalIgnoreCase) && service.Status == WorkspaceHealthStatus.Healthy)
            .Select(service => service.Name)
            .ToList() ?? [];
        return availableApplications.Count == 0
            ? "Workspace services are running, but terminal launch is not ready."
            : $"Workspace services are running, but terminal launch is not ready. {string.Join(", ", availableApplications)} are available.";
    }

    private static bool IsPrimaryWorkSurfaceUsable(IReadOnlyList<WorkspaceCapabilitySnapshot> capabilities)
        => capabilities.Any(item => item.IsPrimaryWorkSurface && item.State == WorkspaceCapabilityState.Available);

    private static WorkspaceCapabilityState MapCapabilityState(WorkspaceHealthStatus status)
        => status switch
        {
            WorkspaceHealthStatus.Healthy => WorkspaceCapabilityState.Available,
            WorkspaceHealthStatus.Provisioning or WorkspaceHealthStatus.Investigating => WorkspaceCapabilityState.Preparing,
            _ => WorkspaceCapabilityState.Unavailable,
        };

    private static WorkspaceActivity DetermineActivity(WorkspaceOperationState operation)
    {
        if (!operation.IsInProgress)
        {
            return WorkspaceActivity.None;
        }

        if (operation.StatusMessage.Contains("provision", StringComparison.OrdinalIgnoreCase)
            || operation.StatusMessage.Contains("installing", StringComparison.OrdinalIgnoreCase)
            || operation.StatusMessage.Contains("generating runtime", StringComparison.OrdinalIgnoreCase)
            || string.Equals(operation.OperationName, "Reprovision", StringComparison.Ordinal)
            || string.Equals(operation.OperationName, "Start", StringComparison.Ordinal))
        {
            return WorkspaceActivity.Preparing;
        }

        if (operation.StatusMessage.Contains("repair", StringComparison.OrdinalIgnoreCase)
            || string.Equals(operation.OperationName, "Rebuild Runtime", StringComparison.Ordinal))
        {
            return WorkspaceActivity.RepairingRuntime;
        }

        if (operation.StatusMessage.Contains("terminal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(operation.OperationName, "Attach", StringComparison.Ordinal)
            || (string.Equals(operation.OperationName, "Open Workspace", StringComparison.Ordinal)
                && (operation.StatusMessage.Contains("open", StringComparison.OrdinalIgnoreCase)
                    || operation.StatusMessage.Contains("attach", StringComparison.OrdinalIgnoreCase)
                    || operation.StatusMessage.Contains("terminal", StringComparison.OrdinalIgnoreCase))))
        {
            return WorkspaceActivity.OpeningTerminal;
        }

        if (operation.StatusMessage.Contains("investigat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(operation.OperationName, "Troubleshoot Workspace", StringComparison.Ordinal))
        {
            return WorkspaceActivity.Investigating;
        }

        return WorkspaceActivity.Preparing;
    }

    private static bool NeedsRebuild(WorkspaceSnapshot? snapshot, WorkspaceHealthSnapshot? health)
    {
        if (snapshot is not null
            && snapshot.RuntimeState == WorkspaceRuntimeState.Running
            && snapshot.LocalRuntimeState is not null
            && snapshot.AppliedState is not null
            && !snapshot.UpdateRequired
            && snapshot.Record.LastOperationSucceeded == true)
        {
            return false;
        }

        if (string.Equals(snapshot?.Record.LastProvisioningHealth?.Repairability, WorkspaceRepairability.CleanupRepair.ToString(), StringComparison.Ordinal))
        {
            return true;
        }

        return (health?.Providers ?? [])
            .Any(provider => string.Equals(provider.Repairability, WorkspaceRepairability.CleanupRepair.ToString(), StringComparison.Ordinal));
    }

    private static bool IsHostBlocked(WorkspaceSnapshot? snapshot, WorkspaceHealthSnapshot? health)
    {
        if (string.Equals(snapshot?.Record.LastProvisioningHealth?.ProblemScope, "HostProblem", StringComparison.Ordinal))
        {
            return true;
        }

        return (health?.Providers ?? [])
            .Any(provider => string.Equals(provider.RecommendedAction, "Run Diagnostics.", StringComparison.Ordinal));
    }

    private static bool CanOpenWorkspace(WorkspaceSnapshot? snapshot)
        => snapshot is not null
            && !string.IsNullOrWhiteSpace(snapshot.Paths.RootPath)
            && Directory.Exists(snapshot.Paths.RootPath)
            && File.Exists(snapshot.Paths.WorkspaceYamlPath);

    private static bool HasLaunchReadinessProblem(WorkspaceSnapshot? snapshot, WorkspaceHealthSnapshot? health)
    {
        var message = snapshot?.Record.LastOperationSucceeded == false
            ? snapshot.Record.LastOperationResult ?? snapshot.Record.LastProvisioningHealth?.Reason ?? string.Empty
            : snapshot?.Record.LastProvisioningHealth?.Reason ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var looksLikeLaunchFailure = message.Contains("terminal-ready state", StringComparison.OrdinalIgnoreCase)
            || message.Contains("terminal launch readiness", StringComparison.OrdinalIgnoreCase)
            || message.Contains("could not finish preparing the terminal", StringComparison.OrdinalIgnoreCase)
            || message.Contains("attach scripts and runtime state", StringComparison.OrdinalIgnoreCase);
        return looksLikeLaunchFailure && (health?.Services.Any(item => item.Status == WorkspaceHealthStatus.Healthy) == true || snapshot?.RuntimeState == WorkspaceRuntimeState.Running);
    }

    private static bool IsFreshWorkspace(WorkspaceSnapshot? snapshot)
        => snapshot is not null
            && snapshot.Record.LastPreparedUtc is null
            && snapshot.Record.LastOperationSucceeded == true
            && string.Equals(snapshot.Record.LastOperationName, "Create Workspace", StringComparison.Ordinal);
}
