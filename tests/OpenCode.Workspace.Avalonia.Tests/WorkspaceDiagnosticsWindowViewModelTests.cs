using OpenCode.Workspace.Avalonia.ViewModels;
using OpenCode.Workspace.Core.Models;
using Xunit;

namespace OpenCode.Workspace.Avalonia.Tests;

public sealed class WorkspaceDiagnosticsWindowViewModelTests
{
    [Fact]
    public void ViewModel_MapsFailureSummaryAttemptedStepsEntriesAndRecommendation()
    {
        var session = new WorkspaceDiagnosticsSession
        {
            WorkspaceName = "alpha",
            WorkspaceRootPath = "/workspace/alpha",
            OperationName = "Open Workspace",
            Mode = WorkspaceDiagnosticsMode.Diagnostics,
            Status = WorkspaceDiagnosticsStatus.Failed,
            Summary = "Workspace provisioning stopped.",
            StartedUtc = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero),
            CompletedUtc = new DateTimeOffset(2026, 7, 2, 12, 5, 0, TimeSpan.Zero),
            Recommendation = WorkspaceNextActionRecommendation.RebuildRuntime,
            FailureSummary = new WorkspaceFailureSummary
            {
                Summary = "Provisioning failed.",
                Reason = "XDB status = INVALID",
                Evidence = "Oracle validation failed.",
            },
            AttemptedSteps =
            [
                new WorkspaceAttemptResult
                {
                    Step = WorkspaceAttemptStep.SafeRepair,
                    Succeeded = false,
                    Summary = "safe repair",
                    Timestamp = new DateTimeOffset(2026, 7, 2, 12, 2, 0, TimeSpan.Zero),
                },
            ],
            Entries =
            [
                new WorkspaceDiagnosticsEntry
                {
                    Timestamp = new DateTimeOffset(2026, 7, 2, 12, 3, 0, TimeSpan.Zero),
                    Kind = WorkspaceDiagnosticsEntryKind.Error,
                    Message = "XDB status = INVALID",
                    Source = "transcript",
                    IsFailureEvidence = true,
                },
            ],
            BundleInfo = new WorkspaceDiagnosticsBundleInfo
            {
                SuggestedFileName = "alpha-open-workspace-diagnostics.zip",
                CanExportToFile = true,
            },
        };

        var viewModel = new WorkspaceDiagnosticsWindowViewModel(session);

        Assert.Equal("alpha", viewModel.WorkspaceName);
        Assert.Equal("/workspace/alpha", viewModel.WorkspaceRootPath);
        Assert.Equal("Rebuild Runtime", viewModel.RecommendationLabel);
        Assert.True(viewModel.HasFailureSummary);
        Assert.True(viewModel.HasAttemptedSteps);
        Assert.True(viewModel.HasEntries);
        Assert.Equal("Provisioning failed.", viewModel.FailureSummaryTitle);
        Assert.Equal("Safe Repair", Assert.Single(viewModel.AttemptedSteps).StepLabel);
        Assert.Equal("Failed", Assert.Single(viewModel.AttemptedSteps).StatusLabel);
        Assert.True(Assert.Single(viewModel.Entries).IsFailureEvidence);
    }

    [Fact]
    public async Task CopySummaryCommand_CopiesSummaryText()
    {
        var clipboard = new TestClipboardService();
        var viewModel = new WorkspaceDiagnosticsWindowViewModel(CreateSession(), clipboard);

        await viewModel.CopySummaryCommand.ExecuteAsync();

        Assert.Equal(viewModel.GetSummaryText(), clipboard.Text);
        Assert.Contains("Workspace Diagnostics", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("Next: Rebuild Runtime", clipboard.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Timeline / Entries", clipboard.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopyFullLogCommand_CopiesFullLogText()
    {
        var clipboard = new TestClipboardService();
        var viewModel = new WorkspaceDiagnosticsWindowViewModel(CreateSession(), clipboard);

        await viewModel.CopyFullLogCommand.ExecuteAsync();

        Assert.Equal(viewModel.GetFullLogText(), clipboard.Text);
        Assert.Contains("Attempted Steps", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("Timeline / Entries", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("XDB status = INVALID", clipboard.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportBundleCommand_UsesPickerAndExporter()
    {
        var exportedSession = default(WorkspaceDiagnosticsSession);
        var exportedPath = string.Empty;
        var exportService = new RecordingBundleExportService((session, path) =>
        {
            exportedSession = session;
            exportedPath = path;
        });
        var viewModel = new WorkspaceDiagnosticsWindowViewModel(
            CreateSession(),
            clipboardService: null,
            selectExportPathAsync: (suggestedFileName, _) => Task.FromResult<string?>(Path.Combine(Path.GetTempPath(), suggestedFileName)),
            exportBundleAsync: exportService.ExportAsync);

        Assert.True(viewModel.CanExportBundle);

        await viewModel.ExportBundleCommand.ExecuteAsync();

        Assert.NotNull(exportedSession);
        Assert.Equal("alpha", exportedSession!.WorkspaceName);
        Assert.EndsWith(".zip", exportedPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportBundleCommand_CancelledPicker_DoesNotExport()
    {
        var exportCalled = false;
        var exportService = new RecordingBundleExportService((_, _) => exportCalled = true);
        var viewModel = new WorkspaceDiagnosticsWindowViewModel(
            CreateSession(),
            clipboardService: null,
            selectExportPathAsync: (_, _) => Task.FromResult<string?>(null),
            exportBundleAsync: exportService.ExportAsync);

        await viewModel.ExportBundleCommand.ExecuteAsync();

        Assert.False(exportCalled);
    }

    private static WorkspaceDiagnosticsSession CreateSession()
        => new()
        {
            WorkspaceName = "alpha",
            WorkspaceRootPath = "/workspace/alpha",
            OperationName = "Open Workspace",
            Mode = WorkspaceDiagnosticsMode.Diagnostics,
            Status = WorkspaceDiagnosticsStatus.Failed,
            Summary = "Workspace provisioning stopped.",
            StartedUtc = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero),
            CompletedUtc = new DateTimeOffset(2026, 7, 2, 12, 5, 0, TimeSpan.Zero),
            Recommendation = WorkspaceNextActionRecommendation.RebuildRuntime,
            FailureSummary = new WorkspaceFailureSummary
            {
                Summary = "Provisioning failed.",
                Reason = "XDB status = INVALID",
                Evidence = "Oracle validation failed.",
            },
            AttemptedSteps =
            [
                new WorkspaceAttemptResult
                {
                    Step = WorkspaceAttemptStep.SafeRepair,
                    Succeeded = false,
                    Summary = "safe repair",
                    Timestamp = new DateTimeOffset(2026, 7, 2, 12, 2, 0, TimeSpan.Zero),
                },
            ],
            Entries =
            [
                new WorkspaceDiagnosticsEntry
                {
                    Timestamp = new DateTimeOffset(2026, 7, 2, 12, 3, 0, TimeSpan.Zero),
                    Kind = WorkspaceDiagnosticsEntryKind.Error,
                    Message = "XDB status = INVALID",
                    Source = "transcript",
                    IsFailureEvidence = true,
                },
            ],
            BundleInfo = new WorkspaceDiagnosticsBundleInfo
            {
                SuggestedFileName = "alpha-open-workspace-diagnostics.zip",
                CanExportToFile = true,
            },
        };

    private sealed class TestClipboardService : Services.IClipboardService
    {
        public string Text { get; private set; } = string.Empty;

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingBundleExportService
    {
        private readonly Action<WorkspaceDiagnosticsSession, string> _record;

        public RecordingBundleExportService(Action<WorkspaceDiagnosticsSession, string> record)
        {
            _record = record;
        }

        public Task ExportAsync(WorkspaceDiagnosticsSession session, string destinationPath, CancellationToken cancellationToken = default)
        {
            _record(session, destinationPath);
            return Task.CompletedTask;
        }
    }
}
