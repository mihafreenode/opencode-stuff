using System.Collections.ObjectModel;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    private string _detailTitle;
    private string _detailSummary;
    private ActionItemViewModel? _detailPrimaryAction;

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

    public ObservableCollection<DetailItemViewModel> DetailItems { get; } = [];
    public ObservableCollection<ActionItemViewModel> DetailActions { get; } = [];
}
