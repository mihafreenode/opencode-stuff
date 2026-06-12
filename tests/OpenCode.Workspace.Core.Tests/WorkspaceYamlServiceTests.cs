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
                Name = "docs-workspace",
                Image = "ubuntu:24.04",
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
            Assert.Equal("ubuntu:24.04", roundTripped.Workspace.Image);
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
}
