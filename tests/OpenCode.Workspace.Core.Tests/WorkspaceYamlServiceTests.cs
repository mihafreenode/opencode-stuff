using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using YamlDotNet.RepresentationModel;

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
            Oracle = new OracleWorkspacePreferences
            {
                HostPort = 1522,
                OrdsPort = 8182,
                Apex = new OracleApexWorkspacePreferences
                {
                    DefaultEnvironment = "dev",
                    Environments = new Dictionary<string, OracleApexEnvironmentPreferences>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["dev"] = new()
                        {
                            Workspace = "TEST",
                            ParsingSchema = "TESTSCHEMA",
                            ApplicationId = 100,
                            SqlclProfile = "local-apex-dev",
                            SyncMode = WorkspaceSynchronizationModes.Manual,
                            SourcePath = "src/apex",
                        },
                    },
                },
            },
            Analytics = new AnalyticsWorkspacePreferences { MarimoPort = 3818 },
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
            Assert.Equal(1522, roundTripped.Oracle.HostPort);
            Assert.Equal(8182, roundTripped.Oracle.OrdsPort);
            Assert.Null(roundTripped.Oracle.DatabaseImage);
            Assert.Equal("dev", roundTripped.Oracle.Apex.DefaultEnvironment);
            Assert.True(roundTripped.Oracle.Apex.Environments.ContainsKey("dev"));
            Assert.Equal(100, roundTripped.Oracle.Apex.Environments["dev"].ApplicationId);
            Assert.Equal("local-apex-dev", roundTripped.Oracle.Apex.Environments["dev"].SqlclProfile);
            Assert.Equal(3818, roundTripped.Analytics.MarimoPort);
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
    public void WriteAndRead_PreservesOracleDatabaseImageOverride()
    {
        var service = new WorkspaceYamlService();
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-image-override" },
            Oracle = new OracleWorkspacePreferences
            {
                DatabaseImage = "gvenzl/oracle-free:23-slim-faststart",
                HostPort = 1522,
                OrdsPort = 8182,
            },
        };

        var yaml = service.Write(definition);
        var filePath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(filePath, yaml);
            var roundTripped = service.Read(filePath);

            Assert.Contains("databaseImage: gvenzl/oracle-free:23-slim-faststart", yaml, StringComparison.Ordinal);
            Assert.Equal("gvenzl/oracle-free:23-slim-faststart", roundTripped.Oracle.DatabaseImage);
            Assert.Equal(1522, roundTripped.Oracle.HostPort);
            Assert.Equal(8182, roundTripped.Oracle.OrdsPort);
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

    [Fact]
    public void ReadAndWrite_PreservesNestedKnowledgePackSettings()
    {
        var service = new WorkspaceYamlService();
        var filePath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(filePath, """
workspace:
  name: knowledge-workspace
provider:
  type: git
runtime:
  default: default
features:
  - core
services: []
skills: []
mcp: []
knowledgePacks:
  - provider: apexlang-atlas
    enabled: true
    mode: optional
    settings:
      buildId: "26.1.0+3102"
      metadataUrl: "https://example.test/meta.json"
      builtinCatalogUrl: "https://example.test/catalog.json"
      nested:
        keep:
          - one
          - two
        flags:
          strict: true
      customObject:
        child:
          value: test
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

            Assert.Single(definition.KnowledgePacks);
            var pack = definition.KnowledgePacks[0];
            Assert.Equal("apexlang-atlas", pack.Provider);
            Assert.True(pack.Enabled);
            Assert.Equal(WorkspaceKnowledgePackModes.Optional, pack.Mode);
            Assert.NotNull(pack.Settings);

            service.WriteToFile(filePath, new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata
                {
                    Name = "knowledge-workspace-updated",
                    Image = "ubuntu:24.04",
                },
                Provider = definition.Provider,
                Runtime = definition.Runtime,
                Features = definition.Features,
                Services = ["postgres"],
                Skills = definition.Skills,
                Mcp = definition.Mcp,
                Agent = definition.Agent,
                Terminal = definition.Terminal,
                Oracle = definition.Oracle,
                Analytics = definition.Analytics,
                KnowledgePacks = definition.KnowledgePacks,
            });

            var updatedYaml = File.ReadAllText(filePath);

            Assert.Contains("knowledgePacks:", updatedYaml);
            Assert.Contains("provider: apexlang-atlas", updatedYaml);
            Assert.Contains("buildId: \"26.1.0+3102\"", updatedYaml);
            Assert.Contains("metadataUrl: \"https://example.test/meta.json\"", updatedYaml);
            Assert.Contains("builtinCatalogUrl: \"https://example.test/catalog.json\"", updatedYaml);
            Assert.Contains("nested:", updatedYaml);
            Assert.Contains("- one", updatedYaml);
            Assert.Contains("strict: true", updatedYaml);
            Assert.Contains("value: test", updatedYaml);
            Assert.Contains("name: knowledge-workspace-updated", updatedYaml);
            Assert.Contains("- postgres", updatedYaml);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Write_RoundTripsKnowledgePackSettingsNode()
    {
        var service = new WorkspaceYamlService();
        var settings = ParseYamlNode("""
buildId: "26.1.0+3102"
metadataUrl: "https://example.test/meta.json"
nested:
  order:
    - first
    - second
""");

        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Name = "knowledge-write",
                Image = "ubuntu:24.04",
            },
            Provider = new WorkspaceProviderDefinition { Type = "git" },
            Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
            Features = ["core"],
            Services = [],
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
            KnowledgePacks =
            [
                new WorkspaceKnowledgePackDefinition
                {
                    Provider = "apexlang-atlas",
                    Enabled = true,
                    Mode = WorkspaceKnowledgePackModes.Required,
                    Settings = settings,
                },
            ],
        };

        var yaml = service.Write(definition);

        Assert.Contains("knowledgePacks:", yaml);
        Assert.Contains("provider: apexlang-atlas", yaml);
        Assert.Contains("mode: required", yaml);
        Assert.Contains("buildId: \"26.1.0+3102\"", yaml);
        Assert.Contains("metadataUrl: \"https://example.test/meta.json\"", yaml);
        Assert.Contains("- first", yaml);
        Assert.Contains("- second", yaml);
    }

    [Fact]
    public void WriteToFile_PreservesOracleApexNestedMetadata()
    {
        var service = new WorkspaceYamlService();
        var filePath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(filePath, """
workspace:
  name: apex-sync
provider:
  type: git
runtime:
  default: default
features:
  - core
services:
  - oracle-demo
  - oracle-ords
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
oracle:
  hostPort: 1522
  ordsPort: 8182
  apex:
    defaultEnvironment: dev
    environments:
      dev:
        workspace: TEST
        parsingSchema: TESTSCHEMA
        applicationId: 100
        sqlclProfile: local-apex-dev
        syncMode: manual
        sourcePath: src/apex
        deploymentProfile: development
""");

            var definition = service.Read(filePath);
            service.WriteToFile(filePath, definition);
            var updatedYaml = File.ReadAllText(filePath);

            Assert.Contains("apex:", updatedYaml);
            Assert.Contains("defaultEnvironment: dev", updatedYaml);
            Assert.Contains("applicationId: 100", updatedYaml);
            Assert.Contains("sqlclProfile: local-apex-dev", updatedYaml);
            Assert.Contains("sourcePath: src/apex", updatedYaml);
            Assert.Contains("deploymentProfile: development", updatedYaml);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static YamlNode ParseYamlNode(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        return stream.Documents[0].RootNode;
    }
}
