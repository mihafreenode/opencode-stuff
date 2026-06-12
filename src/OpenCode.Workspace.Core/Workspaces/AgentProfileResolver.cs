using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

/// <summary>
/// Resolves the effective agent profile using the intended precedence order.
/// The built-in OpenCode default keeps a new workspace usable immediately even
/// when no explicit agent configuration exists yet.
/// </summary>
public sealed class AgentProfileResolver
{
    public static readonly ResolvedAgentProfile BuiltInDefault = new()
    {
        ProfileId = "opencode-default",
        Provider = "opencode",
        Connection = "zen",
        Model = "big-pickle",
        ResolutionSource = "built-in default",
        UsesBuiltInDefault = true,
    };

    public ResolvedAgentProfile Resolve(
        WorkspaceDefinition definition,
        AgentPreferences? userPreferences = null,
        AgentPreferences? catalogDefaults = null)
    {
        var workspace = Normalize(definition.Agent);
        if (HasDirectOverride(workspace))
        {
            var workspaceOverride = workspace!;
            var workspaceProfile = workspaceOverride.Profile;
            return new ResolvedAgentProfile
            {
                ProfileId = string.IsNullOrWhiteSpace(workspaceProfile) ? BuiltInDefault.ProfileId : workspaceProfile,
                Provider = workspaceOverride.Provider ?? BuiltInDefault.Provider,
                Connection = workspaceOverride.Connection ?? BuiltInDefault.Connection,
                Model = workspaceOverride.Model ?? BuiltInDefault.Model,
                ResolutionSource = "workspace override",
                UsesBuiltInDefault = false,
            };
        }

        if (!string.IsNullOrWhiteSpace(workspace?.Profile))
        {
            return ResolveFromProfile(workspace.Profile, "workspace profile");
        }

        var normalizedUser = Normalize(userPreferences);
        if (HasDirectOverride(normalizedUser))
        {
            return new ResolvedAgentProfile
            {
                ProfileId = string.IsNullOrWhiteSpace(normalizedUser!.Profile) ? BuiltInDefault.ProfileId : normalizedUser.Profile,
                Provider = normalizedUser.Provider ?? BuiltInDefault.Provider,
                Connection = normalizedUser.Connection ?? BuiltInDefault.Connection,
                Model = normalizedUser.Model ?? BuiltInDefault.Model,
                ResolutionSource = "user preferences",
                UsesBuiltInDefault = false,
            };
        }

        if (!string.IsNullOrWhiteSpace(normalizedUser?.Profile))
        {
            return ResolveFromProfile(normalizedUser.Profile!, "user preferences");
        }

        var normalizedCatalog = Normalize(catalogDefaults);
        if (HasDirectOverride(normalizedCatalog))
        {
            return new ResolvedAgentProfile
            {
                ProfileId = string.IsNullOrWhiteSpace(normalizedCatalog!.Profile) ? BuiltInDefault.ProfileId : normalizedCatalog.Profile,
                Provider = normalizedCatalog.Provider ?? BuiltInDefault.Provider,
                Connection = normalizedCatalog.Connection ?? BuiltInDefault.Connection,
                Model = normalizedCatalog.Model ?? BuiltInDefault.Model,
                ResolutionSource = "catalog defaults",
                UsesBuiltInDefault = false,
            };
        }

        if (!string.IsNullOrWhiteSpace(normalizedCatalog?.Profile))
        {
            return ResolveFromProfile(normalizedCatalog.Profile!, "catalog defaults");
        }

        return BuiltInDefault;
    }

    private static ResolvedAgentProfile ResolveFromProfile(string profileId, string resolutionSource)
    {
        if (string.Equals(profileId, BuiltInDefault.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedAgentProfile
            {
                ProfileId = BuiltInDefault.ProfileId,
                Provider = BuiltInDefault.Provider,
                Connection = BuiltInDefault.Connection,
                Model = BuiltInDefault.Model,
                ResolutionSource = resolutionSource,
                UsesBuiltInDefault = true,
            };
        }

        // Future profile catalogs can resolve additional profiles here.
        return new ResolvedAgentProfile
        {
            ProfileId = profileId,
            Provider = BuiltInDefault.Provider,
            Connection = BuiltInDefault.Connection,
            Model = BuiltInDefault.Model,
            ResolutionSource = resolutionSource,
            UsesBuiltInDefault = false,
        };
    }

    private static AgentPreferences? Normalize(AgentPreferences? preferences)
    {
        if (preferences is null)
        {
            return null;
        }

        return new AgentPreferences
        {
            Profile = string.IsNullOrWhiteSpace(preferences.Profile) ? string.Empty : preferences.Profile.Trim(),
            Provider = string.IsNullOrWhiteSpace(preferences.Provider) ? null : preferences.Provider.Trim(),
            Connection = string.IsNullOrWhiteSpace(preferences.Connection) ? null : preferences.Connection.Trim(),
            Model = string.IsNullOrWhiteSpace(preferences.Model) ? null : preferences.Model.Trim(),
        };
    }

    private static bool HasDirectOverride(AgentPreferences? preferences)
        => preferences is not null && (!string.IsNullOrWhiteSpace(preferences.Provider) || !string.IsNullOrWhiteSpace(preferences.Connection) || !string.IsNullOrWhiteSpace(preferences.Model));
}
