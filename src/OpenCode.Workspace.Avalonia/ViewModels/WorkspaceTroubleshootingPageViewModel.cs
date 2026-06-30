using System.Collections.ObjectModel;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspaceTroubleshootingPageViewModel : PageViewModel
{
    public WorkspaceTroubleshootingPageViewModel()
        : base("Troubleshoot Workspace", "Interactive, workspace-scoped troubleshooting for the selected workspace.")
    {
    }

    public ObservableCollection<string> SuggestedNextSteps { get; } = [];
    public ObservableCollection<ActionItemViewModel> InvestigationActions { get; } = [];
    public ObservableCollection<WorkspaceTroubleshootingHistoryEntryViewModel> RepairHistory { get; } = [];
    public ObservableCollection<WorkspaceTroubleshootingHistoryEntryViewModel> InvestigationHistory { get; } = [];

    public bool HasSuggestedNextSteps => SuggestedNextSteps.Count > 0;
    public bool HasInvestigationActions => InvestigationActions.Count > 0;
    public bool HasRepairHistory => RepairHistory.Count > 0;
    public bool HasInvestigationHistory => InvestigationHistory.Count > 0;

    private string _transcriptExcerpt = string.Empty;

    public string TranscriptExcerpt
    {
        get => _transcriptExcerpt;
        private set
        {
            if (SetProperty(ref _transcriptExcerpt, value))
            {
                RaisePropertyChanged(nameof(HasTranscriptExcerpt));
            }
        }
    }

    public bool HasTranscriptExcerpt => !string.IsNullOrWhiteSpace(TranscriptExcerpt);

    public string WorkspaceRootPath { get; private set; } = string.Empty;

    public string CurrentDiagnosis { get; private set; } = string.Empty;
    public string CurrentEvidence { get; private set; } = string.Empty;
    public string CurrentConfidence { get; private set; } = string.Empty;
    public string RecommendedNextStep { get; private set; } = string.Empty;
    public string RecommendedNextStepDescription { get; private set; } = string.Empty;
    public string RecommendedNextStepDuration { get; private set; } = string.Empty;

    public void ShowReport(
        WorkspaceTroubleshootingReport report,
        ActionItemViewModel? primaryAction,
        IReadOnlyList<ActionItemViewModel> visibleActions,
        IReadOnlyList<ActionItemViewModel> advancedActions,
        IReadOnlyList<ActionItemViewModel> investigationActions)
    {
        WorkspaceRootPath = report.RootPath;
        DetailTitle = report.WorkspaceName;
        DetailSummary = report.Summary;
        DetailRecommendation = report.Recommendation;
        DetailPrimaryAction = primaryAction;
        CurrentDiagnosis = report.CurrentDiagnosis;
        CurrentEvidence = report.CurrentEvidence;
        CurrentConfidence = report.Confidence;
        RecommendedNextStep = report.RecommendedNextStep;
        RecommendedNextStepDescription = report.RecommendedNextStepDescription;
        RecommendedNextStepDuration = report.RecommendedNextStepDuration;

        DetailItems.Clear();
        DetailVisibleActions.Clear();
        DetailActions.Clear();
        DetailAdvancedActions.Clear();
        SuggestedNextSteps.Clear();
        InvestigationActions.Clear();
        RepairHistory.Clear();
        InvestigationHistory.Clear();

        DetailItems.Add(new DetailItemViewModel("Workspace", report.WorkspaceName));
        DetailItems.Add(new DetailItemViewModel("Root path", report.RootPath));
        DetailItems.Add(new DetailItemViewModel("Status", report.Headline));

        foreach (var fact in report.Facts)
        {
            DetailItems.Add(new DetailItemViewModel(fact.Label, fact.Value));
        }

        foreach (var action in visibleActions)
        {
            DetailVisibleActions.Add(action);
            DetailActions.Add(action);
        }

        foreach (var action in advancedActions)
        {
            DetailAdvancedActions.Add(action);
            DetailActions.Add(action);
        }

        foreach (var action in investigationActions)
        {
            InvestigationActions.Add(action);
        }

        foreach (var step in report.SuggestedNextSteps)
        {
            SuggestedNextSteps.Add(step);
        }

        foreach (var item in report.RepairHistory)
        {
            RepairHistory.Add(new WorkspaceTroubleshootingHistoryEntryViewModel(item.Title, item.Outcome, item.Summary, item.Evidence, item.Recommendation, item.Confidence, item.EstimatedDuration, item.Source, item.OccurredUtc));
        }

        foreach (var item in report.InvestigationHistory)
        {
            InvestigationHistory.Add(new WorkspaceTroubleshootingHistoryEntryViewModel(item.Title, item.Outcome, item.Summary, item.Evidence, item.Recommendation, item.Confidence, item.EstimatedDuration, item.Source, item.OccurredUtc));
        }

        ShowAdvancedActions = report.CanResetRuntime || advancedActions.Count > 0 ? ShowAdvancedActions : false;
        TranscriptExcerpt = report.TranscriptExcerpt;
        RaisePropertyChanged(nameof(HasSuggestedNextSteps));
        RaisePropertyChanged(nameof(HasInvestigationActions));
        RaisePropertyChanged(nameof(HasRepairHistory));
        RaisePropertyChanged(nameof(HasInvestigationHistory));
        RaisePropertyChanged(nameof(HasDetailAdvancedActions));
        RaisePropertyChanged(nameof(CurrentDiagnosis));
        RaisePropertyChanged(nameof(CurrentEvidence));
        RaisePropertyChanged(nameof(CurrentConfidence));
        RaisePropertyChanged(nameof(RecommendedNextStep));
        RaisePropertyChanged(nameof(RecommendedNextStepDescription));
        RaisePropertyChanged(nameof(RecommendedNextStepDuration));
    }
}
