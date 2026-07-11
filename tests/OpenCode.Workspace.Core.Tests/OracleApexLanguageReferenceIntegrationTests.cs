using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexLanguageReferenceIntegrationTests
{
    [Fact]
    public void SemanticModelBuilder_ParsesOfficialCanonicalNamesAndGroupProperties()
    {
        var root = CreateTempRoot();
        try
        {
            var sourceRoot = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourceRoot);
            File.WriteAllText(Path.Combine(sourceRoot, "application.apx"), """
app demo (
  id: 100
  name: "Demo"
  alias: "DEMO"
  version: "Release 1.0"
  type: standard
  navigationMenu {
    listPosition: side
  }
  page home (
    id: 1
    name: "Home"
    alias: "HOME"
    region report_region (
      title: "Report"
      type: "Interactive Report"
      pageItem p1_name (
        name: "P1_NAME"
        type: text
      )
    )
  )
)
""");

            var model = new OracleApexSemanticModelBuilder().Build(sourceRoot);

            Assert.DoesNotContain(model.Diagnostics, diagnostic => diagnostic.Severity == OracleApexSemanticDiagnosticSeverity.Error);
            Assert.Equal("application", model.Application!.SemanticType);
            Assert.Equal("side", model.Application.GetProperty("navigationmenu.listposition"));
            Assert.Contains(model.Nodes, node => node.SemanticType == "item" && node.Identifier == "P1_NAME");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void AtlasBuilder_WritesLanguageReferenceArtifacts()
    {
        var root = CreateTempRoot();
        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(root);
            var sourceRoot = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourceRoot);
            File.WriteAllText(Path.Combine(sourceRoot, "application.apx"), "application demo (\n  id: 100\n  name: Demo\n  alias: DEMO\n  apexlang-version: 26.1\n)\n");
            WriteAtlasMetadata(root, "26.1.0+3102");
            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "oracle-apexlang", Image = "ubuntu:24.04" },
                Features = ["oracle-apexlang-demo"],
                Services = ["oracle-ords"],
                Oracle = new OracleWorkspacePreferences
                {
                    Apex = new OracleApexWorkspacePreferences
                    {
                        DefaultEnvironment = "dev",
                        Environments = new Dictionary<string, OracleApexEnvironmentPreferences>
                        {
                            ["dev"] = new() { Workspace = "TEST", ParsingSchema = "TESTSCHEMA", ApplicationId = 100, SourcePath = "src/apex" },
                        },
                    },
                },
            };

            var result = new OracleApexAtlasBuilder().Rebuild(definition, paths, "dev", force: true);

            Assert.True(result.IsSuccess);
            Assert.NotEmpty(Directory.GetFiles(paths.OpencodePath, "language-reference-state.json", SearchOption.AllDirectories));
            Assert.NotEmpty(Directory.GetFiles(paths.OpencodePath, "catalog-compatibility.json", SearchOption.AllDirectories));
            Assert.NotEmpty(Directory.GetFiles(paths.OpencodePath, "language-reference-diff.json", SearchOption.AllDirectories));
            Assert.NotEmpty(Directory.GetFiles(paths.OpencodePath, "language-reference-summary.md", SearchOption.AllDirectories));
            Assert.NotEmpty(Directory.GetFiles(paths.OpencodePath, "language-reference-diff.md", SearchOption.AllDirectories));

            var prompt = File.ReadAllText(Path.Combine(paths.OpencodePath, "knowledge", "apexlang-language-reference", "prompts", "apexlang-language-reference.md"));
            Assert.Contains("## Version Compatibility", prompt, StringComparison.Ordinal);
            Assert.Contains("Project version: 26.1", prompt, StringComparison.Ordinal);
            Assert.Contains("Reference version: 26.1", prompt, StringComparison.Ordinal);
            Assert.Contains("Atlas version: 26.1.0+3102", prompt, StringComparison.Ordinal);
            Assert.Contains("Status: Compatible", prompt, StringComparison.Ordinal);
            Assert.Contains("language-reference-diff.json", prompt, StringComparison.Ordinal);
            Assert.Contains("language-reference-diff.md", prompt, StringComparison.Ordinal);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void AtlasBuilder_PromptIncludesOnlyWorkspaceRelevantVersionFindingsAndLimitsOutput()
    {
        var root = CreateTempRoot();
        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var sourceRoot = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourceRoot);
            Directory.CreateDirectory(Path.Combine(sourceRoot, "pages"));
            File.WriteAllText(Path.Combine(sourceRoot, "application.apx"), """
application demo (
  id: 100
  name: Demo
  alias: DEMO
  apexlang-version: 25.2
  theme: Vita
)
""");
            for (var index = 1; index <= 12; index++)
            {
                File.WriteAllText(Path.Combine(sourceRoot, "pages", $"p{index:00000}-page.apx"), $$"""
page page{{index}} (
  id: {{index}}
  name: Page {{index}}
)
""");
            }

            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "oracle-apexlang", Image = "ubuntu:24.04" },
                Features = ["oracle-apexlang-demo"],
                Services = ["oracle-ords"],
                Oracle = new OracleWorkspacePreferences
                {
                    Apex = new OracleApexWorkspacePreferences
                    {
                        DefaultEnvironment = "dev",
                        Environments = new Dictionary<string, OracleApexEnvironmentPreferences>
                        {
                            ["dev"] = new() { Workspace = "TEST", ParsingSchema = "TESTSCHEMA", ApplicationId = 100, SourcePath = "src/apex" },
                        },
                    },
                },
            };

            var result = new OracleApexAtlasBuilder().Rebuild(definition, paths, "dev", force: true);

            Assert.True(result.IsSuccess);
            var prompt = File.ReadAllText(Path.Combine(paths.OpencodePath, "knowledge", "apexlang-language-reference", "prompts", "apexlang-language-reference.md"));
            var impactLines = prompt.Split('\n').Where(line => line.StartsWith("- ", StringComparison.Ordinal) && (line.Contains("required", StringComparison.OrdinalIgnoreCase) || line.Contains("theme", StringComparison.OrdinalIgnoreCase) || line.Contains("version", StringComparison.OrdinalIgnoreCase))).ToList();
            Assert.Contains("Status: Version mismatch", prompt, StringComparison.Ordinal);
            Assert.Contains("property 'theme'", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("legacybanner", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.True(impactLines.Count <= 10);
        }
        finally { DeleteTempRoot(root); }
    }

    private static void WriteAtlasMetadata(string root, string buildId)
    {
        var atlasSourceRoot = Path.Combine(root, ".opencode", "apexlang", "source");
        Directory.CreateDirectory(atlasSourceRoot);
        File.WriteAllText(Path.Combine(atlasSourceRoot, "apexlang_meta_data.json"), $$"""
{ "buildID": "{{buildId}}" }
""");
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oracle-apex-language-reference-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            TestFileSystem.DeleteDirectoryIfExists(root);
        }
    }
}
