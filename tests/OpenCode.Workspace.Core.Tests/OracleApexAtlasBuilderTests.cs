using System.Text.Json;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexAtlasBuilderTests
{
    [Fact]
    public void Rebuild_DiscoversPagesAndRegions()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            WriteSamplePackage(root);
            var builder = new OracleApexAtlasBuilder();

            var result = builder.Rebuild(CreateDefinition(), paths, "dev", force: true);

            Assert.True(result.IsSuccess, result.Message);
            using var pagesDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".opencode", "knowledge", "apexlang-atlas", "pages.json")));
            Assert.Equal(2, pagesDocument.RootElement.GetArrayLength());
            Assert.Equal(1, pagesDocument.RootElement[0].GetProperty("pageId").GetInt32());
            Assert.Equal("Home", pagesDocument.RootElement[0].GetProperty("name").GetString());
            Assert.Equal(1, pagesDocument.RootElement[0].GetProperty("regions").GetArrayLength());
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Rebuild_DiscoversSharedComponents()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            WriteSamplePackage(root);
            var builder = new OracleApexAtlasBuilder();

            builder.Rebuild(CreateDefinition(), paths, "dev", force: true);

            using var sharedDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".opencode", "knowledge", "apexlang-atlas", "shared-components.json")));
            Assert.Equal(1, sharedDocument.RootElement.GetProperty("lovs").GetArrayLength());
            Assert.Equal(1, sharedDocument.RootElement.GetProperty("navigationMenus").GetArrayLength());
            Assert.Equal(1, sharedDocument.RootElement.GetProperty("authorizationSchemes").GetArrayLength());
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Rebuild_GeneratesDependenciesAndSearchIndex()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            WriteSamplePackage(root);
            var builder = new OracleApexAtlasBuilder();

            builder.Rebuild(CreateDefinition(), paths, "dev", force: true);

            var dependencies = File.ReadAllText(Path.Combine(root, ".opencode", "knowledge", "apexlang-atlas", "dependencies.json"));
            var searchIndex = File.ReadAllText(Path.Combine(root, ".opencode", "knowledge", "apexlang-atlas", "search-index.json"));

            Assert.Contains("DEMO_CUSTOMERS", dependencies, StringComparison.Ordinal);
            Assert.Contains("DEMO_ORDER_SUMMARY_V", dependencies, StringComparison.Ordinal);
            Assert.Contains("DEMO_CUSTOMER_API.CREATE_ORDER", dependencies, StringComparison.Ordinal);
            Assert.Contains("https://example.test/ords/demo/orders", dependencies, StringComparison.Ordinal);
            Assert.Contains("authorization-scheme", searchIndex, StringComparison.Ordinal);
            Assert.Contains("Home", searchIndex, StringComparison.Ordinal);
            Assert.Contains("Customers", searchIndex, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Rebuild_SkipsWhenSourceDidNotChange()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            WriteSamplePackage(root);
            var builder = new OracleApexAtlasBuilder();

            var first = builder.Rebuild(CreateDefinition(), paths, "dev", force: true);
            var statePath = Path.Combine(root, ".opencode", "knowledge", "apexlang-atlas", "state.json");
            var firstWrite = File.GetLastWriteTimeUtc(statePath);
            Thread.Sleep(1100);
            var second = builder.Rebuild(CreateDefinition(), paths, "dev");

            Assert.True(first.IsSuccess, first.Message);
            Assert.True(second.IsSuccess);
            Assert.True(second.IsSkipped);
            Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(statePath));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Rebuild_GeneratesDocumentation()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            WriteSamplePackage(root);
            var builder = new OracleApexAtlasBuilder();

            builder.Rebuild(CreateDefinition(), paths, "dev", force: true);

            var documentation = File.ReadAllText(Path.Combine(root, "docs", "oracle-apex-atlas.md"));
            Assert.Contains("# Oracle APEX Atlas", documentation, StringComparison.Ordinal);
            Assert.Contains("## Application Summary", documentation, StringComparison.Ordinal);
            Assert.Contains("## Page Inventory", documentation, StringComparison.Ordinal);
            Assert.Contains("Customer Orders Demo", documentation, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Rebuild_WhenApexlangIsMalformed_WritesFailedState()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var sourceRoot = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourceRoot);
            File.WriteAllText(Path.Combine(sourceRoot, "application.apx"), "-- placeholder only");
            var builder = new OracleApexAtlasBuilder();

            var result = builder.Rebuild(CreateDefinition(), paths, "dev", force: true);

            Assert.False(result.IsSuccess);
            using var stateDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".opencode", "knowledge", "apexlang-atlas", "state.json")));
            Assert.Equal("failed", stateDocument.RootElement.GetProperty("status").GetString());
            Assert.Contains("application", stateDocument.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
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
                        ["dev"] = new()
                        {
                            ApplicationId = 100,
                            Workspace = "TEST",
                            ParsingSchema = "TESTSCHEMA",
                            SourcePath = "src/apex",
                        },
                    },
                },
            },
        };

    private static void WriteSamplePackage(string root)
    {
        var sourceRoot = Path.Combine(root, "src", "apex");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(Path.Combine(sourceRoot, "pages"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "lovs"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "lists"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "navigation_menus"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "authorization_schemes"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "authentication_schemes"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "build_options"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "static_files"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "plugins"));

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

        File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00001-home.apx"), """
page home (
    id: 1
    name: Home
    alias: HOME
    mode: Normal
    authentication: Required
    breadcrumb: Main

    region customers (
        title: Customers
        type: Interactive Report
        source-type: SQL Query
        source: select customer_id, customer_name from demo_customers
    )

    item p1_customer_id (
        name: P1_CUSTOMER_ID
        type: Number Field
    )

    button create-order (
        name: CREATE_ORDER
    )

    dynamic action refresh-report (
        name: Refresh Report
    )

    process save-order (
        name: DEMO_CUSTOMER_API.CREATE_ORDER
    )

    branch goto-orders (
        name: Go To Orders
        target-page: 2
    )
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00002-orders.apx"), """
page orders (
    id: 2
    name: Orders
    alias: ORDERS
    mode: Normal
    authentication: Required
    parent-page: 1
    breadcrumb: Main

    region order-summary (
        title: Order Summary
        type: Interactive Report
        source-type: SQL Query
        source: select order_id, customer_name from demo_order_summary_v
        rest-source: https://example.test/ords/demo/orders
    )
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "lovs", "order-statuses.apx"), """
list of values order-statuses (
    name: ORDER_STATUSES
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "lists", "application-links.apx"), """
list application-links (
    name: Application Links
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "navigation_menus", "main-navigation.apx"), """
navigation menu main-navigation (
    name: Main Navigation

    entry home (
        label: Home
        target-page: 1
    )

    entry orders (
        label: Orders
        target-page: 2
        parent-entry: home
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

        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "build_options", "feature-orders.apx"), """
build option feature-orders (
    name: FEATURE_ORDERS
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "static_files", "logo.apx"), """
static file logo (
    name: logo.svg
)
""");

        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "plugins", "custom-chart.apx"), """
plugin custom-chart (
    name: Custom Chart
)
""");
    }

    private static string CreateTempRoot() => Path.Combine(Path.GetTempPath(), $"oracle-apex-atlas-tests-{Guid.NewGuid():N}");

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            TestFileSystem.DeleteDirectoryIfExists(root);
        }
    }
}
