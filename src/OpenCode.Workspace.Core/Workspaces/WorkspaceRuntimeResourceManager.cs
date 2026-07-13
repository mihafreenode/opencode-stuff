using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceRuntimeResourceManager
{
    private readonly WorkspaceRepository _workspaceRepository;
    private readonly WorkspaceRuntimeStateService _runtimeStateService;
    private readonly Func<int, WorkspacePortUsage> _getPortUsage;

    public WorkspaceRuntimeResourceManager(
        WorkspaceRepository workspaceRepository,
        WorkspaceRuntimeStateService runtimeStateService,
        Func<int, bool>? isPortAvailable = null,
        Func<int, WorkspacePortUsage>? getPortUsage = null)
    {
        _workspaceRepository = workspaceRepository;
        _runtimeStateService = runtimeStateService;
        _getPortUsage = getPortUsage
            ?? (isPortAvailable is null
                ? GetPortUsage
                : port => isPortAvailable(port)
                    ? WorkspacePortUsage.Free(port)
                    : WorkspacePortUsage.Occupied(port, "ExternalProcess", "Unknown external process"));
    }

    public WorkspaceRuntimeStateRecord ResolveState(
        WorkspaceDefinition definition,
        WorkspacePaths paths,
        WorkspaceRuntimeStateRecord? existingState = null,
        ResolvedRuntimePlan? resolvedRuntimePlan = null,
        DateTimeOffset? lastSuccessfulProvision = null,
        bool inspectHostAvailability = false,
        string workspaceImageTag = "",
        string workspaceImageInputHash = "",
        IReadOnlyDictionary<string, string>? workspaceImageInputCategories = null,
        DateTimeOffset? generatedArtifactsUtc = null)
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
            WorkspaceImageTag = string.IsNullOrWhiteSpace(workspaceImageTag) ? existingState?.WorkspaceImageTag ?? string.Empty : workspaceImageTag,
            WorkspaceImageInputHash = string.IsNullOrWhiteSpace(workspaceImageInputHash) ? existingState?.WorkspaceImageInputHash ?? string.Empty : workspaceImageInputHash,
            WorkspaceImageInputCategories = workspaceImageInputCategories is null || workspaceImageInputCategories.Count == 0
                ? existingState?.WorkspaceImageInputCategories ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(workspaceImageInputCategories, StringComparer.OrdinalIgnoreCase),
            GeneratedArtifactsUtc = generatedArtifactsUtc ?? existingState?.GeneratedArtifactsUtc,
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
        var currentManagedOwner = currentAllocation is null ? null : FindManagedOwner(currentWorkspaceRootPath, currentAllocation.AllocatedPort);
        if (currentAllocation is not null
            && currentAllocation.AllocatedPort > 0
            && !reservedPorts.Contains(currentAllocation.AllocatedPort)
            && currentManagedOwner is null
            && (!inspectHostAvailability || _getPortUsage(currentAllocation.AllocatedPort).Available))
        {
            return WorkspaceRuntimeResourceCatalog.CreatePortAllocation(requirement, currentAllocation.AllocatedPort, currentAllocation.AllocationKind);
        }

        if (currentAllocation is not null && inspectHostAvailability)
        {
            var currentUsage = _getPortUsage(currentAllocation.AllocatedPort);
            if (!currentUsage.Available || currentManagedOwner is not null)
            {
                conflicts.Add(BuildConflict(
                    requirement,
                    currentManagedOwner,
                    currentUsage,
                    $"Persisted allocation {currentAllocation.AllocatedPort} cannot be reused."));
            }
        }

        var preferredOwner = FindManagedOwner(currentWorkspaceRootPath, requirement.PreferredPort);
        var preferredUsage = inspectHostAvailability ? _getPortUsage(requirement.PreferredPort) : WorkspacePortUsage.Free(requirement.PreferredPort);
        if (!reservedPorts.Contains(requirement.PreferredPort)
            && preferredOwner is null
            && preferredUsage.Available)
        {
            return WorkspaceRuntimeResourceCatalog.CreatePortAllocation(requirement, requirement.PreferredPort, "Preferred");
        }

        foreach (var alternativePort in requirement.AlternativePorts)
        {
            var alternativeOwner = FindManagedOwner(currentWorkspaceRootPath, alternativePort);
            var alternativeUsage = inspectHostAvailability ? _getPortUsage(alternativePort) : WorkspacePortUsage.Free(alternativePort);
            if (reservedPorts.Contains(alternativePort) || alternativeOwner is not null || !alternativeUsage.Available)
            {
                continue;
            }

            conflicts.Add(BuildConflict(requirement, preferredOwner, preferredUsage, $"Allocated alternative port {alternativePort}."));
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
                    || !_getPortUsage(candidate).Available)
                {
                    continue;
                }

                conflicts.Add(BuildConflict(requirement, preferredOwner, preferredUsage, $"Allocated dynamic port {candidate}."));
                return WorkspaceRuntimeResourceCatalog.CreatePortAllocation(requirement, candidate, "Dynamic");
            }
        }

        return WorkspaceRuntimeResourceCatalog.CreatePortAllocation(requirement, requirement.PreferredPort, "Preferred");
    }

    private WorkspaceResourceConflictRecord BuildConflict(WorkspacePortRequirement requirement, WorkspaceRecord? owner, WorkspacePortUsage usage, string resolution)
    {
        var ownerName = owner?.Name;
        return new WorkspaceResourceConflictRecord
        {
            ResourceId = requirement.ResourceId,
            DisplayName = requirement.DisplayName,
            PreferredPort = requirement.PreferredPort,
            ConflictKind = string.IsNullOrWhiteSpace(ownerName) ? usage.OwnerKind : "ManagedWorkspace",
            Owner = string.IsNullOrWhiteSpace(ownerName) ? usage.OwnerDisplay : $"workspace {ownerName}",
            Impact = $"Cannot start {requirement.DisplayName} on preferred port {requirement.PreferredPort}.",
            Recommendation = currentAllocationRecommendation(ownerName, usage),
            Resolution = resolution,
        };

        static string currentAllocationRecommendation(string? managedOwner, WorkspacePortUsage currentUsage)
            => !string.IsNullOrWhiteSpace(managedOwner)
                ? "Open the owning managed workspace or reset its runtime before retrying."
                : currentUsage.Available
                    ? "Allocate another port."
                    : "Stop the current owner or choose another managed port before retrying.";
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

    private static WorkspacePortUsage GetPortUsage(int port)
    {
        var dockerOwner = TryGetDockerPortOwner(port);
        if (!string.IsNullOrWhiteSpace(dockerOwner))
        {
            return WorkspacePortUsage.Occupied(port, "DockerContainer", dockerOwner!);
        }

        if (IsPortAvailable(port))
        {
            return WorkspacePortUsage.Free(port);
        }

        var processOwner = TryGetProcessPortOwner(port);
        if (!string.IsNullOrWhiteSpace(processOwner))
        {
            return WorkspacePortUsage.Occupied(port, "ExternalProcess", processOwner!);
        }

        return WorkspacePortUsage.Occupied(port, "ExternalProcess", "Unknown external process");
    }

    private static string? TryGetDockerPortOwner(int port)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "ps --format \"{{.Names}}\\t{{.Ports}}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return null;
            }

            if (!process.WaitForExit(3000) || process.ExitCode != 0)
            {
                return null;
            }

            foreach (var line in process.StandardOutput.ReadToEnd().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t', 2);
                if (parts.Length < 2 || !parts[1].Contains($":{port}->", StringComparison.Ordinal))
                {
                    continue;
                }

                return parts[0].Trim();
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? TryGetProcessPortOwner(int port)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"$connections = Get-NetTCPConnection -LocalPort {port} -State Listen -ErrorAction SilentlyContinue; if ($connections) {{ foreach ($connection in ($connections | Select-Object -First 1)) {{ try {{ (Get-Process -Id $connection.OwningProcess -ErrorAction Stop).ProcessName }} catch {{ 'pid ' + $connection.OwningProcess }} }} }}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                if (process is not null && process.WaitForExit(3000) && process.ExitCode == 0)
                {
                    var owner = process.StandardOutput.ReadToEnd().Trim();
                    return string.IsNullOrWhiteSpace(owner) ? null : owner;
                }

                return null;
            }

            using var ssProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"-lc \"ss -ltnp '( sport = :{port} )' 2>/dev/null || true\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (ssProcess is not null && ssProcess.WaitForExit(3000))
            {
                var output = ssProcess.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrWhiteSpace(output))
                {
                    return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().Trim();
                }
            }
        }
        catch
        {
        }

        return null;
    }
}

public sealed record WorkspacePortUsage(int Port, bool Available, string OwnerKind, string OwnerDisplay)
{
    public static WorkspacePortUsage Free(int port) => new(port, true, string.Empty, string.Empty);

    public static WorkspacePortUsage Occupied(int port, string ownerKind, string ownerDisplay) => new(port, false, ownerKind, ownerDisplay);
}
