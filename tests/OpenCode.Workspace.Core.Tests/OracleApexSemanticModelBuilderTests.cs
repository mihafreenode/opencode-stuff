using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexSemanticModelBuilderTests
{
    [Fact]
    public void Build_DiscoversComponentsAndHierarchy()
    {
        var root = CreateTempRoot();

        try
        {
            var sourcePath = WriteSamplePackage(root);
            var model = new OracleApexSemanticModelBuilder().Build(sourcePath);

            Assert.NotNull(model.Application);
            Assert.Equal("application", model.Application!.SemanticType);
            Assert.Equal(2, model.GetNodes("page").Count);
            Assert.Contains(model.GetNodes("page"), node => node.Identifier == "Home");
            Assert.Contains(model.GetNodes("region"), node => node.Parent?.SemanticType == "page");
            Assert.Contains(model.GetNodes("navigation-entry"), node => node.Parent?.SemanticType == "navigation-menu");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Build_ParsesPropertiesAndReferences()
    {
        var root = CreateTempRoot();

        try
        {
            var sourcePath = WriteSamplePackage(root);
            var model = new OracleApexSemanticModelBuilder().Build(sourcePath);
            var region = model.GetNodes("region").Single(node => string.Equals(node.GetProperty("title"), "Customers Region", StringComparison.Ordinal));
            var process = model.GetNodes("process").Single();

            Assert.Equal("Interactive Report", region.GetProperty("type"));
            Assert.Contains("DEMO_CUSTOMERS", region.ReferencedObjects);
            Assert.Contains("DEMO_CUSTOMER_API.CREATE_ORDER", process.ReferencedObjects);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Build_ReportsDuplicateAndReferenceValidationIssues()
    {
        var root = CreateTempRoot();

        try
        {
            var sourcePath = WriteSamplePackage(root, withValidationIssues: true);
            var model = new OracleApexSemanticModelBuilder().Build(sourcePath);

            Assert.Contains(model.Diagnostics, item => item.Code == "duplicate-page-alias");
            Assert.Contains(model.Diagnostics, item => item.Code == "duplicate-region-identifier");
            Assert.Contains(model.Diagnostics, item => item.Code == "duplicate-item-name");
            Assert.Contains(model.Diagnostics, item => item.Code == "invalid-navigation-reference");
            Assert.Contains(model.Diagnostics, item => item.Code == "unresolved-shared-component");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Build_WhenMalformedComponentExists_ReportsDiagnostics()
    {
        var root = CreateTempRoot();

        try
        {
            var sourcePath = WriteSamplePackage(root, malformed: true);
            var model = new OracleApexSemanticModelBuilder().Build(sourcePath);

            Assert.Contains(model.Diagnostics, item => item.Code is "unclosed-component" or "missing-required-property");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void ComponentCatalog_GeneratesDocumentation()
    {
        var documentation = OracleApexComponentCatalog.Default.BuildDocumentation();

        Assert.Contains("# Oracle APEX Component Catalog", documentation, StringComparison.Ordinal);
        Assert.Contains("Application", documentation, StringComparison.Ordinal);
        Assert.Contains("Dynamic Action", documentation, StringComparison.Ordinal);
        Assert.Contains("REST Handler", documentation, StringComparison.Ordinal);
    }

    private static string WriteSamplePackage(string root, bool withValidationIssues = false, bool malformed = false)
    {
        var sourceRoot = Path.Combine(root, "src", "apex");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "pages"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "authorization_schemes"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "navigation_menus"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "lovs"));

        File.WriteAllText(Path.Combine(sourceRoot, "application.apx"), """
application customer-orders-demo (
    id: 100
    name: Customer Orders Demo
    alias: CUSTOMER-ORDERS-DEMO
    version: 1.2
    workspace: TEST
    parsing-schema: TESTSCHEMA
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00001-home.apx"), withValidationIssues ? """
page home (
    id: 1
    name: Home
    alias: HOME
    authentication: Required

    region customers-region (
        name: customers-region
        title: Customers Region
        type: Interactive Report
        source: select customer_id from demo_customers
        authorization-scheme: MISSING_SCHEME
    )

    region customers-region (
        name: customers-region
        title: Duplicate Region
        type: Interactive Report
    )

    item p1_customer_id (
        name: P1_CUSTOMER_ID
    )

    item p1_customer_id_duplicate (
        name: P1_CUSTOMER_ID
    )

    branch bad-branch (
        name: Bad Branch
        target-page: 999
    )
)
""" : """
page home (
    id: 1
    name: Home
    alias: HOME
    authentication: Required

    region customers-region (
        name: customers-region
        title: Customers Region
        type: Interactive Report
        source-type: SQL Query
        source: select customer_id from demo_customers
        authorization-scheme: ADMIN_ONLY
    )

    item p1_customer_id (
        name: P1_CUSTOMER_ID
    )

    process create-order (
        name: DEMO_CUSTOMER_API.CREATE_ORDER
    )
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00002-orders.apx"), malformed ? """
page orders (
    id: 2
    alias: HOME
    region broken (
        title: Broken
""" : withValidationIssues ? """
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
    parent-page: 1
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "authorization_schemes", "admin-only.apx"), """
authorization scheme admin-only (
    name: ADMIN_ONLY
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "lovs", "order-statuses.apx"), """
list of values order-statuses (
    name: ORDER_STATUSES
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

        return sourceRoot;
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oracle-apex-semantic-tests-{Guid.NewGuid():N}");
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
