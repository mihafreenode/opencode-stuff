using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexCodeActionServiceTests
{
    [Fact]
    public void GetAvailableActions_IncludesRequestedCodeActions()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            var service = new OracleApexCodeActionService();

            var actions = service.GetAvailableActions(root, CreateEnvironment(), "dev");

            Assert.Contains(actions, action => action.Kind == OracleApexCodeActionKind.RenamePage);
            Assert.Contains(actions, action => action.Kind == OracleApexCodeActionKind.RenameRegion);
            Assert.Contains(actions, action => action.Kind == OracleApexCodeActionKind.RenameItem);
            Assert.Contains(actions, action => action.Kind == OracleApexCodeActionKind.RenameSharedComponent);
            Assert.Contains(actions, action => action.Kind == OracleApexCodeActionKind.AddRegionToPage);
            Assert.Contains(actions, action => action.Kind == OracleApexCodeActionKind.AddItemToRegion);
            Assert.Contains(actions, action => action.Kind == OracleApexCodeActionKind.AddNavigationEntry);
            Assert.Contains(actions, action => action.Kind == OracleApexCodeActionKind.RemovePageSafely);
            Assert.Contains(actions, action => action.Kind == OracleApexCodeActionKind.RemoveRegionSafely);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void Execute_RenamePage_UsesSemanticEditor()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            var fakeEditor = new FakeSemanticEditor();
            var service = new OracleApexCodeActionService(new OracleApexWorkspaceIndexBuilder(), fakeEditor);
            var action = service.GetAvailableActions(root, CreateEnvironment(), "dev").Single(item => item.Kind == OracleApexCodeActionKind.RenamePage && item.TargetIdentifier == "Home");

            var result = service.Execute(root, CreateEnvironment(), "dev", new OracleApexCodeActionRequest { ActionId = action.Id, NewIdentifier = "Dashboard" });

            Assert.True(result.IsSuccess);
            Assert.Single(fakeEditor.Calls);
            Assert.Equal(OracleApexSemanticEditKind.RenamePage, fakeEditor.Calls[0].Single().Kind);
            Assert.Contains("Renamed 'Home' to 'Dashboard'", result.Summary, StringComparison.Ordinal);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void Execute_AddRegionWithoutRequiredProperties_UsesEditorRollbackBehavior()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            var service = new OracleApexCodeActionService();
            var action = service.GetAvailableActions(root, CreateEnvironment(), "dev").Single(item => item.Kind == OracleApexCodeActionKind.AddRegionToPage && item.TargetIdentifier == "Home");

            var result = service.Execute(root, CreateEnvironment(), "dev", new OracleApexCodeActionRequest { ActionId = action.Id, NewIdentifier = "Broken Region" });

            Assert.False(result.IsSuccess);
            Assert.DoesNotContain(result.WorkspaceIndex.Regions, region => region.Identifier == "Broken Region");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void Execute_FixMissingRequiredProperties_UpdatesWorkspaceIndex()
    {
        var root = CreateTempRoot();
        try
        {
            WritePackageWithMissingRequiredProperty(root);
            var service = new OracleApexCodeActionService();
            var action = service.GetAvailableActions(root, CreateEnvironment(), "dev").Single(item => item.Kind == OracleApexCodeActionKind.FixMissingRequiredProperties);

            var result = service.Execute(root, CreateEnvironment(), "dev", new OracleApexCodeActionRequest { ActionId = action.Id, Properties = new Dictionary<string, string> { [action.RequiredPropertyName] = "Interactive Report" } });

            Assert.True(result.IsSuccess, result.Summary);
            Assert.DoesNotContain(result.Diagnostics.Entries, entry => entry.Code == "missing-required-property");
            Assert.Contains(result.WorkspaceIndex.Regions, region => region.Identifier == "Broken Region" && region.Properties["type"] == "Interactive Report");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void Execute_FixInvalidParentPlacement_MovesItemToPage()
    {
        var root = CreateTempRoot();
        try
        {
            WritePackageWithInvalidParentPlacement(root);
            var service = new OracleApexCodeActionService();
            var action = service.GetAvailableActions(root, CreateEnvironment(), "dev").Single(item => item.Kind == OracleApexCodeActionKind.FixInvalidParentPlacement);

            var result = service.Execute(root, CreateEnvironment(), "dev", new OracleApexCodeActionRequest { ActionId = action.Id });

            Assert.True(result.IsSuccess, result.Summary);
            Assert.DoesNotContain(result.Diagnostics.Entries, entry => entry.Code == "invalid-child-component");
            var item = result.WorkspaceIndex.Items.Single(entry => entry.Identifier == "P1_NESTED_ITEM");
            var parent = result.WorkspaceIndex.Entries.Single(entry => entry.NodeId == item.ParentNodeId);
            Assert.Equal("page", parent.SemanticType);
            Assert.Equal("Home", parent.Identifier);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void Execute_RenameSharedComponent_UsesSemanticEditor()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            var fakeEditor = new FakeSemanticEditor();
            var service = new OracleApexCodeActionService(new OracleApexWorkspaceIndexBuilder(), fakeEditor);
            var action = service.GetAvailableActions(root, CreateEnvironment(), "dev").Single(item => item.Kind == OracleApexCodeActionKind.RenameSharedComponent && item.TargetIdentifier == "OLD_LOV");

            var result = service.Execute(root, CreateEnvironment(), "dev", new OracleApexCodeActionRequest { ActionId = action.Id, NewIdentifier = "NEW_LOV" });

            Assert.True(result.IsSuccess);
            Assert.Equal(OracleApexSemanticEditKind.RenameSharedComponent, fakeEditor.Calls.Single().Single().Kind);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void GetAvailableActions_IncludesReviewOnlyMigrationActions()
    {
        var root = CreateTempRoot();
        try
        {
            WritePackageWithReferenceMigrationIssues(root);
            var service = new OracleApexCodeActionService();

            var actions = service.GetAvailableActions(root, CreateEnvironment(), "dev");

            var reviewAction = Assert.Single(actions, item => item.Kind == OracleApexCodeActionKind.ReviewVersionMigrationImpact && item.ReviewMessage.Contains("TRACE", StringComparison.OrdinalIgnoreCase));
            var result = service.Execute(root, CreateEnvironment(), "dev", new OracleApexCodeActionRequest { ActionId = reviewAction.Id });

            Assert.True(result.IsSuccess);
            Assert.Contains("TRACE", result.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(result.ChangedFiles);
        }
        finally { DeleteTempRoot(root); }
    }

    private static OracleApexEnvironmentPreferences CreateEnvironment()
        => new() { ApplicationId = 100, Workspace = "TEST", ParsingSchema = "TESTSCHEMA", SourcePath = "src/apex" };

    private static void WriteValidPackage(string root)
    {
        var sourceRoot = Path.Combine(root, "src", "apex");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "pages"));
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
)
""");
    }

    private static void WritePackageWithMissingRequiredProperty(string root)
    {
        WriteValidPackage(root);
        var sourceRoot = Path.Combine(root, "src", "apex", "pages", "p00001-home.apx");
        File.WriteAllText(sourceRoot, """
page home (
    id: 1
    name: Home
    alias: HOME
    region broken (
        name: broken
        title: Broken Region
    )
)
""");
    }

    private static void WritePackageWithInvalidParentPlacement(string root)
    {
        WriteValidPackage(root);
        var sourceRoot = Path.Combine(root, "src", "apex", "pages", "p00001-home.apx");
        File.WriteAllText(sourceRoot, """
page home (
    id: 1
    name: Home
    alias: HOME
    item wrapper (
        name: P1_WRAPPER
        item nested-item (
            name: P1_NESTED_ITEM
        )
    )
)
""");
    }

    private static void WritePackageWithReferenceMigrationIssues(string root)
    {
        var sourceRoot = Path.Combine(root, "src", "apex");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "pages"));
        File.WriteAllText(Path.Combine(sourceRoot, "application.apx"), """
application customer-orders-demo (
    id: 100
    name: Customer Orders Demo
    alias: CUSTOMER-ORDERS-DEMO
    apexlang-version: 25.2
    theme: Vita
)
""");
        File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00001-home.apx"), """
page home (
    id: 1
    name: Home
    legacy-template: wizard
)
""");
        File.WriteAllText(Path.Combine(sourceRoot, "audit-handler.apx"), """
rest handler audit-handler (
    name: AUDIT_HANDLER
    method: TRACE
)
""");
        File.WriteAllText(Path.Combine(sourceRoot, "legacy-banner.apx"), """
legacyBanner warning-banner (
    title: Legacy warning
)
""");
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oracle-apex-code-actions-tests-{Guid.NewGuid():N}");
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

    private sealed class FakeSemanticEditor : IOracleApexSemanticEditor
    {
        public List<IReadOnlyList<OracleApexSemanticEditOperation>> Calls { get; } = [];

        public OracleApexSemanticEditResult Apply(string rootPath, OracleApexEnvironmentPreferences environment, string environmentName, params OracleApexSemanticEditOperation[] operations)
            => Apply(rootPath, environment, environmentName, (IReadOnlyList<OracleApexSemanticEditOperation>)operations);

        public OracleApexSemanticEditResult Apply(string rootPath, OracleApexEnvironmentPreferences environment, string environmentName, IReadOnlyList<OracleApexSemanticEditOperation> operations)
        {
            Calls.Add(operations);
            var index = new OracleApexWorkspaceIndexBuilder().Build(rootPath, environment, environmentName);
            return new OracleApexSemanticEditResult
            {
                IsSuccess = true,
                Message = "fake",
                WorkspaceIndex = index,
                Diagnostics = new OracleApexSemanticEditDiagnostics { Entries = index.Diagnostics },
                ChangedFiles = [],
            };
        }
    }
}
