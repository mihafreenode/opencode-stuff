using System.Collections.ObjectModel;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspaceTroubleshootingPageViewModel : PageViewModel
{
    public WorkspaceTroubleshootingPageViewModel()
        : base("Investigate Problem", "Workspace-scoped troubleshooting for the selected workspace.")
    {
    }

    public ObservableCollection<string> SuggestedNextSteps { get; } = [];

    public bool HasSuggestedNextSteps => SuggestedNextSteps.Count > 0;

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

    public void ShowReport(
        WorkspaceTroubleshootingReport report,
        ActionItemViewModel? primaryAction,
        IReadOnlyList<ActionItemViewModel> visibleActions,
        IReadOnlyList<ActionItemViewModel> advancedActions)
    {
        WorkspaceRootPath = report.RootPath;
        DetailTitle = report.WorkspaceName;
        DetailSummary = report.Summary;
        DetailRecommendation = report.Recommendation;
        DetailPrimaryAction = primaryAction;

        DetailItems.Clear();
        DetailVisibleActions.Clear();
        DetailActions.Clear();
        DetailAdvancedActions.Clear();
        SuggestedNextSteps.Clear();

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

        foreach (var step in report.SuggestedNextSteps)
        {
            SuggestedNextSteps.Add(step);
        }

        ShowAdvancedActions = report.CanResetRuntime || advancedActions.Count > 0 ? ShowAdvancedActions : false;
        TranscriptExcerpt = report.TranscriptExcerpt;
        RaisePropertyChanged(nameof(HasSuggestedNextSteps));
        RaisePropertyChanged(nameof(HasDetailAdvancedActions));
    }
}
