using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceSmokeSummaryTests
{
    [Fact]
    public void WriteResultSummary_IncludesOracleValidatorData()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smoke-summary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var result = new WorkspaceSmokeResult
            {
                TemplateId = "oracle-apexlang-demo",
                RunId = "run-1",
                WorkspacePath = "/tmp/workspace",
                ComposeProject = "oracle-apexlang-demo-runtime-smoke",
                Status = WorkspaceSmokeStatus.Passed,
                Phase = WorkspaceSmokePhase.Completed,
                FailureClassification = WorkspaceSmokeFailureClassification.None,
                CleanupVerificationSucceeded = true,
                ArtifactDirectory = root,
                Validators =
                [
                    new WorkspaceSmokeValidatorResult
                    {
                        ValidatorId = "oracle-apexlang-runtime",
                        Succeeded = true,
                        Message = "Oracle APEXlang runtime is healthy.",
                        Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["apex_installed"] = "True",
                            ["apex_version"] = "26.1.0",
                            ["apex_registry_status"] = "VALID",
                            ["apexlang_application_name"] = "Hello APEXlang",
                        },
                    },
                ],
                CleanupResult = new global::OpenCode.Workspace.Core.Runtime.SmokeCleanupResult
                {
                    Succeeded = true,
                    DryRun = false,
                    VerificationSucceeded = true,
                },
            };

            WorkspaceSmokeArtifacts.WriteResultSummary(root, result);

            var summary = File.ReadAllText(Path.Combine(root, "summary.txt"));
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "summary.json")));
            Assert.Contains("validator.oracle-apexlang-runtime.apex_installed=True", summary, StringComparison.Ordinal);
            Assert.Contains("validator.oracle-apexlang-runtime.apex_version=26.1.0", summary, StringComparison.Ordinal);
            Assert.Contains("validator.oracle-apexlang-runtime.apex_registry_status=VALID", summary, StringComparison.Ordinal);
            Assert.Contains("validator.oracle-apexlang-runtime.apexlang_application_name=Hello APEXlang", summary, StringComparison.Ordinal);
            Assert.Equal("1", json.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("smokeRun", json.RootElement.GetProperty("kind").GetString());
            Assert.Equal("passed", json.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void OracleSmokeDefinitions_SelectExpectedValidatorFamilies()
    {
        var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        var definitions = WorkspaceSmokeCatalog.BuildDefinitions(provider.LoadTemplates()).ToDictionary(item => item.TemplateId, StringComparer.OrdinalIgnoreCase);

        Assert.Contains("oracle-plsql-runtime", definitions["oracle-plsql-demo"].ValidatorIds);
        Assert.DoesNotContain("oracle-apex-runtime", definitions["oracle-plsql-demo"].ValidatorIds);
        Assert.DoesNotContain("oracle-apexlang-runtime", definitions["oracle-plsql-demo"].ValidatorIds);
        Assert.Contains("oracle-apex-runtime", definitions["oracle-apex-demo"].ValidatorIds);
        Assert.Contains("oracle-apexlang-runtime", definitions["oracle-apexlang-demo"].ValidatorIds);
    }

    [Fact]
    public void AutomationOutcomeClassifier_MapsCleanupFailureSeparately()
    {
        var result = new WorkspaceSmokeResult
        {
            TemplateId = "general-development",
            RunId = "run-1",
            Status = WorkspaceSmokeStatus.Failed,
            FailureClassification = WorkspaceSmokeFailureClassification.CleanupFailure,
        };

        Assert.Equal(WorkspaceSmokeAutomationOutcome.CleanupFailure, WorkspaceSmokeAutomationOutcomeClassifier.Classify(result));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TerminalClassifier_PreservesCancellation_SeparatelyFromCleanup(bool cleanupVerificationSucceeded)
    {
        Assert.Equal(WorkspaceSmokeStatus.Cancelled, WorkspaceSmokeExecutionOutcomeClassifier.ResolveTerminalStatus(WorkspaceSmokeStatus.Cancelled, cleanupVerificationSucceeded));
    }

    [Fact]
    public void TerminalClassifier_PreservesCancellation_WhenCleanupRetryEventuallySucceeds()
    {
        Assert.Equal(WorkspaceSmokeStatus.Cancelled, WorkspaceSmokeExecutionOutcomeClassifier.ResolveTerminalStatus(WorkspaceSmokeStatus.Cancelled, cleanupVerificationSucceeded: true));
    }

    [Fact]
    public void TerminalClassifier_LeavesExecutionFailureFailed_WhenCancellationArrivesLater()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Equal(WorkspaceSmokeStatus.Failed, WorkspaceSmokeExecutionOutcomeClassifier.ClassifyException(new InvalidOperationException("execution failed"), source.Token));
        Assert.Equal(WorkspaceSmokeStatus.Failed, WorkspaceSmokeExecutionOutcomeClassifier.ResolveTerminalStatus(WorkspaceSmokeStatus.Failed, cleanupVerificationSucceeded: true));
    }

    [Fact]
    public void ExceptionClassifier_RecognizesOnlyObservedOperationCancellation()
    {
        using var operation = new CancellationTokenSource();
        operation.Cancel();
        Assert.Equal(WorkspaceSmokeStatus.Cancelled, WorkspaceSmokeExecutionOutcomeClassifier.ClassifyException(new OperationCanceledException(operation.Token), operation.Token));

        using var unrelated = new CancellationTokenSource();
        unrelated.Cancel();
        Assert.Equal(WorkspaceSmokeStatus.Failed, WorkspaceSmokeExecutionOutcomeClassifier.ClassifyException(new OperationCanceledException(unrelated.Token), CancellationToken.None));
    }

    [Fact]
    public void ExceptionClassifier_RecognizesWrappedOperationCancellation()
    {
        using var operation = new CancellationTokenSource();
        operation.Cancel();
        var wrapped = new InvalidOperationException("Docker is not reachable.", new TaskCanceledException());

        Assert.Equal(WorkspaceSmokeStatus.Cancelled, WorkspaceSmokeExecutionOutcomeClassifier.ClassifyException(wrapped, operation.Token));
    }

    [Fact]
    public void ApplicationService_HasNoConsoleDependency()
    {
        var source = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Core", "Smoke", "WorkspaceSmokeApplicationService.cs"));

        Assert.DoesNotContain("Console.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteLine", source, StringComparison.Ordinal);
    }
}
