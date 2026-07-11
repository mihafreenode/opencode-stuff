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
            File.WriteAllText(Path.Combine(sourceRoot, "application.apx"), "application demo (\n  id: 100\n  name: Demo\n  alias: DEMO\n)\n");
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
        }
        finally { DeleteTempRoot(root); }
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
