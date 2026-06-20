using System.Collections.ObjectModel;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class TranscriptsPageViewModel : PageViewModel
{
    private TranscriptEntryViewModel? _selectedEntry;

    public TranscriptsPageViewModel(IDesktopShellService desktopShellService)
        : base("Transcripts", "Structured recent activity instead of raw log walls.")
    {
        Load(desktopShellService);
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

    private void Load(IDesktopShellService desktopShellService)
    {
        foreach (var workspaceItem in desktopShellService.LoadWorkspaceItemsAsync(includeRuntimeInspection: false).GetAwaiter().GetResult().Items.Where(item => item.HasSnapshot))
        {
            var snapshot = workspaceItem.Snapshot!;
            var timeline = desktopShellService.LoadTimeline(snapshot.Paths.TimelinePath);
            foreach (var timelineEvent in timeline.Events.OrderByDescending(item => item.OccurredUtc).Take(8))
            {
                Entries.Add(new TranscriptEntryViewModel(timelineEvent.Summary, snapshot.Definition.Workspace.Name, timelineEvent.Details, timelineEvent.OccurredUtc, snapshot.Paths.TimelinePath));
            }
        }

        SelectedEntry = Entries.OrderByDescending(item => item.Timestamp).FirstOrDefault();
    }
}
