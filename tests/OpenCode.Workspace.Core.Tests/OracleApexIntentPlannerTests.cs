using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexIntentPlannerTests
{
    [Fact]
    public void CreatePlan_TransformsHighLevelIntentIntoOrderedSemanticPlan()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            var planner = new OracleApexIntentPlanner();

            var result = planner.CreatePlan(root, CreateEnvironment(), "dev", "Add a Customers page with an interactive report, a form page, navigation entries, and validation.");

            Assert.True(result.Validation.IsValid);
            Assert.Equal(OracleApexPlanClassification.Additive, result.Plan.Classification);
            Assert.Equal("Create page 'Customers'", result.Plan.Operations[0].Title);
            Assert.Contains(result.Plan.Operations, operation => operation.Title.Contains("interactive report", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Plan.Operations, operation => operation.Title.Contains("form page", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Plan.Operations, operation => operation.Title.Contains("navigation entry", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Plan.Operations, operation => operation.Title.Contains("validation", StringComparison.OrdinalIgnoreCase));
            Assert.True(result.Plan.ExpectedChangedFiles.Count > 0);
            Assert.Contains("Customers", result.Plan.AffectedSymbols);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void CreatePlan_HighLevelCrudRequest_ProducesAlternativesAndBlocksExecutionUntilChosen()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            var planner = new OracleApexIntentPlanner();

            var result = planner.CreatePlan(root, CreateEnvironment(), "dev", "Build CRUD for Products");

            Assert.False(result.Validation.IsValid);
            Assert.Equal(2, result.Plan.Alternatives.Count);
            Assert.Contains(result.Plan.UnresolvedQuestions, question => question.Contains("Choose an implementation approach", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(result.Plan.Operations);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void CreatePlan_HighLevelModuleRequest_ReusesExistingPagesAndPlansOnlyMissingParts()
    {
        var root = CreateTempRoot();
        try
        {
            WritePackageWithCustomerPage(root);
            var planner = new OracleApexIntentPlanner();

            var result = planner.CreatePlan(root, CreateEnvironment(), "dev", "Build customer management module with report and form pages");

            Assert.True(result.Validation.IsValid);
            Assert.DoesNotContain(result.Plan.Operations, operation => operation.Title == "Create page 'Customers'");
            Assert.Contains(result.Plan.Operations, operation => operation.Title.Contains("form page", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Plan.Assumptions, item => item.Contains("Reuse existing component Navigation", StringComparison.OrdinalIgnoreCase));
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void CreatePlan_CompletesRequiredPropertiesForNewPages()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            var planner = new OracleApexIntentPlanner();

            var result = planner.CreatePlan(root, CreateEnvironment(), "dev", "Create Reports page");

            var createPage = result.Plan.Operations.Single(operation => operation.Title.Contains("Create page", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(OracleApexPlannedExecutionMode.SemanticEditor, createPage.ExecutionMode);
            var semanticOperation = createPage.SemanticOperations.Single();
            Assert.Equal("3", semanticOperation.Properties["id"]);
            Assert.Equal("REPORTS", semanticOperation.Properties["alias"]);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void CreatePlan_WhenNavigationMenuIsAmbiguous_ReportsUnresolvedQuestion()
    {
        var root = CreateTempRoot();
        try
        {
            WritePackageWithMultipleMenus(root);
            var planner = new OracleApexIntentPlanner();

            var result = planner.CreatePlan(root, CreateEnvironment(), "dev", "Add navigation entry Reports");

            Assert.False(result.Validation.IsValid);
            Assert.Contains(result.Plan.UnresolvedQuestions, question => question.Contains("ambiguous", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(OracleApexPlanClassification.PotentiallyConflicting, result.Plan.Classification);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void CreatePlan_RemovePage_IsClassifiedAsDestructive()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            var planner = new OracleApexIntentPlanner();

            var result = planner.CreatePlan(root, CreateEnvironment(), "dev", "Remove page Orders");

            Assert.Equal(OracleApexPlanClassification.Destructive, result.Plan.Classification);
            Assert.True(result.Plan.RequiresConfirmation);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void CreatePlan_IsDryRunAndDoesNotModifyFiles()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            var pageFile = Path.Combine(root, "src", "apex", "pages", "p00001-home.apx");
            var before = File.ReadAllText(pageFile);
            var planner = new OracleApexIntentPlanner();

            _ = planner.CreatePlan(root, CreateEnvironment(), "dev", "Create Reports page");

            var after = File.ReadAllText(pageFile);
            Assert.Equal(before, after);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void ExecutePlan_SucceedsAtomicallyAndRebuildsWorkspaceIndex()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            var planner = new OracleApexIntentPlanner();
            var plan = planner.CreatePlan(root, CreateEnvironment(), "dev", "Add a Customers page with an interactive report, a form page, navigation entries, and validation.").Plan;

            var result = planner.ExecutePlan(root, CreateEnvironment(), "dev", plan);

            Assert.True(result.IsSuccess, result.Summary);
            Assert.Contains(result.WorkspaceIndex.Pages, page => page.Identifier == "Customers");
            Assert.Contains(result.WorkspaceIndex.Pages, page => page.Identifier == "Customers Form");
            Assert.Contains(result.WorkspaceIndex.NavigationEntries, entry => entry.Identifier == "Customers");
            Assert.True(result.ChangedFiles.Count > 0);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void ExecutePlan_HighLevelIntentWithExplicitApproach_AppliesBlueprintOperations()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            var planner = new OracleApexIntentPlanner();
            var plan = planner.CreatePlan(root, CreateEnvironment(), "dev", "Build customer management module with report and form pages").Plan;

            var result = planner.ExecutePlan(root, CreateEnvironment(), "dev", plan);

            Assert.True(result.IsSuccess, result.Summary);
            Assert.Contains(result.WorkspaceIndex.Pages, page => page.Identifier == "Customers");
            Assert.Contains(result.WorkspaceIndex.Pages, page => page.Identifier == "Customer Form");
            Assert.Contains(result.WorkspaceIndex.NavigationEntries, entry => entry.Identifier == "Customers");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void ExecutePlan_WhenLaterOperationFails_RollsBackEntirePlan()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            var planner = new OracleApexIntentPlanner();
            var plan = planner.CreatePlan(root, CreateEnvironment(), "dev", "Create Reports page").Plan;
            plan.Operations.Add(new OracleApexPlannedOperation
            {
                Sequence = plan.Operations.Count + 1,
                Title = "Break region add",
                ExecutionMode = OracleApexPlannedExecutionMode.SemanticEditor,
                SemanticOperations = [OracleApexSemanticEditOperation.AddRegion("Reports", "Broken Region")],
                TargetComponentType = "region",
                TargetIdentifier = "Broken Region",
            });

            var result = planner.ExecutePlan(root, CreateEnvironment(), "dev", plan);

            Assert.False(result.IsSuccess);
            var rebuilt = new OracleApexWorkspaceIndexBuilder().Build(root, CreateEnvironment(), "dev");
            Assert.DoesNotContain(rebuilt.Pages, page => page.Identifier == "Reports");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void ExecutePlan_DestructivePlanRequiresExplicitConfirmation()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            var planner = new OracleApexIntentPlanner();
            var plan = planner.CreatePlan(root, CreateEnvironment(), "dev", "Remove page Orders").Plan;

            var result = planner.ExecutePlan(root, CreateEnvironment(), "dev", plan, confirmDestructive: false);

            Assert.False(result.IsSuccess);
            Assert.Contains("requires explicit confirmation", result.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally { DeleteTempRoot(root); }
    }

    private static OracleApexEnvironmentPreferences CreateEnvironment()
        => new() { ApplicationId = 100, Workspace = "TEST", ParsingSchema = "TESTSCHEMA", SourcePath = "src/apex" };

    private static void WriteValidPackage(string root)
    {
        var sourceRoot = Path.Combine(root, "src", "apex");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "pages"));
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
)
""");
        File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00002-orders.apx"), """
page orders (
    id: 2
    name: Orders
    alias: ORDERS
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

    private static void WritePackageWithMultipleMenus(string root)
    {
        WriteValidPackage(root);
        var sourceRoot = Path.Combine(root, "src", "apex", "shared_components", "navigation_menus", "secondary-navigation.apx");
        File.WriteAllText(sourceRoot, """
navigation menu secondary-navigation (
    name: Secondary Navigation
)
""");
    }

    private static void WritePackageWithCustomerPage(string root)
    {
        WriteValidPackage(root);
        File.WriteAllText(Path.Combine(root, "src", "apex", "pages", "p00003-customers.apx"), """
page customers (
    id: 3
    name: Customers
    alias: CUSTOMERS
)
""");
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oracle-apex-intent-tests-{Guid.NewGuid():N}");
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
