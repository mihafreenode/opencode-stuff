using System.Collections.ObjectModel;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class SavePointsPageViewModel : PageViewModel
{
    private SavePointEntryViewModel? _selectedEntry;

    public SavePointsPageViewModel(IDesktopShellService desktopShellService)
        : base("Save Points", "Read-only preview of recent save points, checkpoints, and protection metadata.")
    {
        DetailTitle = "Save Points";
        DetailSummary = "Read-only preview is loaded when this page is expanded in a later phase.";
    }

    public ObservableCollection<SavePointEntryViewModel> Entries { get; } = [];

    public SavePointEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                DetailItems.Clear();
                DetailActions.Clear();
                if (value is null)
                {
                    DetailTitle = "Save Points";
                    DetailSummary = Description;
                    return;
                }

                DetailTitle = value.Title;
                DetailSummary = value.Summary;
                DetailItems.Add(new DetailItemViewModel("Workspace", value.WorkspaceName));
                DetailItems.Add(new DetailItemViewModel("Recorded", value.TimestampLabel));
                DetailActions.Add(new ActionItemViewModel("Create Save Point", string.Empty, false, "Read-only preview in Avalonia phase 1. Use WPF or CLI for write operations.", new RelayCommand(() => { })));
            }
        }
    }

}
