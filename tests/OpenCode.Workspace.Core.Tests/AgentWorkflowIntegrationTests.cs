using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class AgentWorkflowIntegrationTests
{
    [Fact]
    public void AgentWorkflowPath_RoundTripsSkillAndMcpSelectionsThroughCatalogResolution()
    {
        var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var yaml = new WorkspaceYamlService();
        var resolver = new AgentProfileResolver();

        var userRequest = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "agent-workflow" },
            Skills = ["playwright"],
            Mcp = ["github"],
            Agent = new AgentPreferences { Profile = "opencode-default" },
        };

        var persistedDefinition = yaml.Write(userRequest);
        var filePath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(filePath, persistedDefinition);

            var result = yaml.Read(filePath);
            var selectedSkill = Assert.Single(provider.LoadSkills(), skill => result.Skills.Contains(skill.Id, StringComparer.OrdinalIgnoreCase));
            var invokedMcp = Assert.Single(provider.LoadMcpModules(), module => result.Mcp.Contains(module.Id, StringComparer.OrdinalIgnoreCase));
            var resolvedAgent = resolver.Resolve(result);

            Assert.Contains("playwright", result.Skills);
            Assert.Contains("github", result.Mcp);
            Assert.Equal("playwright", selectedSkill.Id);
            Assert.Contains("playwright", selectedSkill.Dependencies.Features);
            Assert.Equal("github", invokedMcp.Id);
            Assert.Equal("opencode-default", resolvedAgent.ProfileId);
            Assert.Equal("opencode", resolvedAgent.Provider);
            Assert.Equal("zen", resolvedAgent.Connection);
            Assert.Equal("big-pickle", resolvedAgent.Model);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
