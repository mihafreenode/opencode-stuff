using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class AnalyticsWorkspaceSettings
{
    public const int DefaultMarimoPort = AnalyticsWorkspacePreferences.DefaultMarimoPort;
    public const int ContainerMarimoPort = 2718;

    public required int MarimoPort { get; init; }

    public static AnalyticsWorkspaceSettings From(WorkspaceDefinition definition)
    {
        return new AnalyticsWorkspaceSettings
        {
            MarimoPort = definition.Analytics.MarimoPort is > 0 ? definition.Analytics.MarimoPort.Value : DefaultMarimoPort,
        };
    }
}
