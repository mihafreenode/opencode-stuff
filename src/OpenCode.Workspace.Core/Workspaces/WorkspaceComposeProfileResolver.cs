using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

internal static class WorkspaceComposeProfileResolver
{
    public static IReadOnlyList<string> GetRuntimeProfiles(WorkspaceDefinition definition)
    {
        var profiles = definition.Services
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(service => service, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (OracleWorkspaceFamily.HasApex(definition)
            && profiles.All(profile => !string.Equals(profile, "oracle-apex", StringComparison.OrdinalIgnoreCase)))
        {
            profiles.Add("oracle-apex");
        }

        return profiles
            .OrderBy(profile => profile, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> GetWorkspaceImageBuildProfiles(WorkspaceDefinition definition)
        => GetRuntimeProfiles(definition);

    public static IReadOnlyList<string> GetServiceProfiles(WorkspaceDefinition definition, ServiceManifest service)
    {
        var profiles = service.Profiles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(profile => profile, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (OracleWorkspaceFamily.HasApex(definition)
            && (string.Equals(service.Id, "oracle-demo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(service.Id, "oracle-ords", StringComparison.OrdinalIgnoreCase))
            && profiles.All(profile => !string.Equals(profile, "oracle-apex", StringComparison.OrdinalIgnoreCase)))
        {
            profiles.Add("oracle-apex");
        }

        return profiles
            .OrderBy(profile => profile, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
