using System.Collections.ObjectModel;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class TranscriptsPageViewModel : PageViewModel
{
    private TranscriptEntryViewModel? _selectedEntry;

    public TranscriptsPageViewModel(IDesktopShellService desktopShellService)
        : base("Transcripts", "Structured recent activity instead of raw log walls.")
    {
        DetailTitle = "Transcripts";
        DetailSummary = "Structured activity preview loads after shell startup work is complete in a later phase.";
    }

    public ObservableCollection<TranscriptEntryViewModel> Entries { get; } = [];

    public TranscriptEntryViewModel? SelectedEntry
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
                    DetailTitle = "Transcripts";
                    DetailSummary = Description;
                    return;
                }

                DetailTitle = value.Action;
                DetailSummary = value.Result;
                DetailItems.Add(new DetailItemViewModel("Workspace", value.Workspace));
                DetailItems.Add(new DetailItemViewModel("Timestamp", value.TimestampLabel));
                DetailItems.Add(new DetailItemViewModel("Transcript", value.TranscriptLink));
            }
        }
    }

}
