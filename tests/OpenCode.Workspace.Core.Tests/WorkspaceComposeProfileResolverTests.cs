using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using System.Reflection;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceComposeProfileResolverTests
{
    [Fact]
    public void GetWorkspaceImageBuildProfiles_UsesOracleDemoProfileForPlSqlTemplate()
    {
        var definition = new WorkspaceDefinition
        {
            Services = ["oracle-demo"],
        };

        var method = typeof(DockerWorkspaceImageBuilder).Assembly
            .GetType("OpenCode.Workspace.Core.Workspaces.WorkspaceComposeProfileResolver")?
            .GetMethod("GetWorkspaceImageBuildProfiles", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        var profiles = (IReadOnlyList<string>)method!.Invoke(null, [definition])!;

        Assert.Equal(["oracle-demo"], profiles);
    }

    [Fact]
    public void GetWorkspaceImageBuildProfiles_IncludesRuntimeProfilesForApexTemplate()
    {
        var definition = new WorkspaceDefinition
        {
            Features = ["oracle-apex-demo"],
            Services = ["oracle-demo", "oracle-ords"],
        };

        var method = typeof(DockerWorkspaceImageBuilder).Assembly
            .GetType("OpenCode.Workspace.Core.Workspaces.WorkspaceComposeProfileResolver")?
            .GetMethod("GetWorkspaceImageBuildProfiles", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        var profiles = (IReadOnlyList<string>)method!.Invoke(null, [definition])!;

        Assert.Equal(["oracle-apex", "oracle-demo", "oracle-ords"], profiles);
    }
}
