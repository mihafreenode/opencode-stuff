using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceRuntimeExplorerService
{
    private readonly WorkspaceRepository _workspaceRepository;
    private readonly WorkspaceRuntimeStateService _runtimeStateService;
    private readonly WorkspaceYamlService _workspaceYamlService;
    private readonly WorkspaceTimelineService _workspaceTimelineService;
    private readonly IProcessRunner _processRunner;

    public WorkspaceRuntimeExplorerService(
        WorkspaceRepository workspaceRepository,
        WorkspaceRuntimeStateService runtimeStateService,
        WorkspaceYamlService workspaceYamlService,
        WorkspaceTimelineService workspaceTimelineService,
        IProcessRunner processRunner)
    {
        _workspaceRepository = workspaceRepository;
        _runtimeStateService = runtimeStateService;
        _workspaceYamlService = workspaceYamlService;
        _workspaceTimelineService = workspaceTimelineService;
        _processRunner = processRunner;
    }

    public async Task<WorkspaceRuntimeExplorerReport> BuildAsync(CancellationToken cancellationToken = default)
    {
        var records = _workspaceRepository.LoadAll();
        var dockerState = await ProbeDockerStateAsync(cancellationToken);
        var workspaceEntries = new List<WorkspaceRuntimeWorkspaceEntry>(records.Count);
        var resourceEntries = new List<WorkspaceRuntimeResourceEntry>();
        var conflictEntries = new List<WorkspaceRuntimeConflictEntry>();

        foreach (var record in records)
        {
            var paths = WorkspacePathBuilder.Build(record.RootPath, record.ConfigurationPath);
            var definition = TryReadDefinition(paths.WorkspaceYamlPath);
            var runtimeState = _runtimeStateService.Read(paths.RuntimeStatePath);
            var timeline = _workspaceTimelineService.Load(paths.TimelinePath);
            var workspaceName = definition?.Workspace.Name ?? record.Name;
            var runtimeId = runtimeState?.ResolvedEngine ?? "docker";
            var slug = runtimeState?.Resources.Identity.WorkspaceSlug;
            if (string.IsNullOrWhiteSpace(slug) && !string.IsNullOrWhiteSpace(workspaceName))
            {
                slug = WorkspacePathBuilder.Slugify(workspaceName);
            }

            var workspaceResources = BuildWorkspaceResources(record, definition, runtimeState, dockerState, workspaceName, runtimeId, slug ?? string.Empty, conflictEntries);
            resourceEntries.AddRange(workspaceResources);

            var ports = workspaceResources.Where(item => string.Equals(item.ResourceType, "Port", StringComparison.Ordinal)).Select(FormatResourceLabel).ToList();
            var containers = workspaceResources.Where(item => string.Equals(item.ResourceType, "Container", StringComparison.Ordinal)).Select(item => item.RuntimeIdentifier).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
            var volumes = workspaceResources.Where(item => string.Equals(item.ResourceType, "Volume", StringComparison.Ordinal)).Select(item => item.RuntimeIdentifier).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
            var networks = workspaceResources.Where(item => string.Equals(item.ResourceType, "Network", StringComparison.Ordinal)).Select(item => item.RuntimeIdentifier).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
            var services = workspaceResources.Where(item => string.Equals(item.ResourceType, "Service Endpoint", StringComparison.Ordinal)).Select(item => item.DisplayName).ToList();

            workspaceEntries.Add(new WorkspaceRuntimeWorkspaceEntry
            {
                WorkspaceName = workspaceName,
                WorkspaceRootPath = record.RootPath,
                OwningRuntime = runtimeId,
                Template = DescribeTemplate(definition),
                CreatedUtc = record.CreatedUtc,
                LastUsedUtc = timeline.Events.OrderByDescending(item => item.OccurredUtc).FirstOrDefault()?.OccurredUtc ?? record.LastOpenedUtc,
                Status = dockerState.ContainerStatuses.Any(item => item.Name.Equals($"{slug}-workspace", StringComparison.OrdinalIgnoreCase) && item.Status.StartsWith("Up", StringComparison.OrdinalIgnoreCase)) ? "Running" : "Stopped",
                Health = runtimeState?.Resources.Conflicts.Count > 0 ? "Attention" : "Healthy",
                RuntimeIdentifier = slug ?? string.Empty,
                Source = File.Exists(paths.RuntimeStatePath) ? "runtime-state.yaml" : "workspace.yaml",
                Ports = ports,
                Containers = containers,
                Volumes = volumes,
                Networks = networks,
                Services = services,
            });

            if (runtimeState is null)
            {
                conflictEntries.Add(new WorkspaceRuntimeConflictEntry
                {
                    ConflictType = "MissingRuntimeState",
                    DisplayName = workspaceName,
                    CurrentOwner = "No runtime-state.yaml",
                    RequestedOwner = workspaceName,
                    RecommendedAction = "Open Workspace.",
                    WorkspaceRootPath = record.RootPath,
                    ResourceType = "Runtime State",
                    ResourceIdentifier = paths.RuntimeStatePath,
                    Details = "Workspace exists without runtime resource ownership metadata.",
                });
            }
            else if (runtimeState.Resources.Ports.Count == 0 && runtimeState.Resources.RuntimeIdentifiers.Count == 0)
            {
                conflictEntries.Add(new WorkspaceRuntimeConflictEntry
                {
                    ConflictType = "EmptyRuntimeState",
                    DisplayName = workspaceName,
                    CurrentOwner = workspaceName,
                    RequestedOwner = workspaceName,
                    RecommendedAction = "Open Workspace.",
                    WorkspaceRootPath = record.RootPath,
                    ResourceType = "Runtime State",
                    ResourceIdentifier = paths.RuntimeStatePath,
                    Details = "runtime-state.yaml exists but does not describe managed resources.",
                });
            }
        }

        conflictEntries.AddRange(DetectDuplicateAllocations(resourceEntries));
        conflictEntries.AddRange(DetectIdentifierCollisions(resourceEntries));

        var orphaned = DetectOrphanedResources(resourceEntries, dockerState);
        var unused = resourceEntries.Where(item => item.CanCleanUpSafely && !string.Equals(item.Status, "Running", StringComparison.OrdinalIgnoreCase)).ToList();
        var health = BuildHealth(resourceEntries, conflictEntries, orphaned, dockerState);

        return new WorkspaceRuntimeExplorerReport
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            UsedDockerProbe = dockerState.WasSuccessful,
            Summary = $"{workspaceEntries.Count} workspaces, {resourceEntries.Count} managed resources, {conflictEntries.Count} conflicts, {orphaned.Count} orphaned.",
            Workspaces = workspaceEntries.OrderBy(item => item.WorkspaceName, StringComparer.OrdinalIgnoreCase).ToList(),
            Resources = resourceEntries.OrderBy(item => item.WorkspaceName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.ResourceType, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            Conflicts = conflictEntries.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            UnusedResources = unused,
            OrphanedResources = orphaned,
            Health = health,
        };
    }

    public async Task<WorkspaceRuntimeInspectResult> InspectResourceAsync(WorkspaceRuntimeResourceEntry resource, CancellationToken cancellationToken = default)
    {
        if (string.Equals(resource.ResourceType, "Runtime State", StringComparison.OrdinalIgnoreCase))
        {
            var details = File.Exists(resource.RuntimeIdentifier) ? await File.ReadAllTextAsync(resource.RuntimeIdentifier, cancellationToken) : "runtime-state.yaml not found.";
            return new WorkspaceRuntimeInspectResult { Title = resource.DisplayName, Summary = resource.Status, Details = details };
        }

        if (string.Equals(resource.ResourceType, "Port", StringComparison.OrdinalIgnoreCase))
        {
            return new WorkspaceRuntimeInspectResult
            {
                Title = resource.DisplayName,
                Summary = resource.Status,
                Details = string.Join(Environment.NewLine,
                [
                    $"Workspace: {resource.WorkspaceName}",
                    $"Preferred: {resource.PreferredPort}",
                    $"Current: {resource.CurrentPort}",
                    $"Status: {resource.Status}",
                    $"Health: {resource.Health}",
                    $"Reason: {resource.Reason}",
                ]),
            };
        }

        var target = resource.RuntimeIdentifier;
        if (string.IsNullOrWhiteSpace(target))
        {
            return new WorkspaceRuntimeInspectResult { Title = resource.DisplayName, Summary = resource.Status, Details = resource.CleanupSummary };
        }

        string[] command = resource.ResourceType switch
        {
            "Container" => ["inspect", target],
            "Volume" => ["volume", "inspect", target],
            "Network" => ["network", "inspect", target],
            _ => ["inspect", target],
        };

        var result = await _processRunner.RunAsync("docker", command, cancellationToken: cancellationToken);
        var detailsText = string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError : result.StandardOutput;
        return new WorkspaceRuntimeInspectResult { Title = resource.DisplayName, Summary = result.IsSuccess ? resource.Status : "Unavailable", Details = detailsText.Trim() };
    }

    public async Task CleanOrphanedResourcesAsync(CancellationToken cancellationToken = default)
    {
        var report = await BuildAsync(cancellationToken);
        foreach (var resource in report.OrphanedResources.Where(item => item.CanCleanUpSafely))
        {
            string[] command = resource.ResourceType switch
            {
                "Container" => ["rm", "-f", resource.RuntimeIdentifier],
                "Volume" => ["volume", "rm", resource.RuntimeIdentifier],
                "Network" => ["network", "rm", resource.RuntimeIdentifier],
                _ => [],
            };

            if (command.Length == 0)
            {
                continue;
            }

            await _processRunner.RunAsync("docker", command, cancellationToken: cancellationToken);
        }
    }

    private List<WorkspaceRuntimeResourceEntry> BuildWorkspaceResources(
        WorkspaceRecord record,
        WorkspaceDefinition? definition,
        WorkspaceRuntimeStateRecord? runtimeState,
        DockerProbeState dockerState,
        string workspaceName,
        string runtimeId,
        string slug,
        List<WorkspaceRuntimeConflictEntry> conflicts)
    {
        var resources = new List<WorkspaceRuntimeResourceEntry>();
        if (runtimeState is null)
        {
            resources.Add(new WorkspaceRuntimeResourceEntry
            {
                ResourceType = "Runtime State",
                DisplayName = "runtime-state.yaml",
                WorkspaceName = workspaceName,
                WorkspaceRootPath = record.RootPath,
                OwningRuntime = runtimeId,
                RuntimeIdentifier = WorkspacePathBuilder.Build(record.RootPath, record.ConfigurationPath).RuntimeStatePath,
                Source = "runtime-state.yaml",
                Status = "Missing",
                Health = "Attention",
                CanCleanUpSafely = false,
                CleanupSummary = "Generate runtime-state.yaml before cleanup decisions.",
            });
            return resources;
        }

        foreach (var port in runtimeState.Resources.Ports)
        {
            resources.Add(new WorkspaceRuntimeResourceEntry
            {
                ResourceType = "Port",
                DisplayName = port.DisplayName,
                WorkspaceName = workspaceName,
                WorkspaceRootPath = record.RootPath,
                OwningRuntime = runtimeId,
                RuntimeIdentifier = port.ResourceId,
                Source = "runtime-state.yaml",
                Status = "Allocated",
                Health = port.AllocatedPort == port.PreferredPort ? "Preferred" : "Allocated automatically",
                CanCleanUpSafely = false,
                CleanupSummary = "Release the owning runtime before freeing this port.",
                ServiceId = port.ServiceId,
                Endpoint = port.Endpoint,
                OpenUrl = port.OpenUrl,
                PreferredPort = port.PreferredPort,
                CurrentPort = port.AllocatedPort,
                Reason = port.AllocatedPort == port.PreferredPort ? "Preferred port available." : FirstConflictResolution(runtimeState, port.ResourceId, "Preferred port already occupied."),
            });
        }

        foreach (var endpoint in runtimeState.Resources.ServiceEndpoints)
        {
            resources.Add(new WorkspaceRuntimeResourceEntry
            {
                ResourceType = "Service Endpoint",
                DisplayName = endpoint.DisplayName,
                WorkspaceName = workspaceName,
                WorkspaceRootPath = record.RootPath,
                OwningRuntime = runtimeId,
                RuntimeIdentifier = endpoint.ServiceId,
                Source = "runtime-state.yaml",
                Status = "Available",
                Health = "Linked",
                CanCleanUpSafely = false,
                CleanupSummary = "Service endpoint is owned by the workspace runtime.",
                ServiceId = endpoint.ServiceId,
                Endpoint = endpoint.Endpoint,
                OpenUrl = endpoint.OpenUrl,
            });
        }

        foreach (var identifier in runtimeState.Resources.RuntimeIdentifiers)
        {
            var resourceType = identifier.ResourceType switch
            {
                "container" => "Container",
                "volume" => "Volume",
                "network" => "Network",
                _ => "Runtime Identifier",
            };
            var status = ResolveIdentifierStatus(resourceType, identifier.Value, dockerState);
            var health = string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "Present", StringComparison.OrdinalIgnoreCase)
                ? "Healthy"
                : string.Equals(status, "Missing", StringComparison.OrdinalIgnoreCase)
                    ? "Drift"
                    : status;
            resources.Add(new WorkspaceRuntimeResourceEntry
            {
                ResourceType = resourceType,
                DisplayName = identifier.DisplayName,
                WorkspaceName = workspaceName,
                WorkspaceRootPath = record.RootPath,
                OwningRuntime = runtimeId,
                RuntimeIdentifier = identifier.Value,
                Source = "runtime-state.yaml",
                Status = status,
                Health = health,
                CanCleanUpSafely = !string.Equals(resourceType, "Container", StringComparison.OrdinalIgnoreCase) || !string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase),
                CleanupSummary = string.Equals(status, "Missing", StringComparison.OrdinalIgnoreCase)
                    ? "Runtime drift detected. Resource can be recreated safely."
                    : string.Equals(status, "Stopped", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "Present", StringComparison.OrdinalIgnoreCase)
                        ? "Resource belongs to this workspace and can be cleaned up through Release Resources or Reset Runtime."
                        : "Resource is active.",
                ContainerName = string.Equals(resourceType, "Container", StringComparison.OrdinalIgnoreCase) ? identifier.Value : string.Empty,
            });

            if (string.Equals(status, "Missing", StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(new WorkspaceRuntimeConflictEntry
                {
                    ConflictType = resourceType switch
                    {
                        "Volume" => "VolumeMismatch",
                        "Network" => "NetworkMismatch",
                        _ => "RuntimeDrift",
                    },
                    DisplayName = identifier.DisplayName,
                    CurrentOwner = "Missing from Docker runtime",
                    RequestedOwner = workspaceName,
                    RecommendedAction = "Open Workspace.",
                    WorkspaceRootPath = record.RootPath,
                    ResourceType = resourceType,
                    ResourceIdentifier = identifier.Value,
                    Details = $"Expected managed {resourceType.ToLowerInvariant()} '{identifier.Value}' is missing.",
                });
            }
        }

        foreach (var conflict in runtimeState.Resources.Conflicts)
        {
            conflicts.Add(new WorkspaceRuntimeConflictEntry
            {
                ConflictType = conflict.ConflictKind,
                DisplayName = conflict.DisplayName,
                CurrentOwner = conflict.Owner,
                RequestedOwner = workspaceName,
                RecommendedAction = conflict.Recommendation,
                WorkspaceRootPath = record.RootPath,
                ResourceType = "Port",
                ResourceIdentifier = conflict.ResourceId,
                Details = conflict.Resolution,
            });
        }

        return resources;
    }

    private static string FormatResourceLabel(WorkspaceRuntimeResourceEntry resource)
        => resource.CurrentPort is > 0 ? resource.CurrentPort.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : resource.DisplayName;

    private static string DescribeTemplate(WorkspaceDefinition? definition)
    {
        if (definition is null)
        {
            return "Unknown";
        }

        if (OracleWorkspaceFamily.IsOracleWorkspace(definition))
        {
            return "Oracle";
        }

        if (definition.Services.Contains("postgres", StringComparer.OrdinalIgnoreCase))
        {
            return "PostgreSQL";
        }

        if (definition.Features.Contains("analytics-reporting", StringComparer.OrdinalIgnoreCase))
        {
            return "Analytics";
        }

        return definition.Features.FirstOrDefault() ?? "Core";
    }

    private static string FirstConflictResolution(WorkspaceRuntimeStateRecord runtimeState, string resourceId, string fallback)
        => runtimeState.Resources.Conflicts.FirstOrDefault(item => string.Equals(item.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))?.Resolution ?? fallback;

    private static string ResolveIdentifierStatus(string resourceType, string identifier, DockerProbeState dockerState)
    {
        if (string.Equals(resourceType, "Container", StringComparison.OrdinalIgnoreCase))
        {
            var status = dockerState.ContainerStatuses.FirstOrDefault(item => item.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase))?.Status;
            if (string.IsNullOrWhiteSpace(status))
            {
                return "Missing";
            }

            return status.StartsWith("Up", StringComparison.OrdinalIgnoreCase) ? "Running" : "Stopped";
        }

        if (string.Equals(resourceType, "Volume", StringComparison.OrdinalIgnoreCase))
        {
            return dockerState.Volumes.Contains(identifier, StringComparer.OrdinalIgnoreCase) ? "Present" : "Missing";
        }

        if (string.Equals(resourceType, "Network", StringComparison.OrdinalIgnoreCase))
        {
            return dockerState.Networks.Contains(identifier, StringComparer.OrdinalIgnoreCase) ? "Present" : "Missing";
        }

        return "Tracked";
    }

    private static IReadOnlyList<WorkspaceRuntimeConflictEntry> DetectDuplicateAllocations(IEnumerable<WorkspaceRuntimeResourceEntry> resources)
        => resources
            .Where(item => string.Equals(item.ResourceType, "Port", StringComparison.OrdinalIgnoreCase) && item.CurrentPort is > 0)
            .GroupBy(item => item.CurrentPort!.Value)
            .Where(group => group.Select(item => item.WorkspaceRootPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => new WorkspaceRuntimeConflictEntry
            {
                ConflictType = "DuplicateAllocation",
                DisplayName = $"Port {group.Key}",
                CurrentOwner = string.Join(", ", group.Select(item => item.WorkspaceName).Distinct(StringComparer.OrdinalIgnoreCase)),
                RequestedOwner = string.Join(", ", group.Select(item => item.WorkspaceName).Distinct(StringComparer.OrdinalIgnoreCase)),
                RecommendedAction = "Restore preferred ports or reallocate one workspace.",
                WorkspaceRootPath = group.First().WorkspaceRootPath,
                ResourceType = "Port",
                ResourceIdentifier = group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Details = "Multiple workspaces claim the same allocated port.",
            })
            .ToList();

    private static IReadOnlyList<WorkspaceRuntimeConflictEntry> DetectIdentifierCollisions(IEnumerable<WorkspaceRuntimeResourceEntry> resources)
        => resources
            .Where(item => item.ResourceType is "Container" or "Volume" or "Network")
            .Where(item => !string.IsNullOrWhiteSpace(item.RuntimeIdentifier))
            .GroupBy(item => item.RuntimeIdentifier, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.WorkspaceRootPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => new WorkspaceRuntimeConflictEntry
            {
                ConflictType = group.First().ResourceType switch
                {
                    "Container" => "ContainerNameCollision",
                    "Volume" => "VolumeMismatch",
                    "Network" => "NetworkMismatch",
                    _ => "UnexpectedOwnership",
                },
                DisplayName = group.Key,
                CurrentOwner = string.Join(", ", group.Select(item => item.WorkspaceName).Distinct(StringComparer.OrdinalIgnoreCase)),
                RequestedOwner = string.Join(", ", group.Select(item => item.WorkspaceName).Distinct(StringComparer.OrdinalIgnoreCase)),
                RecommendedAction = "Reset one runtime or clean orphaned resources.",
                WorkspaceRootPath = group.First().WorkspaceRootPath,
                ResourceType = group.First().ResourceType,
                ResourceIdentifier = group.Key,
                Details = "Multiple workspaces claim the same runtime identifier.",
            })
            .ToList();

    private static IReadOnlyList<WorkspaceRuntimeResourceEntry> DetectOrphanedResources(IEnumerable<WorkspaceRuntimeResourceEntry> resources, DockerProbeState dockerState)
    {
        var expectedContainers = resources.Where(item => string.Equals(item.ResourceType, "Container", StringComparison.OrdinalIgnoreCase)).Select(item => item.RuntimeIdentifier).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedVolumes = resources.Where(item => string.Equals(item.ResourceType, "Volume", StringComparison.OrdinalIgnoreCase)).Select(item => item.RuntimeIdentifier).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedNetworks = resources.Where(item => string.Equals(item.ResourceType, "Network", StringComparison.OrdinalIgnoreCase)).Select(item => item.RuntimeIdentifier).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphaned = new List<WorkspaceRuntimeResourceEntry>();

        foreach (var container in dockerState.ContainerStatuses)
        {
            if (!LooksManagedContainer(container.Name) || expectedContainers.Contains(container.Name))
            {
                continue;
            }

            orphaned.Add(new WorkspaceRuntimeResourceEntry
            {
                ResourceType = "Container",
                DisplayName = container.Name,
                WorkspaceName = "Unowned",
                OwningRuntime = "docker",
                RuntimeIdentifier = container.Name,
                Source = "Docker probe",
                Status = container.Status.StartsWith("Up", StringComparison.OrdinalIgnoreCase) ? "Running" : "Stopped",
                Health = "Orphaned",
                CanCleanUpSafely = !container.Status.StartsWith("Up", StringComparison.OrdinalIgnoreCase),
                CleanupSummary = container.Status.StartsWith("Up", StringComparison.OrdinalIgnoreCase)
                    ? "Inspect before deleting because the container is still running."
                    : "No workspace owns this managed container.",
                ContainerName = container.Name,
            });
        }

        foreach (var volume in dockerState.Volumes)
        {
            if (!LooksManagedVolume(volume) || expectedVolumes.Contains(volume))
            {
                continue;
            }

            orphaned.Add(new WorkspaceRuntimeResourceEntry
            {
                ResourceType = "Volume",
                DisplayName = volume,
                WorkspaceName = "Unowned",
                OwningRuntime = "docker",
                RuntimeIdentifier = volume,
                Source = "Docker probe",
                Status = "Present",
                Health = "Orphaned",
                CanCleanUpSafely = true,
                CleanupSummary = "No workspace owns this managed volume.",
            });
        }

        foreach (var network in dockerState.Networks)
        {
            if (!LooksManagedNetwork(network) || expectedNetworks.Contains(network))
            {
                continue;
            }

            orphaned.Add(new WorkspaceRuntimeResourceEntry
            {
                ResourceType = "Network",
                DisplayName = network,
                WorkspaceName = "Unowned",
                OwningRuntime = "docker",
                RuntimeIdentifier = network,
                Source = "Docker probe",
                Status = "Present",
                Health = "Orphaned",
                CanCleanUpSafely = true,
                CleanupSummary = "No workspace owns this managed network.",
            });
        }

        return orphaned;
    }

    private static IReadOnlyList<WorkspaceRuntimeHealthEntry> BuildHealth(
        IReadOnlyCollection<WorkspaceRuntimeResourceEntry> resources,
        IReadOnlyCollection<WorkspaceRuntimeConflictEntry> conflicts,
        IReadOnlyCollection<WorkspaceRuntimeResourceEntry> orphaned,
        DockerProbeState dockerState)
        =>
        [
            new WorkspaceRuntimeHealthEntry { Category = "Ownership", Status = conflicts.Any(item => item.ConflictType is "DuplicateAllocation" or "ContainerNameCollision") ? "Attention" : "Healthy", Summary = conflicts.Any() ? $"{conflicts.Count} ownership or drift findings detected." : "Managed ownership is consistent." },
            new WorkspaceRuntimeHealthEntry { Category = "Conflicts", Status = conflicts.Count > 0 ? "Attention" : "Healthy", Summary = conflicts.Count > 0 ? $"{conflicts.Count} runtime conflicts need review." : "No runtime conflicts detected." },
            new WorkspaceRuntimeHealthEntry { Category = "Orphaned", Status = orphaned.Count > 0 ? "Attention" : "Healthy", Summary = orphaned.Count > 0 ? $"{orphaned.Count} orphaned runtime resources found." : "No orphaned managed runtime resources were found." },
            new WorkspaceRuntimeHealthEntry { Category = "Docker Probe", Status = dockerState.WasSuccessful ? "Healthy" : "Unavailable", Summary = dockerState.WasSuccessful ? "Container, volume, and network state refreshed from Docker." : "Docker probe unavailable. Showing cached runtime-state ownership only." },
            new WorkspaceRuntimeHealthEntry { Category = "Resources", Status = resources.Any(item => item.Health == "Drift") ? "Attention" : "Healthy", Summary = $"{resources.Count} managed resources indexed." },
        ];

    private WorkspaceDefinition? TryReadDefinition(string workspaceYamlPath)
    {
        try
        {
            return File.Exists(workspaceYamlPath) ? _workspaceYamlService.Read(workspaceYamlPath) : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<DockerProbeState> ProbeDockerStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var containers = await _processRunner.RunAsync("docker", ["ps", "-a", "--format", "{{.Names}}\t{{.Status}}"], cancellationToken: cancellationToken);
            var volumes = await _processRunner.RunAsync("docker", ["volume", "ls", "--format", "{{.Name}}"], cancellationToken: cancellationToken);
            var networks = await _processRunner.RunAsync("docker", ["network", "ls", "--format", "{{.Name}}"], cancellationToken: cancellationToken);
            return new DockerProbeState
            {
                WasSuccessful = containers.IsSuccess && volumes.IsSuccess && networks.IsSuccess,
                ContainerStatuses = containers.StandardOutputLines
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => line.Split('\t', 2))
                    .Where(parts => parts.Length > 0)
                    .Select(parts => new DockerContainerStatus(parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : string.Empty))
                    .ToList(),
                Volumes = volumes.StandardOutputLines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToList(),
                Networks = networks.StandardOutputLines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToList(),
            };
        }
        catch
        {
            return new DockerProbeState();
        }
    }

    private static bool LooksManagedContainer(string name)
        => name.EndsWith("-workspace", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("-postgres-1", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("-pgadmin-1", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("-oracle-demo-1", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("-oracle-ords-1", StringComparison.OrdinalIgnoreCase);

    private static bool LooksManagedVolume(string name)
        => name.EndsWith("_postgres-data", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_oracle-demo-data", StringComparison.OrdinalIgnoreCase);

    private static bool LooksManagedNetwork(string name)
        => name.EndsWith("_default", StringComparison.OrdinalIgnoreCase);

    private sealed class DockerProbeState
    {
        public bool WasSuccessful { get; init; }
        public IReadOnlyList<DockerContainerStatus> ContainerStatuses { get; init; } = Array.Empty<DockerContainerStatus>();
        public IReadOnlyList<string> Volumes { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Networks { get; init; } = Array.Empty<string>();
    }

    private sealed record DockerContainerStatus(string Name, string Status);
}
