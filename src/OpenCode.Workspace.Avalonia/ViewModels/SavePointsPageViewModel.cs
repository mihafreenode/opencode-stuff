using System.Collections.ObjectModel;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class SavePointsPageViewModel : PageViewModel
{
    private SavePointEntryViewModel? _selectedEntry;

    public SavePointsPageViewModel(IDesktopShellService desktopShellService)
        : base("Save Points", "Read-only preview of recent save points, checkpoints, and protection metadata.")
    {
        Load(desktopShellService);
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

    private void Load(IDesktopShellService desktopShellService)
    {
        foreach (var workspaceItem in desktopShellService.LoadWorkspaceItemsAsync(includeRuntimeInspection: false).GetAwaiter().GetResult().Items.Where(item => item.HasSnapshot))
        {
            var snapshot = workspaceItem.Snapshot!;
            var checkpoints = desktopShellService.LoadCheckpointIndex(snapshot.Paths.CheckpointIndexPath);
            foreach (var checkpoint in checkpoints.Items.OrderByDescending(item => item.CreatedUtc).Take(5))
            {
                Entries.Add(new SavePointEntryViewModel("Checkpoint", $"{checkpoint.Id} on {checkpoint.CurrentBranch}", checkpoint.CreatedUtc, snapshot.Definition.Workspace.Name));
            }

            var timeline = desktopShellService.LoadTimeline(snapshot.Paths.TimelinePath);
            foreach (var timelineEvent in timeline.Events.Where(item => string.Equals(item.Type, "save-point", StringComparison.OrdinalIgnoreCase)).OrderByDescending(item => item.OccurredUtc).Take(5))
            {
                Entries.Add(new SavePointEntryViewModel(timelineEvent.Summary, timelineEvent.Details, timelineEvent.OccurredUtc, snapshot.Definition.Workspace.Name));
            }
        }

        SelectedEntry = Entries.OrderByDescending(item => item.Timestamp).FirstOrDefault();
    }
}
