using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

public sealed class OracleRuntimeConfiguration
{
    public const string DeploymentProfileEnv = "ORACLE_DEPLOYMENT_PROFILE";
    public const string HostEnv = "ORACLE_HOST";
    public const string PortEnv = "ORACLE_PORT";
    public const string ServiceNameEnv = "ORACLE_SERVICE_NAME";
    public const string AdminUserEnv = "ORACLE_ADMIN_USER";
    public const string AdminPasswordEnv = "ORACLE_PASSWORD";
    public const string DemoUsernameEnv = "ORACLE_DEMO_USERNAME";
    public const string DemoPasswordEnv = "ORACLE_DEMO_PASSWORD";
    public const string DemoConnectionEnv = "ORACLE_DEMO_CONNECTION";
    public const string HostPortEnv = "ORACLE_HOST_PORT";
    public const string OrdsHostEnv = "ORACLE_ORDS_HOST";
    public const string OrdsInternalPortEnv = "ORACLE_ORDS_INTERNAL_PORT";
    public const string OrdsHostPortEnv = "ORACLE_ORDS_PORT";
    public const string OrdsBaseUrlEnv = "ORACLE_ORDS_BASE_URL";
    public const string OrdsInternalBaseUrlEnv = "ORACLE_ORDS_INTERNAL_BASE_URL";
    public const string OrdsPublicUserEnv = "ORACLE_ORDS_PUBLIC_USER";
    public const string OrdsPublicPasswordEnv = "ORACLE_ORDS_PUBLIC_PASSWORD";
    public const string ApexPublicUserEnv = "ORACLE_APEX_PUBLIC_USER";
    public const string ApexLoginUrlEnv = "ORACLE_APEX_LOGIN_URL";
    public const string ApexMediaDirEnv = "ORACLE_APEX_MEDIA_DIR";
    public const string ApexMediaPreferredZipEnv = "ORACLE_APEX_MEDIA_PREFERRED_ZIP";

    public required OracleWorkspaceKind WorkspaceKind { get; init; }
    public required string DeploymentProfile { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string ServiceName { get; init; }
    public required string AdminUser { get; init; }
    public required string AdminPassword { get; init; }
    public required string DemoUsername { get; init; }
    public required string DemoPassword { get; init; }
    public required int HostPort { get; init; }
    public required int OrdsPort { get; init; }
    public required string OrdsHost { get; init; }
    public required string OrdsPublicUser { get; init; }
    public required string OrdsPublicPassword { get; init; }
    public required string ApexPublicUser { get; init; }
    public required string ApexMediaDir { get; init; }
    public required string ApexMediaPreferredZip { get; init; }

    public string DemoConnection => $"{DemoUsername}/{DemoPassword}@//{Host}:{Port}/{ServiceName}";
    public string OrdsBaseUrl => $"http://localhost:{OrdsPort}/ords";
    public string OrdsInternalBaseUrl => $"http://{OrdsHost}:{OracleWorkspaceSettings.ContainerOrdsPort}/ords";
    public string ApexLoginUrl => $"{OrdsBaseUrl}/apex";

    public bool HasApex => WorkspaceKind is OracleWorkspaceKind.Apex or OracleWorkspaceKind.ApexLang;

    public IReadOnlyDictionary<string, string> ToEnvironmentVariables()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [DeploymentProfileEnv] = DeploymentProfile,
            [HostEnv] = Host,
            [PortEnv] = Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [ServiceNameEnv] = ServiceName,
            [AdminUserEnv] = AdminUser,
            [AdminPasswordEnv] = AdminPassword,
            [DemoUsernameEnv] = DemoUsername,
            [DemoPasswordEnv] = DemoPassword,
            [DemoConnectionEnv] = DemoConnection,
            [HostPortEnv] = HostPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [OrdsHostPortEnv] = OrdsPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [OrdsBaseUrlEnv] = OrdsBaseUrl,
            [ApexLoginUrlEnv] = ApexLoginUrl,
        };

        if (HasApex)
        {
            values[OrdsHostEnv] = OrdsHost;
            values[OrdsInternalPortEnv] = OracleWorkspaceSettings.ContainerOrdsPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            values[OrdsInternalBaseUrlEnv] = OrdsInternalBaseUrl;
            values[OrdsPublicUserEnv] = OrdsPublicUser;
            values[OrdsPublicPasswordEnv] = OrdsPublicPassword;
            values[ApexPublicUserEnv] = ApexPublicUser;
            values[ApexMediaDirEnv] = ApexMediaDir;
            values[ApexMediaPreferredZipEnv] = ApexMediaPreferredZip;
        }

        return values;
    }

    public IReadOnlyList<string> GetProvisioningRequiredEnvironmentVariables()
    {
        var values = new List<string>
        {
            DeploymentProfileEnv,
            HostEnv,
            PortEnv,
            ServiceNameEnv,
            AdminUserEnv,
            AdminPasswordEnv,
            DemoUsernameEnv,
            DemoPasswordEnv,
        };

        if (HasApex)
        {
            values.Add(OrdsInternalBaseUrlEnv);
            values.Add(OrdsPublicUserEnv);
            values.Add(OrdsPublicPasswordEnv);
            values.Add(ApexPublicUserEnv);
            values.Add(ApexMediaDirEnv);
            values.Add(ApexMediaPreferredZipEnv);
        }

        return values;
    }

    public static OracleRuntimeConfiguration From(WorkspaceDefinition definition, WorkspaceRuntimeStateRecord? runtimeState = null)
    {
        var settings = OracleWorkspaceSettings.From(definition);
        var kind = OracleWorkspaceFamily.Detect(definition);
        return new OracleRuntimeConfiguration
        {
            WorkspaceKind = kind,
            DeploymentProfile = kind switch
            {
                OracleWorkspaceKind.ApexLang => "apexlang",
                OracleWorkspaceKind.Apex => "apex",
                OracleWorkspaceKind.PlSql => "plsql",
                _ => "none",
            },
            Host = OracleWorkspaceFamily.OracleDatabaseServiceId,
            Port = OracleWorkspaceSettings.ContainerListenerPort,
            ServiceName = "FREEPDB1",
            AdminUser = "SYS",
            AdminPassword = "change-on-first-demo",
            DemoUsername = "demo_user",
            DemoPassword = "demo_password",
            HostPort = WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(definition, runtimeState, WorkspaceRuntimeResourceCatalog.OracleDatabaseResourceId),
            OrdsPort = OracleWorkspaceFamily.HasApex(definition)
                ? WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(definition, runtimeState, WorkspaceRuntimeResourceCatalog.OracleOrdsResourceId)
                : settings.OrdsPort,
            OrdsHost = OracleWorkspaceFamily.OracleOrdsServiceId,
            OrdsPublicUser = "ORDS_PUBLIC_USER",
            OrdsPublicPassword = "change-on-first-demo",
            ApexPublicUser = "APEX_PUBLIC_USER",
            ApexMediaDir = OracleWorkspaceSettings.ApexDownloadsDirectory,
            ApexMediaPreferredZip = OracleWorkspaceSettings.ApexPreferredZipName,
        };
    }
}
