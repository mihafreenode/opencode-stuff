using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class AnalyticsWorkspaceSettings
{
    public const int DefaultMarimoPort = AnalyticsWorkspacePreferences.DefaultMarimoPort;
    public const int ContainerMarimoPort = 2718;

    public required int MarimoPort { get; init; }

    public static AnalyticsWorkspaceSettings From(WorkspaceDefinition definition)
    {
        var configuredPort = definition.Analytics.MarimoPort;
        if (configuredPort is not null && (configuredPort.Value < 1 || configuredPort.Value > 65535))
        {
            throw new InvalidOperationException($"Analytics configuration is invalid. analytics.marimoPort must be between 1 and 65535, but was '{configuredPort.Value}'.");
        }

        return new AnalyticsWorkspaceSettings
        {
            MarimoPort = configuredPort is > 0 ? configuredPort.Value : DefaultMarimoPort,
        };
    }
}
