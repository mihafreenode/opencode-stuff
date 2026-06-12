using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class AgentProfileResolverTests
{
    [Fact]
    public void Resolve_UsesBuiltInDefault_WhenNothingElseIsConfigured()
    {
        var resolver = new AgentProfileResolver();
        var resolved = resolver.Resolve(new WorkspaceDefinition());

        Assert.Equal("opencode-default", resolved.ProfileId);
        Assert.Equal("opencode", resolved.Provider);
        Assert.Equal("zen", resolved.Connection);
        Assert.Equal("big-pickle", resolved.Model);
        Assert.True(resolved.UsesBuiltInDefault);
    }

    [Fact]
    public void Resolve_PrefersWorkspaceProfileOverUserAndCatalogDefaults()
    {
        var resolver = new AgentProfileResolver();
        var resolved = resolver.Resolve(
            new WorkspaceDefinition
            {
                Agent = new AgentPreferences { Profile = "opencode-default" },
            },
            new AgentPreferences { Profile = "user-profile" },
            new AgentPreferences { Profile = "catalog-profile" });

        Assert.Equal("workspace profile", resolved.ResolutionSource);
        Assert.Equal("opencode-default", resolved.ProfileId);
    }

    [Fact]
    public void Resolve_PrefersWorkspaceDirectOverrideOverProfileReference()
    {
        var resolver = new AgentProfileResolver();
        var resolved = resolver.Resolve(
            new WorkspaceDefinition
            {
                Agent = new AgentPreferences
                {
                    Profile = "opencode-default",
                    Provider = "custom-provider",
                    Connection = "custom-connection",
                    Model = "custom-model",
                },
            });

        Assert.Equal("workspace override", resolved.ResolutionSource);
        Assert.Equal("custom-provider", resolved.Provider);
        Assert.Equal("custom-connection", resolved.Connection);
        Assert.Equal("custom-model", resolved.Model);
        Assert.False(resolved.UsesBuiltInDefault);
    }

    [Fact]
    public void Resolve_UsesUserPreferencesBeforeCatalogDefaults()
    {
        var resolver = new AgentProfileResolver();
        var resolved = resolver.Resolve(
            new WorkspaceDefinition
            {
                Agent = new AgentPreferences { Profile = string.Empty },
            },
            new AgentPreferences { Profile = "opencode-default", Provider = "user-provider", Connection = "user-connection", Model = "user-model" },
            new AgentPreferences { Provider = "catalog-provider", Connection = "catalog-connection", Model = "catalog-model" });

        Assert.Equal("user preferences", resolved.ResolutionSource);
        Assert.Equal("user-provider", resolved.Provider);
        Assert.Equal("user-connection", resolved.Connection);
        Assert.Equal("user-model", resolved.Model);
    }
}
