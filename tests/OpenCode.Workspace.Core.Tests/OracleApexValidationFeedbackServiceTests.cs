using System.Text.Json;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexValidationFeedbackServiceTests
{
    public static TheoryData<string> DiagnosticPaths => new()
    {
        "pages/p00002-orders.apx",
        "oracle/apex/source/pages/p00002-orders.apx",
        "/workspace/oracle/apex/source/pages/p00002-orders.apx",
        @"C:\workspace\oracle\apex\source\pages\p00002-orders.apx",
    };

    [Theory]
    [MemberData(nameof(DiagnosticPaths))]
    public void BuildValidationResult_NormalizesDiagnosticPathsAndMapsExactIndexedNode(string diagnosticPath)
    {
        var root = CreateTempRoot();
        try
        {
            const string sourcePath = "oracle/apex/source";
            WriteIndexedPackage(root, sourcePath);
            var index = new OracleApexWorkspaceIndexBuilder().Build(root, CreateEnvironment(sourcePath), "dev");
            var expectedEntry = Assert.Single(index.Entries, entry => entry.Identifier == "Orders Report");
            var plan = new OracleApexEditPlan { Intent = "Update orders report" };
            plan.Operations.Add(new OracleApexPlannedOperation
            {
                Sequence = 7,
                Title = "Update orders report",
                TargetComponentType = "region",
                TargetIdentifier = "Orders Report",
                ExpectedChangedFiles = ["oracle/apex/source/pages/p00002-orders.apx"],
            });
            var line = $"ERROR {diagnosticPath}:5:9 [APEX-1001] component=region property=type - Invalid value for property 'type'.";

            var result = new OracleApexValidationFeedbackService().BuildValidationResult(CreateProcess(line), index, plan);

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("pages/p00002-orders.apx", diagnostic.FilePath);
            Assert.Equal(5, diagnostic.Line);
            Assert.Equal("APEX-1001", diagnostic.CompilerCode);
            var mapping = Assert.Single(result.Mappings);
            Assert.Equal(expectedEntry.NodeId, mapping.SemanticNodeId);
            Assert.Equal("Orders Report", mapping.WorkspaceIdentifier);
            Assert.Equal(7, mapping.PlannedOperationSequence);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public void BuildValidationResult_DoesNotUseComponentFallbackForUnmatchedFilePath()
    {
        var root = CreateTempRoot();
        try
        {
            const string sourcePath = "oracle/apex/source";
            WriteIndexedPackage(root, sourcePath);
            var index = new OracleApexWorkspaceIndexBuilder().Build(root, CreateEnvironment(sourcePath), "dev");
            var plan = new OracleApexEditPlan();
            plan.Operations.Add(new OracleApexPlannedOperation
            {
                Sequence = 1,
                Title = "Update a region",
                TargetComponentType = "region",
                ExpectedChangedFiles = ["oracle/apex/source/pages/p00002-orders.apx"],
            });
            const string line = "ERROR oracle/apex/source-copy/pages/p00002-orders.apx:5:9 component=region - Invalid value.";

            var result = new OracleApexValidationFeedbackService().BuildValidationResult(CreateProcess(line), index, plan);

            var mapping = Assert.Single(result.Mappings);
            Assert.Equal(string.Empty, mapping.SemanticNodeId);
            Assert.Equal(0, mapping.PlannedOperationSequence);
        }
        finally { DeleteTempRoot(root); }
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

    private static OracleApexEnvironmentPreferences CreateEnvironment(string sourcePath = "src/apex")
        => new() { ApplicationId = 100, Workspace = "TEST", ParsingSchema = "TESTSCHEMA", SourcePath = sourcePath };

    private static ProcessResult CreateProcess(string line)
        => new()
        {
            Command = "validate",
            ExitCode = 1,
            StandardError = line,
            StandardOutput = string.Empty,
            StandardErrorLines = [line],
            StandardOutputLines = Array.Empty<string>(),
            Duration = TimeSpan.Zero,
        };

    private static void WriteIndexedPackage(string root, string sourcePath)
    {
        var sourceRoot = Path.Combine(root, sourcePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "pages"));
        File.WriteAllText(Path.Combine(sourceRoot, "application.apx"), "application demo (\n    id: 100\n    name: Demo\n    alias: DEMO\n)\n");
        File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00001-home.apx"), "page home (\n    id: 1\n    name: Home\n    alias: HOME\n)\n");
        File.WriteAllText(Path.Combine(sourceRoot, "pages", "p00002-orders.apx"), "page orders (\n    id: 2\n    name: Orders\n    alias: ORDERS\n    region orders-report (\n        title: Orders Report\n        type: Interactive Report\n    )\n)\n");
    }

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
