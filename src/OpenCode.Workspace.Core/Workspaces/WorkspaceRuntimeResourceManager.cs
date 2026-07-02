using System.Net;
using System.Net.Sockets;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceRuntimeResourceManager
{
    private readonly WorkspaceRepository _workspaceRepository;
    private readonly WorkspaceRuntimeStateService _runtimeStateService;
    private readonly Func<int, bool> _isPortAvailable;

    public WorkspaceRuntimeResourceManager(
        WorkspaceRepository workspaceRepository,
        WorkspaceRuntimeStateService runtimeStateService,
        Func<int, bool>? isPortAvailable = null)
    {
        _workspaceRepository = workspaceRepository;
        _runtimeStateService = runtimeStateService;
        _isPortAvailable = isPortAvailable ?? IsPortAvailable;
    }

    public WorkspaceRuntimeStateRecord ResolveState(
        WorkspaceDefinition definition,
        WorkspacePaths paths,
        WorkspaceRuntimeStateRecord? existingState = null,
        ResolvedRuntimePlan? resolvedRuntimePlan = null,
        DateTimeOffset? lastSuccessfulProvision = null,
        bool inspectHostAvailability = false)
    {
        existingState ??= _runtimeStateService.Read(paths.RuntimeStatePath);
        var requirements = WorkspaceRuntimeResourceCatalog.BuildPortRequirements(definition);
        var identity = new WorkspaceRuntimeIdentity
        {
            WorkspaceId = string.IsNullOrWhiteSpace(definition.Workspace.Id) ? WorkspacePathBuilder.Slugify(definition.Workspace.Name) : definition.Workspace.Id,
            WorkspaceName = definition.Workspace.Name,
            WorkspaceSlug = WorkspacePathBuilder.Slugify(definition.Workspace.Name),
        };

        var allocations = new List<WorkspacePortAllocationRecord>(requirements.Count);
        var conflicts = new List<WorkspaceResourceConflictRecord>();
        var reservedPorts = new HashSet<int>();
        foreach (var requirement in requirements)
        {
            var allocated = ResolvePortAllocation(paths.RootPath, requirement, existingState, reservedPorts, conflicts, inspectHostAvailability);
            allocations.Add(allocated);
            reservedPorts.Add(allocated.AllocatedPort);
        }

        var endpoints = BuildServiceEndpoints(allocations);
        var identifiers = BuildRuntimeIdentifiers(identity.WorkspaceSlug, definition, allocations);

        return new WorkspaceRuntimeStateRecord
        {
            ResolvedEngine = resolvedRuntimePlan?.Runtime ?? existingState?.ResolvedEngine ?? string.Empty,
            ResolvedPlatform = resolvedRuntimePlan?.TargetPlatform ?? existingState?.ResolvedPlatform ?? string.Empty,
            CompatibilityMode = resolvedRuntimePlan?.CompatibilityMode.ToString() ?? existingState?.CompatibilityMode ?? string.Empty,
            LastSuccessfulProvision = lastSuccessfulProvision ?? existingState?.LastSuccessfulProvision,
            Resources = new WorkspaceManagedRuntimeResources
            {
                Identity = identity,
                Ports = allocations,
                ServiceEndpoints = endpoints,
                RuntimeIdentifiers = identifiers,
                Conflicts = conflicts,
            },
        };
    }

    private WorkspacePortAllocationRecord ResolvePortAllocation(
        string currentWorkspaceRootPath,
        WorkspacePortRequirement requirement,
        WorkspaceRuntimeStateRecord? existingState,
        ISet<int> reservedPorts,
        List<WorkspaceResourceConflictRecord> conflicts,
        bool inspectHostAvailability)
    {
        var currentAllocation = existingState?.Resources.Ports.FirstOrDefault(item => string.Equals(item.ResourceId, requirement.ResourceId, StringComparison.OrdinalIgnoreCase));
        if (currentAllocation is not null
            && currentAllocation.AllocatedPort > 0
            && !reservedPorts.Contains(currentAllocation.AllocatedPort)
            && FindManagedOwner(currentWorkspaceRootPath, currentAllocation.AllocatedPort) is null
            && (!inspectHostAvailability || _isPortAvailable(currentAllocation.AllocatedPort)))
        {
            return WorkspaceRuntimeResourceCatalog.CreatePortAllocation(requirement, currentAllocation.AllocatedPort, currentAllocation.AllocationKind);
        }

        var preferredOwner = FindManagedOwner(currentWorkspaceRootPath, requirement.PreferredPort);
        if (!reservedPorts.Contains(requirement.PreferredPort)
            && preferredOwner is null
            && (!inspectHostAvailability || _isPortAvailable(requirement.PreferredPort)))
        {
            return WorkspaceRuntimeResourceCatalog.CreatePortAllocation(requirement, requirement.PreferredPort, "Preferred");
        }

        var alternativePort = requirement.AlternativePorts.FirstOrDefault(port => !reservedPorts.Contains(port)
            && FindManagedOwner(currentWorkspaceRootPath, port) is null
            && (!inspectHostAvailability || _isPortAvailable(port)));
        if (alternativePort > 0)
        {
            conflicts.Add(BuildConflict(requirement, preferredOwner, $"Allocated alternative port {alternativePort}."));
            return WorkspaceRuntimeResourceCatalog.CreatePortAllocation(requirement, alternativePort, "Alternative");
        }

        if (!inspectHostAvailability)
        {
            return WorkspaceRuntimeResourceCatalog.CreatePortAllocation(requirement, requirement.PreferredPort, "Preferred");
        }

        if (requirement.AllowsDynamicAllocation)
        {
            for (var candidate = requirement.PreferredPort + 1; candidate <= requirement.PreferredPort + 100; candidate++)
            {
                if (reservedPorts.Contains(candidate)
                    || FindManagedOwner(currentWorkspaceRootPath, candidate) is not null
                    || !_isPortAvailable(candidate))
                {
                    continue;
                }

                conflicts.Add(BuildConflict(requirement, preferredOwner, $"Allocated dynamic port {candidate}."));
                return WorkspaceRuntimeResourceCatalog.CreatePortAllocation(requirement, candidate, "Dynamic");
            }
        }

        return WorkspaceRuntimeResourceCatalog.CreatePortAllocation(requirement, requirement.PreferredPort, "Preferred");
    }

    private WorkspaceResourceConflictRecord BuildConflict(WorkspacePortRequirement requirement, WorkspaceRecord? owner, string resolution)
    {
        var ownerName = owner?.Name;
        return new WorkspaceResourceConflictRecord
        {
            ResourceId = requirement.ResourceId,
            DisplayName = requirement.DisplayName,
            PreferredPort = requirement.PreferredPort,
            ConflictKind = string.IsNullOrWhiteSpace(ownerName) ? "ExternalProcess" : "ManagedWorkspace",
            Owner = string.IsNullOrWhiteSpace(ownerName) ? "Unknown external process" : $"workspace {ownerName}",
            Impact = $"Cannot start {requirement.DisplayName} on preferred port {requirement.PreferredPort}.",
            Recommendation = "Allocate another port.",
            Resolution = resolution,
        };
    }

    private WorkspaceRecord? FindManagedOwner(string currentWorkspaceRootPath, int port)
    {
        var normalizedCurrentWorkspaceRootPath = WorkspacePathBuilder.NormalizeHostPathForCurrentOs(currentWorkspaceRootPath);
        foreach (var record in _workspaceRepository.LoadAll())
        {
            var normalizedRecordRootPath = WorkspacePathBuilder.NormalizeHostPathForCurrentOs(record.RootPath);
            if (string.Equals(normalizedRecordRootPath, normalizedCurrentWorkspaceRootPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var runtimeStatePath = WorkspacePathBuilder.Build(normalizedRecordRootPath, record.ConfigurationPath).RuntimeStatePath;
            var state = _runtimeStateService.Read(runtimeStatePath);
            if (state?.Resources.Ports.Any(item => item.AllocatedPort == port) == true)
            {
                return record;
            }
        }

        return null;
    }

    private static List<WorkspaceServiceEndpointRecord> BuildServiceEndpoints(IEnumerable<WorkspacePortAllocationRecord> allocations)
    {
        var endpoints = new List<WorkspaceServiceEndpointRecord>();
        foreach (var allocation in allocations)
        {
            endpoints.Add(new WorkspaceServiceEndpointRecord
            {
                ServiceId = allocation.ServiceId,
                DisplayName = allocation.DisplayName,
                Endpoint = allocation.ResourceId == WorkspaceRuntimeResourceCatalog.OracleOrdsResourceId
                    ? $"http://localhost:{allocation.AllocatedPort}/ords/"
                    : allocation.Endpoint,
                OpenUrl = allocation.OpenUrl,
            });

            if (string.Equals(allocation.ResourceId, WorkspaceRuntimeResourceCatalog.OracleOrdsResourceId, StringComparison.OrdinalIgnoreCase))
            {
                endpoints.Add(new WorkspaceServiceEndpointRecord { ServiceId = "sql-developer-web", DisplayName = "SQL Developer Web", Endpoint = $"http://localhost:{allocation.AllocatedPort}/ords/", OpenUrl = allocation.OpenUrl });
                endpoints.Add(new WorkspaceServiceEndpointRecord { ServiceId = "rest-apis", DisplayName = "REST APIs", Endpoint = $"http://localhost:{allocation.AllocatedPort}/ords/", OpenUrl = $"http://localhost:{allocation.AllocatedPort}/ords/" });
                endpoints.Add(new WorkspaceServiceEndpointRecord { ServiceId = "apex", DisplayName = "Oracle APEX", Endpoint = $"http://localhost:{allocation.AllocatedPort}/ords/", OpenUrl = allocation.OpenUrl });
            }
        }

        return endpoints;
    }

    private static List<WorkspaceRuntimeIdentifierRecord> BuildRuntimeIdentifiers(string workspaceSlug, WorkspaceDefinition definition, IEnumerable<WorkspacePortAllocationRecord> allocations)
    {
        var identifiers = new List<WorkspaceRuntimeIdentifierRecord>
        {
            new() { ResourceType = "container", ResourceId = "workspace", DisplayName = "Workspace container", Value = $"{workspaceSlug}-workspace" },
            new() { ResourceType = "network", ResourceId = "default", DisplayName = "Default network", Value = $"{workspaceSlug}_default" },
        };

        foreach (var service in definition.Services.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            identifiers.Add(new WorkspaceRuntimeIdentifierRecord
            {
                ResourceType = "container",
                ResourceId = service,
                DisplayName = $"{service} container",
                Value = $"{workspaceSlug}-{service}-1",
            });

            if (string.Equals(service, "postgres", StringComparison.OrdinalIgnoreCase))
            {
                identifiers.Add(new WorkspaceRuntimeIdentifierRecord { ResourceType = "volume", ResourceId = "postgres-data", DisplayName = "PostgreSQL data volume", Value = $"{workspaceSlug}_postgres-data" });
            }

            if (string.Equals(service, "oracle-demo", StringComparison.OrdinalIgnoreCase))
            {
                identifiers.Add(new WorkspaceRuntimeIdentifierRecord { ResourceType = "volume", ResourceId = "oracle-demo-data", DisplayName = "Oracle data volume", Value = $"{workspaceSlug}_oracle-demo-data" });
            }
        }

        foreach (var allocation in allocations)
        {
            identifiers.Add(new WorkspaceRuntimeIdentifierRecord
            {
                ResourceType = "port",
                ResourceId = allocation.ResourceId,
                DisplayName = allocation.DisplayName,
                Value = allocation.AllocatedPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        return identifiers;
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
