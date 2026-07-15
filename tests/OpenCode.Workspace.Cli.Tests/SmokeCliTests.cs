using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using Xunit;

namespace OpenCode.Workspace.Cli.Tests;

public sealed class SmokeCliTests
{
    [Fact]
    public async Task SmokeList_Json_PrintsDefinitions()
    {
        var output = new StringWriter();
        var app = new CliApplication(
            output,
            new StringWriter(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            _ => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            _ => Task.FromResult<IReadOnlyList<WorkspaceSmokeDefinition>>([
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
            ]));

        var exitCode = await app.RunAsync(["smoke", "list", "--format", "json"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("general-development", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("lightweight", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SmokeRun_Single_PrintsResult()
    {
        var output = new StringWriter();
        var app = new CliApplication(
            output,
            new StringWriter(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            _ => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            _ => Task.FromResult<IReadOnlyList<WorkspaceSmokeDefinition>>([
                new WorkspaceSmokeDefinition
                {
                    TemplateId = "general-development",
                    DisplayName = "General Development",
                    Family = "lightweight",
                    Supported = true,
                    ResourceClass = WorkspaceSmokeResourceClass.Lightweight,
                    TimeoutClass = WorkspaceSmokeTimeoutClass.Short,
                },
            ]),
            (_, _) => Task.FromResult(new WorkspaceSmokeResult
            {
                TemplateId = "general-development",
                RunId = "run-1",
                Status = WorkspaceSmokeStatus.Passed,
                Phase = WorkspaceSmokePhase.Completed,
                FailureClassification = WorkspaceSmokeFailureClassification.None,
                CleanupVerificationSucceeded = true,
                ArtifactDirectory = "/tmp/artifacts",
            }));

        var exitCode = await app.RunAsync(["smoke", "run", "general-development"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("OpenCode Smoke Run", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("general-development", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SmokeRun_All_UsesMatrixRunnerAndReturnsNonZeroOnFailure()
    {
        var output = new StringWriter();
        var app = new CliApplication(
            output,
            new StringWriter(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            _ => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            _ => Task.FromResult<IReadOnlyList<WorkspaceSmokeDefinition>>([
                new WorkspaceSmokeDefinition
                {
                    TemplateId = "general-development",
                    DisplayName = "General Development",
                    Family = "lightweight",
                    Supported = true,
                    ResourceClass = WorkspaceSmokeResourceClass.Lightweight,
                    TimeoutClass = WorkspaceSmokeTimeoutClass.Short,
                },
            ]),
            (_, _) => throw new NotSupportedException(),
            (_, _) => Task.FromResult(new WorkspaceSmokeMatrixResult
            {
                MatrixRunId = "matrix-1",
                SelectedTemplates = ["general-development"],
                Results = [new WorkspaceSmokeResult { TemplateId = "general-development", RunId = "run-1", Status = WorkspaceSmokeStatus.Failed, Phase = WorkspaceSmokePhase.Validation, FailureClassification = WorkspaceSmokeFailureClassification.SmokeValidationFailure, FailureMessage = "validator failed" }],
                FailedCount = 1,
                Status = WorkspaceSmokeStatus.Failed,
                ArtifactDirectory = "/tmp/artifacts",
            }));

        var exitCode = await app.RunAsync(["smoke", "run", "--all"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("OpenCode Smoke Matrix", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("validator failed", output.ToString(), StringComparison.Ordinal);
    }
}
