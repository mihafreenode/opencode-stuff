using OpenCode.Workspace.Core.Workspaces;
using YamlDotNet.Serialization;

namespace OpenCode.Workspace.Core.Models;

public sealed class WorkspaceManagedRuntimeResources
{
    [YamlMember(Alias = "identity")]
    public WorkspaceRuntimeIdentity Identity { get; init; } = new();

    [YamlMember(Alias = "ports")]
    public List<WorkspacePortAllocationRecord> Ports { get; init; } = [];

    [YamlMember(Alias = "serviceEndpoints")]
    public List<WorkspaceServiceEndpointRecord> ServiceEndpoints { get; init; } = [];

    [YamlMember(Alias = "runtimeIdentifiers")]
    public List<WorkspaceRuntimeIdentifierRecord> RuntimeIdentifiers { get; init; } = [];

    [YamlMember(Alias = "conflicts")]
    public List<WorkspaceResourceConflictRecord> Conflicts { get; init; } = [];
}

public sealed class WorkspaceRuntimeIdentity
{
    [YamlMember(Alias = "workspaceId")]
    public string WorkspaceId { get; init; } = string.Empty;

    [YamlMember(Alias = "workspaceName")]
    public string WorkspaceName { get; init; } = string.Empty;

    [YamlMember(Alias = "workspaceSlug")]
    public string WorkspaceSlug { get; init; } = string.Empty;
}

public sealed class WorkspacePortAllocationRecord
{
    [YamlMember(Alias = "resourceId")]
    public string ResourceId { get; init; } = string.Empty;

    [YamlMember(Alias = "serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [YamlMember(Alias = "displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [YamlMember(Alias = "protocol")]
    public string Protocol { get; init; } = string.Empty;

    [YamlMember(Alias = "host")]
    public string Host { get; init; } = "localhost";

    [YamlMember(Alias = "containerPort")]
    public int ContainerPort { get; init; }

    [YamlMember(Alias = "preferredPort")]
    public int PreferredPort { get; init; }

    [YamlMember(Alias = "allocatedPort")]
    public int AllocatedPort { get; init; }

    [YamlMember(Alias = "alternativePorts")]
    public List<int> AlternativePorts { get; init; } = [];

    [YamlMember(Alias = "allocationKind")]
    public string AllocationKind { get; init; } = string.Empty;

    [YamlMember(Alias = "automatic")]
    public bool Automatic { get; init; }

    [YamlMember(Alias = "endpoint")]
    public string Endpoint { get; init; } = string.Empty;

    [YamlMember(Alias = "openUrl")]
    public string OpenUrl { get; init; } = string.Empty;
}

public sealed class WorkspaceServiceEndpointRecord
{
    [YamlMember(Alias = "serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [YamlMember(Alias = "displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [YamlMember(Alias = "endpoint")]
    public string Endpoint { get; init; } = string.Empty;

    [YamlMember(Alias = "openUrl")]
    public string OpenUrl { get; init; } = string.Empty;
}

public sealed class WorkspaceRuntimeIdentifierRecord
{
    [YamlMember(Alias = "resourceType")]
    public string ResourceType { get; init; } = string.Empty;

    [YamlMember(Alias = "resourceId")]
    public string ResourceId { get; init; } = string.Empty;

    [YamlMember(Alias = "displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [YamlMember(Alias = "value")]
    public string Value { get; init; } = string.Empty;
}

public sealed class WorkspaceResourceConflictRecord
{
    [YamlMember(Alias = "resourceId")]
    public string ResourceId { get; init; } = string.Empty;

    [YamlMember(Alias = "displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [YamlMember(Alias = "preferredPort")]
    public int PreferredPort { get; init; }

    [YamlMember(Alias = "conflictKind")]
    public string ConflictKind { get; init; } = string.Empty;

    [YamlMember(Alias = "owner")]
    public string Owner { get; init; } = string.Empty;

    [YamlMember(Alias = "impact")]
    public string Impact { get; init; } = string.Empty;

    [YamlMember(Alias = "recommendation")]
    public string Recommendation { get; init; } = string.Empty;

    [YamlMember(Alias = "resolution")]
    public string Resolution { get; init; } = string.Empty;
}

internal sealed class WorkspacePortRequirement
{
    public required string ResourceId { get; init; }
    public required string ServiceId { get; init; }
    public required string DisplayName { get; init; }
    public required string Protocol { get; init; }
    public required int ContainerPort { get; init; }
    public required int PreferredPort { get; init; }
    public List<int> AlternativePorts { get; init; } = [];
    public bool AllowsDynamicAllocation { get; init; } = true;
}

public static class WorkspaceRuntimeResourceCatalog
{
    public const string OracleDatabaseResourceId = "oracle-database-port";
    public const string OracleOrdsResourceId = "oracle-ords-port";
    public const string PostgresResourceId = "postgres-port";
    public const string PgAdminResourceId = "pgadmin-port";
    public const string MarimoResourceId = "marimo-port";

    internal static IReadOnlyList<WorkspacePortRequirement> BuildPortRequirements(WorkspaceDefinition definition)
    {
        var requirements = new List<WorkspacePortRequirement>();

        if (OracleWorkspaceFamily.IsOracleWorkspace(definition))
        {
            var settings = OracleWorkspaceSettings.From(definition);
            requirements.Add(new WorkspacePortRequirement
            {
                ResourceId = OracleDatabaseResourceId,
                ServiceId = "oracle-database",
                DisplayName = "Oracle Database",
                Protocol = "tcp",
                ContainerPort = OracleWorkspaceSettings.ContainerListenerPort,
                PreferredPort = settings.HostPort,
                AlternativePorts = BuildFallbackPorts(settings.HostPort).ToList(),
            });

            if (OracleWorkspaceFamily.HasApex(definition))
            {
                requirements.Add(new WorkspacePortRequirement
                {
                    ResourceId = OracleOrdsResourceId,
                    ServiceId = "ords",
                    DisplayName = "ORDS",
                    Protocol = "http",
                    ContainerPort = OracleWorkspaceSettings.ContainerOrdsPort,
                    PreferredPort = settings.OrdsPort,
                    AlternativePorts = BuildFallbackPorts(settings.OrdsPort).ToList(),
                });
            }
        }

        if (definition.Services.Contains("postgres", StringComparer.OrdinalIgnoreCase))
        {
            requirements.Add(new WorkspacePortRequirement
            {
                ResourceId = PostgresResourceId,
                ServiceId = "postgres",
                DisplayName = "PostgreSQL",
                Protocol = "tcp",
                ContainerPort = 5432,
                PreferredPort = 15432,
                AlternativePorts = [15433, 15434],
            });
        }

        if (definition.Services.Contains("pgadmin", StringComparer.OrdinalIgnoreCase))
        {
            requirements.Add(new WorkspacePortRequirement
            {
                ResourceId = PgAdminResourceId,
                ServiceId = "pgadmin",
                DisplayName = "pgAdmin",
                Protocol = "http",
                ContainerPort = 80,
                PreferredPort = 18080,
                AlternativePorts = [18081, 18082],
            });
        }

        if (definition.Features.Contains("analytics-reporting", StringComparer.OrdinalIgnoreCase))
        {
            var settings = AnalyticsWorkspaceSettings.From(definition);
            requirements.Add(new WorkspacePortRequirement
            {
                ResourceId = MarimoResourceId,
                ServiceId = "marimo",
                DisplayName = "Marimo",
                Protocol = "http",
                ContainerPort = AnalyticsWorkspaceSettings.ContainerMarimoPort,
                PreferredPort = settings.MarimoPort,
                AlternativePorts = BuildFallbackPorts(settings.MarimoPort).ToList(),
            });
        }

        return requirements;
    }

    public static WorkspacePortAllocationRecord ResolvePortAllocation(WorkspaceDefinition definition, WorkspaceRuntimeStateRecord? state, string resourceId)
    {
        var existing = state?.Resources.Ports.FirstOrDefault(item => string.Equals(item.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var requirement = BuildPortRequirements(definition).FirstOrDefault(item => string.Equals(item.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
            ?? BuildDefaultRequirement(definition, resourceId);
        return CreatePortAllocation(requirement, requirement.PreferredPort, allocationKind: "Preferred");
    }

    public static int ResolveAllocatedPort(WorkspaceDefinition definition, WorkspaceRuntimeStateRecord? state, string resourceId)
        => ResolvePortAllocation(definition, state, resourceId).AllocatedPort;

    public static string ResolveServiceOpenUrl(WorkspaceDefinition definition, WorkspaceRuntimeStateRecord? state, string serviceId)
    {
        var endpoint = state?.Resources.ServiceEndpoints.FirstOrDefault(item => string.Equals(item.ServiceId, serviceId, StringComparison.OrdinalIgnoreCase));
        if (endpoint is not null && !string.IsNullOrWhiteSpace(endpoint.OpenUrl))
        {
            return endpoint.OpenUrl;
        }

        return serviceId switch
        {
            "ords" or "sql-developer-web" or "apex" => $"http://localhost:{ResolveAllocatedPort(definition, state, OracleOrdsResourceId)}/ords/_/landing",
            "rest-apis" => $"http://localhost:{ResolveAllocatedPort(definition, state, OracleOrdsResourceId)}/ords/",
            "pgadmin" => $"http://localhost:{ResolveAllocatedPort(definition, state, PgAdminResourceId)}/",
            "marimo" => $"http://localhost:{ResolveAllocatedPort(definition, state, MarimoResourceId)}/",
            _ => string.Empty,
        };
    }

    public static string ResolveServiceEndpoint(WorkspaceDefinition definition, WorkspaceRuntimeStateRecord? state, string serviceId)
    {
        var endpoint = state?.Resources.ServiceEndpoints.FirstOrDefault(item => string.Equals(item.ServiceId, serviceId, StringComparison.OrdinalIgnoreCase));
        if (endpoint is not null && !string.IsNullOrWhiteSpace(endpoint.Endpoint))
        {
            return endpoint.Endpoint;
        }

        return serviceId switch
        {
            "oracle-database" => $"tcp://localhost:{ResolveAllocatedPort(definition, state, OracleDatabaseResourceId)}",
            "ords" or "sql-developer-web" or "rest-apis" or "apex" => $"http://localhost:{ResolveAllocatedPort(definition, state, OracleOrdsResourceId)}/ords/",
            "postgres" => $"tcp://localhost:{ResolveAllocatedPort(definition, state, PostgresResourceId)}",
            "pgadmin" => $"http://localhost:{ResolveAllocatedPort(definition, state, PgAdminResourceId)}/",
            "marimo" => $"http://localhost:{ResolveAllocatedPort(definition, state, MarimoResourceId)}/",
            _ => string.Empty,
        };
    }

    internal static WorkspacePortAllocationRecord CreatePortAllocation(WorkspacePortRequirement requirement, int allocatedPort, string allocationKind)
    {
        var endpoint = requirement.Protocol switch
        {
            "http" => $"http://localhost:{allocatedPort}/",
            _ => $"tcp://localhost:{allocatedPort}",
        };
        var openUrl = requirement.ResourceId switch
        {
            OracleOrdsResourceId => $"http://localhost:{allocatedPort}/ords/_/landing",
            PgAdminResourceId => $"http://localhost:{allocatedPort}/",
            MarimoResourceId => $"http://localhost:{allocatedPort}/",
            _ => endpoint,
        };

        return new WorkspacePortAllocationRecord
        {
            ResourceId = requirement.ResourceId,
            ServiceId = requirement.ServiceId,
            DisplayName = requirement.DisplayName,
            Protocol = requirement.Protocol,
            ContainerPort = requirement.ContainerPort,
            PreferredPort = requirement.PreferredPort,
            AllocatedPort = allocatedPort,
            AlternativePorts = requirement.AlternativePorts,
            AllocationKind = allocationKind,
            Automatic = !string.Equals(allocationKind, "Preferred", StringComparison.OrdinalIgnoreCase),
            Endpoint = endpoint,
            OpenUrl = openUrl,
        };
    }

    private static int[] BuildFallbackPorts(int preferredPort)
        => [preferredPort + 1, preferredPort + 2];

    private static WorkspacePortRequirement BuildDefaultRequirement(WorkspaceDefinition definition, string resourceId)
        => resourceId switch
        {
            OracleDatabaseResourceId => new WorkspacePortRequirement
            {
                ResourceId = OracleDatabaseResourceId,
                ServiceId = "oracle-database",
                DisplayName = "Oracle Database",
                Protocol = "tcp",
                ContainerPort = OracleWorkspaceSettings.ContainerListenerPort,
                PreferredPort = OracleWorkspaceSettings.From(definition).HostPort,
                AlternativePorts = BuildFallbackPorts(OracleWorkspaceSettings.From(definition).HostPort).ToList(),
            },
            OracleOrdsResourceId => new WorkspacePortRequirement
            {
                ResourceId = OracleOrdsResourceId,
                ServiceId = "ords",
                DisplayName = "ORDS",
                Protocol = "http",
                ContainerPort = OracleWorkspaceSettings.ContainerOrdsPort,
                PreferredPort = OracleWorkspaceSettings.From(definition).OrdsPort,
                AlternativePorts = BuildFallbackPorts(OracleWorkspaceSettings.From(definition).OrdsPort).ToList(),
            },
            PostgresResourceId => new WorkspacePortRequirement
            {
                ResourceId = PostgresResourceId,
                ServiceId = "postgres",
                DisplayName = "PostgreSQL",
                Protocol = "tcp",
                ContainerPort = 5432,
                PreferredPort = 15432,
                AlternativePorts = [15433, 15434],
            },
            PgAdminResourceId => new WorkspacePortRequirement
            {
                ResourceId = PgAdminResourceId,
                ServiceId = "pgadmin",
                DisplayName = "pgAdmin",
                Protocol = "http",
                ContainerPort = 80,
                PreferredPort = 18080,
                AlternativePorts = [18081, 18082],
            },
            MarimoResourceId => new WorkspacePortRequirement
            {
                ResourceId = MarimoResourceId,
                ServiceId = "marimo",
                DisplayName = "Marimo",
                Protocol = "http",
                ContainerPort = AnalyticsWorkspaceSettings.ContainerMarimoPort,
                PreferredPort = AnalyticsWorkspaceSettings.From(definition).MarimoPort,
                AlternativePorts = BuildFallbackPorts(AnalyticsWorkspaceSettings.From(definition).MarimoPort).ToList(),
            },
            _ => throw new InvalidOperationException($"Unknown runtime resource '{resourceId}'."),
        };
}
