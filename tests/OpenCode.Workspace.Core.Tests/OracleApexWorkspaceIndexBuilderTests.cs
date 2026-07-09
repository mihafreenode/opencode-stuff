using System.Text.Json;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexWorkspaceIndexBuilderTests
{
    [Fact]
    public void Build_CreatesWorkspaceIndexFromSemanticModel()
    {
        var root = CreateTempRoot();

        try
        {
            WriteSamplePackage(root);
            var builder = new OracleApexWorkspaceIndexBuilder();

            var index = builder.Build(root, CreateEnvironment(), "dev");

            Assert.Equal("dev", index.EnvironmentName);
            Assert.Equal(2, index.Pages.Count);
            Assert.Single(index.Regions);
            Assert.Single(index.Items);
            Assert.Contains(index.SharedComponents, entry => entry.SemanticType == "authorization-scheme");
            Assert.Single(index.NavigationEntries);
            Assert.Single(index.DeploymentProfiles);
            Assert.Contains(index.References, entry => entry.Reference == "DEMO_CUSTOMERS");
            Assert.Contains(index.SearchEntries, entry => entry.Type == "deployment-profile" && entry.Name == "development");
            Assert.Contains(index.SourceLocations, entry => entry.SemanticType == "page" && entry.Identifier == "Home");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Build_IncludesDiagnosticsAndDeploymentValidation()
    {
        var root = CreateTempRoot();

        try
        {
            WriteSamplePackage(root, withIssues: true);
            var builder = new OracleApexWorkspaceIndexBuilder();

            var index = builder.Build(root, CreateEnvironment(), "dev");

            Assert.Contains(index.Diagnostics, entry => entry.Code == "duplicate-page-alias");
            Assert.Contains(index.Diagnostics, entry => entry.Code == "invalid-deployment-profile");
            Assert.Contains(index.DeploymentProfiles, entry => !entry.IsValid);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void AtlasBuilder_WritesWorkspaceIndexJson()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            WriteSamplePackage(root);
            var atlasBuilder = new OracleApexAtlasBuilder();

            var result = atlasBuilder.Rebuild(CreateDefinition(), paths, "dev", force: true);

            Assert.True(result.IsSuccess, result.Message);
            var workspaceIndexPath = Path.Combine(root, ".opencode", "knowledge", "apexlang-atlas", "workspace-index.json");
            using var document = JsonDocument.Parse(File.ReadAllText(workspaceIndexPath));
            Assert.Equal("dev", document.RootElement.GetProperty("environmentName").GetString());
            Assert.True(document.RootElement.GetProperty("deploymentProfiles").GetArrayLength() > 0);
            Assert.True(document.RootElement.GetProperty("sourceLocations").GetArrayLength() > 0);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static WorkspaceDefinition CreateDefinition()
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = "atlas", Image = "ubuntu:24.04" },
            Features = ["core", "oracle-demo", "oracle-apex-demo", "oracle-apexlang-demo"],
            Services = ["oracle-demo", "oracle-ords"],
            Oracle = new OracleWorkspacePreferences
            {
                Apex = new OracleApexWorkspacePreferences
                {
                    DefaultEnvironment = "dev",
                    Environments = new Dictionary<string, OracleApexEnvironmentPreferences>
                    {
                        ["dev"] = CreateEnvironment(),
                    },
                },
            },
        };

    private static OracleApexEnvironmentPreferences CreateEnvironment()
        => new()
        {
            ApplicationId = 100,
            Workspace = "TEST",
            ParsingSchema = "TESTSCHEMA",
            SourcePath = "src/apex",
            DeploymentProfile = "development",
        };

    private static void WriteSamplePackage(string root, bool withIssues = false)
    {
        var sourceRoot = Path.Combine(root, "src", "apex");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "pages"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "authorization_schemes"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "navigation_menus"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "deployments"));

        File.WriteAllText(Path.Combine(sourceRoot, "application.apx"), """
application customer-orders-demo (
    id: 100
    name: Customer Orders Demo
    alias: CUSTOMER-ORDERS-DEMO
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00001-home.apx"), withIssues ? """
page home (
    id: 1
    name: Home
    alias: HOME
    region customers (
        title: Customers
        type: Interactive Report
        source: select customer_id from demo_customers
    )
)
""" : """
page home (
    id: 1
    name: Home
    alias: HOME
    region customers (
        title: Customers
        type: Interactive Report
        source: select customer_id from demo_customers
    )
    item p1_customer_id (
        name: P1_CUSTOMER_ID
    )
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00002-orders.apx"), withIssues ? """
page orders (
    id: 2
    name: Orders
    alias: HOME
)
""" : """
page orders (
    id: 2
    name: Orders
    alias: ORDERS
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "authorization_schemes", "admin-only.apx"), """
authorization scheme admin-only (
    name: ADMIN_ONLY
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "navigation_menus", "main-navigation.apx"), """
navigation menu main-navigation (
    name: Main Navigation
    entry home (
        label: Home
        target-page: 1
    )
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "deployments", "development.apx"), """
deployment development (
    workspace: TEST
    parsing-schema: TESTSCHEMA
    application-id: 100
)
""");

        if (withIssues)
        {
            File.WriteAllText(Path.Combine(sourceRoot, "deployments", "broken.apx"), """
page not-a-deployment (
    id: 3
)
""");
        }
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oracle-apex-index-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            TestFileSystem.DeleteDirectoryIfExists(root);
        }
    }
}
