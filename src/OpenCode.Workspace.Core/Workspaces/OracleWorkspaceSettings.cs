using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleWorkspaceSettings
{
    public const string DownloadsRootRelativePath = ".local/oracle/downloads";
    public const string ApexDownloadsRelativePath = ".local/oracle/downloads/apex";
    public const string SqlclDownloadsRelativePath = ".local/oracle/downloads/sqlcl";
    public const string OrdsDownloadsRelativePath = ".local/oracle/downloads/ords";
    public const int DefaultHostPort = 1521;
    public const int DefaultOrdsPort = 8181;
    public const int ContainerListenerPort = 1521;
    public const int ContainerOrdsPort = 8080;
    public const string ApexDownloadsDirectory = "/workspace/.local/oracle/downloads/apex";
    public const string ApexPreferredZipName = "apex.zip";

    public required int HostPort { get; init; }
    public required int OrdsPort { get; init; }

    public string OrdsBaseUrl => $"http://localhost:{OrdsPort}/ords";

    public string ApexLoginUrl => $"{OrdsBaseUrl}/apex_admin";

    public static OracleWorkspaceSettings From(WorkspaceDefinition definition)
    {
        if (definition.Oracle.HostPort is not null && (definition.Oracle.HostPort.Value < 1 || definition.Oracle.HostPort.Value > 65535))
        {
            throw new InvalidOperationException($"Oracle configuration is invalid. oracle.hostPort must be between 1 and 65535, but was '{definition.Oracle.HostPort.Value}'.");
        }

        if (definition.Oracle.OrdsPort is not null && (definition.Oracle.OrdsPort.Value < 1 || definition.Oracle.OrdsPort.Value > 65535))
        {
            throw new InvalidOperationException($"Oracle configuration is invalid. oracle.ordsPort must be between 1 and 65535, but was '{definition.Oracle.OrdsPort.Value}'.");
        }

        return new OracleWorkspaceSettings
        {
            HostPort = definition.Oracle.HostPort is > 0 ? definition.Oracle.HostPort.Value : DefaultHostPort,
            OrdsPort = definition.Oracle.OrdsPort is > 0 ? definition.Oracle.OrdsPort.Value : DefaultOrdsPort,
        };
    }
}
