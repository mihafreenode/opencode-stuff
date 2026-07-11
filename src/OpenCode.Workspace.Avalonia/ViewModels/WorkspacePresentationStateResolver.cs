using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Avalonia.ViewModels;

internal sealed class WorkspacePresentationStateResolver
{
    public WorkspacePresentationState Resolve(WorkspacePresentationResolutionContext context)
    {
        var status = ResolveStatus(context);
        var statusLabel = ResolveStatusLabel(context, status);
        var tone = ResolveTone(context, status);
        var primaryAction = ResolvePrimaryAction(context, status);
        var secondaryActions = ResolveSecondaryActions(context, primaryAction);
        var advancedActions = ResolveAdvancedActions(context, status);
        var services = ResolveServices(context, status);

        return new WorkspacePresentationState(
            status,
            tone,
            statusLabel,
            ResolveSummary(context),
            ResolveRecommendation(context, primaryAction),
            context.IsOperationRunning,
            context.CurrentOperationName,
            context.CurrentOperationStatus,
            primaryAction,
            secondaryActions,
            advancedActions,
            services,
            context.EffectiveReadiness,
            statusLabel,
            ResolveCurrentActivity(context),
            ResolveActivitySummary(context),
            context.AggregatedState.CapabilitiesSummary,
            context.AggregatedState.ApplicationsSummary,
            context.AggregatedState.DevelopmentEnvironmentSummary,
            context.AggregatedState.ServicesSummary,
            context.AggregatedState.RecentHistoryNote);
    }

    private static WorkspacePresentationStatusKind ResolveStatus(WorkspacePresentationResolutionContext context)
    {
        if (context.Workspace.IsLoading)
        {
            return WorkspacePresentationStatusKind.Checking;
        }

        if (!context.Workspace.HasSnapshot)
        {
            return WorkspacePresentationStatusKind.Invalid;
        }

        if (context.IsOperationRunning || context.EffectiveReadiness?.IsOperationInProgress == true)
        {
            return WorkspacePresentationStatusKind.Provisioning;
        }

        if (context.EffectiveReadiness?.Status == WorkspaceReadinessStatus.ProvisioningFailed)
        {
            return WorkspacePresentationStatusKind.ProvisioningFailed;
        }

        if (context.EffectiveReadiness?.Status == WorkspaceReadinessStatus.NeedsRebuild)
        {
            return WorkspacePresentationStatusKind.NeedsRebuild;
        }

        if (context.IsFreshWorkspace)
        {
            return WorkspacePresentationStatusKind.Provisioning;
        }

        if (context.RequiresPreparation)
        {
            return WorkspacePresentationStatusKind.NeedsRecovery;
        }

        if (context.Workspace.Snapshot?.RuntimeState == WorkspaceRuntimeState.Stopped)
        {
            return WorkspacePresentationStatusKind.Stopped;
        }

        if (context.EffectiveReadiness?.Status == WorkspaceReadinessStatus.Ready)
        {
            return WorkspacePresentationStatusKind.Ready;
        }

        return WorkspacePresentationStatusKind.Unavailable;
    }

    private static WorkspacePresentationTone ResolveTone(WorkspacePresentationResolutionContext context, WorkspacePresentationStatusKind status)
        => status switch
        {
            WorkspacePresentationStatusKind.Unavailable when IsPartiallyReady(context) => WorkspacePresentationTone.Warning,
            WorkspacePresentationStatusKind.Ready when IsPartiallyReady(context) => WorkspacePresentationTone.Warning,
            WorkspacePresentationStatusKind.Ready => WorkspacePresentationTone.Ready,
            WorkspacePresentationStatusKind.Stopped => WorkspacePresentationTone.Warning,
            WorkspacePresentationStatusKind.Provisioning => WorkspacePresentationTone.Warning,
            WorkspacePresentationStatusKind.ProvisioningFailed => WorkspacePresentationTone.Warning,
            WorkspacePresentationStatusKind.NeedsRebuild => WorkspacePresentationTone.Warning,
            WorkspacePresentationStatusKind.NeedsRecovery => WorkspacePresentationTone.Warning,
            _ => WorkspacePresentationTone.Unavailable,
        };

    private static string ResolveStatusLabel(WorkspacePresentationResolutionContext context, WorkspacePresentationStatusKind status)
        => status switch
        {
            WorkspacePresentationStatusKind.Checking => "Checking",
            WorkspacePresentationStatusKind.Provisioning => "Provisioning",
            WorkspacePresentationStatusKind.ProvisioningFailed => "Provisioning Failed",
            WorkspacePresentationStatusKind.Stopped => "Stopped",
            WorkspacePresentationStatusKind.NeedsRebuild => "Needs Rebuild",
            WorkspacePresentationStatusKind.NeedsRecovery => "Needs Preparation",
            WorkspacePresentationStatusKind.Invalid => "Discovery Failed",
            WorkspacePresentationStatusKind.Unavailable when IsPartiallyReady(context) => "Workspace Partially Ready",
            WorkspacePresentationStatusKind.Ready when IsPartiallyReady(context) => "Workspace Partially Ready",
            WorkspacePresentationStatusKind.Ready => "Workspace Ready",
            _ => "Unavailable",
        };

    private static string ResolveSummary(WorkspacePresentationResolutionContext context)
    {
        if (context.Workspace.HasTransientOperationFailure && !context.IsOperationRunning)
        {
            return SummarizeTransientOperationMessage(context.Workspace.LastActivity);
        }

        if (context.IsOperationRunning)
        {
            return "Preparing workspace. This may take several minutes.";
        }

        if (context.IsFreshWorkspace)
        {
            return "Open Workspace will prepare the runtime and open the terminal.";
        }

        if (context.Workspace.Snapshot?.LocalRuntimeState is null)
        {
            return "Open Workspace can safely regenerate runtime state.";
        }

        if (context.Workspace.Snapshot?.UpdateRequired == true || context.Workspace.Snapshot?.AppliedState is null)
        {
            return "Open Workspace will repair safe runtime issues automatically before opening the terminal.";
        }

        if (!string.IsNullOrWhiteSpace(context.Workspace.Record.LastOperationResult)
            && context.EffectiveReadiness?.Status == WorkspaceReadinessStatus.Ready
            && context.Workspace.Record.LastOperationSucceeded == true
            && !IsReadinessTrackedOperation(context.Workspace.Record.LastOperationName ?? string.Empty))
        {
            return context.Workspace.Record.LastOperationResult!;
        }

        return context.AggregatedState.Summary;
    }

    private static string ResolveRecommendation(WorkspacePresentationResolutionContext context, WorkspacePresentedAction? primaryAction)
    {
        var attention = context.EffectiveReadiness?.AttentionItems
            .Where(item => !string.IsNullOrWhiteSpace(item.RecommendedActionLabel))
            .OrderByDescending(item => item.Severity)
            .FirstOrDefault();
        if (attention is not null)
        {
            return attention.RecommendedActionLabel.TrimEnd('.') + ".";
        }

        return primaryAction is null ? string.Empty : primaryAction.Label + ".";
    }

    private static string ResolveCurrentActivity(WorkspacePresentationResolutionContext context)
    {
        if (context.Workspace.IsLoading)
        {
            return "Checking workspace";
        }

        if (!context.Workspace.HasSnapshot)
        {
            return "None";
        }

        if (context.IsOperationRunning)
        {
            return "Provisioning";
        }

        return string.IsNullOrWhiteSpace(context.AggregatedState.CurrentActivity) ? "None" : context.AggregatedState.CurrentActivity;
    }

    private static string ResolveActivitySummary(WorkspacePresentationResolutionContext context)
    {
        if (context.Workspace.IsLoading)
        {
            return "Loading current workspace state.";
        }

        if (!context.Workspace.HasSnapshot)
        {
            return "No active workspace operation.";
        }

        if (context.IsOperationRunning)
        {
            return string.IsNullOrWhiteSpace(context.CurrentOperationStatus)
                ? "Preparing workspace. This may take several minutes."
                : context.CurrentOperationStatus;
        }

        return string.IsNullOrWhiteSpace(context.AggregatedState.ActivitySummary)
            ? "No active workspace operation."
            : context.AggregatedState.ActivitySummary;
    }

    private static WorkspacePresentedAction? ResolvePrimaryAction(WorkspacePresentationResolutionContext context, WorkspacePresentationStatusKind status)
        => status switch
        {
            WorkspacePresentationStatusKind.Invalid => CreateAction(context, WorkspacePresentedActionKind.RunDiagnostics, enabled: true, disabledReason: string.Empty, isPrimary: true),
            WorkspacePresentationStatusKind.Provisioning => CreateOperationBlockedAction(context, WorkspacePresentedActionKind.OpenWorkspace, isPrimary: true),
            WorkspacePresentationStatusKind.ProvisioningFailed => CreateAction(context, WorkspacePresentedActionKind.RetryProvisioning, CanPrepareWorkspace(context), GetRetryProvisioningDisabledReason(context), isPrimary: true),
            WorkspacePresentationStatusKind.NeedsRebuild => CreateAction(context, WorkspacePresentedActionKind.RebuildRuntime, CanRebuildRuntime(context), GetRebuildRuntimeDisabledReason(context), isPrimary: true),
            _ => CreateAction(context, WorkspacePresentedActionKind.OpenWorkspace, CanStartWorkspace(context.Workspace), GetOpenWorkspaceDisabledReason(context), isPrimary: true),
        };

    private static IReadOnlyList<WorkspacePresentedAction> ResolveSecondaryActions(WorkspacePresentationResolutionContext context, WorkspacePresentedAction? primaryAction)
    {
        var actions = new List<WorkspacePresentedAction>();
        if (primaryAction is null)
        {
            return actions;
        }

        if (!context.Workspace.HasSnapshot)
        {
            actions.Add(CreateAction(context, WorkspacePresentedActionKind.Refresh, !context.IsWorkspaceActionBlocked, context.IsWorkspaceActionBlocked ? context.WorkspaceActionBlockedReason : string.Empty));
            return actions;
        }

        if (primaryAction.Kind != WorkspacePresentedActionKind.OpenWorkspace)
        {
            actions.Add(CreateAction(context, WorkspacePresentedActionKind.OpenWorkspace, CanStartWorkspace(context.Workspace), GetOpenWorkspaceDisabledReason(context)));
        }

        actions.Add(CreateAction(context, WorkspacePresentedActionKind.OpenFolder, enabled: true, disabledReason: string.Empty));
        return actions;
    }

    private static IReadOnlyList<WorkspacePresentedAction> ResolveAdvancedActions(WorkspacePresentationResolutionContext context, WorkspacePresentationStatusKind status)
    {
        if (!context.Workspace.HasSnapshot)
        {
            return
            [
                CreateAction(context, WorkspacePresentedActionKind.RunDiagnostics, enabled: true, disabledReason: string.Empty),
                CreateAction(context, WorkspacePresentedActionKind.OpenFolder, enabled: true, disabledReason: string.Empty),
                CreateAction(context, WorkspacePresentedActionKind.Remove, CanRemoveWorkspace(context), GetRemoveDisabledReason(context)),
            ];
        }

        var actions = new List<WorkspacePresentedAction>();
        if (status == WorkspacePresentationStatusKind.ProvisioningFailed)
        {
            actions.Add(CreateAction(context, WorkspacePresentedActionKind.RetryProvisioning, CanPrepareWorkspace(context), GetRetryProvisioningDisabledReason(context)));
        }

        if (ShouldShowRebuildRuntime(context, status))
        {
            actions.Add(CreateAction(context, WorkspacePresentedActionKind.RebuildRuntime, CanRebuildRuntime(context), GetRebuildRuntimeDisabledReason(context)));
        }

        if (CanRetryWorkspace(context) && status != WorkspacePresentationStatusKind.ProvisioningFailed)
        {
            actions.Add(CreateAction(context, WorkspacePresentedActionKind.Retry, enabled: true, disabledReason: string.Empty));
        }

        if (context.IsOracleApexMediaMissing)
        {
            actions.Add(CreateAction(context, WorkspacePresentedActionKind.OpenDownloadFolder, enabled: true, disabledReason: string.Empty));
            actions.Add(CreateAction(context, WorkspacePresentedActionKind.OpenOracleDownloadPage, enabled: true, disabledReason: string.Empty));
        }

        actions.Add(CreateAction(context, WorkspacePresentedActionKind.RunDiagnostics, CanRunDiagnostics(context), GetRunDiagnosticsDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.StartOnly, CanStartWorkspace(context.Workspace), GetStartDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.AttachOnly, CanAttachWorkspace(context), GetAttachDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.Validate, CanRunSynchronization(context), GetSynchronizationDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.Export, CanRunSynchronization(context), GetSynchronizationDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.Import, CanRunSynchronization(context), GetSynchronizationDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.Synchronize, CanRunSynchronization(context), GetSynchronizationDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.ShowDiff, CanRunSynchronization(context), GetSynchronizationDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.PullChanges, CanRunSynchronization(context), GetSynchronizationDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.PushChanges, CanRunSynchronization(context), GetSynchronizationDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.PlanApexlangChange, CanPlanApexlangChange(context), GetPlanApexlangDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.CreateApplication, enabled: false, disabledReason: "Create Application is not available yet."));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.ConnectExistingApplication, CanConnectExistingApplication(context), GetConnectExistingApplicationDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.SavePoint, CanCreateSavePoint(context), GetSavePointDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.Checkpoint, CanCreateCheckpoint(context), GetCheckpointDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.Backup, CanBackupWorkspace(context), GetBackupDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.Publish, CanPublishWorkspace(context), GetPublishDisabledReason(context)));
        actions.Add(CreateAction(context, WorkspacePresentedActionKind.Remove, CanRemoveWorkspace(context), GetRemoveDisabledReason(context)));
        return actions;
    }

    private static IReadOnlyList<WorkspacePresentedService> ResolveServices(WorkspacePresentationResolutionContext context, WorkspacePresentationStatusKind status)
    {
        var reason = ResolveServiceUnavailableReason(status, context);
        var isAvailable = status == WorkspacePresentationStatusKind.Ready || IsPartiallyReady(context);
        return context.ServiceCandidates
            .Select(candidate =>
            {
                var actions = new List<WorkspacePresentedServiceAction>();
                AddServiceAction(actions, WorkspacePresentedServiceActionKind.Open, "Open", candidate.CanOpen, isAvailable, reason);
                AddServiceAction(actions, WorkspacePresentedServiceActionKind.CopyUrl, "Copy URL", context.HasClipboardService && !string.IsNullOrWhiteSpace(candidate.HostUrl), isAvailable, reason);
                AddServiceAction(actions, WorkspacePresentedServiceActionKind.CopyCredentials, "Copy Credentials", context.HasClipboardService && !string.IsNullOrWhiteSpace(candidate.Credentials), isAvailable, reason);
                AddServiceAction(actions, WorkspacePresentedServiceActionKind.CopyCommand, "Copy Command", context.HasClipboardService && !string.IsNullOrWhiteSpace(candidate.PrimaryCommand), isAvailable, reason);
                AddServiceAction(actions, WorkspacePresentedServiceActionKind.OpenDocumentation, "Documentation", !string.IsNullOrWhiteSpace(candidate.DocsPath), isAvailable, reason);

                return new WorkspacePresentedService(
                    candidate.Service,
                    candidate.Category,
                    candidate.Description,
                    isAvailable ? "Ready" : reason,
                    isAvailable ? WorkspacePresentationTone.Ready : status is WorkspacePresentationStatusKind.Provisioning or WorkspacePresentationStatusKind.Stopped or WorkspacePresentationStatusKind.NeedsRebuild or WorkspacePresentationStatusKind.NeedsRecovery or WorkspacePresentationStatusKind.ProvisioningFailed ? WorkspacePresentationTone.Warning : WorkspacePresentationTone.Unavailable,
                    isAvailable,
                    isAvailable ? string.Empty : reason,
                    !string.IsNullOrWhiteSpace(candidate.HostUrl) ? candidate.HostUrl : string.IsNullOrWhiteSpace(candidate.PrimaryCommand) ? "Open Workspace" : candidate.PrimaryCommand,
                    candidate.Credentials,
                    candidate.DocsPath,
                    actions);
            })
            .ToList();
    }

    private static void AddServiceAction(List<WorkspacePresentedServiceAction> actions, WorkspacePresentedServiceActionKind kind, string label, bool isVisible, bool servicesAvailable, string unavailableReason)
    {
        if (!isVisible)
        {
            return;
        }

        actions.Add(new WorkspacePresentedServiceAction(kind, label, true, servicesAvailable, servicesAvailable ? string.Empty : unavailableReason));
    }

    private static string ResolveServiceUnavailableReason(WorkspacePresentationStatusKind status, WorkspacePresentationResolutionContext context)
        => status switch
        {
            WorkspacePresentationStatusKind.Provisioning => string.IsNullOrWhiteSpace(context.CurrentOperationStatus) ? "Provisioning is in progress." : context.CurrentOperationStatus,
            WorkspacePresentationStatusKind.ProvisioningFailed => "Retry Provisioning before opening services.",
            WorkspacePresentationStatusKind.NeedsRebuild => "Rebuild Runtime before opening services.",
            WorkspacePresentationStatusKind.Stopped => "Open Workspace to start the runtime and make services available.",
            WorkspacePresentationStatusKind.NeedsRecovery => "Open Workspace to repair safe runtime issues before opening services.",
            WorkspacePresentationStatusKind.Invalid => "Workspace details could not be loaded. Run Diagnostics first.",
            WorkspacePresentationStatusKind.Unavailable => "Open Workspace or Run Diagnostics before opening services.",
            _ => "Open Workspace before opening services.",
        };

    private static WorkspacePresentedAction CreateAction(WorkspacePresentationResolutionContext context, WorkspacePresentedActionKind kind, bool enabled, string disabledReason, bool isPrimary = false)
        => new(
            kind,
            GetLabel(kind),
            GetDescription(context, kind),
            true,
            enabled,
            enabled ? string.Empty : disabledReason,
            isPrimary);

    private static WorkspacePresentedAction CreateOperationBlockedAction(WorkspacePresentationResolutionContext context, WorkspacePresentedActionKind kind, bool isPrimary)
        => new(
            kind,
            GetLabel(kind),
            GetDescription(context, kind),
            true,
            false,
            string.IsNullOrWhiteSpace(context.CurrentOperationStatus) ? "Provisioning is in progress." : context.CurrentOperationStatus,
            isPrimary);

    private static string GetLabel(WorkspacePresentedActionKind kind)
        => kind switch
        {
            WorkspacePresentedActionKind.Refresh => "Refresh",
            WorkspacePresentedActionKind.OpenWorkspace => "Open Workspace",
            WorkspacePresentedActionKind.RetryProvisioning => "Retry Provisioning",
            WorkspacePresentedActionKind.RebuildRuntime => "Rebuild Runtime",
            WorkspacePresentedActionKind.RunDiagnostics => "Run Diagnostics",
            WorkspacePresentedActionKind.OpenFolder => "Open Folder",
            WorkspacePresentedActionKind.StartOnly => "Start Only",
            WorkspacePresentedActionKind.AttachOnly => "Attach Only",
            WorkspacePresentedActionKind.Retry => "Retry",
            WorkspacePresentedActionKind.Validate => "Validate",
            WorkspacePresentedActionKind.Export => "Export",
            WorkspacePresentedActionKind.Import => "Import",
            WorkspacePresentedActionKind.Synchronize => "Synchronize",
            WorkspacePresentedActionKind.ShowDiff => "Show Diff",
            WorkspacePresentedActionKind.PullChanges => "Pull Changes",
            WorkspacePresentedActionKind.PushChanges => "Push Changes",
            WorkspacePresentedActionKind.PlanApexlangChange => "Plan APEXlang Change",
            WorkspacePresentedActionKind.CreateApplication => "Create Application",
            WorkspacePresentedActionKind.ConnectExistingApplication => "Connect Existing Application",
            WorkspacePresentedActionKind.SavePoint => "Save Point",
            WorkspacePresentedActionKind.Checkpoint => "Checkpoint",
            WorkspacePresentedActionKind.Backup => "Backup",
            WorkspacePresentedActionKind.Publish => "Publish",
            WorkspacePresentedActionKind.Remove => "Remove",
            WorkspacePresentedActionKind.OpenOracleDownloadPage => "Open Oracle Download Page",
            WorkspacePresentedActionKind.OpenDownloadFolder => "Open Download Folder",
            _ => kind.ToString(),
        };

    private static string GetDescription(WorkspacePresentationResolutionContext context, WorkspacePresentedActionKind kind)
        => kind switch
        {
            WorkspacePresentedActionKind.Refresh => "Refresh the workspace list and reload workspace details.",
            WorkspacePresentedActionKind.OpenWorkspace => BuildOpenDescription(context),
            WorkspacePresentedActionKind.RetryProvisioning => "Retry initial runtime provisioning and refresh the workspace state without forcing a rebuild.",
            WorkspacePresentedActionKind.RebuildRuntime => CanRebuildRuntime(context)
                ? "Recreate managed containers and volumes from workspace.yaml while keeping workspace files, history, downloads, docs, and user scripts."
                : "Rebuild Runtime is not available for the current workspace state.",
            WorkspacePresentedActionKind.RunDiagnostics => context.Workspace.IsLoading
                ? "Loading workspace details before diagnostics become available."
                : "Inspect workspace, runtime, Docker, template, and provider diagnostics for this workspace.",
            WorkspacePresentedActionKind.OpenFolder => "Open the workspace folder with the host shell.",
            WorkspacePresentedActionKind.StartOnly => BuildStartDescription(context),
            WorkspacePresentedActionKind.AttachOnly => CanAttachWorkspace(context)
                ? "Advanced action: attach to an already running workspace terminal session."
                : "Workspace root or configuration file is missing, so attach cannot run.",
            WorkspacePresentedActionKind.Retry => BuildRetryDescription(context),
            WorkspacePresentedActionKind.Validate => BuildSynchronizationDescription(context, "validate"),
            WorkspacePresentedActionKind.Export => BuildSynchronizationDescription(context, "export"),
            WorkspacePresentedActionKind.Import => BuildSynchronizationDescription(context, "import"),
            WorkspacePresentedActionKind.Synchronize => BuildSynchronizationDescription(context, "synchronize"),
            WorkspacePresentedActionKind.ShowDiff => BuildSynchronizationDescription(context, "diff"),
            WorkspacePresentedActionKind.PullChanges => BuildSynchronizationDescription(context, "pull"),
            WorkspacePresentedActionKind.PushChanges => BuildSynchronizationDescription(context, "push"),
            WorkspacePresentedActionKind.PlanApexlangChange => "Build a reviewable semantic APEXlang plan before changing application source.",
            WorkspacePresentedActionKind.CreateApplication => "Create a new Oracle APEX application for the configured environment. This flow is still intentionally disabled while connect-first synchronization stabilizes.",
            WorkspacePresentedActionKind.ConnectExistingApplication => "Discover an existing Oracle APEX application, bind it into workspace metadata, export it to source control, and validate the exported source.",
            WorkspacePresentedActionKind.SavePoint => CanCreateSavePoint(context)
                ? "Capture the current local milestone for recovery using the shared Git-backed Save Point flow."
                : "Workspace root or configuration file is missing, so Save Point creation cannot run.",
            WorkspacePresentedActionKind.Checkpoint => CanCreateCheckpoint(context)
                ? "Capture tracked changes and durable untracked files for stronger local recovery than a normal Save Point."
                : "Workspace root or configuration file is missing, so checkpoint creation cannot run.",
            WorkspacePresentedActionKind.Backup => CanBackupWorkspace(context)
                ? "Export a portable zip backup with workspace config, history, mounts, docs, runtime metadata, and tracked repository content."
                : "Workspace root or configuration file is missing, so backup cannot run.",
            WorkspacePresentedActionKind.Publish => CanPublishWorkspace(context)
                ? "Publish committed Working Copy changes to configured remote backup without force-pushing."
                : "Workspace root or configuration file is missing, so publish cannot run.",
            WorkspacePresentedActionKind.Remove => CanRemoveWorkspace(context)
                ? "Remove the workspace from the local index, clean Docker resources, or delete workspace files after permission repair."
                : "Workspace record is unavailable, so removal cannot run.",
            WorkspacePresentedActionKind.OpenOracleDownloadPage => "Open the official Oracle APEX download page because Oracle media must be downloaded manually.",
            WorkspacePresentedActionKind.OpenDownloadFolder => "Open the shared OpenCode Stuff Oracle APEX download cache folder.",
            _ => string.Empty,
        };

    private static string BuildOpenDescription(WorkspacePresentationResolutionContext context)
    {
        if (context.IsWorkspaceActionBlocked)
        {
            return context.WorkspaceActionBlockedReason;
        }

        if (context.Workspace.Snapshot?.AppliedState is null)
        {
            return "Provision the workspace, start containers, and open the terminal session.";
        }

        if (context.Workspace.Snapshot?.RuntimeState == WorkspaceRuntimeState.Running)
        {
            return "Open the running workspace terminal session.";
        }

        if (context.Workspace.Snapshot?.RuntimeState == WorkspaceRuntimeState.Stopped)
        {
            return "Start the workspace runtime and open the terminal session.";
        }

        if (context.Workspace.Snapshot?.LocalRuntimeState is null || context.Workspace.Snapshot?.UpdateRequired == true)
        {
            return "Open Workspace will repair safe runtime issues automatically before opening the terminal.";
        }

        return "Open the workspace and let OpenCode decide what needs to run.";
    }

    private static string BuildStartDescription(WorkspacePresentationResolutionContext context)
    {
        if (context.IsWorkspaceActionBlocked)
        {
            return context.WorkspaceActionBlockedReason;
        }

        if (context.Workspace.Snapshot?.RuntimeState == WorkspaceRuntimeState.Running)
        {
            return "Workspace runtime is already running. Start will re-check runtime readiness.";
        }

        if (context.Workspace.Snapshot?.LocalRuntimeState is null)
        {
            return "Runtime state is missing. Start will regenerate runtime files and bring the workspace online.";
        }

        return "Start the workspace runtime and provision it if generated files are out of date.";
    }

    private static string BuildSynchronizationDescription(WorkspacePresentationResolutionContext context, string operation)
    {
        var environment = context.Workspace.Snapshot?.Synchronization.DefaultEnvironment;
        if (environment is null)
        {
            return "Oracle APEX synchronization is not configured yet.";
        }

        return operation switch
        {
            "validate" => $"Validate the Oracle APEX source at '{environment.SourcePath}' for environment '{environment.EnvironmentName}'.",
            "export" => $"Export Builder changes from Oracle APEX into '{environment.SourcePath}'.",
            "import" => $"Import '{environment.SourcePath}' into Oracle APEX for immediate preview.",
            "pull" => $"Pull Builder changes from Oracle APEX into Git-managed source for '{environment.EnvironmentName}'.",
            "push" => $"Push Git-managed APEX source into Oracle APEX for '{environment.EnvironmentName}'.",
            "diff" => $"Compare Git-managed APEX source with the current Oracle APEX export for '{environment.EnvironmentName}'.",
            _ => context.Workspace.Snapshot?.Synchronization.Summary ?? "Oracle APEX synchronization is not configured yet.",
        };
    }

    private static string BuildRetryDescription(WorkspacePresentationResolutionContext context)
        => string.IsNullOrWhiteSpace(context.RetryOperationName)
            ? "Retry the last failed workspace action."
            : $"Retry the last failed workspace action: {context.RetryOperationName}.";

    private static bool ShouldShowRebuildRuntime(WorkspacePresentationResolutionContext context, WorkspacePresentationStatusKind status)
        => status != WorkspacePresentationStatusKind.Ready
            && status != WorkspacePresentationStatusKind.ProvisioningFailed
            || context.Workspace.Record.LastProvisioningHealth is not null
            || (context.Workspace.Record.LastOperationSucceeded == false && context.EffectiveReadiness?.Status != WorkspaceReadinessStatus.ProvisioningFailed);

    private static bool CanRunDiagnostics(WorkspacePresentationResolutionContext context)
        => !context.Workspace.IsLoading;

    private static bool CanPrepareWorkspace(WorkspacePresentationResolutionContext context)
        => !context.IsWorkspaceActionBlocked && CanStartWorkspace(context.Workspace);

    private static bool CanRebuildRuntime(WorkspacePresentationResolutionContext context)
        => !context.IsWorkspaceActionBlocked
            && CanStartWorkspace(context.Workspace)
            && (context.RepairabilityClassification == WorkspaceRepairability.CleanupRepair
                || string.Equals(context.Workspace.Record.LastProvisioningHealth?.Repairability, WorkspaceRepairability.CleanupRepair.ToString(), StringComparison.Ordinal));

    private static bool CanAttachWorkspace(WorkspacePresentationResolutionContext context)
        => !context.IsWorkspaceActionBlocked && CanStartWorkspace(context.Workspace);

    private static bool CanRunSynchronization(WorkspacePresentationResolutionContext context)
        => !context.IsWorkspaceActionBlocked && context.SupportsSynchronization;

    private static bool CanPlanApexlangChange(WorkspacePresentationResolutionContext context)
        => context.SupportsApexAssistant && !context.IsWorkspaceActionBlocked;

    private static bool CanConnectExistingApplication(WorkspacePresentationResolutionContext context)
        => context.IsOracleApexWorkspace && !context.IsWorkspaceActionBlocked;

    private static bool CanCreateSavePoint(WorkspacePresentationResolutionContext context)
        => context.HasInteractionService && !context.IsWorkspaceActionBlocked && CanStartWorkspace(context.Workspace);

    private static bool CanCreateCheckpoint(WorkspacePresentationResolutionContext context)
        => context.HasInteractionService && !context.IsWorkspaceActionBlocked && CanStartWorkspace(context.Workspace);

    private static bool CanRemoveWorkspace(WorkspacePresentationResolutionContext context)
        => context.HasInteractionService && !context.IsWorkspaceActionBlocked && !string.IsNullOrWhiteSpace(context.Workspace.RootPath);

    private static bool CanPublishWorkspace(WorkspacePresentationResolutionContext context)
        => context.HasInteractionService && !context.IsWorkspaceActionBlocked && CanStartWorkspace(context.Workspace);

    private static bool CanBackupWorkspace(WorkspacePresentationResolutionContext context)
        => context.HasInteractionService && !context.IsWorkspaceActionBlocked && CanStartWorkspace(context.Workspace);

    private static bool CanRetryWorkspace(WorkspacePresentationResolutionContext context)
        => GetRetryExecutionKind(context) is not null;

    internal static WorkspacePresentedActionKind? GetRetryExecutionKind(WorkspacePresentationResolutionContext context)
        => context.RetryOperationName switch
        {
            null => null,
            "Open Workspace" when CanStartWorkspace(context.Workspace) && !context.IsWorkspaceActionBlocked => WorkspacePresentedActionKind.OpenWorkspace,
            "Prepare" when CanStartWorkspace(context.Workspace) && !context.IsWorkspaceActionBlocked => WorkspacePresentedActionKind.OpenWorkspace,
            "Start" when CanStartWorkspace(context.Workspace) && !context.IsWorkspaceActionBlocked => WorkspacePresentedActionKind.StartOnly,
            "Attach" when CanAttachWorkspace(context) => WorkspacePresentedActionKind.AttachOnly,
            "Recover" when CanStartWorkspace(context.Workspace) && context.HasInteractionService && !context.IsWorkspaceActionBlocked => WorkspacePresentedActionKind.RebuildRuntime,
            "Reprovision" when context.Workspace.HasSnapshot && !context.IsWorkspaceActionBlocked => WorkspacePresentedActionKind.OpenWorkspace,
            _ => null,
        };

    private static string GetRunDiagnosticsDisabledReason(WorkspacePresentationResolutionContext context)
        => context.Workspace.IsLoading
            ? "Workspace details are still loading. Troubleshooting will be available when background checks finish."
            : string.Empty;

    private static string GetRetryProvisioningDisabledReason(WorkspacePresentationResolutionContext context)
        => context.IsWorkspaceActionBlocked
            ? context.WorkspaceActionBlockedReason
            : "Retry Provisioning is not available for the current workspace state.";

    private static string GetRebuildRuntimeDisabledReason(WorkspacePresentationResolutionContext context)
        => context.IsWorkspaceActionBlocked
            ? context.WorkspaceActionBlockedReason
            : "Rebuild Runtime is not available for the current workspace state.";

    private static string GetStartDisabledReason(WorkspacePresentationResolutionContext context)
        => context.IsWorkspaceActionBlocked
            ? context.WorkspaceActionBlockedReason
            : "Workspace root or configuration file is missing, so start cannot run.";

    private static string GetOpenWorkspaceDisabledReason(WorkspacePresentationResolutionContext context)
        => context.IsWorkspaceActionBlocked
            ? context.WorkspaceActionBlockedReason
            : "Workspace root or configuration file is missing, so Open Workspace cannot run.";

    private static string GetAttachDisabledReason(WorkspacePresentationResolutionContext context)
        => context.IsWorkspaceActionBlocked
            ? context.WorkspaceActionBlockedReason
            : "Workspace root or configuration file is missing, so attach cannot run.";

    private static string GetSavePointDisabledReason(WorkspacePresentationResolutionContext context)
        => context.IsWorkspaceActionBlocked
            ? context.WorkspaceActionBlockedReason
            : context.HasInteractionService
                ? "Workspace root or configuration file is missing, so Save Point creation cannot run."
                : "Workspace interaction services are unavailable.";

    private static string GetCheckpointDisabledReason(WorkspacePresentationResolutionContext context)
        => context.IsWorkspaceActionBlocked
            ? context.WorkspaceActionBlockedReason
            : context.HasInteractionService
                ? "Workspace root or configuration file is missing, so checkpoint creation cannot run."
                : "Workspace interaction services are unavailable.";

    private static string GetRemoveDisabledReason(WorkspacePresentationResolutionContext context)
        => context.IsWorkspaceActionBlocked
            ? context.WorkspaceActionBlockedReason
            : context.HasInteractionService
                ? "Workspace record is unavailable, so removal cannot run."
                : "Workspace interaction services are unavailable.";

    private static string GetPublishDisabledReason(WorkspacePresentationResolutionContext context)
        => context.IsWorkspaceActionBlocked
            ? context.WorkspaceActionBlockedReason
            : context.HasInteractionService
                ? "Workspace root or configuration file is missing, so publish cannot run."
                : "Workspace interaction services are unavailable.";

    private static string GetBackupDisabledReason(WorkspacePresentationResolutionContext context)
        => context.IsWorkspaceActionBlocked
            ? context.WorkspaceActionBlockedReason
            : context.HasInteractionService
                ? "Workspace root or configuration file is missing, so backup cannot run."
                : "Workspace interaction services are unavailable.";

    private static string GetSynchronizationDisabledReason(WorkspacePresentationResolutionContext context)
        => context.IsWorkspaceActionBlocked
            ? context.WorkspaceActionBlockedReason
            : "Oracle APEX synchronization is not configured. Add oracle.apex environments to workspace.yaml first.";

    private static string GetPlanApexlangDisabledReason(WorkspacePresentationResolutionContext context)
        => context.IsWorkspaceActionBlocked ? context.WorkspaceActionBlockedReason : string.Empty;

    private static string GetConnectExistingApplicationDisabledReason(WorkspacePresentationResolutionContext context)
        => context.IsWorkspaceActionBlocked
            ? context.WorkspaceActionBlockedReason
            : "Connect Existing Application is only available for Oracle APEX workspaces.";

    private static bool IsPartiallyReady(WorkspacePresentationResolutionContext context)
        => context.EffectiveReadiness?.Status == WorkspaceReadinessStatus.Unavailable
            && (context.EffectiveReadiness.Capabilities.Any(item => !item.IsPrimaryWorkSurface && item.State == WorkspaceCapabilityState.Available)
                || context.Workspace.Health?.Services.Any(item => item.Status == WorkspaceHealthStatus.Healthy) == true);

    private static bool CanStartWorkspace(WorkspaceSummaryViewModel? workspace)
    {
        if (workspace is null || workspace.IsLoading)
        {
            return false;
        }

        if (workspace.HasSnapshot)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(workspace.RootPath) || !Directory.Exists(workspace.RootPath))
        {
            return false;
        }

        return File.Exists(WorkspaceRecordPathResolver.GetWorkspaceConfigurationPath(workspace.Record));
    }

    private static bool IsReadinessTrackedOperation(string operationName)
        => operationName is "Open Workspace" or "Start" or "Prepare" or "Reprovision" or "Recover" or "Rebuild Runtime" or "Attach";

    private static string SummarizeTransientOperationMessage(string message)
        => message.Contains('\n', StringComparison.Ordinal) || message.Contains('\r', StringComparison.Ordinal)
            ? ExtractFailureReason(message)
            : message;

    private static string ExtractFailureReason(string failureMessage)
    {
        foreach (var rawLine in failureMessage.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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

            return rawLine;
        }

        return "See Operation Log for the full failure details.";
    }
}

internal sealed record WorkspacePresentationResolutionContext(
    WorkspaceSummaryViewModel Workspace,
    WorkspaceAggregatedState AggregatedState,
    WorkspaceReadinessSnapshot? EffectiveReadiness,
    bool IsWorkspaceActionBlocked,
    string WorkspaceActionBlockedReason,
    bool IsOperationRunning,
    string CurrentOperationName,
    string CurrentOperationStatus,
    bool HasInteractionService,
    bool HasClipboardService,
    bool SupportsApexAssistant,
    bool SupportsSynchronization,
    bool IsOracleApexWorkspace,
    bool IsOracleApexMediaMissing,
    bool IsFreshWorkspace,
    bool RequiresPreparation,
    string? RetryOperationName,
    WorkspaceRepairability? RepairabilityClassification,
    IReadOnlyList<WorkspacePresentedServiceCandidate> ServiceCandidates);

internal sealed record WorkspacePresentedServiceCandidate(
    string ServiceId,
    string Service,
    string Category,
    string Description,
    string HostUrl,
    string PrimaryCommand,
    string Credentials,
    string DocsPath,
    bool CanOpen);
