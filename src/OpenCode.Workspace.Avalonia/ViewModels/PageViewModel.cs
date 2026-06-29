using System.Collections.ObjectModel;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    private string _detailTitle;
    private string _detailSummary;
    private string _detailRecommendation = string.Empty;
    private ActionItemViewModel? _detailPrimaryAction;
    private bool _showAdvancedActions;

    protected PageViewModel(string title, string description)
    {
        Title = title;
        Description = description;
        _detailTitle = title;
        _detailSummary = description;
    }

    public string Title { get; }
    public string Description { get; }

    public string DetailTitle
    {
        get => _detailTitle;
        protected set => SetProperty(ref _detailTitle, value);
    }

    public string DetailSummary
    {
        get => _detailSummary;
        protected set => SetProperty(ref _detailSummary, value);
    }

    public ActionItemViewModel? DetailPrimaryAction
    {
        get => _detailPrimaryAction;
        protected set
        {
            if (SetProperty(ref _detailPrimaryAction, value))
            {
                RaisePropertyChanged(nameof(ShowDetailPrimaryAction));
            }
        }
    }

    public bool ShowDetailPrimaryAction => DetailPrimaryAction is not null;
    public string DetailRecommendation
    {
        get => _detailRecommendation;
        protected set
        {
            if (SetProperty(ref _detailRecommendation, value))
            {
                RaisePropertyChanged(nameof(HasDetailRecommendation));
            }
        }
    }

    public bool HasDetailRecommendation => !string.IsNullOrWhiteSpace(DetailRecommendation);

    public bool ShowAdvancedActions
    {
        get => _showAdvancedActions;
        set => SetProperty(ref _showAdvancedActions, value);
    }

    public bool HasDetailAdvancedActions => DetailAdvancedActions.Count > 0;

    public ObservableCollection<DetailItemViewModel> DetailItems { get; } = [];
    public ObservableCollection<ActionItemViewModel> DetailVisibleActions { get; } = [];
    public ObservableCollection<ActionItemViewModel> DetailActions { get; } = [];
    public ObservableCollection<ActionItemViewModel> DetailAdvancedActions { get; } = [];
}
