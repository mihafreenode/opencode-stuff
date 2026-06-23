using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleWorkspaceFamilyTests
{
    [Fact]
    public void Detect_TemplateKind_ReturnsExpectedOracleWorkspaceKinds()
    {
        var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        var templates = provider.LoadTemplates();

        Assert.Equal(OracleWorkspaceKind.PlSql, OracleWorkspaceFamily.Detect(templates.Single(item => item.Id == "oracle-plsql-demo")));
        Assert.Equal(OracleWorkspaceKind.Apex, OracleWorkspaceFamily.Detect(templates.Single(item => item.Id == "oracle-apex-demo")));
        Assert.Equal(OracleWorkspaceKind.ApexLang, OracleWorkspaceFamily.Detect(templates.Single(item => item.Id == "oracle-apexlang-demo")));
    }

    [Fact]
    public void Detect_DefinitionKind_ReturnsExpectedOracleWorkspaceKinds()
    {
        Assert.Equal(
            OracleWorkspaceKind.PlSql,
            OracleWorkspaceFamily.Detect(new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "plsql" },
                Features = ["core", "oracle-demo"],
                Services = ["oracle-demo"],
            }));

        Assert.Equal(
            OracleWorkspaceKind.Apex,
            OracleWorkspaceFamily.Detect(new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "apex" },
                Features = ["core", "oracle-demo", "oracle-apex-demo"],
                Services = ["oracle-demo", "oracle-ords"],
            }));

        Assert.Equal(
            OracleWorkspaceKind.ApexLang,
            OracleWorkspaceFamily.Detect(new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "apexlang" },
                Features = ["core", "oracle-demo", "oracle-apex-demo", "oracle-apexlang-demo"],
                Services = ["oracle-demo", "oracle-ords"],
            }));
    }
}
