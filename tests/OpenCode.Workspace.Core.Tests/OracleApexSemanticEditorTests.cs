using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexSemanticEditorTests
{
    [Fact]
    public void AddPage_AddsValidPageAndUpdatesIndex()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.AddPage("Reports", new Dictionary<string, string> { ["id"] = "3", ["alias"] = "REPORTS" }));
            Assert.True(result.IsSuccess, result.Message);
            Assert.Contains(result.WorkspaceIndex.Pages, page => page.Identifier == "Reports");
            Assert.Contains(result.WorkspaceIndex.SearchEntries, entry => entry.Type == "page" && entry.Name == "Reports");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void RemovePage_RemovesPageFile()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.RemovePage("Orders"));
            Assert.True(result.IsSuccess, result.Message);
            Assert.DoesNotContain(result.WorkspaceIndex.Pages, page => page.Identifier == "Orders");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void RenamePage_RenamesPage()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.RenamePage("Home", "Dashboard"));
            Assert.True(result.IsSuccess, result.Message);
            Assert.Contains(result.WorkspaceIndex.Pages, page => page.Identifier == "Dashboard");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void AddRegion_AddsRegionToPage()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.AddRegion("Home", "Orders Region", new Dictionary<string, string> { ["type"] = "Interactive Report" }));
            Assert.True(result.IsSuccess, result.Message);
            Assert.Contains(result.WorkspaceIndex.Regions, region => region.Identifier == "Orders Region");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void RemoveRegion_RemovesRegion()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.RemoveRegion("Home", "Customers Region"));
            Assert.True(result.IsSuccess, result.Message);
            Assert.DoesNotContain(result.WorkspaceIndex.Regions, region => region.Identifier == "Customers Region");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void MoveRegion_MovesRegionToAnotherPage()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.MoveRegion("Home", "Customers Region", "Orders"));
            Assert.True(result.IsSuccess, result.Message);
            var region = result.WorkspaceIndex.Regions.Single(item => item.Identifier == "Customers Region");
            var parent = result.WorkspaceIndex.Entries.Single(item => item.NodeId == region.ParentNodeId);
            Assert.Equal("Orders", parent.Identifier);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void RenameRegion_RenamesRegionProperties()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.RenameRegion("Home", "Customers Region", "Client Region"));
            Assert.True(result.IsSuccess, result.Message);
            Assert.Contains(result.WorkspaceIndex.Regions, region => region.Identifier == "Client Region");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void AddItem_AddsItemToPage()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.AddItem("Home", "P1_STATUS"));
            Assert.True(result.IsSuccess, result.Message);
            Assert.Contains(result.WorkspaceIndex.Items, item => item.Identifier == "P1_STATUS");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void RemoveItem_RemovesItem()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.RemoveItem("Home", "P1_CUSTOMER_ID"));
            Assert.True(result.IsSuccess, result.Message);
            Assert.DoesNotContain(result.WorkspaceIndex.Items, item => item.Identifier == "P1_CUSTOMER_ID");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void RenameItem_RenamesItem()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.RenameItem("Home", "P1_CUSTOMER_ID", "P1_CLIENT_ID"));
            Assert.True(result.IsSuccess, result.Message);
            Assert.Contains(result.WorkspaceIndex.Items, item => item.Identifier == "P1_CLIENT_ID");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void AddButton_AddsButton()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.AddButton("Home", "CREATE_ORDER"));
            Assert.True(result.IsSuccess, result.Message);
            Assert.Contains(result.WorkspaceIndex.Entries, entry => entry.SemanticType == "button" && entry.Identifier == "CREATE_ORDER");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void AddProcess_AddsProcess()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.AddProcess("Home", "CREATE_PROCESS"));
            Assert.True(result.IsSuccess, result.Message);
            Assert.Contains(result.WorkspaceIndex.Entries, entry => entry.SemanticType == "process" && entry.Identifier == "CREATE_PROCESS");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void AddDynamicAction_AddsDynamicAction()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.AddDynamicAction("Home", "Refresh Grid"));
            Assert.True(result.IsSuccess, result.Message);
            Assert.Contains(result.WorkspaceIndex.Entries, entry => entry.SemanticType == "dynamic-action" && entry.Identifier == "Refresh Grid");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void AddSharedComponent_AddsSharedComponent()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.AddSharedComponent("authorization-scheme", "MANAGERS_ONLY"));
            Assert.True(result.IsSuccess, result.Message);
            Assert.Contains(result.WorkspaceIndex.SharedComponents, entry => entry.Identifier == "MANAGERS_ONLY");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void RenameSharedComponent_UpdatesReferences()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.RenameSharedComponent("lov", "OLD_LOV", "NEW_LOV"));
            Assert.True(result.IsSuccess, result.Message);
            var item = result.WorkspaceIndex.Items.Single(item => item.Identifier == "P1_CUSTOMER_ID");
            Assert.Equal("NEW_LOV", item.Properties["lov"]);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void AddNavigationEntry_AddsNavigationEntry()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.AddNavigationEntry("Main Navigation", "Reports", new Dictionary<string, string> { ["target-page"] = "2" }));
            Assert.True(result.IsSuccess, result.Message);
            Assert.Contains(result.WorkspaceIndex.NavigationEntries, entry => entry.Identifier == "Reports");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void RenameNavigationEntry_UpdatesParentEntryReferences()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.RenameNavigationEntry("Main Navigation", "Home", "Start"));
            Assert.True(result.IsSuccess, result.Message);
            var child = result.WorkspaceIndex.NavigationEntries.Single(entry => entry.Identifier == "Orders");
            Assert.Equal("Start", child.Properties["parent-entry"]);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void AddRegion_WhenParentPlacementIsInvalid_ReturnsFailure()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var operation = new OracleApexSemanticEditOperation { Kind = OracleApexSemanticEditKind.AddNavigationEntry, ParentIdentifier = "Home", ParentSemanticType = "page", NewIdentifier = "Broken" };
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", operation);
            Assert.False(result.IsSuccess);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void AddRegion_WhenRequiredPropertyIsMissing_ReturnsFailure()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.AddRegion("Home", "Broken Region"));
            Assert.False(result.IsSuccess);
            Assert.Contains("requires property 'type'", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void AddPage_WhenDuplicateAliasIntroduced_ReturnsDiagnosticsAndRollsBack()
    {
        var root = CreateTempRoot();
        try
        {
            WriteBasePackage(root);
            var result = CreateEditor().Apply(root, CreateEnvironment(), "dev", OracleApexSemanticEditOperation.AddPage("Duplicate", new Dictionary<string, string> { ["id"] = "3", ["alias"] = "HOME" }));
            Assert.False(result.IsSuccess);
            Assert.Contains(result.Diagnostics.Entries, entry => entry.Code == "duplicate-page-alias");
            Assert.DoesNotContain(result.WorkspaceIndex.Pages, page => page.Identifier == "Duplicate");
        }
        finally { DeleteTempRoot(root); }
    }

    private static OracleApexSemanticEditor CreateEditor() => new();

    private static OracleApexEnvironmentPreferences CreateEnvironment()
        => new() { ApplicationId = 100, Workspace = "TEST", ParsingSchema = "TESTSCHEMA", SourcePath = "src/apex" };

    private static void WriteBasePackage(string root)
    {
        var sourceRoot = Path.Combine(root, "src", "apex");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "pages"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "authorization_schemes"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "lovs"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "shared_components", "navigation_menus"));
        File.WriteAllText(Path.Combine(sourceRoot, "application.apx"), """
application customer-orders-demo (
    id: 100
    name: Customer Orders Demo
    alias: CUSTOMER-ORDERS-DEMO
)
""");
        File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00001-home.apx"), """
page home (
    id: 1
    name: Home
    alias: HOME
    region customers (
        name: customers
        title: Customers Region
        type: Interactive Report
        lov: OLD_LOV
    )
    item p1_customer_id (
        name: P1_CUSTOMER_ID
        lov: OLD_LOV
    )
)
""");
        File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00002-orders.apx"), """
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
        File.WriteAllText(Path.Combine(sourceRoot, "shared_components", "lovs", "old-lov.apx"), """
list of values old-lov (
    name: OLD_LOV
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
        parent-entry: Home
    )
)
""");
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oracle-apex-editor-tests-{Guid.NewGuid():N}");
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
