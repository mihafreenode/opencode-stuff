using AppSupportTranscript = OpenCode.Workspace.AppSupport.OperationTranscript;
using AppSupportTranscriptLine = OpenCode.Workspace.AppSupport.OperationTranscriptLine;
using AppSupportTranscriptLineKind = OpenCode.Workspace.AppSupport.OperationTranscriptLineKind;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceDiagnosticsSessionBuilderTests
{
    [Fact]
    public void Build_RunningTranscript_CreatesProgressSession()
    {
        var transcript = CreateTranscript(
            operationName: "Open Workspace",
            succeeded: null,
            completedUtc: null,
            new AppSupportTranscriptLine { Kind = AppSupportTranscriptLineKind.Status, Text = "Provisioning runtime..." });

        var session = WorkspaceDiagnosticsSessionBuilder.Build(new WorkspaceDiagnosticsSessionBuildInput
        {
            Transcript = transcript,
        });

        Assert.Equal(WorkspaceDiagnosticsMode.Progress, session.Mode);
        Assert.Equal(WorkspaceDiagnosticsStatus.Running, session.Status);
        Assert.Equal("Provisioning runtime...", session.Summary);
    }

    [Fact]
    public void Build_FailedTranscript_CreatesDiagnosticsSession()
    {
        var transcript = CreateTranscript(
            operationName: "Open Workspace",
            succeeded: false,
            completedUtc: DateTimeOffset.UtcNow,
            new AppSupportTranscriptLine { Kind = AppSupportTranscriptLineKind.StandardError, Text = "Docker daemon is unavailable." },
            new AppSupportTranscriptLine { Kind = AppSupportTranscriptLineKind.Result, Text = "Failed." });

        var session = WorkspaceDiagnosticsSessionBuilder.Build(new WorkspaceDiagnosticsSessionBuildInput
        {
            Transcript = transcript,
        });

        Assert.Equal(WorkspaceDiagnosticsMode.Diagnostics, session.Mode);
        Assert.Equal(WorkspaceDiagnosticsStatus.Failed, session.Status);
        Assert.NotNull(session.FailureSummary);
        Assert.Contains(session.Entries, entry => entry.Kind == WorkspaceDiagnosticsEntryKind.Error);
    }

    [Fact]
    public void Build_SuccessfulTranscript_CreatesSucceededSession()
    {
        var transcript = CreateTranscript(
            operationName: "Attach",
            succeeded: true,
            completedUtc: DateTimeOffset.UtcNow,
            new AppSupportTranscriptLine { Kind = AppSupportTranscriptLineKind.Status, Text = "Preparing attach..." },
            new AppSupportTranscriptLine { Kind = AppSupportTranscriptLineKind.Result, Text = "Completed." });

        var session = WorkspaceDiagnosticsSessionBuilder.Build(new WorkspaceDiagnosticsSessionBuildInput
        {
            Transcript = transcript,
        });

        Assert.Equal(WorkspaceDiagnosticsMode.Diagnostics, session.Mode);
        Assert.Equal(WorkspaceDiagnosticsStatus.Succeeded, session.Status);
        Assert.Null(session.FailureSummary);
        Assert.Equal("Completed.", session.Summary);
    }

    [Fact]
    public void Build_RepairProvisionStartAttachEntries_BecomeAttemptedSteps()
    {
        var transcript = CreateTranscript(
            operationName: "Open Workspace",
            succeeded: false,
            completedUtc: DateTimeOffset.UtcNow,
            new AppSupportTranscriptLine { Kind = AppSupportTranscriptLineKind.Status, Text = "Running safe repair..." },
            new AppSupportTranscriptLine { Kind = AppSupportTranscriptLineKind.Status, Text = "Provisioning runtime..." },
            new AppSupportTranscriptLine { Kind = AppSupportTranscriptLineKind.Status, Text = "Checking workspace..." },
            new AppSupportTranscriptLine { Kind = AppSupportTranscriptLineKind.Status, Text = "Preparing attach..." });

        var session = WorkspaceDiagnosticsSessionBuilder.Build(new WorkspaceDiagnosticsSessionBuildInput
        {
            Transcript = transcript,
        });

        Assert.Contains(session.AttemptedSteps, step => step.Step == WorkspaceAttemptStep.SafeRepair);
        Assert.Contains(session.AttemptedSteps, step => step.Step == WorkspaceAttemptStep.Provision);
        Assert.Contains(session.AttemptedSteps, step => step.Step == WorkspaceAttemptStep.Start);
        Assert.Contains(session.AttemptedSteps, step => step.Step == WorkspaceAttemptStep.Attach);
    }

    [Theory]
    [InlineData(WorkspacePrimaryAction.OpenWorkspace, WorkspaceNextActionRecommendation.OpenWorkspace)]
    [InlineData(WorkspacePrimaryAction.RebuildRuntime, WorkspaceNextActionRecommendation.RebuildRuntime)]
    [InlineData(WorkspacePrimaryAction.RunDiagnostics, WorkspaceNextActionRecommendation.RunDiagnostics)]
    [InlineData(WorkspacePrimaryAction.OpenFolder, WorkspaceNextActionRecommendation.OpenFolder)]
    public void Build_ReadinessPrimaryAction_MapsToRecommendation(WorkspacePrimaryAction primaryAction, WorkspaceNextActionRecommendation recommendation)
    {
        var session = WorkspaceDiagnosticsSessionBuilder.Build(new WorkspaceDiagnosticsSessionBuildInput
        {
            Transcript = CreateTranscript(operationName: "Open Workspace", succeeded: true, completedUtc: DateTimeOffset.UtcNow),
            Readiness = new WorkspaceReadinessSnapshot
            {
                PrimaryAction = primaryAction,
            },
        });

        Assert.Equal(recommendation, session.Recommendation);
    }

    [Fact]
    public void Build_BundleInfo_HasSuggestedFilenameAndCapabilities()
    {
        var transcript = CreateTranscript(
            operationName: "Open Workspace",
            workspaceName: "Alpha Workspace",
            succeeded: true,
            completedUtc: DateTimeOffset.UtcNow,
            new AppSupportTranscriptLine { Kind = AppSupportTranscriptLineKind.Result, Text = "Completed." });

        var session = WorkspaceDiagnosticsSessionBuilder.Build(new WorkspaceDiagnosticsSessionBuildInput
        {
            Transcript = transcript,
        });

        Assert.Equal("alpha-workspace-open-workspace-diagnostics-20260702-120000.txt", session.BundleInfo.SuggestedFileName);
        Assert.True(session.BundleInfo.CanCopyToClipboard);
        Assert.True(session.BundleInfo.CanExportToFile);
    }

    private static AppSupportTranscript CreateTranscript(string operationName, bool? succeeded, DateTimeOffset? completedUtc, params AppSupportTranscriptLine[] lines)
        => CreateTranscript(operationName, "alpha", succeeded, completedUtc, lines);

    private static AppSupportTranscript CreateTranscript(string operationName, string workspaceName, bool? succeeded, DateTimeOffset? completedUtc, params AppSupportTranscriptLine[] lines)
    {
        var startedUtc = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        var transcript = new AppSupportTranscript
        {
            OperationName = operationName,
            WorkspaceName = workspaceName,
            StartedUtc = startedUtc,
            CompletedUtc = completedUtc,
            Succeeded = succeeded,
        };

        foreach (var line in lines)
        {
            transcript.Lines.Add(new AppSupportTranscriptLine
            {
                Kind = line.Kind,
                Text = line.Text,
                Timestamp = line.Timestamp == default ? startedUtc : line.Timestamp,
            });
        }

        return transcript;
    }
}
