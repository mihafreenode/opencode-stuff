using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleWorkspaceSettings
{
    public const int DefaultHostPort = 1521;
    public const int DefaultOrdsPort = 8181;
    public const int ContainerListenerPort = 1521;
    public const int ContainerOrdsPort = 8181;

    public required int HostPort { get; init; }
    public required int OrdsPort { get; init; }

    public string OrdsBaseUrl => $"http://localhost:{OrdsPort}/ords";

    public string ApexLoginUrl => $"{OrdsBaseUrl}/apex";

    public static OracleWorkspaceSettings From(WorkspaceDefinition definition)
    {
        return new OracleWorkspaceSettings
        {
            HostPort = definition.Oracle.HostPort is > 0 ? definition.Oracle.HostPort.Value : DefaultHostPort,
            OrdsPort = definition.Oracle.OrdsPort is > 0 ? definition.Oracle.OrdsPort.Value : DefaultOrdsPort,
        };
    }
}
