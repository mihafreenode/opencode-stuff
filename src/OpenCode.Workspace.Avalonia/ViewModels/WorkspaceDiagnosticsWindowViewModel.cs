using System.Collections.ObjectModel;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspaceDiagnosticsWindowViewModel : ObservableObject
{
    private readonly IClipboardService? _clipboardService;
    private readonly Func<string, CancellationToken, Task<string?>>? _selectExportPathAsync;
    private readonly Func<WorkspaceDiagnosticsSession, string, CancellationToken, Task>? _exportBundleAsync;

    public WorkspaceDiagnosticsWindowViewModel(WorkspaceDiagnosticsSession session)
        : this(session, null, null, null)
    {
    }

    public WorkspaceDiagnosticsWindowViewModel(
        WorkspaceDiagnosticsSession session,
        IClipboardService? clipboardService,
        Func<string, CancellationToken, Task<string?>>? selectExportPathAsync = null,
        Func<WorkspaceDiagnosticsSession, string, CancellationToken, Task>? exportBundleAsync = null)
    {
        Session = session;
        _clipboardService = clipboardService;
        _selectExportPathAsync = selectExportPathAsync;
        _exportBundleAsync = exportBundleAsync;
        Title = "Workspace Diagnostics";
        WorkspaceName = session.WorkspaceName;
        WorkspaceRootPath = session.WorkspaceRootPath;
        OperationName = session.OperationName;
        ModeLabel = session.Mode.ToString();
        StatusLabel = session.Status.ToString();
        Summary = session.Summary;
        RecommendationLabel = session.Recommendation switch
        {
            WorkspaceNextActionRecommendation.OpenWorkspace => "Open Workspace",
            WorkspaceNextActionRecommendation.RebuildRuntime => "Rebuild Runtime",
            WorkspaceNextActionRecommendation.RunDiagnostics => "Run Diagnostics",
            WorkspaceNextActionRecommendation.OpenFolder => "Open Folder",
            _ => string.Empty,
        };
        StartedUtcText = session.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        CompletedUtcText = session.CompletedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
        SuggestedBundleFileName = session.BundleInfo.SuggestedFileName;
        FailureSummaryTitle = session.FailureSummary?.Summary ?? string.Empty;
        FailureReason = session.FailureSummary?.Reason ?? string.Empty;
        FailureEvidence = session.FailureSummary?.Evidence ?? string.Empty;

        AttemptedSteps = new ObservableCollection<WorkspaceAttemptResultViewModel>(session.AttemptedSteps.Select(static item => new WorkspaceAttemptResultViewModel(item)));
        Entries = new ObservableCollection<WorkspaceDiagnosticsEntryViewModel>(session.Entries.Select(static item => new WorkspaceDiagnosticsEntryViewModel(item)));
        SessionDetails =
        [
            new DetailItemViewModel("Workspace", WorkspaceName),
            new DetailItemViewModel("Root path", WorkspaceRootPath),
            new DetailItemViewModel("Operation", OperationName),
            new DetailItemViewModel("Mode", ModeLabel),
            new DetailItemViewModel("Status", StatusLabel),
            new DetailItemViewModel("Started", StartedUtcText),
            new DetailItemViewModel("Completed", string.IsNullOrWhiteSpace(CompletedUtcText) ? "In progress" : CompletedUtcText),
            new DetailItemViewModel("Suggested filename", SuggestedBundleFileName),
        ];

        CopySummaryCommand = new AsyncRelayCommand(CopySummaryAsync, () => _clipboardService is not null);
        CopyFullLogCommand = new AsyncRelayCommand(CopyFullLogAsync, () => _clipboardService is not null && HasEntries);
        ExportBundleCommand = new AsyncRelayCommand(ExportBundleAsync, () => CanExportBundle);
    }

    public WorkspaceDiagnosticsSession Session { get; }
    public string Title { get; }
    public string WorkspaceName { get; }
    public string WorkspaceRootPath { get; }
    public string OperationName { get; }
    public string ModeLabel { get; }
    public string StatusLabel { get; }
    public string Summary { get; }
    public string RecommendationLabel { get; }
    public string StartedUtcText { get; }
    public string CompletedUtcText { get; }
    public string SuggestedBundleFileName { get; }
    public string FailureSummaryTitle { get; }
    public string FailureReason { get; }
    public string FailureEvidence { get; }
    public ObservableCollection<WorkspaceAttemptResultViewModel> AttemptedSteps { get; }
    public ObservableCollection<WorkspaceDiagnosticsEntryViewModel> Entries { get; }
    public IReadOnlyList<DetailItemViewModel> SessionDetails { get; }
    public AsyncRelayCommand CopySummaryCommand { get; }
    public AsyncRelayCommand CopyFullLogCommand { get; }
    public AsyncRelayCommand ExportBundleCommand { get; }
    public bool HasRecommendation => !string.IsNullOrWhiteSpace(RecommendationLabel);
    public bool HasFailureSummary => !string.IsNullOrWhiteSpace(FailureSummaryTitle) || !string.IsNullOrWhiteSpace(FailureReason) || !string.IsNullOrWhiteSpace(FailureEvidence);
    public bool HasAttemptedSteps => AttemptedSteps.Count > 0;
    public bool HasEntries => Entries.Count > 0;
    public bool CanExportBundle => Session.BundleInfo.CanExportToFile && _selectExportPathAsync is not null && _exportBundleAsync is not null;

    public string GetSummaryText()
        => WorkspaceDiagnosticsTextFormatter.BuildSummaryText(Session);

    public string GetFullLogText()
        => WorkspaceDiagnosticsTextFormatter.BuildFullLogText(Session);

    private Task CopySummaryAsync()
        => _clipboardService is null ? Task.CompletedTask : _clipboardService.SetTextAsync(GetSummaryText());

    private Task CopyFullLogAsync()
        => _clipboardService is null ? Task.CompletedTask : _clipboardService.SetTextAsync(GetFullLogText());

    private async Task ExportBundleAsync()
    {
        if (!CanExportBundle)
        {
            return;
        }

        var destinationPath = await _selectExportPathAsync!(Session.BundleInfo.SuggestedFileName, CancellationToken.None);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return;
        }

        await _exportBundleAsync!(Session, destinationPath, CancellationToken.None);
    }
}
