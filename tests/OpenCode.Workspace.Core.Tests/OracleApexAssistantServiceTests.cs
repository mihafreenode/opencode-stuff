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
    public void CreatePlan_HighLevelIntentReview_IncludesAlternativesAndCategorizedChanges()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var service = new OracleApexAssistantService(new FakeSyncService());

            var response = service.CreatePlan(CreateSnapshot(root), new OracleApexAssistantRequest { Prompt = "Build CRUD for Products" });

            Assert.Contains("Estimated complexity:", response.Review, StringComparison.Ordinal);
            Assert.Contains("Alternatives:", response.Review, StringComparison.Ordinal);
            Assert.Contains("Unresolved Questions:", response.Review, StringComparison.Ordinal);
            Assert.Equal(2, response.Plan.Alternatives.Count);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void CreatePlan_IncludesCompatibilitySummaryAndProvenance()
    {
        var root = CreateTempRoot();
        try
        {
            WritePackageWithReferenceCompatibilityIssues(root);
            WriteAtlasState(root);
            var service = new OracleApexAssistantService(new FakeSyncService());

            var response = service.CreatePlan(CreateSnapshot(root), new OracleApexAssistantRequest { Prompt = "Create Reports page" });

            Assert.Contains("Compatibility:", response.Review, StringComparison.Ordinal);
            Assert.Contains("Target APEXlang version:", response.Review, StringComparison.Ordinal);
            Assert.Contains("SQLcl validation especially important: Yes", response.Review, StringComparison.Ordinal);
            Assert.NotEmpty(response.Compatibility.Findings);
            Assert.All(response.Compatibility.Findings, finding => Assert.NotEmpty(finding.Provenance.ToDocumentationReference));
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void CompatibilityAnalyzer_WarnsForRemovedPropertyAndNewerVersionOnlyComponent()
    {
        var root = CreateTempRoot();
        try
        {
            WritePackageWithReferenceCompatibilityIssues(root);
            var index = new OracleApexWorkspaceIndexBuilder().Build(root, CreateEnvironment(), "dev");
            var importer = new OracleApexLanguageReferenceImporter();
            var previous = importer.Import(
                File.ReadAllText(Path.Combine(GetRepositoryRoot(), "tests", "OpenCode.Workspace.Core.Tests", "Fixtures", "apexlang-reference-v25.2.md")),
                File.ReadAllText(Path.Combine(GetRepositoryRoot(), "tests", "OpenCode.Workspace.Core.Tests", "Fixtures", "apexlang-reference-v25.2.ebnf")),
                new OracleApexLanguageReferenceProvenance { SourceKind = "fixture", SourceLocation = "previous", GrammarLocation = "previous", ApexVersion = "25.2", ImportedUtc = DateTimeOffset.UnixEpoch });
            var current = importer.Import(
                File.ReadAllText(Path.Combine(GetRepositoryRoot(), "tests", "OpenCode.Workspace.Core.Tests", "Fixtures", "apexlang-reference-v26.1.md")),
                File.ReadAllText(Path.Combine(GetRepositoryRoot(), "tests", "OpenCode.Workspace.Core.Tests", "Fixtures", "apexlang-reference-v26.1.ebnf")),
                new OracleApexLanguageReferenceProvenance { SourceKind = "fixture", SourceLocation = "current", GrammarLocation = "current", ApexVersion = "26.1", ImportedUtc = DateTimeOffset.UnixEpoch });
            var diff = new OracleApexLanguageReferenceCatalogComparer().Compare(
                previous,
                current,
                OracleApexComponentCatalog.AtlasSeed.CompareWithReference(previous),
                OracleApexComponentCatalog.AtlasSeed.CompareWithReference(current));
            var analyzer = new OracleApexLanguageReferenceWorkspaceImpactAnalyzer(diff);
            var plan = new OracleApexEditPlan { Summary = "compatibility test" };
            plan.Operations.Add(new OracleApexPlannedOperation
            {
                Sequence = 1,
                Title = "Update removed property",
                ExecutionMode = OracleApexPlannedExecutionMode.SemanticEditor,
                SemanticOperations = [OracleApexSemanticEditOperation.UpdateProperties("application", "Customer Orders Demo", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["theme"] = "Vita" })],
                TargetComponentType = "application",
                TargetIdentifier = "Customer Orders Demo",
                Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["theme"] = "Vita" },
            });
            plan.Operations.Add(new OracleApexPlannedOperation
            {
                Sequence = 2,
                Title = "Create deployment profile",
                ExecutionMode = OracleApexPlannedExecutionMode.SemanticEditor,
                SemanticOperations = [OracleApexSemanticEditOperation.AddSharedComponent("deployment", "DEV_DEPLOYMENT", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = "DEV_DEPLOYMENT" })],
                TargetComponentType = "deployment",
                TargetIdentifier = "DEV_DEPLOYMENT",
                Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = "DEV_DEPLOYMENT" },
            });

            var compatibility = analyzer.AnalyzePlan(index, plan);

            Assert.Contains(compatibility.Findings, finding => finding.Code == "plan-property-removed");
            Assert.Contains(compatibility.Findings, finding => finding.Code == "plan-component-newer-version-only");
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
    public async Task ExecutePlan_BlocksOnlyKnownInvalidCompatibilityConstructs()
    {
        var root = CreateTempRoot();
        try
        {
            WritePackageWithReferenceCompatibilityIssues(root);
            WriteAtlasState(root);
            var service = new OracleApexAssistantService(new FakeSyncService());
            var snapshot = CreateSnapshot(root);
            var blockedPlan = new OracleApexEditPlan { Summary = "blocked compatibility plan" };
            blockedPlan.Operations.Add(new OracleApexPlannedOperation
            {
                Sequence = 1,
                Title = "Create deployment profile",
                ExecutionMode = OracleApexPlannedExecutionMode.SemanticEditor,
                SemanticOperations = [OracleApexSemanticEditOperation.AddSharedComponent("deployment-profile", "DEV_DEPLOYMENT", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = "DEV_DEPLOYMENT" })],
                TargetComponentType = "deployment-profile",
                TargetIdentifier = "DEV_DEPLOYMENT",
                Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = "DEV_DEPLOYMENT" },
            });

            var blocked = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Create deployment profile", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.SourceOnly }, blockedPlan);

            Assert.False(blocked.IsSuccess);
            Assert.Contains("known invalid or removed APEXlang construct", blocked.Summary, StringComparison.OrdinalIgnoreCase);

            var validRoot = CreateTempRoot();
            try
            {
                WriteValidPackage(validRoot);
                WriteAtlasState(validRoot);
                var validSnapshot = CreateSnapshot(validRoot);
                var validPlan = service.CreatePlan(validSnapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page" }).Plan;
                var allowed = await service.ExecutePlanAsync(validSnapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.SourceOnly }, validPlan);

                Assert.True(allowed.IsSuccess, allowed.Summary);
            }
            finally
            {
                DeleteTempRoot(validRoot);
            }
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
    public async Task ExecutePlan_ValidationFailure_ReturnsCompilerDiagnosticsAndRepairPlan()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var sync = new FakeSyncService
            {
                ValidationState = WorkspaceSynchronizationState.ValidationFailed,
                ValidationSuccess = false,
                ValidationResultFactory = _ => new WorkspaceSynchronizationOperationResult
                {
                    Snapshot = new WorkspaceSynchronizationSnapshot
                    {
                        State = WorkspaceSynchronizationState.ValidationFailed,
                        DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot { EnvironmentName = "dev", State = WorkspaceSynchronizationState.ValidationFailed },
                    },
                    Message = "validation failed",
                    ProcessResult = CreateProcessResult("validate", 1, standardErrorLines: ["ERROR src/apex/pages/p00003-reports.apx:4:5 [APEX-1001] component=page property=alias - Missing required property 'alias'."]),
                    Validation = new OracleApexValidationResult
                    {
                        IsSuccess = false,
                        Diagnostics = [new OracleApexCompilerDiagnostic { FilePath = "src/apex/pages/p00003-reports.apx", Line = 4, Column = 5, Component = "page", Property = "alias", Severity = "Error", CompilerCode = "APEX-1001", Message = "Missing required property 'alias'.", Category = "missing-required-property" }],
                    },
                },
            };
            var service = new OracleApexAssistantService(sync);
            var snapshot = CreateSnapshot(root, syncState: WorkspaceSynchronizationState.InSync);
            var plan = service.CreatePlan(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page" }).Plan;

            var response = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.ValidateOnly }, plan);

            Assert.True(response.IsSuccess);
            Assert.NotNull(response.CompilerValidation);
            Assert.NotEmpty(response.CompilerValidation!.Diagnostics);
            Assert.NotNull(response.SuggestedRepairPlan);
            Assert.True(response.SuggestedRepairPlan!.Operations.Count > 0 || response.SuggestedRepairPlan.UnresolvedQuestions.Count > 0);
            Assert.False(response.SafeToContinueDeployment);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task ExecutePlan_AutoRepair_RevalidatesAfterRepair()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            WriteAutoRepairSettings(root, enabled: true);
            var validationCalls = 0;
            var sync = new FakeSyncService
            {
                ValidationResultFactory = _ =>
                {
                    validationCalls++;
                    return validationCalls == 1
                        ? new WorkspaceSynchronizationOperationResult
                        {
                            Snapshot = new WorkspaceSynchronizationSnapshot
                            {
                                State = WorkspaceSynchronizationState.ValidationFailed,
                                DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot { EnvironmentName = "dev", State = WorkspaceSynchronizationState.ValidationFailed },
                            },
                            Message = "validation failed",
                            ProcessResult = CreateProcessResult("validate", 1, standardErrorLines: ["ERROR src/apex/pages/p00003-reports.apx:4:5 [APEX-1001] component=page property=alias - Missing required property 'alias'."]),
                            Validation = new OracleApexValidationResult
                            {
                                IsSuccess = false,
                                Diagnostics = [new OracleApexCompilerDiagnostic { FilePath = "src/apex/pages/p00003-reports.apx", Line = 4, Column = 5, Component = "page", Property = "alias", Severity = "Error", CompilerCode = "APEX-1001", Message = "Missing required property 'alias'.", Category = "missing-required-property" }],
                            },
                        }
                        : new WorkspaceSynchronizationOperationResult
                        {
                            Snapshot = new WorkspaceSynchronizationSnapshot
                            {
                                State = WorkspaceSynchronizationState.InSync,
                                DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot { EnvironmentName = "dev", State = WorkspaceSynchronizationState.InSync },
                            },
                            Message = "validated",
                            ProcessResult = CreateProcessResult("validate", 0),
                            Validation = new OracleApexValidationResult { IsSuccess = true },
                        };
                },
            };
            var service = new OracleApexAssistantService(sync);
            var snapshot = CreateSnapshot(root, syncState: WorkspaceSynchronizationState.InSync);
            var plan = service.CreatePlan(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page" }).Plan;

            var response = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.ValidateOnly, EnableSafeAutomaticRepair = true }, plan);

            Assert.True(response.IsSuccess);
            Assert.True(response.SafeToContinueDeployment);
            Assert.Equal(2, validationCalls);
            Assert.NotNull(response.CompilerValidation);
            Assert.True(response.CompilerValidation!.IsSuccess);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task ExecutePlan_NonDevelopmentImportRequiresExplicitOverride()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var service = new OracleApexAssistantService(new FakeSyncService());
            var snapshot = CreateSnapshot(root, environmentName: "production", syncState: WorkspaceSynchronizationState.InSync);
            var plan = service.CreatePlan(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", EnvironmentName = "production" }).Plan;

            var response = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", EnvironmentName = "production", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.ValidateAndImport }, plan);

            Assert.True(response.IsSuccess);
            Assert.False(response.SafeToContinueDeployment);
            Assert.Contains(response.Warnings, warning => warning.Contains("requires explicit override", StringComparison.OrdinalIgnoreCase));
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
    public async Task ExecutePlan_SuccessfulExecution_CreatesRollbackManifest()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var service = new OracleApexAssistantService(new FakeSyncService());
            var snapshot = CreateSnapshot(root);
            var plan = service.CreatePlan(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page" }).Plan;

            var response = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.SourceOnly }, plan);

            Assert.True(response.IsSuccess);
            Assert.NotNull(response.RollbackManifest);
            Assert.Equal(OracleApexAssistantRollbackState.Available, response.RollbackManifest!.RollbackState);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task Rollback_BeforeExecution_IsUnavailable()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var service = new OracleApexAssistantService(new FakeSyncService());

            var response = await service.RollBackGeneratedChangeAsync(CreateSnapshot(root));

            Assert.False(response.IsSuccess);
            Assert.Equal(OracleApexAssistantRollbackState.Blocked, response.RollbackState);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task Rollback_RestoresOnlyAssistantTouchedFiles()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var untouchedPath = Path.Combine(root, "src", "apex", "readme.txt");
            File.WriteAllText(untouchedPath, "keep me\n");
            var service = new OracleApexAssistantService(new FakeSyncService());
            var snapshot = CreateSnapshot(root);
            var plan = service.CreatePlan(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page" }).Plan;

            var execution = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.SourceOnly }, plan);
            var rollback = await service.RollBackGeneratedChangeAsync(snapshot);
            var rebuilt = new OracleApexWorkspaceIndexBuilder().Build(root, CreateEnvironment(), "dev");

            Assert.True(rollback.IsSuccess);
            Assert.DoesNotContain(rebuilt.Pages, page => page.Identifier == "Reports");
            Assert.Equal("keep me\n", File.ReadAllText(untouchedPath));
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task Rollback_LaterEditsToTouchedFiles_BlockRestore()
    {
        var root = CreateTempRoot();
        try
        {
            WriteValidPackage(root);
            WriteAtlasState(root);
            var service = new OracleApexAssistantService(new FakeSyncService());
            var snapshot = CreateSnapshot(root);
            var plan = service.CreatePlan(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page" }).Plan;

            var execution = await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.SourceOnly }, plan);
            var reportsPath = Path.Combine(root, "src", "apex", "pages", "p00003-reports.apx");
            File.AppendAllText(reportsPath, "-- later user edit\n");

            var rollback = await service.RollBackGeneratedChangeAsync(snapshot);

            Assert.False(rollback.IsSuccess);
            Assert.Equal(OracleApexAssistantRollbackState.Blocked, rollback.RollbackState);
            Assert.Contains("later edits", rollback.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task Rollback_RecordsEvidenceAndRefreshesSynchronization()
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

            await service.ExecutePlanAsync(snapshot, new OracleApexAssistantRequest { Prompt = "Create Reports page", ConfirmPlan = true, PostEditBehavior = OracleApexAssistantPostEditBehavior.SourceOnly }, plan);
            var rollback = await service.RollBackGeneratedChangeAsync(snapshot);
            var evidence = new OracleApexValidationFeedbackService().ReadEvidence(snapshot);

            Assert.True(rollback.IsSuccess);
            Assert.NotNull(rollback.Synchronization);
            Assert.Contains(evidence.Entries, entry => string.Equals(entry.RollbackResult, "Rollback completed.", StringComparison.Ordinal));
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

    private static WorkspaceSnapshot CreateSnapshot(string root, WorkspaceSynchronizationState syncState = WorkspaceSynchronizationState.InSync, string environmentName = "dev")
    {
        var paths = WorkspacePathBuilder.Build(root);
        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord { Name = "oracle-apexlang", RootPath = root, RepositoryPath = root, ConfigurationPath = paths.WorkspaceYamlPath, CreatedUtc = DateTimeOffset.UtcNow, LastOpenedUtc = DateTimeOffset.UtcNow },
            Definition = CreateDefinition(environmentName),
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
                    EnvironmentName = environmentName,
                    SyncMode = WorkspaceSynchronizationModes.Manual,
                    State = syncState,
                },
                Environments =
                [
                    new WorkspaceSynchronizationEnvironmentSnapshot
                    {
                        EnvironmentName = environmentName,
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

    private static WorkspaceDefinition CreateDefinition(string environmentName = "dev")
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-apexlang", Image = "ubuntu:24.04" },
            Features = ["core", "oracle-demo", "oracle-apex-demo", "oracle-apexlang-demo"],
            Services = ["oracle-demo", "oracle-ords"],
            Oracle = new OracleWorkspacePreferences
            {
                Apex = new OracleApexWorkspacePreferences
                {
                    DefaultEnvironment = environmentName,
                    Environments = new Dictionary<string, OracleApexEnvironmentPreferences>
                    {
                        [environmentName] = CreateEnvironment(),
                    },
                },
            },
        };

    private static ProcessResult CreateProcessResult(string command, int exitCode, IReadOnlyList<string>? standardOutputLines = null, IReadOnlyList<string>? standardErrorLines = null)
        => new()
        {
            Command = command,
            ExitCode = exitCode,
            StandardOutput = standardOutputLines is null ? string.Empty : string.Join(Environment.NewLine, standardOutputLines),
            StandardError = standardErrorLines is null ? string.Empty : string.Join(Environment.NewLine, standardErrorLines),
            StandardOutputLines = standardOutputLines ?? Array.Empty<string>(),
            StandardErrorLines = standardErrorLines ?? Array.Empty<string>(),
            Duration = TimeSpan.Zero,
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

    private static void WritePackageWithReferenceCompatibilityIssues(string root)
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
)
""");
    }

    private static void WriteAtlasState(string root)
    {
        var atlasPath = Path.Combine(root, ".opencode", "knowledge", "apexlang-atlas");
        Directory.CreateDirectory(atlasPath);
        File.WriteAllText(Path.Combine(atlasPath, "state.json"), "{}\n");
    }

    private static void WriteAutoRepairSettings(string root, bool enabled)
    {
        var knowledgePath = Path.Combine(root, ".opencode", "knowledge", "apex-assistant");
        Directory.CreateDirectory(knowledgePath);
        File.WriteAllText(Path.Combine(knowledgePath, "settings.json"), $$"""
{ "SafeAutomaticRepairEnabled": {{enabled.ToString().ToLowerInvariant()}} }
""");
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oracle-apex-assistant-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

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
        public Func<string?, WorkspaceSynchronizationOperationResult>? ValidationResultFactory { get; set; }
        public Func<string?, WorkspaceSynchronizationOperationResult>? ImportResultFactory { get; set; }

        public Task<WorkspaceSynchronizationStatusResult> GetStatusAsync(WorkspaceSnapshot snapshot, string? environmentName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceSynchronizationStatusResult { Snapshot = snapshot.Synchronization });

        public Task<WorkspaceSynchronizationOperationResult> ValidateAsync(WorkspaceSnapshot snapshot, string? environmentName = null, CancellationToken cancellationToken = default)
        {
            ValidateCalls++;
            if (ValidationResultFactory is not null)
            {
                return Task.FromResult(ValidationResultFactory(environmentName));
            }

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
            if (ImportResultFactory is not null)
            {
                return Task.FromResult(ImportResultFactory(environmentName));
            }

            return Task.FromResult(new WorkspaceSynchronizationOperationResult
            {
                Snapshot = snapshot.Synchronization,
                Message = "imported",
                ProcessResult = CreateProcessResult("import", 0),
            });
        }

        private static ProcessResult CreateProcessResult(string command, int exitCode, IReadOnlyList<string>? standardOutputLines = null, IReadOnlyList<string>? standardErrorLines = null)
            => new()
            {
                Command = command,
                ExitCode = exitCode,
                StandardOutput = standardOutputLines is null ? string.Empty : string.Join(Environment.NewLine, standardOutputLines),
                StandardError = standardErrorLines is null ? string.Empty : string.Join(Environment.NewLine, standardErrorLines),
                StandardOutputLines = standardOutputLines ?? Array.Empty<string>(),
                StandardErrorLines = standardErrorLines ?? Array.Empty<string>(),
                Duration = TimeSpan.Zero,
            };
    }
}
