using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexIntentExpansionServiceTests
{
    [Fact]
    public void Expand_CustomerManagement_GeneratesAlternativesAndWorkspaceReuse()
    {
        var root = CreateTempRoot();
        try
        {
            WriteAwarePackage(root, withCustomerPage: false);
            var index = new OracleApexWorkspaceIndexBuilder().Build(root, CreateEnvironment(), "dev");
            var service = new OracleApexIntentExpansionService();

            var result = service.Expand(index, "Build a customer management module");

            Assert.NotNull(result.Blueprint);
            Assert.Equal(2, result.Blueprint!.Alternatives.Count);
            Assert.Contains(result.Blueprint.UnresolvedQuestions, item => item.Contains("implementation approach", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Blueprint.Assumptions, item => item.Contains("deployment targets", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Blueprint.Modules.Single().ReusedComponents, item => item.StartsWith("Navigation:", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Blueprint.Assumptions, item => item.Contains("Reuse authentication scheme", StringComparison.OrdinalIgnoreCase));
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void Expand_UserAdministration_ReusesAuthorizationScheme()
    {
        var root = CreateTempRoot();
        try
        {
            WriteAwarePackage(root, withCustomerPage: false);
            var index = new OracleApexWorkspaceIndexBuilder().Build(root, CreateEnvironment(), "dev");
            var service = new OracleApexIntentExpansionService();

            var result = service.Expand(index, "Add user administration");

            var module = Assert.Single(result.Blueprint!.Modules);
            Assert.True(module.RequiresAuthorization);
            Assert.Equal("ADMIN_ONLY", module.AuthorizationSchemeName);
            Assert.Empty(module.Alternatives);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void Expand_ExplicitApproach_SelectsSingleBlueprint()
    {
        var root = CreateTempRoot();
        try
        {
            WriteAwarePackage(root, withCustomerPage: false);
            var index = new OracleApexWorkspaceIndexBuilder().Build(root, CreateEnvironment(), "dev");
            var service = new OracleApexIntentExpansionService();

            var result = service.Expand(index, "Build CRUD for Products with report and form pages");

            var module = Assert.Single(result.Blueprint!.Modules);
            Assert.Equal("report-form", module.Approach);
            Assert.Empty(result.Blueprint.Alternatives);
            Assert.Empty(result.Blueprint.UnresolvedQuestions);
        }
        finally { DeleteTempRoot(root); }
    }

    private static OracleApexEnvironmentPreferences CreateEnvironment()
        => new() { ApplicationId = 100, Workspace = "TEST", ParsingSchema = "TESTSCHEMA", SourcePath = "src/apex" };

    private static void WriteAwarePackage(string root, bool withCustomerPage)
    {
        var sourceRoot = Path.Combine(root, "src", "apex");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "pages"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "navigation_menus"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "authorization_schemes"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "authentication_schemes"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "lovs"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "deployments"));
        File.WriteAllText(Path.Combine(sourceRoot, "application.apx"), """
application customer-demo (
    id: 100
    name: Customer Demo
    alias: CUSTOMER-DEMO
)
""");
        File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00001-home.apx"), """
page home (
    id: 1
    name: Home
    alias: HOME
)
""");
        if (withCustomerPage)
        {
            File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00002-customers.apx"), """
page customers (
    id: 2
    name: Customers
    alias: CUSTOMERS
)
""");
        }

        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "navigation_menus", "main-navigation.apx"), """
navigation menu main-navigation (
    name: Main Navigation
    entry home (
        label: Home
        target-page: 1
    )
)
""");
        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "authorization_schemes", "admin-only.apx"), """
authorization scheme admin-only (
    name: ADMIN_ONLY
)
""");
        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "authentication_schemes", "workspace-auth.apx"), """
authentication scheme workspace-auth (
    name: Workspace Auth
)
""");
        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "lovs", "customer-types.apx"), """
list of values customer-types (
    name: CUSTOMER_TYPES
)
""");
        File.WriteAllText(Path.Combine(sourceRoot, "deployments", "development.apx"), """
deployment development (
    workspace: TEST
    parsing-schema: TESTSCHEMA
    application-id: 100
)
""");
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oracle-apex-intent-expansion-tests-{Guid.NewGuid():N}");
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
