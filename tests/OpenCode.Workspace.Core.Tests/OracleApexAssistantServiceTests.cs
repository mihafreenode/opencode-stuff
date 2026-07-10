using System.Reflection;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexAssistantServiceTests
{
    [Fact]
    public void CreatePlan_ProducesReviewablePlan()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var service = new OracleApexAssistantService(new FakeSyncService());

            var response = service.CreatePlan(CreateSnapshot(root), new OracleApexAssistantRequest { Prompt = "Create Reports page" });

            Assert.Contains("Summary:", response.Review, StringComparison.Ordinal);
            Assert.Contains("Classification:", response.Review, StringComparison.Ordinal);
            Assert.Contains("Operations:", response.Review, StringComparison.Ordinal);
            Assert.NotEmpty(response.Plan.Operations);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task ExecutePlan_DestructivePlanRequiresApproval()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var service = new OracleApexAssistantService(new FakeSyncService());
            var snapshot = CreateSnapshot(root);
            var plan = service.CreatePlan(snapshot, new OracleApexAssistantRequest { Prompt = "Remove page Orders" }).Plan;

            var response = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Remove page Orders", ConfirmPlan = false }, plan);

            Assert.False(response.IsSuccess);
            Assert.True(response.ConfirmationRequired);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task ExecutePlan_UnresolvedQuestionsBlockApply()
    {
        var root = CreateTempRoot();
        try
        {
            WritePackageWithMultipleMenus(root);
            WriteAtlasState(root);
            var service = new OracleApexAssistantService(new FakeSyncService());
            var snapshot = CreateSnapshot(root);
            var plan = service.CreatePlan(snapshot, new OracleApexAssistantRequest { Prompt = "Add navigation entry Reports" }).Plan;

            var response = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Add navigation entry Reports", ConfirmPlan = true }, plan);

            Assert.False(response.IsSuccess);
            Assert.NotEmpty(response.UnresolvedQuestions);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task ExecutePlan_ApprovedPlanExecutesAtomically()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var sync = new FakeSyncService();
            var service = new OracleApexAssistantService(sync);
            var snapshot = CreateSnapshot(root);
            var plan = service.CreatePlan(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page" }).Plan;

            var response = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.SourceOnly }, plan);

            Assert.True(response.IsSuccess, response.Summary);
            Assert.Contains(response.WorkspaceIndex.Pages, page => page.Identifier == "Reports");
            Assert.Equal(0, sync.ValidateCalls);
            Assert.Equal(0, sync.ImportCalls);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task ExecutePlan_FailureRollsBack()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var service = new OracleApexAssistantService(new FakeSyncService());
            var snapshot = CreateSnapshot(root);
            var plan = service.CreatePlan(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page" }).Plan;
            plan.Operations.Add(new OracleApexPlannedOperation
            {
                Sequence = plan.Operations.Count + 1,
                Title = "Broken region",
                ExecutionMode = OracleApexPlannedExecutionMode.SemanticEditor,
                SemanticOperations = [OracleApexSemanticEditOperation.AddRegion("Reports", "Broken Region")],
                TargetComponentType = "region",
                TargetIdentifier = "Broken Region",
            });

            var response = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.SourceOnly }, plan);

            Assert.False(response.IsSuccess);
            var rebuilt = new OracleApexWorkspaceIndexBuilder().Build(root, CreateEnvironment(), "dev");
            Assert.DoesNotContain(rebuilt.Pages, page => page.Identifier == "Reports");
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task ExecutePlan_ValidateOnlyFlowRunsValidation()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var sync = new FakeSyncService();
            var service = new OracleApexAssistantService(sync);
            var snapshot = CreateSnapshot(root);
            var plan = service.CreatePlan(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page" }).Plan;

            var response = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.ValidateOnly }, plan);

            Assert.True(response.IsSuccess, response.Summary);
            Assert.Equal(1, sync.ValidateCalls);
            Assert.Equal(0, sync.ImportCalls);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task ExecutePlan_ValidateAndImportFlowRunsImport()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var sync = new FakeSyncService();
            var service = new OracleApexAssistantService(sync);
            var snapshot = CreateSnapshot(root, syncState: WorkspaceSynchronizationState.InSync);
            var plan = service.CreatePlan(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page" }).Plan;

            var response = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.ValidateAndImport }, plan);

            Assert.True(response.IsSuccess, response.Summary);
            Assert.Equal(1, sync.ValidateCalls);
            Assert.Equal(1, sync.ImportCalls);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task ExecutePlan_ImportBlockedAfterValidationFailure()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var sync = new FakeSyncService { ValidationState = WorkspaceSynchronizationState.ValidationFailed, ValidationSuccess = false };
            var service = new OracleApexAssistantService(sync);
            var snapshot = CreateSnapshot(root, syncState: WorkspaceSynchronizationState.InSync);
            var plan = service.CreatePlan(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page" }).Plan;

            var response = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.ValidateAndImport }, plan);

            Assert.True(response.IsSuccess);
            Assert.False(response.SafeToContinueDeployment);
            Assert.Equal(1, sync.ValidateCalls);
            Assert.Equal(0, sync.ImportCalls);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task ExecutePlan_DivergedSynchronizationBlocksUnsafeAutomaticDeployment()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var sync = new FakeSyncService();
            var service = new OracleApexAssistantService(sync);
            var snapshot = CreateSnapshot(root, syncState: WorkspaceSynchronizationState.Diverged);
            var plan = service.CreatePlan(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page" }).Plan;

            var response = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.ValidateAndImport }, plan);

            Assert.True(response.IsSuccess);
            Assert.False(response.SafeToContinueDeployment);
            Assert.Equal(1, sync.ValidateCalls);
            Assert.Equal(0, sync.ImportCalls);
            Assert.Contains(response.Warnings, warning => warning.Contains("blocks automatic import", StringComparison.OrdinalIgnoreCase));
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void OracleApexLangSkill_ProhibitsRawApxMutation()
    {
        var definition = CreateDefinition();
        var generatedType = typeof(WorkspaceDefinition).Assembly.GetType("OpenCode.Workspace.Core.Generation.OracleWorkspaceGeneratedContent", throwOnError: true)!;
        var generate = generatedType.GetMethod("Generate", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var files = (IReadOnlyDictionary<string, string>)generate.Invoke(null,
        [
            definition,
            null,
            (Func<string, string>)(content => content),
            (Func<string, string>)(content => content),
            (Func<string, string>)(content => content),
        ])!;

        var skill = files[Path.Combine("skills", "oracle", "apexlang.md")];
        Assert.Contains("inspect the workspace index before planning a change", skill, StringComparison.Ordinal);
        Assert.Contains("execute APEXlang changes only through the semantic planner", skill, StringComparison.Ordinal);
        Assert.Contains("do not edit raw `.apx` text directly", skill, StringComparison.Ordinal);
    }

    private static WorkspaceSnapshot CreateSnapshot(string root, WorkspaceSynchronizationState syncState = WorkspaceSynchronizationState.InSync)
    {
        var paths = WorkspacePathBuilder.Build(root);
        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord { Name = "oracle-apexlang", RootPath = root, RepositoryPath = root, ConfigurationPath = paths.WorkspaceYamlPath, CreatedUtc = DateTimeOffset.UtcNow, LastOpenedUtc = DateTimeOffset.UtcNow },
            Definition = CreateDefinition(),
            Paths = paths,
            ConfigurationPath = paths.WorkspaceYamlPath,
            RuntimeState = WorkspaceRuntimeState.Stopped,
            Safety = new WorkspaceSafetySnapshot { OverallStatus = WorkspaceSafetyLevel.Protected, Headline = "Safe", Message = "Safe", LocalRecovery = new WorkspaceLocalRecoverySnapshot(), Backup = new WorkspaceBackupSnapshot(), IgnorePolicy = new WorkspaceIgnorePolicyReview(), AdvancedGit = new WorkspaceAdvancedGitSnapshot { LatestCommitSha = "abc123" } },
            Session = new WorkspaceSessionSnapshot(),
            Synchronization = new WorkspaceSynchronizationSnapshot
            {
                State = syncState,
                DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                {
                    EnvironmentName = "dev",
                    SyncMode = WorkspaceSynchronizationModes.Manual,
                    State = syncState,
                },
                Environments =
                [
                    new WorkspaceSynchronizationEnvironmentSnapshot
                    {
                        EnvironmentName = "dev",
                        SyncMode = WorkspaceSynchronizationModes.Manual,
                        State = syncState,
                    },
                ],
            },
            Health = new WorkspaceHealthSnapshot(),
            Readiness = new WorkspaceReadinessSnapshot(),
            AvailableServices = Array.Empty<WorkspaceServiceInfo>(),
        };
    }

    private static WorkspaceDefinition CreateDefinition()
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-apexlang", Image = "ubuntu:24.04" },
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
        File.WriteAllText(Path.Combine(root, "src", "apex", "shared_components", "navigation_menus", "secondary-navigation.apx"), """
navigation menu secondary-navigation (
    name: Secondary Navigation
)
""");
    }

    private static void WriteAtlasState(string root)
    {
        var atlasPath = Path.Combine(root, ".opencode", "knowledge", "apexlang-atlas");
        Directory.CreateDirectory(atlasPath);
        File.WriteAllText(Path.Combine(atlasPath, "state.json"), "{}\n");
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oracle-apex-assistant-tests-{Guid.NewGuid():N}");
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

    private sealed class FakeSyncService : IOracleApexAssistantSynchronizationService
    {
        public int ValidateCalls { get; private set; }
        public int ImportCalls { get; private set; }
        public WorkspaceSynchronizationState ValidationState { get; set; } = WorkspaceSynchronizationState.InSync;
        public bool ValidationSuccess { get; set; } = true;

        public Task<WorkspaceSynchronizationStatusResult> GetStatusAsync(WorkspaceSnapshot snapshot, string? environmentName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceSynchronizationStatusResult { Snapshot = snapshot.Synchronization });

        public Task<WorkspaceSynchronizationOperationResult> ValidateAsync(WorkspaceSnapshot snapshot, string? environmentName = null, CancellationToken cancellationToken = default)
        {
            ValidateCalls++;
            return Task.FromResult(new WorkspaceSynchronizationOperationResult
            {
                Snapshot = new WorkspaceSynchronizationSnapshot
                {
                    State = ValidationState,
                    DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot { EnvironmentName = environmentName ?? "dev", State = ValidationState },
                },
                Message = "validated",
                ProcessResult = CreateProcessResult("validate", ValidationSuccess ? 0 : 1),
            });
        }

        public Task<WorkspaceSynchronizationOperationResult> ImportAsync(WorkspaceSnapshot snapshot, string? environmentName = null, CancellationToken cancellationToken = default)
        {
            ImportCalls++;
            return Task.FromResult(new WorkspaceSynchronizationOperationResult
            {
                Snapshot = snapshot.Synchronization,
                Message = "imported",
                ProcessResult = CreateProcessResult("import", 0),
            });
        }

        private static ProcessResult CreateProcessResult(string command, int exitCode)
            => new()
            {
                Command = command,
                ExitCode = exitCode,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
                StandardOutputLines = Array.Empty<string>(),
                StandardErrorLines = Array.Empty<string>(),
                Duration = TimeSpan.Zero,
            };
    }
}
