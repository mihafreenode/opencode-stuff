using System.Collections.ObjectModel;
using System.IO;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class SavePointsPageViewModel : PageViewModel
{
    private readonly IDesktopShellService _desktopShellService;
    private SavePointEntryViewModel? _selectedEntry;
    private WorkspaceSummaryViewModel? _selectedWorkspace;
    private IClipboardService? _clipboardService;
    private bool _isLoading;
    private bool _hasLoadError;
    private string _statusMessage;
    private int _refreshVersion;

    public SavePointsPageViewModel(IDesktopShellService desktopShellService)
        : base("Timeline", "Workspace history from Save Points and related recovery events.")
    {
        _desktopShellService = desktopShellService;
        _statusMessage = "Select a workspace to inspect its timeline.";
        DetailTitle = "Timeline";
        DetailSummary = _statusMessage;
    }

    public ObservableCollection<SavePointEntryViewModel> Entries { get; } = [];
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasLoadError
    {
        get => _hasLoadError;
        private set => SetProperty(ref _hasLoadError, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool ShowEmptyState => !IsLoading && !HasLoadError && Entries.Count == 0;
    public bool ShowErrorState => HasLoadError;
    public bool ShowList => Entries.Count > 0;

    public SavePointEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                UpdateSelectionDetails();
            }
        }
    }

    public void SetClipboardService(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
        UpdateSelectionDetails();
    }

    public async Task RefreshAsync(WorkspaceSummaryViewModel? workspace, CancellationToken cancellationToken = default)
    {
        var refreshVersion = Interlocked.Increment(ref _refreshVersion);
        _selectedWorkspace = workspace;
        IsLoading = false;
        HasLoadError = false;
        StatusMessage = "Select a workspace to inspect its timeline.";
        RaisePropertyChanged(nameof(ShowEmptyState));
        RaisePropertyChanged(nameof(ShowErrorState));
        RaisePropertyChanged(nameof(ShowList));

        if (workspace?.Snapshot is null)
        {
            Entries.Clear();
            SelectedEntry = null;
            DetailTitle = "Timeline";
            DetailSummary = StatusMessage;
            return;
        }

        IsLoading = true;
        StatusMessage = $"Loading timeline for {workspace.Name}...";
        DetailTitle = "Timeline";
        DetailSummary = StatusMessage;
        RaisePropertyChanged(nameof(ShowEmptyState));
        RaisePropertyChanged(nameof(ShowErrorState));
        RaisePropertyChanged(nameof(ShowList));

        try
        {
            var timelinePath = workspace.Snapshot.Paths.TimelinePath;
            var historyPath = workspace.Snapshot.Paths.HistoryPath;
            var timeline = await Task.Run(() => _desktopShellService.LoadTimeline(timelinePath), cancellationToken);
            var entries = timeline.Events
                .OrderByDescending(item => item.OccurredUtc)
                .Select(item => CreateEntry(item, workspace, timelinePath, historyPath))
                .ToList();

            if (refreshVersion != _refreshVersion)
            {
                return;
            }

            var selectedId = SelectedEntry?.Id;
            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }

            if (entries.Count == 0)
            {
                SelectedEntry = null;
                StatusMessage = File.Exists(timelinePath)
                    ? $"No timeline entries exist yet for {workspace.Name}. Create a Save Point to capture workspace history."
                    : $"Timeline file has not been created yet for {workspace.Name}. Create a Save Point to start workspace history.";
                DetailTitle = "Timeline";
                DetailSummary = StatusMessage;
            }
            else
            {
                SelectedEntry = selectedId is null
                    ? entries[0]
                    : entries.FirstOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.Ordinal)) ?? entries[0];
                StatusMessage = string.Empty;
            }
        }
        catch (Exception exception)
        {
            if (refreshVersion != _refreshVersion)
            {
                return;
            }

            Entries.Clear();
            SelectedEntry = null;
            HasLoadError = true;
            StatusMessage = $"Timeline could not be loaded for {_selectedWorkspace?.Name ?? "the selected workspace"}. Open the history folder or timeline file to inspect the issue. {exception.Message}";
            DetailTitle = "Timeline Error";
            DetailSummary = StatusMessage;
            DetailItems.Clear();
            DetailActions.Clear();
            if (workspace?.Snapshot is not null)
            {
                DetailActions.Add(new ActionItemViewModel("Open Timeline File", "Open the workspace history file with the host shell.", true, string.Empty, new AsyncRelayCommand(() => _desktopShellService.OpenPathAsync(workspace.Snapshot.Paths.TimelinePath))));
                DetailActions.Add(new ActionItemViewModel("Open History Folder", "Open the containing history folder with the host shell.", true, string.Empty, new AsyncRelayCommand(() => _desktopShellService.OpenPathAsync(workspace.Snapshot.Paths.HistoryPath))));
            }
        }
        finally
        {
            if (refreshVersion == _refreshVersion)
            {
                IsLoading = false;
                RaisePropertyChanged(nameof(ShowEmptyState));
                RaisePropertyChanged(nameof(ShowErrorState));
                RaisePropertyChanged(nameof(ShowList));
            }
        }
    }

    private void UpdateSelectionDetails()
    {
        DetailItems.Clear();
        DetailActions.Clear();
        if (SelectedEntry is null)
        {
            DetailTitle = "Timeline";
            DetailSummary = string.IsNullOrWhiteSpace(StatusMessage) ? Description : StatusMessage;
            return;
        }

        DetailTitle = SelectedEntry.Title;
        DetailSummary = string.IsNullOrWhiteSpace(SelectedEntry.Message) ? SelectedEntry.Summary : SelectedEntry.Message;
        DetailItems.Add(new DetailItemViewModel("Workspace", SelectedEntry.WorkspaceName));
        DetailItems.Add(new DetailItemViewModel("Recorded", SelectedEntry.TimestampLabel));
        DetailItems.Add(new DetailItemViewModel("Action", SelectedEntry.EventTypeLabel));
        DetailItems.Add(new DetailItemViewModel("Message", string.IsNullOrWhiteSpace(SelectedEntry.Message) ? SelectedEntry.Summary : SelectedEntry.Message));

        var branch = SelectedEntry.HasBranch ? SelectedEntry.Branch : _selectedWorkspace?.CurrentBranch ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(branch))
        {
            DetailItems.Add(new DetailItemViewModel("Branch", branch));
        }

        var commitSha = SelectedEntry.HasCommitSha
            ? SelectedEntry.CommitSha
            : SelectedEntry == Entries.FirstOrDefault() ? _selectedWorkspace?.Snapshot?.Safety.AdvancedGit.LatestCommitSha ?? string.Empty : string.Empty;
        if (!string.IsNullOrWhiteSpace(commitSha))
        {
            DetailItems.Add(new DetailItemViewModel("Commit", commitSha));
        }

        if (SelectedEntry.HasAffectedPaths)
        {
            DetailItems.Add(new DetailItemViewModel("Affected files", $"{SelectedEntry.AffectedPathCount} file(s)"));
            DetailItems.Add(new DetailItemViewModel("Paths", string.Join(Environment.NewLine, SelectedEntry.AffectedPaths))); 
        }

        DetailActions.Add(new ActionItemViewModel("Copy Summary", "Copy a concise summary for sharing or diagnostics.", _clipboardService is not null, _clipboardService is null ? "Clipboard is unavailable." : string.Empty, new AsyncRelayCommand(() => CopyAsync(BuildSummaryText(SelectedEntry)), () => _clipboardService is not null)));
        DetailActions.Add(new ActionItemViewModel("Copy Message", "Copy the full timeline message text.", _clipboardService is not null, _clipboardService is null ? "Clipboard is unavailable." : string.Empty, new AsyncRelayCommand(() => CopyAsync(string.IsNullOrWhiteSpace(SelectedEntry.Message) ? SelectedEntry.Summary : SelectedEntry.Message), () => _clipboardService is not null)));
        DetailActions.Add(new ActionItemViewModel("Copy Commit Id", "Copy the captured Git commit id when available.", !string.IsNullOrWhiteSpace(commitSha) && _clipboardService is not null, string.IsNullOrWhiteSpace(commitSha) ? "This timeline entry does not have a commit id available." : _clipboardService is null ? "Clipboard is unavailable." : string.Empty, new AsyncRelayCommand(() => CopyAsync(commitSha), () => !string.IsNullOrWhiteSpace(commitSha) && _clipboardService is not null)));
        DetailActions.Add(new ActionItemViewModel("Open Timeline File", "Open the workspace history file with the host shell.", true, string.Empty, new AsyncRelayCommand(() => _desktopShellService.OpenPathAsync(SelectedEntry.TimelinePath))));
        DetailActions.Add(new ActionItemViewModel("Open History Folder", "Open the containing history folder with the host shell.", true, string.Empty, new AsyncRelayCommand(() => _desktopShellService.OpenPathAsync(SelectedEntry.HistoryPath))));
    }

    private static SavePointEntryViewModel CreateEntry(WorkspaceTimelineEvent item, WorkspaceSummaryViewModel workspace, string timelinePath, string historyPath)
    {
        var message = item.Details?.Trim() ?? string.Empty;
        var summary = string.IsNullOrWhiteSpace(message) ? item.Summary : message.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
        return new SavePointEntryViewModel(
            item.Id,
            item.Summary,
            summary,
            item.Type,
            message,
            item.OccurredUtc,
            workspace.Name,
            timelinePath,
            historyPath,
            item.Branch,
            item.CommitSha,
            item.AffectedPaths);
    }

    private async Task CopyAsync(string text)
    {
        if (_clipboardService is null)
        {
            return;
        }

        try
        {
            await _clipboardService.SetTextAsync(text);
        }
        catch (Exception exception)
        {
            Services.StartupLog.WriteGlobalException("Timeline copy failed", exception);
            DetailSummary = $"Timeline copy failed: {exception.Message}";
        }
    }

    private static string BuildSummaryText(SavePointEntryViewModel entry)
        => string.Join(Environment.NewLine, new[]
        {
            $"Workspace: {entry.WorkspaceName}",
            $"Recorded: {entry.TimestampLabel}",
            $"Action: {entry.EventTypeLabel}",
            $"Summary: {entry.Title}",
            $"Message: {(string.IsNullOrWhiteSpace(entry.Message) ? entry.Summary : entry.Message)}",
        }.Concat(string.IsNullOrWhiteSpace(entry.Branch) ? [] : [$"Branch: {entry.Branch}"])
         .Concat(string.IsNullOrWhiteSpace(entry.CommitSha) ? [] : [$"Commit: {entry.CommitSha}"]));

}
