using System.Text.Json;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using Xunit;

namespace OpenCode.Workspace.Cli.Tests;

public sealed class SmokeCliTests
{
    [Fact]
    public async Task SmokeList_Json_PrintsCatalogEnvelope()
    {
        var output = new StringWriter();
        var app = CreateSmokeCli(
            output,
            smokeDefinitionRunner: (_, _) => Task.FromResult(new WorkspaceSmokeDefinitionCatalogResult
            {
                Definitions =
                [
                    new WorkspaceSmokeDefinition
                    {
                        TemplateId = "general-development",
                        DisplayName = "General Development",
                        Family = "lightweight",
                        Supported = true,
                        ResourceClass = WorkspaceSmokeResourceClass.Lightweight,
                        TimeoutClass = WorkspaceSmokeTimeoutClass.Short,
                        ValidatorIds = ["core-tooling"],
                    },
                ],
            }));

        var exitCode = await app.RunAsync(["smoke", "list", "--format", "json"]);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("smokeDefinitionCatalog", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("general-development", document.RootElement.GetProperty("definitions")[0].GetProperty("templateId").GetString());
    }

    [Fact]
    public async Task SmokeRun_Single_Json_ContainsSchemaVersion_AndArtifactPaths()
    {
        var output = new StringWriter();
        var app = CreateSmokeCli(
            output,
            smokeDefinitionRunner: (_, _) => Task.FromResult(new WorkspaceSmokeDefinitionCatalogResult
            {
                Definitions = [CreateDefinition("general-development", "lightweight")],
            }),
            smokeRunRunner: (_, _) => Task.FromResult(new WorkspaceSmokeResult
            {
                TemplateId = "general-development",
                RunId = "run-1",
                Status = WorkspaceSmokeStatus.Passed,
                Phase = WorkspaceSmokePhase.Completed,
                FailureClassification = WorkspaceSmokeFailureClassification.None,
                CleanupVerificationSucceeded = true,
                ArtifactDirectory = "/tmp/artifacts/run-1",
                SummaryJsonPath = "/tmp/artifacts/run-1/summary.json",
                SummaryTextPath = "/tmp/artifacts/run-1/summary.txt",
            }));

        var exitCode = await app.RunAsync(["smoke", "run", "general-development", "--format", "json"]);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("smokeRun", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("passed", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("/tmp/artifacts/run-1/summary.json", document.RootElement.GetProperty("summaryJsonPath").GetString());
        Assert.Equal("/tmp/artifacts/run-1/summary.txt", document.RootElement.GetProperty("summaryTextPath").GetString());
    }

    [Fact]
    public async Task SmokeRun_All_UsesMatrixRunner_AndMapsValidationFailureToExitCodeOne()
    {
        var output = new StringWriter();
        var app = CreateSmokeCli(
            output,
            smokeDefinitionRunner: (_, _) => Task.FromResult(new WorkspaceSmokeDefinitionCatalogResult
            {
                Definitions = [CreateDefinition("general-development", "lightweight")],
            }),
            smokeMatrixRunner: (_, _) => Task.FromResult(new WorkspaceSmokeMatrixResult
            {
                MatrixRunId = "matrix-1",
                SelectedTemplates = ["general-development"],
                Results = [new WorkspaceSmokeResult { TemplateId = "general-development", RunId = "run-1", Status = WorkspaceSmokeStatus.Failed, Phase = WorkspaceSmokePhase.Validation, FailureClassification = WorkspaceSmokeFailureClassification.SmokeValidationFailure, FailureMessage = "validator failed" }],
                FailedCount = 1,
                Status = WorkspaceSmokeStatus.Failed,
                FailureClassification = WorkspaceSmokeFailureClassification.SmokeValidationFailure,
                FailureMessage = "validator failed",
                ArtifactDirectory = "/tmp/artifacts/matrix-1",
                SummaryJsonPath = "/tmp/artifacts/matrix-1/matrix-summary.json",
                SummaryTextPath = "/tmp/artifacts/matrix-1/matrix-summary.txt",
            }));

        var exitCode = await app.RunAsync(["smoke", "run", "--all"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("OpenCode Smoke Matrix", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("validator failed", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SmokeRun_RejectsAmbiguousSelection()
    {
        var error = new StringWriter();
        var app = CreateSmokeCli(new StringWriter(), error);

        var exitCode = await app.RunAsync(["smoke", "run", "general-development", "--all"]);

        Assert.Equal(2, exitCode);
        Assert.Contains("exactly one smoke selection", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SmokeRun_UnknownFamily_ReturnsUnsupportedSelectionExitCode()
    {
        var error = new StringWriter();
        var app = CreateSmokeCli(
            new StringWriter(),
            error,
            smokeDefinitionRunner: (_, _) => Task.FromResult(new WorkspaceSmokeDefinitionCatalogResult
            {
                Definitions = [CreateDefinition("general-development", "lightweight")],
            }));

        var exitCode = await app.RunAsync(["smoke", "run", "--family", "oracle"]);

        Assert.Equal(6, exitCode);
        Assert.Contains("Unknown smoke family 'oracle'", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SmokeRun_Cancelled_Returns130()
    {
        var error = new StringWriter();
        var app = CreateSmokeCli(
            new StringWriter(),
            error,
            smokeDefinitionRunner: (_, _) => Task.FromResult(new WorkspaceSmokeDefinitionCatalogResult
            {
                Definitions = [CreateDefinition("general-development", "lightweight")],
            }),
            smokeRunRunner: (_, cancellationToken) => Task.FromCanceled<WorkspaceSmokeResult>(cancellationToken));
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var exitCode = await app.RunAsync(["smoke", "run", "general-development"], cancellationSource.Token);

        Assert.Equal(130, exitCode);
        Assert.Contains("cancelled", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SmokeCleanup_Json_ParsesCleanly()
    {
        var output = new StringWriter();
        var app = CreateSmokeCli(
            output,
            smokeCleanupRunner: (_, _) => Task.FromResult(new SmokeCleanupResult
            {
                Succeeded = true,
                DryRun = true,
                ComposeDownAttempted = true,
                ComposeDownSucceeded = true,
                VerificationSucceeded = true,
                Actions = ["compose-down:oracle-smoke"],
            }));

        var exitCode = await app.RunAsync(["smoke", "cleanup", "--dry-run", "--format", "json"]);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("smokeCleanup", document.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task RuntimeList_Json_UsesInventoryModelEnvelope()
    {
        var output = new StringWriter();
        var app = CreateSmokeCli(
            output,
            runtimeInventoryRunner: (_, _) => Task.FromResult(new RuntimeResourceInventory
            {
                Resources =
                [
                    new RuntimeOwnedResource
                    {
                        ResourceId = "container-1",
                        Name = "runtime-container",
                        Type = RuntimeResourceType.Container,
                        OwnerKind = "smoke",
                        RunId = "run-1",
                        Project = "runtime-project",
                        Template = "oracle-apexlang-demo",
                    },
                ],
            }));

        var exitCode = await app.RunAsync(["runtime", "list", "--format", "json", "--owner", "smoke"]);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("runtimeInventory", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("runtime-container", document.RootElement.GetProperty("resources")[0].GetProperty("name").GetString());
    }

    private static WorkspaceSmokeDefinition CreateDefinition(string templateId, string family)
        => new()
        {
            TemplateId = templateId,
            DisplayName = templateId,
            Family = family,
            Supported = true,
            ResourceClass = WorkspaceSmokeResourceClass.Lightweight,
            TimeoutClass = WorkspaceSmokeTimeoutClass.Short,
        };

    private static CliApplication CreateSmokeCli(
        TextWriter output,
        TextWriter? error = null,
        Func<WorkspaceSmokeDefinitionQuery, CancellationToken, Task<WorkspaceSmokeDefinitionCatalogResult>>? smokeDefinitionRunner = null,
        Func<WorkspaceSmokeSingleRunRequest, CancellationToken, Task<WorkspaceSmokeResult>>? smokeRunRunner = null,
        Func<WorkspaceSmokeMatrixRunRequest, CancellationToken, Task<WorkspaceSmokeMatrixResult>>? smokeMatrixRunner = null,
        Func<SmokeCleanupOptions, CancellationToken, Task<SmokeCleanupResult>>? smokeCleanupRunner = null,
        Func<RuntimeOwnershipQuery, CancellationToken, Task<RuntimeResourceInventory>>? runtimeInventoryRunner = null)
        => new(
            output,
            error ?? new StringWriter(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            _ => throw new NotSupportedException(),
            smokeCleanupRunner ?? ((_, _) => throw new NotSupportedException()),
            (_, _) => throw new NotSupportedException(),
            runtimeInventoryRunner ?? ((_, _) => throw new NotSupportedException()),
            smokeDefinitionRunner ?? ((_, _) => throw new NotSupportedException()),
            smokeRunRunner ?? ((_, _) => throw new NotSupportedException()),
            smokeMatrixRunner ?? ((_, _) => throw new NotSupportedException()));
}
