using System.Text.Json;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexValidationFeedbackServiceTests
{
    [Fact]
    public void BuildValidationResult_ParsesStructuredDiagnosticsAndMapsToOperations()
    {
        var service = new OracleApexValidationFeedbackService();
        var index = new OracleApexWorkspaceIndex
        {
            SourcePath = "src/apex",
            Entries =
            [
                new OracleApexWorkspaceIndexEntry
                {
                    NodeId = "page:customers",
                    SemanticType = "page",
                    Identifier = "Customers",
                    SourceFile = "src/apex/pages/p00003-customers.apx",
                    Line = 1,
                    EndLine = 10,
                    Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "3" },
                },
            ],
        };
        var plan = new OracleApexEditPlan
        {
            Intent = "Build customer management module",
            BlueprintModules = ["Customers Management"],
            BlueprintEntities = ["Customer"],
        };
        plan.Operations.Add(new OracleApexPlannedOperation
        {
            Sequence = 1,
            Title = "Create page 'Customers'",
            TargetComponentType = "page",
            TargetIdentifier = "Customers",
            AffectedSymbols = ["Customers"],
            ExpectedChangedFiles = ["src/apex/pages/p00003-customers.apx"],
        });
        var process = new ProcessResult
        {
            Command = "validate",
            ExitCode = 1,
            StandardError = "ERROR src/apex/pages/p00003-customers.apx:4:5 [APEX-1001] component=page property=alias - Missing required property 'alias'.",
            StandardOutput = string.Empty,
            StandardErrorLines = ["ERROR src/apex/pages/p00003-customers.apx:4:5 [APEX-1001] component=page property=alias - Missing required property 'alias'."],
            StandardOutputLines = Array.Empty<string>(),
            Duration = TimeSpan.Zero,
        };

        var result = service.BuildValidationResult(process, index, plan);

        Assert.False(result.IsSuccess);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("src/apex/pages/p00003-customers.apx", diagnostic.FilePath);
        Assert.Equal(4, diagnostic.Line);
        Assert.Equal("alias", diagnostic.Property);
        Assert.Equal("APEX-1001", diagnostic.CompilerCode);
        Assert.Equal("missing-required-property", diagnostic.Category);
        var mapping = Assert.Single(result.Mappings);
        Assert.Equal("page:customers", mapping.SemanticNodeId);
        Assert.Equal(1, mapping.PlannedOperationSequence);
        Assert.Equal("Customers Management", mapping.BlueprintModule);
    }

    [Fact]
    public void PersistEvidence_WritesWorkspaceKnowledgeState()
    {
        var root = CreateTempRoot();
        try
        {
            var snapshot = CreateSnapshot(root);
            var service = new OracleApexValidationFeedbackService();
            var validation = new OracleApexValidationResult
            {
                IsSuccess = false,
                Diagnostics =
                [
                    new OracleApexCompilerDiagnostic { Component = "page", Property = "alias", Category = "missing-required-property", Severity = "Error", Message = "Missing required property 'alias'." },
                ],
                Mappings =
                [
                    new OracleApexDiagnosticMapping { Diagnostic = new OracleApexCompilerDiagnostic { Message = "Missing required property 'alias'." }, PlannedOperationTitle = "Create page 'Customers'", PlannedOperationSequence = 1 },
                ],
            };

            service.PersistEvidence(snapshot, validation);

            var evidencePath = Path.Combine(snapshot.Paths.OpencodePath, "knowledge", "apex-assistant", "evidence.json");
            var state = JsonSerializer.Deserialize<OracleApexAssistantWorkspaceEvidenceState>(File.ReadAllText(evidencePath));
            Assert.NotNull(state);
            Assert.Equal(1, state!.MissingProperties["alias"]);
            Assert.Equal(1, state.FailedBlueprintOperations["Create page 'Customers'"]);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void CreateRepairPlan_BuildsSemanticPropertyFixes()
    {
        var root = CreateTempRoot();
        try
        {
            WritePackage(root);
            var repairService = new OracleApexSemanticRepairService();
            var validation = new OracleApexValidationResult
            {
                Diagnostics =
                [
                    new OracleApexCompilerDiagnostic { Property = "alias", Category = "missing-required-property", Message = "Missing required property 'alias'." },
                ],
                Mappings =
                [
                    new OracleApexDiagnosticMapping
                    {
                        Diagnostic = new OracleApexCompilerDiagnostic { Property = "alias", Category = "missing-required-property", Message = "Missing required property 'alias'." },
                        SemanticNodeId = "page:customers",
                        WorkspaceIdentifier = "Customers",
                    },
                ],
            };

            var plan = repairService.CreateRepairPlan(root, CreateEnvironment(), "dev", new OracleApexEditPlan { Intent = "Build customer management" }, validation);

            Assert.True(plan.Operations.Count > 0);
            Assert.Contains(plan.Operations, operation => operation.Title.Contains("Set alias", StringComparison.OrdinalIgnoreCase));
        }
        finally { DeleteTempRoot(root); }
    }

    private static WorkspaceSnapshot CreateSnapshot(string root)
        => new()
        {
            Record = new WorkspaceRecord { Name = "apex", RootPath = root, RepositoryPath = root, ConfigurationPath = Path.Combine(root, "workspace.yaml"), CreatedUtc = DateTimeOffset.UtcNow, LastOpenedUtc = DateTimeOffset.UtcNow },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "apex", Image = "ubuntu:24.04" },
                Oracle = new OracleWorkspacePreferences
                {
                    Apex = new OracleApexWorkspacePreferences
                    {
                        DefaultEnvironment = "dev",
                        Environments = new Dictionary<string, OracleApexEnvironmentPreferences> { ["dev"] = CreateEnvironment() },
                    },
                },
            },
            Paths = WorkspacePathBuilder.Build(root),
            ConfigurationPath = Path.Combine(root, "workspace.yaml"),
            RuntimeState = WorkspaceRuntimeState.Stopped,
            Safety = new WorkspaceSafetySnapshot { OverallStatus = WorkspaceSafetyLevel.Protected, Headline = "Safe", Message = "Safe", LocalRecovery = new WorkspaceLocalRecoverySnapshot(), Backup = new WorkspaceBackupSnapshot(), IgnorePolicy = new WorkspaceIgnorePolicyReview(), AdvancedGit = new WorkspaceAdvancedGitSnapshot { StatusSummary = "clean" } },
            Session = new WorkspaceSessionSnapshot(),
            Synchronization = new WorkspaceSynchronizationSnapshot(),
            Health = new WorkspaceHealthSnapshot(),
            Readiness = new WorkspaceReadinessSnapshot(),
            AvailableServices = Array.Empty<WorkspaceServiceInfo>(),
        };

    private static OracleApexEnvironmentPreferences CreateEnvironment()
        => new() { ApplicationId = 100, Workspace = "TEST", ParsingSchema = "TESTSCHEMA", SourcePath = "src/apex" };

    private static void WritePackage(string root)
    {
        var sourceRoot = Path.Combine(root, "src", "apex");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "pages"));
        File.WriteAllText(Path.Combine(sourceRoot, "application.apx"), "application demo (\n    id: 100\n    name: Demo\n    alias: DEMO\n)\n");
        File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00003-customers.apx"), "page customers (\n    id: 3\n    name: Customers\n    alias: CUSTOMERS\n)\n");
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oracle-apex-validation-tests-{Guid.NewGuid():N}");
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
