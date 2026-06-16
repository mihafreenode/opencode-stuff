using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceYamlServiceTests
{
    [Fact]
    public void WriteAndRead_RoundTripsWorkspaceDefinition()
    {
        var service = new WorkspaceYamlService();
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Id = "docs-workspace",
                Name = "docs-workspace",
                Image = "ubuntu:24.04",
            },
            Provider = new WorkspaceProviderDefinition
            {
                Type = "git",
                Url = "https://example.test/docs-workspace.git",
            },
            Runtime = new WorkspaceRuntimeDefinition
            {
                Default = "default",
                Node = 22,
            },
            Features = new List<string> { "core", "document-processing" },
            Services = new List<string> { "postgres", "pgadmin" },
            Skills = new List<string>(),
            Mcp = new List<string>(),
            Terminal = new TerminalPreferences
            {
                InstallIfMissing = true,
                Font = new TerminalFontPreferences { Provider = "nerd-fonts", Family = "JetBrainsMono Nerd Font" },
                Prompt = new TerminalPromptPreferences { Provider = "starship" },
                Utilities = new TerminalUtilityPreferences { Zoxide = true, Fzf = false },
            },
        };

        var yaml = service.Write(definition);
        var filePath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(filePath, yaml);
            var roundTripped = service.Read(filePath);

            Assert.Equal("docs-workspace", roundTripped.Workspace.Name);
            Assert.Equal("docs-workspace", roundTripped.Workspace.Id);
            Assert.Equal("ubuntu:24.04", roundTripped.Workspace.Image);
            Assert.Equal("git", roundTripped.Provider.Type);
            Assert.Equal("https://example.test/docs-workspace.git", roundTripped.Provider.Url);
            Assert.Equal("default", roundTripped.Runtime.Default);
            Assert.Equal(22, roundTripped.Runtime.Node);
            Assert.Contains("core", roundTripped.Features);
            Assert.Contains("document-processing", roundTripped.Features);
            Assert.Contains("postgres", roundTripped.Services);
            Assert.Contains("pgadmin", roundTripped.Services);
            Assert.Equal("JetBrainsMono Nerd Font", roundTripped.Terminal.Font.Family);
            Assert.Equal("starship", roundTripped.Terminal.Prompt.Provider);
            Assert.True(roundTripped.Terminal.Utilities.Zoxide);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Read_WhenNodeVersionIsOmitted_DefaultsToNode22()
    {
        var service = new WorkspaceYamlService();
        var filePath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(filePath, """
workspace:
  id: legacy-workspace
  name: legacy-workspace
  image: ubuntu:24.04
provider:
  type: git
runtime:
  default: default
features:
  - core
skills: []
services: []
mcp: []
agent:
  profile: opencode-default
terminal:
  font:
    provider: nerd-fonts
    family: JetBrainsMono Nerd Font
  prompt:
    provider: starship
  installIfMissing: true
  utilities:
    zoxide: false
    fzf: false
""");

            var definition = service.Read(filePath);

            Assert.Equal(22, definition.Runtime.Node);
            Assert.Equal(22, definition.Runtime.GetEffectiveNodeMajorVersion());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void WriteToFile_PreservesUnrelatedTopLevelValues()
    {
        var service = new WorkspaceYamlService();
        var filePath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(filePath, """
workspace:
  name: existing-workspace
provider:
  type: git
runtime:
  default: default
features:
  - core
services: []
skills: []
mcp: []
agent:
  profile: opencode-default
terminal:
  font:
    provider: nerd-fonts
    family: JetBrainsMono Nerd Font
  prompt:
    provider: starship
  installIfMissing: true
  utilities:
    zoxide: false
    fzf: false
customSection:
  owner: team
  notes:
    - keep me
""");

            service.WriteToFile(filePath, new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata
                {
                    Name = "updated-workspace",
                    Image = "ubuntu:24.04",
                },
                Provider = new WorkspaceProviderDefinition { Type = "git" },
                Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
                Features = ["core"],
                Services = ["postgres"],
                Skills = [],
                Mcp = [],
                Agent = new AgentPreferences { Profile = AgentProfileResolver.BuiltInDefault.ProfileId },
                Terminal = new TerminalPreferences
                {
                    InstallIfMissing = true,
                    Font = new TerminalFontPreferences { Provider = "nerd-fonts", Family = "JetBrainsMono Nerd Font" },
                    Prompt = new TerminalPromptPreferences { Provider = "starship" },
                    Utilities = new TerminalUtilityPreferences(),
                },
            });

            var updatedYaml = File.ReadAllText(filePath);

            Assert.Contains("customSection:", updatedYaml);
            Assert.Contains("owner: team", updatedYaml);
            Assert.Contains("- keep me", updatedYaml);
            Assert.Contains("name: updated-workspace", updatedYaml);
            Assert.Contains("- postgres", updatedYaml);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
