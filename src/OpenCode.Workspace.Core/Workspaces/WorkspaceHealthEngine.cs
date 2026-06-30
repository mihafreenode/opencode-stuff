using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public static class WorkspaceHealthEngine
{
    public static WorkspaceHealthSnapshot Build(WorkspaceSnapshot snapshot)
        => BuildAsync(snapshot).GetAwaiter().GetResult();

    public static async Task<WorkspaceHealthSnapshot> BuildAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var providers = new List<WorkspaceProviderHealthSnapshot>
        {
            BuildWorkspaceProvider(snapshot, timestamp),
            BuildRuntimeProvider(snapshot, timestamp),
            BuildContainerProvider(snapshot, timestamp),
            BuildGitProvider(snapshot, timestamp),
        };

        var services = await WorkspaceServiceHealthEngine.BuildAsync(snapshot, cancellationToken: cancellationToken);
        providers.Add(BuildServicesProvider(services, timestamp));

        if (IsOracleWorkspace(snapshot))
        {
            providers.AddRange(BuildOracleProviders(snapshot, services, timestamp));
        }

        var overallStatus = providers.Select(item => item.Status).DefaultIfEmpty(WorkspaceHealthStatus.Healthy).MaxBy(SeverityRank);
        var recommendation = providers
            .OrderByDescending(item => SeverityRank(item.Status))
            .Select(item => item.RecommendedAction)
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
            ?? "Open Workspace.";
        var summary = providers
            .OrderByDescending(item => SeverityRank(item.Status))
            .Select(item => item.WorkspaceImpact)
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
            ?? "Workspace can be opened.";
        var confidence = providers
            .OrderByDescending(item => SeverityRank(item.Status))
            .Select(item => item.Confidence)
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
            ?? "MEDIUM";

        return new WorkspaceHealthSnapshot
        {
            OverallStatus = overallStatus,
            Summary = summary,
            Recommendation = recommendation,
            Confidence = confidence,
            Timestamp = timestamp,
            Providers = providers,
            Services = services,
        };
    }

    private static WorkspaceProviderHealthSnapshot BuildWorkspaceProvider(WorkspaceSnapshot snapshot, DateTimeOffset timestamp)
    {
        var status = File.Exists(snapshot.Paths.WorkspaceYamlPath)
            ? WorkspaceHealthStatus.Healthy
            : WorkspaceHealthStatus.Unavailable;
        return new WorkspaceProviderHealthSnapshot
        {
            ProviderKey = "workspace",
            DisplayName = "Workspace",
            Status = status,
            Summary = status == WorkspaceHealthStatus.Healthy ? "Workspace definition loaded." : "Workspace definition is missing.",
            Evidence =
            [
                new WorkspaceHealthFact { Label = "configuration", Value = File.Exists(snapshot.Paths.WorkspaceYamlPath) ? "workspace.yaml loaded" : "workspace.yaml missing" },
                new WorkspaceHealthFact { Label = "root", Value = snapshot.Paths.RootPath },
            ],
            Confidence = "HIGH",
            Timestamp = timestamp,
            RefreshInterval = TimeSpan.FromMinutes(5),
            Repairability = WorkspaceRepairability.ManualRepair.ToString(),
            RecommendedAction = status == WorkspaceHealthStatus.Healthy ? "Open Workspace." : "Inspect workspace files.",
            IsVolatile = false,
            WorkspaceImpact = status == WorkspaceHealthStatus.Healthy ? "Workspace definition is ready." : "Workspace cannot be opened until the definition is restored.",
        };
    }

    private static WorkspaceProviderHealthSnapshot BuildRuntimeProvider(WorkspaceSnapshot snapshot, DateTimeOffset timestamp)
    {
        var evidence = new List<WorkspaceHealthFact>
        {
            new() { Label = "runtime-state.yaml", Value = snapshot.LocalRuntimeState is null ? "missing" : "generated" },
            new() { Label = "applied-state.yaml", Value = snapshot.AppliedState is null ? "missing" : "generated" },
            new() { Label = "compose.yaml", Value = File.Exists(snapshot.Paths.ComposePath) ? "generated" : "missing" },
            new() { Label = "provision.sh", Value = File.Exists(snapshot.Paths.ProvisionScriptPath) ? "generated" : "missing" },
        };

        var status = snapshot.LocalRuntimeState is null || snapshot.AppliedState is null
            ? WorkspaceHealthStatus.Degraded
            : snapshot.UpdateRequired
                ? WorkspaceHealthStatus.Attention
                : WorkspaceHealthStatus.Healthy;

        return new WorkspaceProviderHealthSnapshot
        {
            ProviderKey = "runtime",
            DisplayName = "Runtime",
            Status = status,
            Summary = status switch
            {
                WorkspaceHealthStatus.Healthy => "Runtime files are generated and current.",
                WorkspaceHealthStatus.Attention => "Runtime files need refresh before the next open.",
                _ => "Managed runtime files are missing or stale.",
            },
            Evidence = evidence,
            Confidence = "HIGH",
            Timestamp = timestamp,
            RefreshInterval = TimeSpan.FromMinutes(5),
            Repairability = WorkspaceRepairability.AutomaticRepair.ToString(),
            RecommendedAction = status switch
            {
                WorkspaceHealthStatus.Healthy => "Open Workspace.",
                WorkspaceHealthStatus.Attention => "Open Workspace.",
                _ => "Open Workspace.",
            },
            IsVolatile = false,
            WorkspaceImpact = status == WorkspaceHealthStatus.Healthy
                ? "Workspace can be opened with the current runtime files."
                : "Open Workspace will need to repair runtime artifacts before you can work.",
        };
    }

    private static WorkspaceProviderHealthSnapshot BuildContainerProvider(WorkspaceSnapshot snapshot, DateTimeOffset timestamp)
    {
        var status = snapshot.RuntimeState switch
        {
            WorkspaceRuntimeState.Running => WorkspaceHealthStatus.Healthy,
            WorkspaceRuntimeState.Stopped => WorkspaceHealthStatus.Attention,
            _ => WorkspaceHealthStatus.Attention,
        };

        return new WorkspaceProviderHealthSnapshot
        {
            ProviderKey = "container",
            DisplayName = "Container",
            Status = status,
            Summary = snapshot.RuntimeState switch
            {
                WorkspaceRuntimeState.Running => "Workspace runtime is running.",
                WorkspaceRuntimeState.Stopped => "Workspace runtime is stopped.",
                _ => "Workspace runtime has not been confirmed yet.",
            },
            Evidence =
            [
                new WorkspaceHealthFact { Label = "runtime", Value = snapshot.RuntimeState.ToString() },
                new WorkspaceHealthFact { Label = "session", Value = snapshot.Session.State.ToString() },
            ],
            Confidence = "MEDIUM",
            Timestamp = timestamp,
            RefreshInterval = TimeSpan.FromSeconds(30),
            Repairability = WorkspaceRepairability.AutomaticRepair.ToString(),
            RecommendedAction = snapshot.RuntimeState == WorkspaceRuntimeState.Running ? "Open Workspace." : "Open Workspace.",
            IsVolatile = true,
            WorkspaceImpact = snapshot.RuntimeState == WorkspaceRuntimeState.Running
                ? "Terminal attach should be available."
                : "Workspace can still be opened, but the runtime will need to start first.",
        };
    }

    private static WorkspaceProviderHealthSnapshot BuildGitProvider(WorkspaceSnapshot snapshot, DateTimeOffset timestamp)
    {
        var gitStatus = snapshot.Safety.AdvancedGit.StatusSummary;
        var status = snapshot.Safety.OverallStatus switch
        {
            WorkspaceSafetyLevel.AtRisk or WorkspaceSafetyLevel.NeedsReview => WorkspaceHealthStatus.Attention,
            _ => WorkspaceHealthStatus.Healthy,
        };

        return new WorkspaceProviderHealthSnapshot
        {
            ProviderKey = "git",
            DisplayName = "Git",
            Status = status,
            Summary = string.IsNullOrWhiteSpace(gitStatus) ? "Git state loaded." : gitStatus,
            Evidence =
            [
                new WorkspaceHealthFact { Label = "branch", Value = string.IsNullOrWhiteSpace(snapshot.Safety.AdvancedGit.CurrentBranch) ? "unknown" : snapshot.Safety.AdvancedGit.CurrentBranch },
                new WorkspaceHealthFact { Label = "working tree", Value = string.IsNullOrWhiteSpace(gitStatus) ? "unknown" : gitStatus },
            ],
            Confidence = "MEDIUM",
            Timestamp = timestamp,
            RefreshInterval = TimeSpan.FromMinutes(10),
            Repairability = WorkspaceRepairability.Unknown.ToString(),
            RecommendedAction = status == WorkspaceHealthStatus.Healthy ? "Open Workspace." : "Review workspace changes.",
            IsVolatile = false,
            WorkspaceImpact = status == WorkspaceHealthStatus.Healthy ? "Git state should not block normal workspace use." : "Workspace can still be opened, but Git safety needs review.",
        };
    }

    private static IEnumerable<WorkspaceProviderHealthSnapshot> BuildOracleProviders(WorkspaceSnapshot snapshot, IReadOnlyList<WorkspaceServiceHealthSnapshot> services, DateTimeOffset timestamp)
    {
        var investigationHistory = snapshot.Record.LastProvisioningHealth?.InvestigationHistory ?? Array.Empty<WorkspaceInvestigationRecord>();
        var ordsService = services.FirstOrDefault(item => item.ServiceId == "ords");
        var apexService = services.FirstOrDefault(item => item.ServiceId == "apex");
        var databaseService = services.FirstOrDefault(item => item.ServiceId == "oracle-database");
        var oracleInvestigation = investigationHistory.LastOrDefault(item => string.Equals(item.ProviderName, "Oracle", StringComparison.Ordinal));
        var oracleEvidence = databaseService?.Evidence.FirstOrDefault()?.Value ?? (snapshot.RuntimeState == WorkspaceRuntimeState.Running ? "Database container reachable." : "Database container not running yet.");
        var oracleStatus = databaseService?.Status ?? (snapshot.RuntimeState == WorkspaceRuntimeState.Running ? WorkspaceHealthStatus.Healthy : WorkspaceHealthStatus.Attention);
        var xdbStatus = TryMatchEvidence(investigationHistory, "XDB") switch
        {
            var evidence when evidence.Contains("INVALID", StringComparison.OrdinalIgnoreCase) => WorkspaceHealthStatus.Degraded,
            var evidence when !string.IsNullOrWhiteSpace(evidence) => WorkspaceHealthStatus.Healthy,
            _ => oracleStatus == WorkspaceHealthStatus.Healthy ? WorkspaceHealthStatus.Attention : WorkspaceHealthStatus.Attention,
        };
        var ordsStatus = ordsService?.Status ?? (TryMatchEvidence(investigationHistory, "ORDS") switch
        {
            var evidence when evidence.Contains("did not become reachable", StringComparison.OrdinalIgnoreCase) => WorkspaceHealthStatus.Degraded,
            var evidence when !string.IsNullOrWhiteSpace(evidence) => WorkspaceHealthStatus.Healthy,
            _ => oracleStatus == WorkspaceHealthStatus.Healthy ? WorkspaceHealthStatus.Attention : WorkspaceHealthStatus.Attention,
        });
        var apexEvidence = TryMatchEvidence(investigationHistory, "APEX");
        var apexStatus = apexService?.Status ?? (apexEvidence switch
        {
            var evidence when evidence.Contains("still active", StringComparison.OrdinalIgnoreCase) => WorkspaceHealthStatus.Provisioning,
            var evidence when evidence.Contains("missing", StringComparison.OrdinalIgnoreCase) => WorkspaceHealthStatus.Attention,
            var evidence when !string.IsNullOrWhiteSpace(evidence) => WorkspaceHealthStatus.Healthy,
            _ => WorkspaceHealthStatus.Attention,
        });

        yield return new WorkspaceProviderHealthSnapshot
        {
            ProviderKey = "oracle",
            DisplayName = "Oracle",
            Status = oracleStatus,
            Summary = oracleStatus == WorkspaceHealthStatus.Healthy ? "Oracle database running." : "Oracle database is not ready yet.",
            Evidence =
            [
                new WorkspaceHealthFact { Label = "database", Value = oracleEvidence },
                new WorkspaceHealthFact { Label = "runtime", Value = snapshot.RuntimeState.ToString() },
                new WorkspaceHealthFact { Label = "latest inspection", Value = oracleInvestigation?.Summary ?? "No Oracle runtime inspection recorded." },
            ],
            Confidence = string.IsNullOrWhiteSpace(oracleInvestigation?.Confidence) ? "MEDIUM" : oracleInvestigation!.Confidence,
            Timestamp = oracleInvestigation?.CompletedUtc ?? timestamp,
            RefreshInterval = TimeSpan.FromSeconds(30),
            Repairability = snapshot.Record.LastProvisioningHealth?.Repairability ?? WorkspaceRepairability.Unknown.ToString(),
            RecommendedAction = oracleStatus == WorkspaceHealthStatus.Healthy ? "Open Workspace." : "Troubleshoot Workspace.",
            IsVolatile = true,
            WorkspaceImpact = oracleStatus == WorkspaceHealthStatus.Healthy ? "Database-backed workspace features should work." : "Database-backed features may be unavailable until Oracle is ready.",
        };

        yield return new WorkspaceProviderHealthSnapshot
        {
            ProviderKey = "oracle-xdb",
            DisplayName = "XDB",
            Status = xdbStatus,
            Summary = xdbStatus switch
            {
                WorkspaceHealthStatus.Degraded => "XDB is invalid.",
                WorkspaceHealthStatus.Healthy => "XDB validation looks healthy.",
                _ => "XDB has not been confirmed yet.",
            },
            Evidence =
            [
                new WorkspaceHealthFact { Label = "xdb", Value = string.IsNullOrWhiteSpace(TryMatchEvidence(investigationHistory, "XDB")) ? "not inspected" : TryMatchEvidence(investigationHistory, "XDB") },
            ],
            Confidence = xdbStatus == WorkspaceHealthStatus.Degraded ? "HIGH" : "MEDIUM",
            Timestamp = oracleInvestigation?.CompletedUtc ?? timestamp,
            RefreshInterval = TimeSpan.FromMinutes(5),
            Repairability = WorkspaceRepairability.CleanupRepair.ToString(),
            RecommendedAction = xdbStatus == WorkspaceHealthStatus.Degraded ? "Troubleshoot Workspace." : "Open Workspace.",
            IsVolatile = false,
            WorkspaceImpact = xdbStatus == WorkspaceHealthStatus.Degraded ? "Workspace can still open, but Oracle APEX setup cannot complete until XDB is fixed." : "XDB should not block current workspace use.",
        };

        yield return new WorkspaceProviderHealthSnapshot
        {
            ProviderKey = "ords",
            DisplayName = "ORDS",
            Status = ordsStatus,
            Summary = ordsStatus switch
            {
                WorkspaceHealthStatus.Degraded => "ORDS endpoint is not usable.",
                WorkspaceHealthStatus.Healthy => "ORDS endpoint is reachable.",
                _ => "ORDS has not been confirmed yet.",
            },
            Evidence =
            [
                new WorkspaceHealthFact { Label = "ords", Value = ordsService?.Evidence.FirstOrDefault()?.Value ?? (string.IsNullOrWhiteSpace(TryMatchEvidence(investigationHistory, "ORDS")) ? "not inspected" : TryMatchEvidence(investigationHistory, "ORDS")) },
            ],
            Confidence = ordsStatus == WorkspaceHealthStatus.Degraded ? "HIGH" : "MEDIUM",
            Timestamp = oracleInvestigation?.CompletedUtc ?? timestamp,
            RefreshInterval = TimeSpan.FromSeconds(30),
            Repairability = WorkspaceRepairability.CleanupRepair.ToString(),
            RecommendedAction = ordsStatus == WorkspaceHealthStatus.Degraded ? "Troubleshoot Workspace." : "Open Workspace.",
            IsVolatile = true,
            WorkspaceImpact = ordsStatus == WorkspaceHealthStatus.Degraded ? "Workspace can still be opened, but APEX web access is unavailable." : "ORDS should support web access for this workspace.",
        };

        yield return new WorkspaceProviderHealthSnapshot
        {
            ProviderKey = "apex",
            DisplayName = "APEX",
            Status = apexStatus,
            Summary = apexStatus switch
            {
                WorkspaceHealthStatus.Provisioning => "APEX installation is still running.",
                WorkspaceHealthStatus.Healthy => "APEX installation evidence is healthy.",
                WorkspaceHealthStatus.Attention => string.IsNullOrWhiteSpace(apexEvidence) ? "APEX has not been confirmed yet." : "APEX needs attention.",
                _ => "APEX is unavailable.",
            },
            Evidence =
            [
                new WorkspaceHealthFact { Label = "apex", Value = apexService?.Evidence.FirstOrDefault()?.Value ?? (string.IsNullOrWhiteSpace(apexEvidence) ? "not inspected" : apexEvidence) },
            ],
            Confidence = apexStatus is WorkspaceHealthStatus.Healthy or WorkspaceHealthStatus.Provisioning ? "HIGH" : "MEDIUM",
            Timestamp = oracleInvestigation?.CompletedUtc ?? timestamp,
            RefreshInterval = apexStatus == WorkspaceHealthStatus.Provisioning ? TimeSpan.FromSeconds(5) : TimeSpan.FromMinutes(5),
            Repairability = WorkspaceRepairability.Unknown.ToString(),
            RecommendedAction = apexStatus == WorkspaceHealthStatus.Provisioning ? "Keep Waiting." : apexStatus == WorkspaceHealthStatus.Attention ? "Troubleshoot Workspace." : "Open Workspace.",
            IsVolatile = apexStatus == WorkspaceHealthStatus.Provisioning,
            WorkspaceImpact = apexStatus switch
            {
                WorkspaceHealthStatus.Healthy => "APEX applications should be available.",
                WorkspaceHealthStatus.Provisioning => "Workspace can still be opened, but APEX applications are not ready yet.",
                _ => "Workspace can still be opened, but APEX applications may not be usable yet.",
            },
        };
    }

    private static bool IsOracleWorkspace(WorkspaceSnapshot snapshot)
        => snapshot.Definition.Services.Any(service => service.Contains("oracle", StringComparison.OrdinalIgnoreCase) || service.Contains("ords", StringComparison.OrdinalIgnoreCase))
            || snapshot.Definition.Features.Any(feature => feature.Contains("oracle", StringComparison.OrdinalIgnoreCase) || feature.Contains("apex", StringComparison.OrdinalIgnoreCase));

    private static WorkspaceProviderHealthSnapshot BuildServicesProvider(IReadOnlyList<WorkspaceServiceHealthSnapshot> services, DateTimeOffset timestamp)
    {
        var overallStatus = services.Select(item => item.Status).DefaultIfEmpty(WorkspaceHealthStatus.Healthy).MaxBy(SeverityRank);
        var summary = services.Count == 0
            ? "No declared application services."
            : services.Any(item => item.Status is WorkspaceHealthStatus.Degraded or WorkspaceHealthStatus.Unavailable)
                ? "One or more workspace services are not currently usable."
                : services.Any(item => item.Status is WorkspaceHealthStatus.Attention or WorkspaceHealthStatus.Provisioning)
                    ? "Workspace services need attention before all application features are available."
                    : "Workspace services are reachable.";
        return new WorkspaceProviderHealthSnapshot
        {
            ProviderKey = "services",
            DisplayName = "Services",
            Status = overallStatus,
            Summary = summary,
            Evidence = services.Select(service => new WorkspaceHealthFact
            {
                Label = service.Name,
                Value = string.IsNullOrWhiteSpace(service.Evidence.FirstOrDefault()?.Value)
                    ? service.Status.ToString()
                    : $"{service.Status}: {service.Evidence.FirstOrDefault()!.Value}",
            }).ToList(),
            Confidence = services.All(item => item.Confidence == "HIGH") ? "HIGH" : "MEDIUM",
            Timestamp = timestamp,
            RefreshInterval = services.Count == 0 ? TimeSpan.FromMinutes(5) : services.Min(item => item.RefreshInterval),
            Repairability = WorkspaceRepairability.Unknown.ToString(),
            RecommendedAction = services.FirstOrDefault(item => item.Status is WorkspaceHealthStatus.Degraded or WorkspaceHealthStatus.Unavailable or WorkspaceHealthStatus.Attention)?.Recommendation ?? "Open Workspace.",
            IsVolatile = services.Any(item => item.RefreshInterval <= TimeSpan.FromSeconds(30)),
            WorkspaceImpact = summary,
        };
    }

    private static string TryMatchEvidence(IEnumerable<WorkspaceInvestigationRecord> investigations, string pattern)
        => investigations.LastOrDefault(item => item.Title.Contains(pattern, StringComparison.OrdinalIgnoreCase)
            || item.Evidence.Contains(pattern, StringComparison.OrdinalIgnoreCase)
            || item.Summary.Contains(pattern, StringComparison.OrdinalIgnoreCase))?.Evidence ?? string.Empty;

    private static int SeverityRank(WorkspaceHealthStatus status)
        => status switch
        {
            WorkspaceHealthStatus.Unavailable => 5,
            WorkspaceHealthStatus.Degraded => 4,
            WorkspaceHealthStatus.Provisioning => 3,
            WorkspaceHealthStatus.Investigating => 3,
            WorkspaceHealthStatus.Attention => 2,
            _ => 1,
        };

    private static T MaxBy<T>(this IEnumerable<T> source, Func<T, int> selector)
    {
        using var enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return default!;
        }

        var best = enumerator.Current;
        var bestScore = selector(best);
        while (enumerator.MoveNext())
        {
            var score = selector(enumerator.Current);
            if (score > bestScore)
            {
                best = enumerator.Current;
                bestScore = score;
            }
        }

        return best;
    }
}
