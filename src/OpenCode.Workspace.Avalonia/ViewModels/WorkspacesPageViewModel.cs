using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Avalonia.Threading;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspacesPageViewModel : PageViewModel
{
    private const string DeleteWorkspaceFilesUnavailableMessage = "Delete workspace files is not available in this version. Use File Explorer or terminal after creating a backup.";
    private const int VisibleOperationLogLineLimit = 5000;
    private const int NormalOperationLogFlushBatchSize = 600;
    private static readonly TimeSpan NormalOperationLogFlushInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MediumOperationLogFlushInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HeavyOperationLogFlushInterval = TimeSpan.FromSeconds(3);

    private readonly IDesktopShellService _desktopShellService;
    private readonly IReadOnlyList<OpenCode.Workspace.Core.Models.TemplateManifest> _templates;
    private WorkspaceSummaryViewModel? _selectedWorkspace;
    private string _emptyStateTitle = string.Empty;
    private string _emptyStateMessage = string.Empty;
    private WorkspaceLoadReport _workspaceLoadReport = new();
    private string _loadingTitle = string.Empty;
    private string _loadingMessage = string.Empty;
    private string _loadingProgressLabel = string.Empty;
    private bool _isLoading;
    private bool _hasLoadError;
    private string _loadErrorMessage = string.Empty;
    private bool _isReprovisioning;
    private string _reprovisionStatusMessage = string.Empty;
    private IClipboardService? _clipboardService;
    private bool _followLatestOutput = true;
    private string _operationLogText = string.Empty;
    private OperationTranscript? _lastOperationTranscript;
    private readonly List<OperationTranscriptLine> _visibleOperationTranscriptLines = [];
    private readonly List<string> _visibleOperationLogLines = [];
    private readonly DispatcherTimer _operationLogFlushTimer;
    private TranscriptBuffer? _operationTranscriptBuffer;
    private bool _isOperationLogVisible;
    private IWorkspaceInteractionService? _interactionService;
    private bool _isWorkspaceActionRunning;
    private string _workspaceActionStatusMessage = string.Empty;

    public WorkspacesPageViewModel(IDesktopShellService desktopShellService, IReadOnlyList<OpenCode.Workspace.Core.Models.TemplateManifest>? templates = null)
        : base("Workspaces", "Inspect local workspaces, repository state, and runtime readiness.")
    {
        _desktopShellService = desktopShellService;
        _templates = templates ?? [];
        CreateWorkspaceCommand = new AsyncRelayCommand(CreateWorkspaceAsync, () => _interactionService is not null && !IsBusyForWorkspaceActions);
        OpenExistingRepositoryCommand = new AsyncRelayCommand(OpenExistingRepositoryAsync, () => _interactionService is not null && !IsBusyForWorkspaceActions);
        RefreshWorkspacesCommand = new AsyncRelayCommand(() => LoadAsync(), () => !IsBusyForWorkspaceActions);
        OpenSelectedWorkspaceCommand = new AsyncRelayCommand(OpenSelectedWorkspaceAsync, () => SelectedWorkspace is not null);
        OpenWorkspaceFolderCommand = new AsyncRelayCommand(OpenSelectedWorkspaceFolderAsync, () => SelectedWorkspace is not null);
        TroubleshootWorkspaceCommand = new AsyncRelayCommand(TroubleshootWorkspaceInternalAsync, () => SelectedWorkspace is not null);
        RemoveWorkspaceCommand = new AsyncRelayCommand(RemoveWorkspaceAsync, CanRemoveSelectedWorkspace);
        PublishWorkspaceCommand = new AsyncRelayCommand(PublishWorkspaceAsync, CanPublishSelectedWorkspace);
        BackupWorkspaceCommand = new AsyncRelayCommand(BackupWorkspaceAsync, CanBackupSelectedWorkspace);
        CreateSavePointCommand = new AsyncRelayCommand(CreateSavePointAsync, CanCreateSavePointSelectedWorkspace);
        CreateCheckpointCommand = new AsyncRelayCommand(CreateCheckpointAsync, CanCreateCheckpointSelectedWorkspace);
        StartWorkspaceCommand = new AsyncRelayCommand(StartSelectedWorkspaceAsync, CanStartSelectedWorkspace);
        RecoverWorkspaceCommand = new AsyncRelayCommand(RecoverSelectedWorkspaceAsync, CanRecoverSelectedWorkspace);
        ResetRuntimeCommand = new AsyncRelayCommand(ResetRuntimeSelectedWorkspaceAsync, CanResetRuntimeSelectedWorkspace);
        AttachWorkspaceCommand = new AsyncRelayCommand(AttachSelectedWorkspaceAsync, CanAttachSelectedWorkspace);
        ReprovisionWorkspaceCommand = new AsyncRelayCommand(ReprovisionSelectedWorkspaceAsync, CanReprovisionSelectedWorkspace);
        RetryWorkspaceCommand = new AsyncRelayCommand(RetrySelectedWorkspaceAsync, CanRetrySelectedWorkspace);
        CopyOperationLogCommand = new AsyncRelayCommand(CopyOperationLogAsync, () => HasOperationLog && _clipboardService is not null);
        ClearOperationLogCommand = new RelayCommand(ClearOperationLog, () => HasOperationLog);
        ToggleOperationLogVisibilityCommand = new RelayCommand(ToggleOperationLogVisibility);
        DisabledActionCommand = new RelayCommand(() => { });
        _operationLogFlushTimer = new DispatcherTimer { Interval = NormalOperationLogFlushInterval };
        _operationLogFlushTimer.Tick += (_, _) => FlushPendingOperationLogToUi();
        SetLoadingState();
    }

    public ObservableCollection<WorkspaceSummaryViewModel> Workspaces { get; } = [];
    public AsyncRelayCommand CreateWorkspaceCommand { get; }
    public AsyncRelayCommand OpenExistingRepositoryCommand { get; }
    public AsyncRelayCommand RefreshWorkspacesCommand { get; }
    public AsyncRelayCommand OpenSelectedWorkspaceCommand { get; }
    public AsyncRelayCommand OpenWorkspaceFolderCommand { get; }
    public AsyncRelayCommand TroubleshootWorkspaceCommand { get; }
    public AsyncRelayCommand RemoveWorkspaceCommand { get; }
    public AsyncRelayCommand PublishWorkspaceCommand { get; }
    public AsyncRelayCommand BackupWorkspaceCommand { get; }
    public AsyncRelayCommand CreateSavePointCommand { get; }
    public AsyncRelayCommand CreateCheckpointCommand { get; }
    public AsyncRelayCommand StartWorkspaceCommand { get; }
    public AsyncRelayCommand RecoverWorkspaceCommand { get; }
    public AsyncRelayCommand ResetRuntimeCommand { get; }
    public AsyncRelayCommand AttachWorkspaceCommand { get; }
    public AsyncRelayCommand ReprovisionWorkspaceCommand { get; }
    public AsyncRelayCommand RetryWorkspaceCommand { get; }
    public AsyncRelayCommand CopyOperationLogCommand { get; }
    public RelayCommand ClearOperationLogCommand { get; }
    public RelayCommand ToggleOperationLogVisibilityCommand { get; }
    public RelayCommand DisabledActionCommand { get; }
    public Func<string, Task>? TroubleshootWorkspaceAsync { get; set; }
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

    public string LoadErrorMessage
    {
        get => _loadErrorMessage;
        private set => SetProperty(ref _loadErrorMessage, value);
    }

    public bool IsReprovisioning
    {
        get => _isReprovisioning;
        private set => SetProperty(ref _isReprovisioning, value);
    }

    public string ReprovisionStatusMessage
    {
        get => _reprovisionStatusMessage;
        private set => SetProperty(ref _reprovisionStatusMessage, value);
    }

    public bool FollowLatestOutput
    {
        get => _followLatestOutput;
        set => SetProperty(ref _followLatestOutput, value);
    }

    public string OperationLogText
    {
        get => _operationLogText;
        private set
        {
            if (SetProperty(ref _operationLogText, value))
            {
                RaisePropertyChanged(nameof(HasOperationLog));
                RaisePropertyChanged(nameof(ShowOperationLogToggleButton));
                CopyOperationLogCommand.RaiseCanExecuteChanged();
                ClearOperationLogCommand.RaiseCanExecuteChanged();
                if (HasOperationLog)
                {
                    IsOperationLogVisible = true;
                }
            }
        }
    }

    public bool HasOperationLog => !string.IsNullOrWhiteSpace(OperationLogText);
    public bool ShowOperationLogToggleButton => HasOperationLog;
    public bool IsBusyForWorkspaceActions => _isWorkspaceActionRunning || IsReprovisioning;

    public OperationTranscript? LastOperationTranscript
    {
        get => _lastOperationTranscript;
        private set => SetProperty(ref _lastOperationTranscript, value);
    }

    public bool IsOperationLogVisible
    {
        get => _isOperationLogVisible;
        private set
        {
            if (SetProperty(ref _isOperationLogVisible, value))
            {
                RaisePropertyChanged(nameof(OperationLogToggleLabel));
                RaisePropertyChanged(nameof(ShowOperationLogPanel));
                RaisePropertyChanged(nameof(ShowOperationLogToggleButton));
            }
        }
    }

    public string OperationLogToggleLabel => IsOperationLogVisible ? "Hide Operation Log" : "Show Operation Log";

    public bool HasWorkspaces => Workspaces.Count > 0;
    public bool ShowEmptyState => !IsLoading && !HasLoadError && !HasWorkspaces;
    public bool ShowLoadingState => IsLoading;
    public bool ShowErrorState => HasLoadError && !HasWorkspaces;
    public bool ShowOperationLogPanel => HasOperationLog && IsOperationLogVisible;
    public string LoadingTitle
    {
        get => _loadingTitle;
        private set => SetProperty(ref _loadingTitle, value);
    }

    public string LoadingMessage
    {
        get => _loadingMessage;
        private set => SetProperty(ref _loadingMessage, value);
    }

    public string LoadingProgressLabel
    {
        get => _loadingProgressLabel;
        private set => SetProperty(ref _loadingProgressLabel, value);
    }

    public WorkspaceLoadReport WorkspaceLoadReport
    {
        get => _workspaceLoadReport;
        private set => SetProperty(ref _workspaceLoadReport, value);
    }

    public string EmptyStateTitle
    {
        get => _emptyStateTitle;
        private set => SetProperty(ref _emptyStateTitle, value);
    }

    public string EmptyStateMessage
    {
        get => _emptyStateMessage;
        private set => SetProperty(ref _emptyStateMessage, value);
    }

    public WorkspaceSummaryViewModel? SelectedWorkspace
    {
        get => _selectedWorkspace;
        set
        {
            if (SetProperty(ref _selectedWorkspace, value))
            {
                UpdateWorkspaceSelectionState();
                UpdateDetailPanel();
                OpenSelectedWorkspaceCommand.RaiseCanExecuteChanged();
                OpenWorkspaceFolderCommand.RaiseCanExecuteChanged();
                TroubleshootWorkspaceCommand.RaiseCanExecuteChanged();
                RemoveWorkspaceCommand.RaiseCanExecuteChanged();
                PublishWorkspaceCommand.RaiseCanExecuteChanged();
                BackupWorkspaceCommand.RaiseCanExecuteChanged();
                CreateCheckpointCommand.RaiseCanExecuteChanged();
                StartWorkspaceCommand.RaiseCanExecuteChanged();
                RecoverWorkspaceCommand.RaiseCanExecuteChanged();
                ResetRuntimeCommand.RaiseCanExecuteChanged();
                AttachWorkspaceCommand.RaiseCanExecuteChanged();
                ReprovisionWorkspaceCommand.RaiseCanExecuteChanged();
                RetryWorkspaceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var preferredSelectedRootPath = SelectedWorkspace?.RootPath;
        SetLoadingState();
        Workspaces.Clear();
        try
        {
            var loadResult = await _desktopShellService.LoadWorkspaceItemsAsync(includeRuntimeInspection: true, progress: update =>
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    ApplyLoadProgressUpdate(update);
                }
                else
                {
                    Dispatcher.UIThread.Post(() => ApplyLoadProgressUpdate(update));
                }
            }, cancellationToken);
            WorkspaceLoadReport = loadResult.Report;
            foreach (var item in loadResult.Items.OrderBy(item => item, WorkspaceDisplayComparer.Instance))
            {
                ApplyWorkspaceItem(new WorkspaceSummaryViewModel(item));
            }

            HasLoadError = false;
            LoadErrorMessage = string.Empty;
            DetailSummary = BuildCompletedLoadSummary(loadResult.Report);
        }
        catch (Exception exception)
        {
            HasLoadError = true;
            LoadErrorMessage = exception.Message;
            DetailTitle = "Workspace discovery failed";
            DetailSummary = "The window is available, but workspace discovery did not complete.";
            DetailRecommendation = "Troubleshoot Workspace reviews workspace and runtime diagnostics without changing durable data.";
            DetailItems.Clear();
            DetailItems.Add(new DetailItemViewModel("Error", exception.Message));
            DetailPrimaryAction = null;
            DetailActions.Clear();
            DetailVisibleActions.Clear();
            DetailAdvancedActions.Clear();
            ShowAdvancedActions = false;
            DetailActions.Add(new ActionItemViewModel("Open Folder", string.Empty, false, "Workspace discovery failed.", DisabledActionCommand));
            SelectedWorkspace = null;
        }
        finally
        {
            IsLoading = false;
        }

        RaisePropertyChanged(nameof(HasWorkspaces));
        RaisePropertyChanged(nameof(ShowEmptyState));
        RaisePropertyChanged(nameof(ShowLoadingState));
        RaisePropertyChanged(nameof(ShowErrorState));

        if (!HasLoadError)
        {
            if (string.IsNullOrWhiteSpace(preferredSelectedRootPath))
            {
                SelectedWorkspace = Workspaces.FirstOrDefault();
            }
            else
            {
                SelectedWorkspace = Workspaces.FirstOrDefault(item => string.Equals(item.RootPath, preferredSelectedRootPath, StringComparison.OrdinalIgnoreCase))
                    ?? Workspaces.FirstOrDefault();
            }

            if (SelectedWorkspace is not null)
            {
                UpdateDetailPanel();
            }
        }
        if (SelectedWorkspace is null)
        {
            if (HasLoadError)
            {
                EmptyStateTitle = string.Empty;
                EmptyStateMessage = string.Empty;
                return;
            }

            EmptyStateTitle = "No workspaces discovered.";
            EmptyStateMessage = "OpenCode looks for workspace.yaml,\nworkspace.yml,\n.opencode/profile.yaml,\n.opencode/profile.yml\n\nUse Create Workspace or Open Existing Repository.";
            DetailTitle = EmptyStateTitle;
            DetailSummary = EmptyStateMessage;
            DetailRecommendation = string.Empty;
            DetailItems.Clear();
            DetailPrimaryAction = null;
            DetailActions.Clear();
            DetailAdvancedActions.Clear();
            ShowAdvancedActions = false;
            return;
        }

        EmptyStateTitle = string.Empty;
        EmptyStateMessage = string.Empty;
    }

    private void ApplyLoadProgressUpdate(WorkspaceLoadProgressUpdate update)
    {
        LoadingTitle = update.Title;
        LoadingMessage = update.Message;
        LoadingProgressLabel = update.ProgressLabel;
        EmptyStateTitle = update.Title;
        EmptyStateMessage = update.Message;
        DetailSummary = string.IsNullOrWhiteSpace(update.ProgressLabel)
            ? update.Message
            : $"{update.ProgressLabel}. {update.Message}";

        if (update.LoadedItem is not null)
        {
            ApplyWorkspaceItem(new WorkspaceSummaryViewModel(update.LoadedItem));
        }
    }

    private void ApplyWorkspaceItem(WorkspaceSummaryViewModel summary)
    {
        var existingIndex = Workspaces
            .Select((item, index) => new { item, index })
            .FirstOrDefault(pair => string.Equals(pair.item.RootPath, summary.RootPath, StringComparison.OrdinalIgnoreCase));

        if (existingIndex is null)
        {
            Workspaces.Add(summary);
            ApplyWorkspacePresentationToSummary(summary);
            SortWorkspaces();
            RaisePropertyChanged(nameof(HasWorkspaces));
            if (SelectedWorkspace is null)
            {
                SelectedWorkspace = summary;
            }

            return;
        }

        var wasSelected = ReferenceEquals(SelectedWorkspace, existingIndex.item)
            || string.Equals(SelectedWorkspace?.RootPath, summary.RootPath, StringComparison.OrdinalIgnoreCase);
        ApplyWorkspacePresentationToSummary(summary);
        Workspaces[existingIndex.index] = summary;
        SortWorkspaces();
        if (wasSelected)
        {
            SelectedWorkspace = Workspaces.FirstOrDefault(item => string.Equals(item.RootPath, summary.RootPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void UpdateWorkspaceSelectionState()
    {
        foreach (var workspace in Workspaces)
        {
            workspace.IsSelected = ReferenceEquals(workspace, SelectedWorkspace)
                || string.Equals(workspace.RootPath, SelectedWorkspace?.RootPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void SortWorkspaces()
    {
        if (Workspaces.Count < 2)
        {
            return;
        }

        var selectedRootPath = SelectedWorkspace?.RootPath;
        var ordered = Workspaces.OrderBy(item => item, WorkspaceDisplayComparer.Instance).ToList();
        var changed = false;
        for (var index = 0; index < ordered.Count; index++)
        {
            if (!ReferenceEquals(Workspaces[index], ordered[index]))
            {
                changed = true;
                break;
            }
        }

        if (!changed)
        {
            return;
        }

        Workspaces.Clear();
        foreach (var item in ordered)
        {
            Workspaces.Add(item);
        }

        if (!string.IsNullOrWhiteSpace(selectedRootPath))
        {
            SelectedWorkspace = Workspaces.FirstOrDefault(item => string.Equals(item.RootPath, selectedRootPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class WorkspaceDisplayComparer : IComparer<WorkspaceShellItem>, IComparer<WorkspaceSummaryViewModel>
    {
        public static WorkspaceDisplayComparer Instance { get; } = new();

        public int Compare(WorkspaceShellItem? left, WorkspaceShellItem? right)
            => CompareRecords(left?.Record, right?.Record);

        public int Compare(WorkspaceSummaryViewModel? left, WorkspaceSummaryViewModel? right)
            => CompareRecords(left?.Record, right?.Record);

        private static int CompareRecords(WorkspaceRecord? left, WorkspaceRecord? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return 1;
            }

            if (right is null)
            {
                return -1;
            }

            var lastOpened = right.LastOpenedUtc.CompareTo(left.LastOpenedUtc);
            if (lastOpened != 0)
            {
                return lastOpened;
            }

            var created = right.CreatedUtc.CompareTo(left.CreatedUtc);
            if (created != 0)
            {
                return created;
            }

            var name = StringComparer.OrdinalIgnoreCase.Compare(
                string.IsNullOrWhiteSpace(left.Name) ? left.RootPath : left.Name,
                string.IsNullOrWhiteSpace(right.Name) ? right.RootPath : right.Name);
            if (name != 0)
            {
                return name;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left.RootPath, right.RootPath);
        }
    }

    private void SetLoadingState()
    {
        IsLoading = true;
        HasLoadError = false;
        LoadErrorMessage = string.Empty;
        LoadingTitle = "Loading workspace index...";
        LoadingMessage = "Reading the shared workspace index.";
        LoadingProgressLabel = string.Empty;
        EmptyStateTitle = LoadingTitle;
        EmptyStateMessage = LoadingMessage;
        DetailTitle = "Workspaces";
        DetailSummary = "Loading workspace index and startup diagnostics.";
        RaisePropertyChanged(nameof(ShowLoadingState));
        RaisePropertyChanged(nameof(ShowErrorState));
        RaisePropertyChanged(nameof(ShowEmptyState));
    }

    private static string BuildCompletedLoadSummary(WorkspaceLoadReport report)
    {
        var loadedSummary = $"Loaded {report.SnapshotCount} of {report.RawRecordCount} workspaces in {FormatDuration(report.TotalDuration)}.";
        return report.SlowestTiming is null
            ? loadedSummary
            : $"{loadedSummary} Slowest stage: {report.SlowestTiming.StageLabel} for {report.SlowestTiming.WorkspaceName} in {FormatDuration(report.SlowestTiming.Duration)}.";
    }

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalMilliseconds >= 1000
            ? $"{duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s"
            : $"{Math.Max(1, duration.TotalMilliseconds).ToString("F0", CultureInfo.InvariantCulture)} ms";

    private async Task CreateWorkspaceAsync()
    {
        if (_interactionService is null)
        {
            return;
        }

        var draft = await _interactionService.ShowCreateWorkspaceDialogAsync(_templates);
        if (draft is null)
        {
            return;
        }

        StartOperationTranscript("Create Workspace", draft.WorkspaceName);
        AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = $"Creating workspace {draft.WorkspaceName}..." });
        AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Generating workspace files..." });
        var definition = _desktopShellService.BuildWorkspaceDefinition(draft);
        if (!await ConfirmOracleSoftwareNoticeIfRequiredAsync(draft.Template, draft.WorkspaceName))
        {
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Cancelled." });
            DetailSummary = "Workspace creation cancelled.";
            return;
        }

        var snapshot = await _desktopShellService.CreateWorkspaceAsync(draft.WorkspaceRootPath, definition, new OperationTranscriptSink(this));
        FlushPendingOperationLogToUi(forceDrainAll: true);
        if (_desktopShellService.BuildOracleSoftwareNotice(draft.Template, draft.WorkspaceName) is not null)
        {
            snapshot = await _desktopShellService.AcknowledgeOracleSoftwareNoticeAsync(snapshot.Paths.RootPath, snapshot);
        }

        await LoadAsync();
        SelectWorkspaceByRootPath(snapshot.Paths.RootPath);
        DetailSummary = $"Workspace '{snapshot.Definition.Workspace.Name}' created successfully.";
    }

    private async Task OpenExistingRepositoryAsync()
    {
        if (_interactionService is null)
        {
            return;
        }

        var draft = await _interactionService.ShowOpenExistingRepositoryDialogAsync(_desktopShellService.InspectExistingGitCheckoutAsync, _desktopShellService.ValidateExistingGitCheckoutBranchAsync);
        if (draft is null)
        {
            return;
        }

        StartOperationTranscript("Open Existing Repository", draft.WorkspaceName);
        AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = $"Importing repository {draft.WorkspaceName}..." });
        var snapshot = await _desktopShellService.ImportExistingGitCheckoutAsync(new OpenCode.Workspace.Core.Workspaces.ExistingGitCheckoutImportRequest
        {
            RepositoryPath = draft.RepositoryPath,
            WorkspaceName = draft.WorkspaceName,
            BranchMode = draft.BranchMode,
            NamedBranch = draft.NamedBranch,
            ReuseExistingNamedBranch = draft.ReuseExistingNamedBranch,
        }, new OperationTranscriptSink(this));
        FlushPendingOperationLogToUi(forceDrainAll: true);
        await LoadAsync();
        SelectWorkspaceByRootPath(snapshot.Paths.RootPath);
        DetailSummary = $"Imported existing Git checkout '{snapshot.Definition.Workspace.Name}'.";
    }

    private async Task OpenSelectedWorkspaceAsync()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        if (!await ConfirmOracleSoftwareNoticeIfRequiredAsync(SelectedWorkspace.Snapshot))
        {
            StartOperationTranscript("Open Workspace", SelectedWorkspace.Name);
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Cancelled." });
            DetailSummary = "Workspace open cancelled.";
            return;
        }

        StartOperationTranscript("Open Workspace", SelectedWorkspace.Name);
        AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Checking workspace..." });
        DetailSummary = "Checking workspace...";

        try
        {
            await RunWorkspaceOperationAsync(
                "Open Workspace",
                "Checking workspace...",
                (rootPath, snapshot, sink) => _desktopShellService.OpenWorkspaceAsync(rootPath, snapshot, sink),
                preserveExistingTranscript: true);
        }
        catch
        {
        }
    }

    private async Task OpenSelectedWorkspaceFolderAsync()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        await _desktopShellService.OpenPathAsync(SelectedWorkspace.RootPath);
    }

    public void SelectWorkspaceByRootPath(string rootPath)
    {
        SelectedWorkspace = Workspaces.FirstOrDefault(item => string.Equals(item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
    }

    private async Task TroubleshootWorkspaceInternalAsync()
    {
        if (SelectedWorkspace is null || TroubleshootWorkspaceAsync is null)
        {
            return;
        }

        var rootPath = SelectedWorkspace.RootPath;

        try
        {
            var refreshedSnapshot = await _desktopShellService.RefreshVolatileWorkspaceStateAsync(rootPath, SelectedWorkspace.Snapshot);
            ReplaceSelectedWorkspace(refreshedSnapshot);
        }
        catch (WorkspaceProvisioningException)
        {
            await LoadAsync();
            SelectWorkspaceByRootPath(rootPath);
        }

        await TroubleshootWorkspaceAsync(rootPath);
    }

    private async Task CreateSavePointAsync()
    {
        if (SelectedWorkspace is null || _interactionService is null)
        {
            return;
        }

        StartOperationTranscript("Create Save Point", SelectedWorkspace.Name);
        AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Preparing Save Point..." });
        DetailSummary = "Preparing Save Point...";

        string suggestion;
        try
        {
            suggestion = await _desktopShellService.SuggestSavePointMessageAsync(SelectedWorkspace.RootPath);
        }
        catch (Exception exception)
        {
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.StandardError, Text = exception.Message });
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Failed." });
            DetailSummary = exception.Message;
            SelectedWorkspace.SetOperationFailureState(exception.Message, "Create Save Point");
            throw;
        }

        var draft = await _interactionService.ShowSavePointDialogAsync(suggestion);
        if (draft is null)
        {
            Services.StartupLog.WriteGlobal("Save Point flow observed dialog cancellation.");
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Cancelled." });
            DetailSummary = "Save Point cancelled.";
            return;
        }

        Services.StartupLog.WriteGlobal($"Save Point flow accepted dialog result. Message length: {draft.Message.Length}.");

        await RunWorkspaceOperationAsync(
            "Create Save Point",
            "Creating Save Point...",
            (rootPath, snapshot, sink) => _desktopShellService.CreateSavePointAsync(rootPath, draft.Message, snapshot, sink),
            preserveExistingTranscript: true);
    }

    private async Task CreateCheckpointAsync()
    {
        if (SelectedWorkspace is null || _interactionService is null)
        {
            return;
        }

        StartOperationTranscript("Create Checkpoint", SelectedWorkspace.Name);
        AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Preparing checkpoint..." });
        DetailSummary = "Preparing checkpoint...";

        var confirmed = await _interactionService.ConfirmCheckpointAsync(new WorkspaceCheckpointPrompt
        {
            WorkspaceName = SelectedWorkspace.Name,
            WorkspaceRoot = SelectedWorkspace.RootPath,
            Summary = "Checkpoint captures tracked changes and durable untracked files for stronger local recovery than a normal Save Point.",
            ConfirmationMessage = "Create a checkpoint now?",
        });

        if (!confirmed)
        {
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Cancelled." });
            DetailSummary = "Checkpoint creation cancelled.";
            return;
        }

        await RunWorkspaceOperationAsync(
            "Create Checkpoint",
            "Creating checkpoint...",
            async (rootPath, snapshot, sink) =>
            {
                var result = await _desktopShellService.CreateCheckpointAsync(rootPath, snapshot, sink);
                DetailItems.Clear();
                DetailItems.Add(new DetailItemViewModel("Checkpoint", result.Checkpoint.Id));
                DetailItems.Add(new DetailItemViewModel("Branch", string.IsNullOrWhiteSpace(result.Checkpoint.CurrentBranch) ? "Unavailable" : result.Checkpoint.CurrentBranch));
                DetailItems.Add(new DetailItemViewModel("Commit", string.IsNullOrWhiteSpace(result.Checkpoint.CurrentCommitSha) ? "Unavailable" : result.Checkpoint.CurrentCommitSha));
                DetailItems.Add(new DetailItemViewModel("Untracked files", result.Checkpoint.UntrackedFiles.Count.ToString(CultureInfo.InvariantCulture)));
                return new WorkspaceOperationResult
                {
                    Snapshot = result.Snapshot,
                    Message = result.Message,
                    Transcript = result.Transcript,
                };
            },
            preserveExistingTranscript: true);
    }

    private async Task BackupWorkspaceAsync()
    {
        if (SelectedWorkspace is null || _interactionService is null)
        {
            return;
        }

        StartOperationTranscript("Backup", SelectedWorkspace.Name);
        AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Preparing backup..." });
        DetailSummary = "Preparing backup...";

        var suggestedFileName = BuildBackupArchiveFileName(SelectedWorkspace);
        var archivePath = await _interactionService.ShowBackupDestinationDialogAsync(suggestedFileName);
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Cancelled." });
            DetailSummary = "Backup cancelled.";
            return;
        }

        if (!archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            archivePath += ".zip";
        }

        var backupFailed = false;
        try
        {
            _isWorkspaceActionRunning = true;
            _workspaceActionStatusMessage = "Creating backup archive...";
            RaiseWorkspaceActionCommandStates();
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Creating backup archive..." });
            DetailSummary = "Creating backup archive...";
            var result = await _desktopShellService.BackupWorkspaceAsync(SelectedWorkspace.RootPath, archivePath, SelectedWorkspace.Snapshot, new OperationTranscriptSink(this));
            FlushPendingOperationLogToUi(forceDrainAll: true);
            ReplaceSelectedWorkspace(result.Snapshot);
            CompleteOperationTranscript(result.Transcript);
            _workspaceActionStatusMessage = result.Message;
            DetailSummary = result.Message;
            DetailItems.Clear();
            DetailItems.Add(new DetailItemViewModel("Archive", result.Export.ArchivePath));
            DetailItems.Add(new DetailItemViewModel("Included files", result.Export.FileCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            DetailItems.Add(new DetailItemViewModel("Archive size", WorkspaceBackupExportService.FormatSize(result.Export.ArchiveSizeBytes)));
            DetailItems.Add(new DetailItemViewModel("Excluded entries", result.Export.ExcludedEntries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            DetailItems.Add(new DetailItemViewModel("Manifest", result.Manifest.ManifestPath));
            DetailItems.Add(new DetailItemViewModel("Manifest warnings", result.Manifest.WarningCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (result.Export.Warnings.Count > 0)
            {
                DetailItems.Add(new DetailItemViewModel("Warnings", string.Join(Environment.NewLine, result.Export.Warnings)));
            }

            RefreshDetailActions();
        }
        catch (Exception exception)
        {
            backupFailed = true;
            _workspaceActionStatusMessage = exception.Message;
            SelectedWorkspace?.SetOperationFailureState(exception.Message, "Backup");
            DetailSummary = exception.Message;
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.StandardError, Text = exception.Message });
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Failed." });
            throw;
        }
        finally
        {
            _isWorkspaceActionRunning = false;
            FlushPendingOperationLogToUi(forceDrainAll: true);
            RaiseWorkspaceActionCommandStates();
            if (backupFailed)
            {
                UpdateDetailPanel();
            }
            else
            {
                RefreshDetailActions();
            }
        }
    }

    private async Task PublishWorkspaceAsync()
    {
        if (SelectedWorkspace is null || _interactionService is null)
        {
            return;
        }

        StartOperationTranscript("Publish", SelectedWorkspace.Name);
        AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Preparing publish..." });
        DetailSummary = "Preparing publish...";

        WorkspacePublishAssessment assessment;
        var publishFailed = false;
        try
        {
            assessment = await _desktopShellService.AssessWorkspacePublishAsync(SelectedWorkspace.RootPath, SelectedWorkspace.Snapshot, new OperationTranscriptSink(this));
            FlushPendingOperationLogToUi(forceDrainAll: true);
        }
        catch (Exception exception)
        {
            SelectedWorkspace.SetOperationFailureState(exception.Message, "Publish");
            DetailSummary = exception.Message;
            throw;
        }

        ApplyPublishAssessmentDetails(assessment);
        if (assessment.IsBlocked)
        {
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.StandardError, Text = assessment.Summary });
            DetailSummary = assessment.Summary;
            return;
        }

        if (assessment.RequiresConfirmation && !await _interactionService.ConfirmPublishAsync(assessment))
        {
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Cancelled." });
            DetailSummary = "Publish cancelled.";
            return;
        }

        try
        {
            _isWorkspaceActionRunning = true;
            _workspaceActionStatusMessage = "Publishing Working Copy...";
            RaiseWorkspaceActionCommandStates();
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Publishing Working Copy..." });
            DetailSummary = "Publishing Working Copy...";
            var result = await _desktopShellService.PublishWorkspaceAsync(SelectedWorkspace.RootPath, SelectedWorkspace.Snapshot, new OperationTranscriptSink(this));
            FlushPendingOperationLogToUi(forceDrainAll: true);
            ReplaceSelectedWorkspace(result.Snapshot);
            CompleteOperationTranscript(result.Transcript);
            _workspaceActionStatusMessage = result.Message;
            DetailSummary = result.Message;
            ApplyPublishResultDetails(result);
            RefreshDetailActions();
        }
        catch (Exception exception)
        {
            publishFailed = true;
            _workspaceActionStatusMessage = exception.Message;
            SelectedWorkspace.SetOperationFailureState(exception.Message, "Publish");
            DetailSummary = exception.Message;
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.StandardError, Text = exception.Message });
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Failed." });
            throw;
        }
        finally
        {
            _isWorkspaceActionRunning = false;
            FlushPendingOperationLogToUi(forceDrainAll: true);
            RaiseWorkspaceActionCommandStates();
            if (publishFailed)
            {
                UpdateDetailPanel();
            }
            else
            {
                RefreshDetailActions();
            }
        }
    }

    private async Task RemoveWorkspaceAsync()
    {
        if (SelectedWorkspace is null || _interactionService is null)
        {
            return;
        }

        var selectedWorkspace = SelectedWorkspace;
        var removedRootPath = selectedWorkspace.RootPath;

        var prompt = new WorkspaceRemovalPrompt
        {
            WorkspaceName = selectedWorkspace.Name,
            WorkspaceRoot = selectedWorkspace.RootPath,
            DeleteWorkspaceFilesSupported = false,
            DeleteWorkspaceFilesUnavailableReason = DeleteWorkspaceFilesUnavailableMessage,
        };

        StartOperationTranscript("Remove", selectedWorkspace.Name);
        AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Preparing removal..." });
        DetailSummary = "Preparing removal...";
        try
        {
            var decision = await _interactionService.ConfirmRemoveWorkspaceAsync(prompt);
            if (decision is null)
            {
                AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Cancelled." });
                DetailSummary = "Workspace removal cancelled.";
                return;
            }

            if (decision.Choice == WorkspaceRemovalChoice.DeleteFiles)
            {
                throw new InvalidOperationException(DeleteWorkspaceFilesUnavailableMessage);
            }

            _isWorkspaceActionRunning = true;
            _workspaceActionStatusMessage = decision.Choice switch
            {
                WorkspaceRemovalChoice.DeleteFiles => "Deleting workspace files...",
                WorkspaceRemovalChoice.DockerResources => "Removing Docker resources...",
                _ => "Removing workspace from list...",
            };
            RaiseWorkspaceActionCommandStates();
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = _workspaceActionStatusMessage });
            DetailSummary = _workspaceActionStatusMessage;
            var result = await _desktopShellService.RemoveWorkspaceAsync(removedRootPath, decision.Choice, selectedWorkspace.Snapshot, new OperationTranscriptSink(this));
            FlushPendingOperationLogToUi(forceDrainAll: true);
            CompleteOperationTranscript(result.Transcript);
            _workspaceActionStatusMessage = result.Message;
            RemoveWorkspaceFromList(removedRootPath);
            DetailSummary = result.Message;
            if (result.Removal.Warnings.Count > 0)
            {
                DetailItems.Clear();
                DetailItems.Add(new DetailItemViewModel("Warnings", string.Join(Environment.NewLine, result.Removal.Warnings)));
            }
        }
        catch (Exception exception)
        {
            Services.StartupLog.WriteGlobalException("Workspace operation 'Remove' failed", exception);
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.StandardError, Text = exception.Message });
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Failed." });
            _workspaceActionStatusMessage = exception.Message;
            selectedWorkspace.SetOperationFailureState(exception.Message, "Remove");
            DetailSummary = exception.Message;
        }
        finally
        {
            _isWorkspaceActionRunning = false;
            FlushPendingOperationLogToUi(forceDrainAll: true);
            RaiseWorkspaceActionCommandStates();
            RefreshDetailActions();
        }
    }

    private async Task StartSelectedWorkspaceAsync()
    {
        if (!await ConfirmOracleSoftwareNoticeIfRequiredAsync(SelectedWorkspace?.Snapshot))
        {
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Cancelled." });
            DetailSummary = "Workspace start cancelled.";
            return;
        }

        await RunWorkspaceOperationAsync(
            "Start",
            "Starting workspace...",
            (rootPath, snapshot, sink) => _desktopShellService.StartWorkspaceAsync(rootPath, snapshot, sink));
    }

    private async Task RecoverSelectedWorkspaceAsync()
    {
        if (SelectedWorkspace is null || _interactionService is null)
        {
            return;
        }

        StartOperationTranscript("Recover", SelectedWorkspace.Name);
        AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Assessing recovery..." });
        DetailSummary = "Assessing recovery...";

        var assessment = new WorkspaceRecoveryAssessment
        {
            Title = "Recover Workspace",
            Summary = "Checking current state...",
            Findings = Array.Empty<string>(),
            ConfirmationMessage = "Run workspace recovery now?",
            WorkspaceName = SelectedWorkspace.Name,
            StatusSummary = "Checking current state...",
        };

        if (!await _interactionService.ConfirmRecoveryAsync(
                assessment,
                token => _desktopShellService.AssessWorkspaceRecoveryAsync(SelectedWorkspace.RootPath, null, token)))
        {
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Cancelled." });
            DetailSummary = "Recovery cancelled.";
            return;
        }

        await RunWorkspaceOperationAsync(
            "Recover",
            "Recovering workspace...",
            (rootPath, snapshot, sink) => _desktopShellService.RecoverWorkspaceAsync(rootPath, snapshot, sink));
    }

    private async Task ResetRuntimeSelectedWorkspaceAsync()
    {
        if (SelectedWorkspace is null || _interactionService is null)
        {
            return;
        }

        var currentSnapshot = SelectedWorkspace.Snapshot;
        if (currentSnapshot is null)
        {
            return;
        }

        var prompt = _desktopShellService.BuildRuntimeResetPrompt(currentSnapshot);
        if (!await _interactionService.ConfirmResetRuntimeAsync(prompt))
        {
            return;
        }

        await RunWorkspaceOperationAsync(
            "Reset Runtime",
            "Resetting runtime...",
            (rootPath, snapshot, sink) => _desktopShellService.ResetRuntimeAsync(rootPath, snapshot, sink));
    }

    private async Task AttachSelectedWorkspaceAsync()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        StartOperationTranscript("Attach", SelectedWorkspace.Name);
        AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Preparing attach..." });
        DetailSummary = "Preparing attach...";

        try
        {
            await RunWorkspaceOperationAsync(
                "Attach",
                "Preparing attach...",
                (rootPath, snapshot, sink) => _desktopShellService.AttachWorkspaceAsync(rootPath, snapshot, sink),
                preserveExistingTranscript: true);
        }
        catch
        {
        }
    }

    private async Task ReprovisionSelectedWorkspaceAsync()
    {
        if (!CanReprovisionSelectedWorkspace() || SelectedWorkspace is null)
        {
            return;
        }

        if (!await ConfirmOracleSoftwareNoticeIfRequiredAsync(SelectedWorkspace.Snapshot))
        {
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Cancelled." });
            DetailSummary = "Workspace reprovision cancelled.";
            return;
        }

        try
        {
            IsReprovisioning = true;
            ReprovisionStatusMessage = "Starting reprovision...";
            StartOperationTranscript("Reprovision", SelectedWorkspace.Name);
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = $"Starting reprovision for {SelectedWorkspace.Name}..." });
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Loading current workspace state..." });
            SelectedWorkspace.SetReprovisioningState("Reprovisioning workspace... Generating runtime files...");
            UpdateDetailPanel();
            DetailSummary = "Reprovisioning workspace... Generating runtime files...";

            var result = await _desktopShellService.ReprovisionWorkspaceAsync(
                SelectedWorkspace.RootPath,
                SelectedWorkspace.Snapshot,
                new OperationTranscriptSink(this));

            FlushPendingOperationLogToUi(forceDrainAll: true);
            ReplaceSelectedWorkspace(result.Snapshot);
            ReprovisionStatusMessage = result.Message;
            CompleteOperationTranscript(result.Transcript);
            DetailSummary = result.Message;
        }
        catch (Exception exception)
        {
            var selectedWorkspaceRootPath = SelectedWorkspace?.RootPath ?? string.Empty;
            ReprovisionStatusMessage = exception.Message;
            SelectedWorkspace?.SetOperationFailureState(exception.Message, "Reprovision");
            DetailSummary = exception.Message;
            DetailItems.Clear();
            DetailItems.Add(new DetailItemViewModel("Root path", selectedWorkspaceRootPath));
        }
        finally
        {
            IsReprovisioning = false;
            FlushPendingOperationLogToUi(forceDrainAll: true);
            ReprovisionWorkspaceCommand.RaiseCanExecuteChanged();
            RetryWorkspaceCommand.RaiseCanExecuteChanged();
            UpdateDetailPanel();
        }
    }

    public void SetClipboardService(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
        CopyOperationLogCommand.RaiseCanExecuteChanged();
    }

    public void SetInteractionService(IWorkspaceInteractionService interactionService)
    {
        _interactionService = interactionService;
        CreateWorkspaceCommand.RaiseCanExecuteChanged();
        OpenExistingRepositoryCommand.RaiseCanExecuteChanged();
        RaiseWorkspaceActionCommandStates();
        UpdateDetailPanel();
    }

    private async Task<bool> ConfirmOracleSoftwareNoticeIfRequiredAsync(OpenCode.Workspace.Core.Models.TemplateManifest template, string workspaceName)
    {
        if (_interactionService is null)
        {
            return true;
        }

        var prompt = _desktopShellService.BuildOracleSoftwareNotice(template, workspaceName);
        return prompt is null || await _interactionService.ConfirmOracleSoftwareNoticeAsync(prompt);
    }

    private async Task<bool> ConfirmOracleSoftwareNoticeIfRequiredAsync(OpenCode.Workspace.Core.Models.WorkspaceSnapshot? snapshot)
    {
        if (_interactionService is null || snapshot is null)
        {
            return true;
        }

        var prompt = _desktopShellService.BuildOracleSoftwareNotice(snapshot);
        if (prompt is null || snapshot.Record.OracleSoftwareNoticeShown)
        {
            return true;
        }

        var confirmed = await _interactionService.ConfirmOracleSoftwareNoticeAsync(prompt);
        if (!confirmed)
        {
            return false;
        }

        var updated = await _desktopShellService.AcknowledgeOracleSoftwareNoticeAsync(snapshot.Paths.RootPath, snapshot);
        ReplaceSelectedWorkspace(updated);
        return true;
    }

    private void ToggleOperationLogVisibility()
    {
        IsOperationLogVisible = !IsOperationLogVisible;
    }

    public void AppendOperationTranscriptLine(OperationTranscriptLine line)
    {
        EnsureActiveOperationTranscript("Workspace operation", SelectedWorkspace?.Name ?? string.Empty, line.Timestamp);
        AppendOperationTranscriptLineCore(line, flushImmediately: true);
    }

    private void StartOperationTranscript(string operationName, string workspaceName)
    {
        ResetOperationTranscriptState();
        LastOperationTranscript = new OperationTranscript
        {
            OperationName = operationName,
            WorkspaceName = workspaceName,
            StartedUtc = DateTimeOffset.UtcNow,
        };
        EnsureOperationLogFlushTimerRunning();
    }

    private async Task CopyOperationLogAsync()
    {
        if (_clipboardService is null || !HasOperationLog)
        {
            return;
        }

        await _clipboardService.SetTextAsync(await GetCopyAllOperationLogTextAsync());
    }

    private void ClearOperationLog()
    {
        ResetOperationTranscriptState();
        LastOperationTranscript = null;
    }

    public string GetCopyAllOperationLogText()
    {
        FlushPendingOperationLogToUi(forceDrainAll: true);
        return _operationTranscriptBuffer?.ReadAllText() ?? string.Empty;
    }

    internal Task<string> GetCopyAllOperationLogTextAsync()
        => Task.FromResult(GetCopyAllOperationLogText());

    internal void FlushPendingOperationLogForTesting(bool forceDrainAll = false)
        => FlushPendingOperationLogToUi(forceDrainAll);

    internal int PendingOperationLogLineCountForTesting
        => _operationTranscriptBuffer?.PendingLineCount ?? 0;

    internal int VisibleOperationLogLineCountForTesting
        => _visibleOperationLogLines.Count;

    internal string? OperationTranscriptFilePathForTesting
        => _operationTranscriptBuffer?.TranscriptFilePath;

    internal string? CurrentOperationTranscriptFilePath
        => _operationTranscriptBuffer?.TranscriptFilePath;

    internal bool HasActiveWorkspaceOperation
        => _isWorkspaceActionRunning || IsReprovisioning;

    internal string CurrentWorkspaceOperationName
        => IsReprovisioning ? "Reprovision" : LastOperationTranscript?.OperationName ?? string.Empty;

    internal string CurrentWorkspaceOperationStatus
        => IsReprovisioning
            ? ReprovisionStatusMessage
            : string.IsNullOrWhiteSpace(_workspaceActionStatusMessage)
                ? DetailSummary
                : _workspaceActionStatusMessage;

    internal void StartOperationTranscriptForTesting(string operationName, string workspaceName)
        => StartOperationTranscript(operationName, workspaceName);

    private void CompleteOperationTranscript(OperationTranscript transcript)
    {
        if (LastOperationTranscript is null)
        {
            LastOperationTranscript = new OperationTranscript
            {
                OperationName = transcript.OperationName,
                WorkspaceName = transcript.WorkspaceName,
                StartedUtc = transcript.StartedUtc,
                CompletedUtc = transcript.CompletedUtc,
                Succeeded = transcript.Succeeded,
            };
        }

        LastOperationTranscript.CompletedUtc = transcript.CompletedUtc;
        LastOperationTranscript.Succeeded = transcript.Succeeded;
    }

    private void EnsureActiveOperationTranscript(string operationName, string workspaceName, DateTimeOffset startedUtc)
    {
        if (LastOperationTranscript is not null)
        {
            return;
        }

        LastOperationTranscript = new OperationTranscript
        {
            OperationName = operationName,
            WorkspaceName = workspaceName,
            StartedUtc = startedUtc,
        };

        EnsureOperationLogFlushTimerRunning();
    }

    private void AppendOperationTranscriptLineCore(OperationTranscriptLine line, bool flushImmediately)
    {
        EnsureActiveOperationTranscript("Workspace operation", SelectedWorkspace?.Name ?? string.Empty, line.Timestamp);
        EnsureTranscriptBuffer();
        _operationTranscriptBuffer!.Append(line, TryGetBufferedStatusText(line));

        if (flushImmediately)
        {
            FlushPendingOperationLogToUi(forceDrainAll: true);
        }
    }

    private void EnsureTranscriptBuffer()
    {
        if (_operationTranscriptBuffer is not null)
        {
            return;
        }

        var operationName = LastOperationTranscript?.OperationName ?? "Workspace operation";
        var workspaceName = LastOperationTranscript?.WorkspaceName ?? SelectedWorkspace?.Name ?? string.Empty;
        _operationTranscriptBuffer = new TranscriptBuffer(operationName, workspaceName, FormatOperationTranscriptLine);
    }

    private void EnsureOperationLogFlushTimerRunning()
    {
        _operationLogFlushTimer.Interval = NormalOperationLogFlushInterval;
        if (!_operationLogFlushTimer.IsEnabled)
        {
            _operationLogFlushTimer.Start();
        }
    }

    private void FlushPendingOperationLogToUi(bool forceDrainAll = false)
    {
        if (_operationTranscriptBuffer is null)
        {
            UpdateOperationLogFlushTimerState(0);
            return;
        }

        var pendingLineCount = _operationTranscriptBuffer.PendingLineCount;
        if (pendingLineCount == 0 && !forceDrainAll)
        {
            UpdateOperationLogFlushTimerState(0);
            return;
        }

        var maxLines = forceDrainAll ? int.MaxValue : DetermineOperationLogFlushBatchSize(pendingLineCount);
        var batch = _operationTranscriptBuffer.DrainPendingLines(maxLines);
        if (batch.Lines.Count == 0)
        {
            ApplyBufferedStatus(batch.LatestStatusText);
            UpdateOperationLogFlushTimerState(batch.RemainingPendingLineCount);
            return;
        }

        foreach (var bufferedLine in batch.Lines)
        {
            _visibleOperationTranscriptLines.Add(bufferedLine.Line);
            _visibleOperationLogLines.Add(bufferedLine.FormattedText);
        }

        TrimVisibleOperationLogTail();

        if (LastOperationTranscript is not null)
        {
            LastOperationTranscript.Lines.Clear();
            LastOperationTranscript.Lines.AddRange(_visibleOperationTranscriptLines);
        }

        OperationLogText = string.Join(Environment.NewLine, _visibleOperationLogLines);
        ApplyBufferedStatus(batch.LatestStatusText);
        UpdateOperationLogFlushTimerState(batch.RemainingPendingLineCount);
    }

    private void ApplyBufferedStatus(string? latestStatusText)
    {
        if (string.IsNullOrWhiteSpace(latestStatusText) || (!_isWorkspaceActionRunning && !IsReprovisioning))
        {
            return;
        }

        ApplyImmediateBufferedStatus(latestStatusText);
    }

    private void ApplyImmediateBufferedStatus(string latestStatusText)
    {
        _workspaceActionStatusMessage = latestStatusText;
        DetailSummary = latestStatusText;

        if (IsReprovisioning)
        {
            ReprovisionStatusMessage = latestStatusText;
            SelectedWorkspace?.SetReprovisioningState(latestStatusText);
        }

        RefreshWorkspacePresentations();
        if (SelectedWorkspace is not null)
        {
            UpdateDetailPanel();
        }
    }

    private void TrimVisibleOperationLogTail()
    {
        var overflow = _visibleOperationLogLines.Count - VisibleOperationLogLineLimit;
        if (overflow <= 0)
        {
            return;
        }

        _visibleOperationLogLines.RemoveRange(0, overflow);
        _visibleOperationTranscriptLines.RemoveRange(0, overflow);
    }

    private void UpdateOperationLogFlushTimerState(int remainingPendingLineCount)
    {
        if (remainingPendingLineCount <= 0 && !_isWorkspaceActionRunning && !IsReprovisioning)
        {
            _operationLogFlushTimer.Stop();
            _operationLogFlushTimer.Interval = NormalOperationLogFlushInterval;
            return;
        }

        _operationLogFlushTimer.Interval = DetermineOperationLogFlushInterval(remainingPendingLineCount);
        if (!_operationLogFlushTimer.IsEnabled)
        {
            _operationLogFlushTimer.Start();
        }
    }

    private void ResetOperationTranscriptState()
    {
        _operationLogFlushTimer.Stop();
        _operationTranscriptBuffer?.DeleteFile();
        _operationTranscriptBuffer = null;
        _visibleOperationTranscriptLines.Clear();
        _visibleOperationLogLines.Clear();
        OperationLogText = string.Empty;
        IsOperationLogVisible = false;
    }

    private static int DetermineOperationLogFlushBatchSize(int pendingLineCount)
        => pendingLineCount switch
        {
            > 5000 => 1800,
            > 1500 => 1200,
            > 300 => 800,
            _ => NormalOperationLogFlushBatchSize,
        };

    private static TimeSpan DetermineOperationLogFlushInterval(int pendingLineCount)
        => pendingLineCount switch
        {
            > 5000 => HeavyOperationLogFlushInterval,
            > 1500 => MediumOperationLogFlushInterval,
            _ => NormalOperationLogFlushInterval,
        };

    private static string? TryGetBufferedStatusText(OperationTranscriptLine line)
    {
        var stageText = TryMapOracleStageText(line.Text);
        if (!string.IsNullOrWhiteSpace(stageText))
        {
            return stageText;
        }

        return line.Kind is OperationTranscriptLineKind.Status or OperationTranscriptLineKind.Comment
            ? line.Text
            : null;
    }

    private static string? TryMapOracleStageText(string text)
    {
        var markerIndex = text.IndexOf("Stage:", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var stage = text[(markerIndex + "Stage:".Length)..].Trim();
        if (string.IsNullOrWhiteSpace(stage))
        {
            return null;
        }

        return stage switch
        {
            "Installing APEX" or "Install APEX" => "Installing APEX...",
            "Installing ORDS" or "Configure ORDS" => "Configuring ORDS...",
            "Running Validation" or "Final verification" => "Validating workspace...",
            "Configuring Workspace" or "Workspace configuration" => "Configuring workspace...",
            "Ready" => "Workspace ready.",
            _ => stage.EndsWith("...", StringComparison.Ordinal) ? stage : $"{stage}...",
        };
    }

    private void UpdateDetailPanel()
    {
        DetailItems.Clear();

        if (SelectedWorkspace is null)
        {
            DetailPrimaryAction = null;
            DetailActions.Clear();
            DetailServices.Clear();
            DetailAdvancedActions.Clear();
            DetailRecommendation = string.Empty;
            ShowAdvancedActions = false;
            DetailTitle = "No workspace selected";
            DetailSummary = "Select a workspace to inspect repository and runtime details.";
            return;
        }

        DetailTitle = SelectedWorkspace.Name;
        var failureGuidance = TryBuildFailureGuidance(SelectedWorkspace);
        var provisioningHealth = SelectedWorkspace.Record.LastProvisioningHealth;
        var presentation = BuildWorkspacePresentation(SelectedWorkspace, useWorkspaceScopedCommands: false);
        DetailSummary = SelectedWorkspace.HasTransientOperationFailure && !HasActiveWorkspaceOperation
            ? SummarizeTransientOperationMessage(SelectedWorkspace.LastActivity)
            : presentation.Summary;
        DetailRecommendation = presentation.Recommendation;
        DetailServices.Clear();
        DetailItems.Add(new DetailItemViewModel("Workspace", presentation.CurrentStatus));
        DetailItems.Add(new DetailItemViewModel("Current Activity", presentation.CurrentActivity));
        DetailItems.Add(new DetailItemViewModel("Activity Summary", presentation.ActivitySummary));
        if (!string.IsNullOrWhiteSpace(presentation.CapabilitiesSummary))
        {
            DetailItems.Add(new DetailItemViewModel("Capabilities", presentation.CapabilitiesSummary));
        }

        if (!string.IsNullOrWhiteSpace(presentation.ApplicationsSummary))
        {
            DetailItems.Add(new DetailItemViewModel("Applications", presentation.ApplicationsSummary));
        }

        DetailItems.Add(new DetailItemViewModel("Runtime", SelectedWorkspace.RuntimeSummary));
        DetailItems.Add(new DetailItemViewModel("Git", SelectedWorkspace.RepositoryStatus));
        DetailItems.Add(new DetailItemViewModel("Current branch", SelectedWorkspace.CurrentBranch));
        DetailItems.Add(new DetailItemViewModel("Runtime-state status", SelectedWorkspace.LocalRuntimeStateStatus));
        if (SelectedWorkspace.Snapshot?.LocalRuntimeState?.Resources.Ports.Count > 0)
        {
            DetailItems.Add(new DetailItemViewModel("Resources", FormatManagedResources(SelectedWorkspace.Snapshot.LocalRuntimeState.Resources.Ports)));
        }
        DetailItems.Add(new DetailItemViewModel("Services", SelectedWorkspace.Services));
        DetailItems.Add(new DetailItemViewModel("Protection state", SelectedWorkspace.ProtectionLabel));
        DetailItems.Add(new DetailItemViewModel("Root path", SelectedWorkspace.RootPath));
        DetailItems.Add(new DetailItemViewModel("Repository path", SelectedWorkspace.RepositoryPath));
        DetailItems.Add(new DetailItemViewModel("Features", SelectedWorkspace.Features));
        DetailItems.Add(new DetailItemViewModel("Runtime target", SelectedWorkspace.RuntimeTarget));
        if (!string.IsNullOrWhiteSpace(presentation.RecentHistoryNote))
        {
            DetailItems.Add(new DetailItemViewModel("Recent History", presentation.RecentHistoryNote));
        }
        if (SelectedWorkspace.Health is not null)
        {
            foreach (var provider in SelectedWorkspace.Health.Providers)
            {
                DetailItems.Add(new DetailItemViewModel($"Provider Health: {provider.DisplayName}", provider.Status.ToString()));
                if (!string.IsNullOrWhiteSpace(provider.WorkspaceImpact))
                {
                    DetailItems.Add(new DetailItemViewModel($"Provider Details: {provider.DisplayName}", provider.WorkspaceImpact));
                }
            }

            foreach (var service in SelectedWorkspace.Health.Services)
            {
                DetailServices.Add(BuildServiceHealthRow(service));
            }
        }

        if (!string.IsNullOrWhiteSpace(DetailRecommendation))
        {
            DetailItems.Add(new DetailItemViewModel("Recommendation", DetailRecommendation));
        }
        if (SelectedWorkspace.HasError)
        {
            DetailItems.Add(new DetailItemViewModel("Load failure", SelectedWorkspace.ErrorMessage));
        }

        if (failureGuidance is not null && ShouldShowFailureEvidence(SelectedWorkspace))
        {
            var lastRepairAttempt = provisioningHealth?.RepairHistory.LastOrDefault();
            if (!string.IsNullOrWhiteSpace(provisioningHealth?.Stage ?? failureGuidance.Stage))
            {
                DetailItems.Add(new DetailItemViewModel("Current diagnosis stage", provisioningHealth?.Stage ?? failureGuidance.Stage));
            }

            DetailItems.Add(new DetailItemViewModel("Current diagnosis", failureGuidance.Reason));
            if (!string.IsNullOrWhiteSpace(provisioningHealth?.Evidence ?? failureGuidance.Evidence))
            {
                DetailItems.Add(new DetailItemViewModel("Evidence", provisioningHealth?.Evidence ?? failureGuidance.Evidence));
            }

            if (!string.IsNullOrWhiteSpace(provisioningHealth?.Repairability ?? failureGuidance.Repairability))
            {
                DetailItems.Add(new DetailItemViewModel("Repairability", provisioningHealth?.Repairability ?? failureGuidance.Repairability));
            }

            if (!string.IsNullOrWhiteSpace(provisioningHealth?.ProblemScope))
            {
                DetailItems.Add(new DetailItemViewModel("Problem scope", provisioningHealth.ProblemScope));
            }

            if (!string.IsNullOrWhiteSpace(provisioningHealth?.Confidence ?? failureGuidance.Confidence))
            {
                DetailItems.Add(new DetailItemViewModel("Confidence", provisioningHealth?.Confidence ?? failureGuidance.Confidence));
            }

            if (!string.IsNullOrWhiteSpace(provisioningHealth?.EstimatedDuration ?? failureGuidance.EstimatedDuration))
            {
                DetailItems.Add(new DetailItemViewModel("Estimated duration", provisioningHealth?.EstimatedDuration ?? failureGuidance.EstimatedDuration));
            }

            if (lastRepairAttempt is not null)
            {
                DetailItems.Add(new DetailItemViewModel("Repair attempted", lastRepairAttempt.RepairType));
                DetailItems.Add(new DetailItemViewModel("Outcome", FormatRepairOutcome(lastRepairAttempt.Result)));
                if (!lastRepairAttempt.RootCauseChanged && !string.IsNullOrWhiteSpace(lastRepairAttempt.EvidenceAfter))
                {
                    DetailItems.Add(new DetailItemViewModel("Root cause comparison", $"Unchanged: {lastRepairAttempt.EvidenceAfter}"));
                }
            }

            if (!string.IsNullOrWhiteSpace(provisioningHealth?.PreviousRecommendedAction))
            {
                DetailItems.Add(new DetailItemViewModel("Previous recommendation", provisioningHealth.PreviousRecommendedAction));
            }

            DetailItems.Add(new DetailItemViewModel("Detailed recommendation", failureGuidance.RecommendedAction));
        }

        ApplyDetailPresentation(presentation);
    }

    private void ApplyDetailPresentation(WorkspacePresentation presentation)
    {
        DetailActions.Clear();
        DetailVisibleActions.Clear();
        DetailAdvancedActions.Clear();
        DetailPrimaryAction = null;

        DetailPrimaryAction = presentation.PrimaryAction;
        if (presentation.PrimaryAction is not null)
        {
            DetailActions.Add(presentation.PrimaryAction);
        }

        foreach (var action in presentation.SecondaryActions)
        {
            DetailVisibleActions.Add(action);
            DetailActions.Add(action);
        }

        foreach (var action in presentation.AdvancedActions)
        {
            DetailActions.Add(action);
            DetailAdvancedActions.Add(action);

            if (string.Equals(action.Label, "Start Only", StringComparison.Ordinal))
            {
                DetailActions.Add(new ActionItemViewModel("Start", action.Description, action.IsEnabled, action.DisabledReason, action.Command));
            }

            if (string.Equals(action.Label, "Attach Only", StringComparison.Ordinal))
            {
                DetailActions.Add(new ActionItemViewModel("Attach", action.Description, action.IsEnabled, action.DisabledReason, action.Command));
            }
        }

        if (DetailAdvancedActions.Count == 0)
        {
            ShowAdvancedActions = false;
        }

        RaisePropertyChanged(nameof(HasDetailAdvancedActions));
        RaisePropertyChanged(nameof(SelectedWorkspace));
    }

    private void RefreshDetailActions()
    {
        if (SelectedWorkspace is null)
        {
            DetailPrimaryAction = null;
            DetailActions.Clear();
            DetailServices.Clear();
            DetailVisibleActions.Clear();
            DetailAdvancedActions.Clear();
            ShowAdvancedActions = false;
            return;
        }

        ApplyDetailPresentation(BuildWorkspacePresentation(SelectedWorkspace, useWorkspaceScopedCommands: false));
    }

    private void RefreshWorkspacePresentations()
    {
        foreach (var workspace in Workspaces)
        {
            ApplyWorkspacePresentationToSummary(workspace);
        }
    }

    private void ApplyWorkspacePresentationToSummary(WorkspaceSummaryViewModel workspace)
        => workspace.ApplyPresentation(BuildWorkspacePresentation(workspace, useWorkspaceScopedCommands: true));

    private WorkspacePresentation BuildWorkspacePresentation(WorkspaceSummaryViewModel workspace, bool useWorkspaceScopedCommands)
    {
        var failureGuidance = TryBuildFailureGuidance(workspace);
        var openWorkspaceAction = CreatePresentationAction(workspace, "Open Workspace", BuildOpenDescription(workspace), CanStartWorkspace(workspace), GetOpenDisabledReason(workspace), OpenSelectedWorkspaceAsync, useWorkspaceScopedCommands);
        var openDevelopmentShellAction = CreatePresentationAction(workspace, "Open Development Shell", BuildOpenDevelopmentShellDescription(workspace), CanStartWorkspace(workspace), GetOpenDisabledReason(workspace), OpenSelectedWorkspaceAsync, useWorkspaceScopedCommands);
        var rebuildRuntimeAction = CreatePresentationAction(workspace, "Rebuild Runtime", BuildResetRuntimeDescription(workspace), CanResetRuntimeWorkspace(workspace), GetResetRuntimeDisabledReason(workspace), ResetRuntimeSelectedWorkspaceAsync, useWorkspaceScopedCommands);
        var investigateProblemAction = CreatePresentationAction(workspace, "Troubleshoot Workspace", BuildInvestigateProblemDescription(workspace), CanTroubleshootWorkspace(workspace), GetTroubleshootDisabledReason(workspace), TroubleshootWorkspaceInternalAsync, useWorkspaceScopedCommands);
        var openFolderAction = CreatePresentationAction(workspace, "Open Folder", "Open the workspace folder with the host shell.", true, string.Empty, OpenSelectedWorkspaceFolderAsync, useWorkspaceScopedCommands);
        var advancedActions = new List<ActionItemViewModel>
        {
            rebuildRuntimeAction,
            CreatePresentationAction(workspace, "Start Only", BuildStartDescription(workspace), CanStartWorkspace(workspace), GetStartDisabledReason(workspace), StartSelectedWorkspaceAsync, useWorkspaceScopedCommands),
            CreatePresentationAction(workspace, "Attach Only", BuildAttachDescription(workspace), CanAttachWorkspace(workspace), GetAttachDisabledReason(workspace), AttachSelectedWorkspaceAsync, useWorkspaceScopedCommands),
            CreatePresentationAction(workspace, "Save Point", BuildSavePointDescription(workspace), CanCreateSavePointWorkspace(workspace), GetSavePointDisabledReason(workspace), CreateSavePointAsync, useWorkspaceScopedCommands),
            CreatePresentationAction(workspace, "Checkpoint", BuildCheckpointDescription(workspace), CanCreateCheckpointWorkspace(workspace), GetCheckpointDisabledReason(workspace), CreateCheckpointAsync, useWorkspaceScopedCommands),
            CreatePresentationAction(workspace, "Backup", BuildBackupDescription(workspace), CanBackupWorkspace(workspace), GetBackupDisabledReason(workspace), BackupWorkspaceAsync, useWorkspaceScopedCommands),
            CreatePresentationAction(workspace, "Publish", BuildPublishDescription(workspace), CanPublishWorkspace(workspace), GetPublishDisabledReason(workspace), PublishWorkspaceAsync, useWorkspaceScopedCommands),
            CreatePresentationAction(workspace, "Remove", BuildRemoveDescription(workspace), CanRemoveWorkspace(workspace), GetRemoveDisabledReason(workspace), RemoveWorkspaceAsync, useWorkspaceScopedCommands),
        };

        if (failureGuidance?.CanRetry == true)
        {
            advancedActions.Insert(0, CreatePresentationAction(workspace, "Retry", BuildRetryDescription(workspace), CanRetryWorkspace(workspace), GetRetryDisabledReason(workspace), RetrySelectedWorkspaceAsync, useWorkspaceScopedCommands));
        }

        return WorkspaceHealthAggregator.BuildPresentation(
            workspace,
            isOperationInProgress: ReferenceEquals(workspace, SelectedWorkspace) && HasActiveWorkspaceOperation,
            currentOperationName: ReferenceEquals(workspace, SelectedWorkspace) ? CurrentWorkspaceOperationName : string.Empty,
            currentStatusMessage: ReferenceEquals(workspace, SelectedWorkspace) ? CurrentWorkspaceOperationStatus : string.Empty,
            new WorkspacePresentationActions
            {
                OpenWorkspace = openWorkspaceAction,
                OpenDevelopmentShell = openDevelopmentShellAction,
                RebuildRuntime = rebuildRuntimeAction,
                TroubleshootWorkspace = investigateProblemAction,
                OpenFolder = openFolderAction,
                AdvancedActions = advancedActions,
            });
    }

    private static bool ShouldShowFailureEvidence(WorkspaceSummaryViewModel workspace)
    {
        if (workspace.Snapshot is null)
        {
            return false;
        }

        if (workspace.Snapshot.Health.OverallStatus is WorkspaceHealthStatus.Degraded or WorkspaceHealthStatus.Unavailable or WorkspaceHealthStatus.Investigating)
        {
            return true;
        }

        return false;
    }

    private WorkspaceAggregatedState BuildAggregatedState(WorkspaceSummaryViewModel workspace)
        => WorkspaceHealthAggregator.BuildState(
            workspace,
            isOperationInProgress: ReferenceEquals(workspace, SelectedWorkspace) && HasActiveWorkspaceOperation,
            currentOperationName: ReferenceEquals(workspace, SelectedWorkspace) ? CurrentWorkspaceOperationName : string.Empty,
            currentStatusMessage: ReferenceEquals(workspace, SelectedWorkspace) ? CurrentWorkspaceOperationStatus : string.Empty);

    private ActionItemViewModel CreatePresentationAction(WorkspaceSummaryViewModel workspace, string label, string description, bool isEnabled, string disabledReason, Func<Task> executeAsync, bool useWorkspaceScopedCommands)
        => new(
            label,
            description,
            isEnabled,
            disabledReason,
            new AsyncRelayCommand(async () =>
            {
                if (useWorkspaceScopedCommands)
                {
                    SelectWorkspaceByRootPath(workspace.RootPath);
                }

                await executeAsync();
            }));

    private static string BuildInvestigateProblemDescription(WorkspaceSummaryViewModel workspace)
        => workspace.IsLoading
            ? "Loading workspace details before diagnostics become available."
            : "Inspect workspace, runtime, Docker, template, and provider diagnostics for this workspace.";

    private string BuildOpenDevelopmentShellDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : workspace.Snapshot?.RuntimeState == OpenCode.Workspace.Core.Models.WorkspaceRuntimeState.Running
                ? "Open the development shell for the current workspace."
                : "Start what is needed and open the development shell.";

    private bool CanReprovisionSelectedWorkspace()
        => SelectedWorkspace is { HasSnapshot: true } && !IsReprovisioning;

    private bool CanReprovisionWorkspace(WorkspaceSummaryViewModel workspace)
        => workspace.HasSnapshot && !IsReprovisioning;

    private bool CanResetRuntimeSelectedWorkspace()
        => CanResetRuntimeWorkspace(SelectedWorkspace);

    private bool CanRetrySelectedWorkspace()
        => CanRetryWorkspace(SelectedWorkspace);

    private bool CanStartSelectedWorkspace()
        => CanStartWorkspace(SelectedWorkspace);

    private bool CanRecoverSelectedWorkspace()
        => CanRecoverWorkspace(SelectedWorkspace);

    private bool CanAttachSelectedWorkspace()
        => CanAttachWorkspace(SelectedWorkspace);

    private bool CanTroubleshootSelectedWorkspace()
        => CanTroubleshootWorkspace(SelectedWorkspace);

    private bool CanRemoveSelectedWorkspace()
        => CanRemoveWorkspace(SelectedWorkspace);

    private bool CanPublishSelectedWorkspace()
        => CanPublishWorkspace(SelectedWorkspace);

    private bool CanBackupSelectedWorkspace()
        => CanBackupWorkspace(SelectedWorkspace);

    private bool CanCreateSavePointSelectedWorkspace()
        => CanCreateSavePointWorkspace(SelectedWorkspace);

    private bool CanCreateCheckpointSelectedWorkspace()
        => CanCreateCheckpointWorkspace(SelectedWorkspace);

    private string GetTroubleshootDisabledReason(WorkspaceSummaryViewModel workspace)
        => workspace.IsLoading
            ? "Workspace details are still loading. Troubleshooting will be available when background checks finish."
            : string.Empty;

    private string GetRetryDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanRetryWorkspace(workspace) ? string.Empty : "Retry is not available for the current workspace state.";

    private string GetStartDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanStartWorkspace(workspace) ? string.Empty : "Workspace root or configuration file is missing, so start cannot run.";

    private string GetOpenDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanStartWorkspace(workspace) ? string.Empty : "Workspace root or configuration file is missing, so Open Workspace cannot run.";

    private string GetRecoverDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanRecoverWorkspace(workspace) ? string.Empty : _interactionService is null ? "Workspace interaction services are unavailable." : "Workspace root or configuration file is missing, so recovery cannot run.";

    private string GetResetRuntimeDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanResetRuntimeWorkspace(workspace) ? string.Empty : "Rebuild Runtime is not available for the current workspace state.";

    private string GetAttachDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanAttachWorkspace(workspace) ? string.Empty : "Workspace root or configuration file is missing, so attach cannot run.";

    private string GetSavePointDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanCreateSavePointWorkspace(workspace) ? string.Empty : _interactionService is null ? "Workspace interaction services are unavailable." : "Workspace root or configuration file is missing, so Save Point creation cannot run.";

    private string GetCheckpointDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanCreateCheckpointWorkspace(workspace) ? string.Empty : _interactionService is null ? "Workspace interaction services are unavailable." : "Workspace root or configuration file is missing, so checkpoint creation cannot run.";

    private string GetRemoveDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanRemoveWorkspace(workspace) ? string.Empty : _interactionService is null ? "Workspace interaction services are unavailable." : "Workspace record is unavailable, so removal cannot run.";

    private string GetPublishDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanPublishWorkspace(workspace) ? string.Empty : _interactionService is null ? "Workspace interaction services are unavailable." : "Workspace root or configuration file is missing, so publish cannot run.";

    private string GetBackupDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanBackupWorkspace(workspace) ? string.Empty : _interactionService is null ? "Workspace interaction services are unavailable." : "Workspace root or configuration file is missing, so backup cannot run.";

    private static string BuildTroubleshootDescription(WorkspaceSummaryViewModel workspace)
        => workspace.IsLoading
            ? "Loading workspace details before troubleshooting becomes available."
            : "Inspect workspace-specific doctor results, runtime-state status, and related evidence for this workspace.";

    private static string BuildHostDiagnosticsDescription(WorkspaceSummaryViewModel workspace)
        => workspace.IsLoading
            ? "Loading workspace details before host diagnostics becomes available."
            : "Check host prerequisites such as Docker, Docker Compose, Windows Terminal, and platform support.";

    private string BuildStartDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : workspace.Snapshot?.RuntimeState == OpenCode.Workspace.Core.Models.WorkspaceRuntimeState.Running
                ? "Workspace runtime is already running. Start will re-check runtime readiness."
                : workspace.Snapshot?.LocalRuntimeState is null
                    ? "Runtime state is missing. Start will regenerate runtime files and bring the workspace online."
                    : "Start the workspace runtime and provision it if generated files are out of date.";

    private string BuildOpenDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : workspace.Snapshot?.AppliedState is null
                ? "Provision the workspace, start containers, and open the terminal session."
                : workspace.Snapshot?.RuntimeState == OpenCode.Workspace.Core.Models.WorkspaceRuntimeState.Running
                    ? "Open the running workspace terminal session."
                    : workspace.Snapshot?.RuntimeState == OpenCode.Workspace.Core.Models.WorkspaceRuntimeState.Stopped
                        ? "Start the workspace runtime and open the terminal session."
                        : workspace.Snapshot?.LocalRuntimeState is null || workspace.Snapshot?.UpdateRequired == true
                            ? "Open Workspace will repair safe runtime issues automatically before opening the terminal."
                            : "Open the workspace and let OpenCode decide what needs to run.";

    private string BuildRecoverDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanRecoverWorkspace(workspace)
                ? "Regenerate managed runtime files safely without deleting workspace files or user content."
                : "Workspace root or configuration file is missing, so recovery cannot run.";

    private string BuildResetRuntimeDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanResetRuntimeWorkspace(workspace)
                ? "Recreate managed containers and volumes from workspace.yaml while keeping workspace files, history, downloads, docs, and user scripts."
                : "Rebuild Runtime is not available for the current workspace state.";

    private string BuildAttachDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanAttachWorkspace(workspace)
                ? "Advanced action: attach to an already running workspace terminal session."
                : "Workspace root or configuration file is missing, so attach cannot run.";

    private string BuildSavePointDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanCreateSavePointWorkspace(workspace)
                ? "Capture the current local milestone for recovery using the shared Git-backed Save Point flow."
                : "Workspace root or configuration file is missing, so Save Point creation cannot run.";

    private string BuildRemoveDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanRemoveWorkspace(workspace)
                ? "Remove the workspace from the local index, clean Docker resources, or delete workspace files after permission repair."
                : "Workspace record is unavailable, so removal cannot run.";

    private string BuildCheckpointDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanCreateCheckpointWorkspace(workspace)
                ? "Capture tracked changes and durable untracked files for stronger local recovery than a normal Save Point."
                : "Workspace root or configuration file is missing, so checkpoint creation cannot run.";

    private string BuildPublishDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanPublishWorkspace(workspace)
                ? "Publish committed Working Copy changes to configured remote backup without force-pushing."
                : "Workspace root or configuration file is missing, so publish cannot run.";

    private string BuildBackupDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanBackupWorkspace(workspace)
                ? "Export a portable zip backup with workspace config, history, mounts, docs, runtime metadata, and tracked repository content."
                : "Workspace root or configuration file is missing, so backup cannot run.";

    private string GetReprovisionDisabledReason(WorkspaceSummaryViewModel workspace)
    {
        if (IsReprovisioning)
        {
            return string.IsNullOrWhiteSpace(ReprovisionStatusMessage) ? "Reprovision is already running." : ReprovisionStatusMessage;
        }

        return workspace.HasSnapshot
            ? string.Empty
            : "Workspace configuration must load successfully before reprovision can run.";
    }

    private string BuildReprovisionDescription(WorkspaceSummaryViewModel workspace)
    {
        if (IsReprovisioning)
        {
            return ReprovisionStatusMessage;
        }

        if (workspace.Snapshot?.LocalRuntimeState is null)
        {
            return "Runtime state is missing. Open Workspace will regenerate it automatically.";
        }

        if (workspace.IsLoading)
        {
            return string.IsNullOrWhiteSpace(workspace.LastActivity) ? "Loading details..." : workspace.LastActivity;
        }

        if (workspace.Snapshot?.UpdateRequired == true || workspace.Snapshot?.AppliedState is null)
        {
            return "Workspace files are out of date. Open Workspace will regenerate managed runtime files automatically.";
        }

        return "Regenerate runtime files, validate compose, and reprovision the workspace runtime.";
    }

    private string BuildRetryDescription(WorkspaceSummaryViewModel workspace)
    {
        var operationName = GetRetryOperationName(workspace);
        return string.IsNullOrWhiteSpace(operationName)
            ? "Retry the last failed workspace action."
            : $"Retry the last failed workspace action: {operationName}.";
    }

    private string BuildWorkspaceSummary(WorkspaceSummaryViewModel workspace)
    {
        var failureGuidance = TryBuildFailureGuidance(workspace);
        if (failureGuidance is not null)
        {
            return failureGuidance.Summary;
        }

        if (IsReprovisioning)
        {
            return string.IsNullOrWhiteSpace(ReprovisionStatusMessage) ? "Reprovision in progress." : ReprovisionStatusMessage;
        }

        if (workspace.Snapshot?.LocalRuntimeState is null)
        {
            return "Runtime state is missing. Open Workspace will regenerate it automatically.";
        }

        if (workspace.IsLoading)
        {
            return string.IsNullOrWhiteSpace(workspace.LastActivity) ? "Loading details..." : workspace.LastActivity;
        }

        if (workspace.Snapshot?.UpdateRequired == true || workspace.Snapshot?.AppliedState is null)
        {
            return "Workspace files are out of date. Open Workspace will regenerate managed runtime files automatically.";
        }

        return workspace.SafetyState;
    }

    private static bool CanStartWorkspace(WorkspaceSummaryViewModel? workspace)
    {
        if (workspace is null || workspace.IsLoading)
        {
            return false;
        }

        if (workspace.HasSnapshot)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(workspace.RootPath) || !Directory.Exists(workspace.RootPath))
        {
            return false;
        }

        var fullConfigurationPath = WorkspaceRecordPathResolver.GetWorkspaceConfigurationPath(workspace.Record);
        return File.Exists(fullConfigurationPath);
    }

    private bool CanRecoverWorkspace(WorkspaceSummaryViewModel? workspace)
    {
        if (_interactionService is null || IsBusyForWorkspaceActions)
        {
            return false;
        }

        return CanStartWorkspace(workspace);
    }

    private bool CanResetRuntimeWorkspace(WorkspaceSummaryViewModel? workspace)
    {
        if (IsBusyForWorkspaceActions || workspace is null || !CanStartWorkspace(workspace))
        {
            return false;
        }

        return string.Equals(workspace.Record.LastProvisioningHealth?.Repairability, WorkspaceRepairability.CleanupRepair.ToString(), StringComparison.Ordinal)
            || GetRepairabilityAssessment(workspace)?.Classification == WorkspaceRepairability.CleanupRepair;
    }

    private bool CanRetryWorkspace(WorkspaceSummaryViewModel? workspace)
    {
        if (workspace is null || IsBusyForWorkspaceActions)
        {
            return false;
        }

        if (!HasFailureMessage(workspace))
        {
            return false;
        }

        return GetRetryOperationName(workspace) switch
        {
            "Open Workspace" => CanStartWorkspace(workspace),
            "Start" => CanStartWorkspace(workspace),
            "Attach" => CanAttachWorkspace(workspace),
            "Recover" => CanRecoverWorkspace(workspace),
            "Reprovision" => workspace.HasSnapshot,
            "Prepare" => CanStartWorkspace(workspace),
            _ => false,
        };
    }

    private bool CanAttachWorkspace(WorkspaceSummaryViewModel? workspace)
    {
        if (IsBusyForWorkspaceActions)
        {
            return false;
        }

        return CanStartWorkspace(workspace);
    }

    private bool CanCreateSavePointWorkspace(WorkspaceSummaryViewModel? workspace)
    {
        if (_interactionService is null || IsBusyForWorkspaceActions)
        {
            return false;
        }

        return CanStartWorkspace(workspace);
    }

    private bool CanCreateCheckpointWorkspace(WorkspaceSummaryViewModel? workspace)
    {
        if (_interactionService is null || IsBusyForWorkspaceActions)
        {
            return false;
        }

        return CanStartWorkspace(workspace);
    }

    private bool CanRemoveWorkspace(WorkspaceSummaryViewModel? workspace)
    {
        if (_interactionService is null || IsBusyForWorkspaceActions)
        {
            return false;
        }

        return workspace is not null && !string.IsNullOrWhiteSpace(workspace.RootPath);
    }

    private bool CanPublishWorkspace(WorkspaceSummaryViewModel? workspace)
    {
        if (_interactionService is null || IsBusyForWorkspaceActions)
        {
            return false;
        }

        return CanStartWorkspace(workspace);
    }

    private bool CanBackupWorkspace(WorkspaceSummaryViewModel? workspace)
    {
        if (_interactionService is null || IsBusyForWorkspaceActions)
        {
            return false;
        }

        return CanStartWorkspace(workspace);
    }

    private static bool CanTroubleshootWorkspace(WorkspaceSummaryViewModel? workspace)
        => workspace is { IsLoading: false };

    private static string BuildBackupArchiveFileName(WorkspaceSummaryViewModel workspace)
        => $"{WorkspacePathBuilder.Slugify(workspace.Name)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip";

    private void ApplyPublishAssessmentDetails(WorkspacePublishAssessment assessment)
    {
        DetailItems.Clear();
        DetailItems.Add(new DetailItemViewModel("Branch", assessment.CurrentBranch));
        DetailItems.Add(new DetailItemViewModel("Remote", assessment.HasRemoteConfigured ? assessment.RemoteName : "Not configured"));
        DetailItems.Add(new DetailItemViewModel("Tracking", string.IsNullOrWhiteSpace(assessment.RemoteBranch) ? "Will be created on first publish" : assessment.RemoteBranch));
        DetailItems.Add(new DetailItemViewModel("Ahead", assessment.AheadCount.ToString(CultureInfo.InvariantCulture)));
        DetailItems.Add(new DetailItemViewModel("Behind", assessment.BehindCount.ToString(CultureInfo.InvariantCulture)));
        DetailItems.Add(new DetailItemViewModel("Findings", string.Join(Environment.NewLine, assessment.Findings)));
        if (assessment.Warnings.Count > 0)
        {
            DetailItems.Add(new DetailItemViewModel("Warnings", string.Join(Environment.NewLine, assessment.Warnings)));
        }
    }

    private void ApplyPublishResultDetails(WorkspacePublishResult result)
    {
        DetailItems.Clear();
        DetailItems.Add(new DetailItemViewModel("Branch", result.Review.WorkingCopyName));
        DetailItems.Add(new DetailItemViewModel("Remote", result.Review.RemoteName));
        DetailItems.Add(new DetailItemViewModel("Tracking", string.IsNullOrWhiteSpace(result.Review.RemoteBranch) ? "Not tracked" : result.Review.RemoteBranch));
        DetailItems.Add(new DetailItemViewModel("Ahead", result.Review.AheadCount.ToString(CultureInfo.InvariantCulture)));
        DetailItems.Add(new DetailItemViewModel("Behind", result.Review.BehindCount.ToString(CultureInfo.InvariantCulture)));
        DetailItems.Add(new DetailItemViewModel("Latest commit", string.IsNullOrWhiteSpace(result.Review.LatestCommitSha) ? "Unavailable" : result.Review.LatestCommitSha));
    }

    private void RemoveWorkspaceFromList(string rootPath)
    {
        var removedIndex = Workspaces
            .Select((item, index) => new { item, index })
            .FirstOrDefault(pair => string.Equals(pair.item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
        if (removedIndex is null)
        {
            return;
        }

        WorkspaceSummaryViewModel? nextSelection = null;
        if (Workspaces.Count > removedIndex.index + 1)
        {
            nextSelection = Workspaces[removedIndex.index + 1];
        }
        else if (removedIndex.index > 0)
        {
            nextSelection = Workspaces[removedIndex.index - 1];
        }

        Workspaces.RemoveAt(removedIndex.index);
        RaisePropertyChanged(nameof(HasWorkspaces));
        RaisePropertyChanged(nameof(ShowEmptyState));
        SelectedWorkspace = nextSelection;
        if (SelectedWorkspace is null && Workspaces.Count == 0)
        {
            EmptyStateTitle = "No workspaces discovered.";
            EmptyStateMessage = "OpenCode looks for workspace.yaml,\nworkspace.yml,\n.opencode/profile.yaml,\n.opencode/profile.yml\n\nUse Create Workspace or Open Existing Repository.";
            DetailTitle = EmptyStateTitle;
            DetailSummary = EmptyStateMessage;
            DetailRecommendation = string.Empty;
            DetailItems.Clear();
            DetailPrimaryAction = null;
            DetailActions.Clear();
            DetailAdvancedActions.Clear();
            ShowAdvancedActions = false;
        }
    }

    private void ReplaceSelectedWorkspace(OpenCode.Workspace.Core.Models.WorkspaceSnapshot snapshot)
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        var replacement = new WorkspaceSummaryViewModel(new WorkspaceShellItem { Record = snapshot.Record, Snapshot = snapshot });
        var index = Workspaces.IndexOf(SelectedWorkspace);
        if (index >= 0)
        {
            ApplyWorkspacePresentationToSummary(replacement);
            Workspaces[index] = replacement;
            SelectedWorkspace = replacement;
        }
    }

    private async Task RetrySelectedWorkspaceAsync()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        switch (GetRetryOperationName(SelectedWorkspace))
        {
            case "Open Workspace":
            case "Prepare":
                await OpenSelectedWorkspaceAsync();
                break;
            case "Start":
                await StartSelectedWorkspaceAsync();
                break;
            case "Attach":
                await AttachSelectedWorkspaceAsync();
                break;
            case "Recover":
                await RecoverSelectedWorkspaceAsync();
                break;
            case "Reprovision":
                await ReprovisionSelectedWorkspaceAsync();
                break;
        }
    }

    private WorkspaceFailureGuidance? TryBuildFailureGuidance(WorkspaceSummaryViewModel? workspace)
    {
        if (workspace is null || workspace.IsLoading)
        {
            return null;
        }

        var overallHealth = workspace.Snapshot?.Health.OverallStatus;
        var hasCurrentFailure = overallHealth is WorkspaceHealthStatus.Degraded or WorkspaceHealthStatus.Unavailable or WorkspaceHealthStatus.Investigating;
        var requiresRuntimeRepair = workspace.Snapshot?.LocalRuntimeState is null || workspace.Snapshot?.AppliedState is null || workspace.Snapshot?.UpdateRequired == true;
        var isFreshWorkspace = workspace.Record.LastPreparedUtc is null
            && workspace.Record.LastOperationSucceeded == true
            && string.Equals(workspace.Record.LastOperationName, "Create Workspace", StringComparison.Ordinal);
        if (!workspace.HasTransientOperationFailure && isFreshWorkspace && requiresRuntimeRepair)
        {
            return null;
        }

        if (!workspace.HasTransientOperationFailure
            && requiresRuntimeRepair
            && workspace.Record.LastOperationSucceeded != false
            && workspace.Record.LastProvisioningHealth is null)
        {
            return null;
        }

        if (!workspace.HasTransientOperationFailure && !hasCurrentFailure && !requiresRuntimeRepair)
        {
            return null;
        }

        var hasHistoricalFailure = workspace.Record.LastOperationSucceeded == false
            || workspace.Record.LastProvisioningHealth is not null
            || !string.IsNullOrWhiteSpace(workspace.FailedOperationName)
            || workspace.Snapshot?.LocalRuntimeState is null
            || workspace.Snapshot?.UpdateRequired == true
            || workspace.Snapshot?.AppliedState is null;
        if (!workspace.HasTransientOperationFailure
            && !hasHistoricalFailure
            && workspace.Snapshot?.Health.OverallStatus is not (WorkspaceHealthStatus.Degraded or WorkspaceHealthStatus.Unavailable or WorkspaceHealthStatus.Investigating))
        {
            return null;
        }

        var failureMessage = GetFailureMessage(workspace);
        if (string.IsNullOrWhiteSpace(failureMessage)
            && (workspace.Record.LastOperationSucceeded == false
                || !string.IsNullOrWhiteSpace(workspace.FailedOperationName)
                || workspace.Record.LastProvisioningHealth is not null))
        {
            failureMessage = workspace.LastActivity;
        }

        if (string.IsNullOrWhiteSpace(failureMessage)
            && !string.IsNullOrWhiteSpace(workspace.Snapshot?.Health.Summary))
        {
            failureMessage = workspace.Snapshot!.Health.Summary;
        }

        if (string.IsNullOrWhiteSpace(failureMessage) && workspace.Snapshot?.LocalRuntimeState is null)
        {
            failureMessage = "Runtime state is missing.";
        }

        if (string.IsNullOrWhiteSpace(failureMessage))
        {
            return null;
        }

        var reason = NormalizeFailureReason(ExtractFailureReason(failureMessage));
        var health = workspace.Record.LastProvisioningHealth;
        var repairability = GetRepairabilityAssessment(workspace);
        var canRetry = CanRetryWorkspace(workspace);
        var canRecover = CanRecoverWorkspace(workspace);
        var canTroubleshoot = CanTroubleshootWorkspace(workspace);
        var canCleanup = CanResetRuntimeWorkspace(workspace);
        var scope = ClassifyProblemScope(workspace, health, reason, repairability);
        var primaryAction = BuildPrimaryAction(workspace, health, reason, repairability, scope, canRetry, canRecover, canTroubleshoot, canCleanup);
        return new WorkspaceFailureGuidance(
            BuildFailureHeadline(workspace.FailedOperationName, health?.Reason ?? reason),
            health?.Stage ?? string.Empty,
            reason,
            string.IsNullOrWhiteSpace(health?.Evidence) ? repairability?.Evidence ?? string.Empty : health.Evidence,
            scope,
            repairability?.Classification.ToString() ?? string.Empty,
            string.IsNullOrWhiteSpace(health?.Confidence) ? repairability?.Confidence ?? string.Empty : health.Confidence,
            repairability?.EstimatedDuration ?? health?.EstimatedDuration ?? string.Empty,
            primaryAction,
            BuildRecommendedAction(workspace, health, reason, repairability, scope, primaryAction, canRetry, canRecover, canTroubleshoot, canCleanup),
            WorkspaceFailureSeverity.Error,
            canRetry,
            canRecover,
            canTroubleshoot,
            canCleanup);
    }

    private static WorkspaceRepairabilityAssessment? GetRepairabilityAssessment(WorkspaceSummaryViewModel? workspace)
        => workspace?.HasSnapshot == true
            ? WorkspaceRepairabilityAnalyzer.Analyze(workspace.Snapshot, workspace.Record.LastProvisioningHealth)
            : null;

    private static bool HasFailureMessage(WorkspaceSummaryViewModel? workspace)
        => !string.IsNullOrWhiteSpace(GetFailureMessage(workspace));

    private static string? GetFailureMessage(WorkspaceSummaryViewModel? workspace)
    {
        if (workspace is null || workspace.IsLoading)
        {
            return null;
        }

        if ((workspace.Record.LastOperationSucceeded == false
                || !string.IsNullOrWhiteSpace(workspace.FailedOperationName)
                || workspace.Record.LastProvisioningHealth is not null)
            && !string.IsNullOrWhiteSpace(workspace.LastActivity))
        {
            return workspace.LastActivity;
        }

        if (workspace.Snapshot?.LocalRuntimeState is null)
        {
            return "Runtime state is missing.";
        }

        return workspace.Snapshot?.Health.OverallStatus is WorkspaceHealthStatus.Degraded or WorkspaceHealthStatus.Unavailable or WorkspaceHealthStatus.Investigating
            ? workspace.Snapshot.Health.Summary
            : null;
    }

    private static string? GetRetryOperationName(WorkspaceSummaryViewModel? workspace)
        => HasFailureMessage(workspace) ? workspace?.FailedOperationName : null;

    private static string SummarizeTransientOperationMessage(string message)
        => message.Contains('\n', StringComparison.Ordinal) || message.Contains('\r', StringComparison.Ordinal)
            ? ExtractFailureReason(message)
            : message;

    private static string ExtractFailureReason(string failureMessage)
    {
        foreach (var rawLine in failureMessage.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith("Command:", StringComparison.OrdinalIgnoreCase)
                || rawLine.StartsWith("Exit code:", StringComparison.OrdinalIgnoreCase)
                || rawLine.StartsWith("Likely causes:", StringComparison.OrdinalIgnoreCase)
                || rawLine.StartsWith("Suggested actions:", StringComparison.OrdinalIgnoreCase)
                || rawLine.StartsWith("Host port details:", StringComparison.OrdinalIgnoreCase)
                || rawLine.StartsWith("This workspace docker compose ps:", StringComparison.OrdinalIgnoreCase)
                || rawLine.StartsWith("Running containers:", StringComparison.OrdinalIgnoreCase)
                || rawLine.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            return rawLine;
        }

        return "See Operation Log for the full failure details.";
    }

    private static string BuildFailureHeadline(string? operationName, string reason)
    {
        if (IsTerminalLaunchReadinessProblem(reason))
        {
            return "Terminal launch readiness failed.";
        }

        if (reason.Contains("runtime state is missing", StringComparison.OrdinalIgnoreCase))
        {
            return "Runtime state is missing.";
        }

        if (reason.Contains("already in use", StringComparison.OrdinalIgnoreCase))
        {
            return "Workspace could not start.";
        }

        return operationName switch
        {
            "Attach" => "Workspace could not open terminal session.",
            "Open Workspace" or "Prepare" or "Start" or "Recover" or "Reprovision" => "Workspace could not be prepared.",
            _ => "Workspace action failed.",
        };
    }

    private WorkspaceFailureProblemScope ClassifyProblemScope(WorkspaceSummaryViewModel workspace, WorkspaceProvisioningHealthRecord? health, string reason, WorkspaceRepairabilityAssessment? repairability)
    {
        var evidence = string.IsNullOrWhiteSpace(health?.Evidence) ? repairability?.Evidence ?? string.Empty : health.Evidence;
        var stage = health?.Stage ?? string.Empty;

        if (IsHostProblem(reason, evidence, stage))
        {
            return WorkspaceFailureProblemScope.HostProblem;
        }

        if (repairability?.Classification == WorkspaceRepairability.CleanupRepair || IsRuntimeProblem(reason, evidence, stage))
        {
            return WorkspaceFailureProblemScope.RuntimeProblem;
        }

        if (workspace.Snapshot?.LocalRuntimeState is null || workspace.Snapshot?.UpdateRequired == true)
        {
            return WorkspaceFailureProblemScope.WorkspaceProblem;
        }

        if (IsWorkspaceProblem(reason, evidence, stage))
        {
            return WorkspaceFailureProblemScope.WorkspaceProblem;
        }

        return WorkspaceFailureProblemScope.Unknown;
    }

    private string? BuildPrimaryAction(WorkspaceSummaryViewModel workspace, WorkspaceProvisioningHealthRecord? health, string reason, WorkspaceRepairabilityAssessment? repairability, WorkspaceFailureProblemScope scope, bool canRetry, bool canRecover, bool canTroubleshoot, bool canCleanup)
    {
        if (IsTerminalLaunchReadinessProblem(reason) && canTroubleshoot)
        {
            return "Troubleshoot Workspace";
        }

        if (reason.Contains("not running", StringComparison.OrdinalIgnoreCase) && CanStartWorkspace(workspace))
        {
            return "Open Workspace";
        }

        if (scope == WorkspaceFailureProblemScope.RuntimeProblem)
        {
            if ((repairability?.Classification == WorkspaceRepairability.CleanupRepair
                    || string.Equals(health?.Repairability, WorkspaceRepairability.CleanupRepair.ToString(), StringComparison.Ordinal))
                && canCleanup)
            {
                return "Rebuild Runtime";
            }

            if (canTroubleshoot)
            {
                return "Troubleshoot Workspace";
            }
        }

        if (scope == WorkspaceFailureProblemScope.HostProblem && canTroubleshoot)
        {
            return "Run Diagnostics";
        }

        if (workspace.Snapshot?.LocalRuntimeState is null || workspace.Snapshot?.UpdateRequired == true)
        {
            return "Open Workspace";
        }

        if (scope == WorkspaceFailureProblemScope.WorkspaceProblem)
        {
            if (RequiresRecoverWorkspace(reason, health))
            {
                return "Open Workspace";
            }

            if (canTroubleshoot)
            {
                return "Troubleshoot Workspace";
            }
        }

        var healthPrimaryAction = TryMapPrimaryAction(health?.RecommendedAction, scope, canRetry, canRecover, canTroubleshoot, canCleanup, CanStartWorkspace(workspace));
        if (!string.IsNullOrWhiteSpace(healthPrimaryAction))
        {
            return healthPrimaryAction;
        }

        if (scope == WorkspaceFailureProblemScope.Unknown && canTroubleshoot)
        {
            return "Troubleshoot Workspace";
        }

        if (canTroubleshoot)
        {
            return scope == WorkspaceFailureProblemScope.HostProblem ? "Run Diagnostics" : "Troubleshoot Workspace";
        }

        if (CanStartWorkspace(workspace))
        {
            return "Open Workspace";
        }

        return null;
    }

    private string BuildRecommendedAction(WorkspaceSummaryViewModel workspace, WorkspaceProvisioningHealthRecord? health, string reason, WorkspaceRepairabilityAssessment? repairability, WorkspaceFailureProblemScope scope, string? primaryAction, bool canRetry, bool canRecover, bool canTroubleshoot, bool canCleanup)
    {
        if (!string.IsNullOrWhiteSpace(primaryAction))
        {
            if (IsTerminalLaunchReadinessProblem(reason) && string.Equals(primaryAction, "Troubleshoot Workspace", StringComparison.Ordinal))
            {
                return "Troubleshoot Workspace can inspect attach scripts and runtime state.";
            }

            return primaryAction switch
            {
                "Open Workspace" => "Open Workspace.",
                "Rebuild Runtime" => "Rebuild Runtime.",
                "Run Diagnostics" => "Run Diagnostics.",
                "Troubleshoot Workspace" => reason.Contains("already in use", StringComparison.OrdinalIgnoreCase)
                    ? "Troubleshoot Workspace and stop the conflicting workspace."
                    : "Troubleshoot Workspace.",
                "Retry" => reason.Contains("already in use", StringComparison.OrdinalIgnoreCase)
                    ? "Stop the conflicting workspace and retry."
                    : "Retry.",
                _ => primaryAction,
            };
        }

        if (scope == WorkspaceFailureProblemScope.HostProblem && canTroubleshoot)
        {
            return "Run Diagnostics.";
        }

        if (scope == WorkspaceFailureProblemScope.Unknown && canTroubleshoot)
        {
            return "Troubleshoot Workspace.";
        }

        if (repairability?.Classification == WorkspaceRepairability.ManualRepair && !string.IsNullOrWhiteSpace(repairability.RecommendedNextAction))
        {
            return repairability.RecommendedNextAction;
        }

        if (!string.IsNullOrWhiteSpace(health?.RecommendedAction))
        {
            return health.RecommendedAction;
        }

        if (canCleanup)
        {
            return "Rebuild Runtime.";
        }

        return "Troubleshoot Workspace.";
    }

    private static bool RequiresRecoverWorkspace(string reason, WorkspaceProvisioningHealthRecord? health)
        => !IsTerminalLaunchReadinessProblem(reason)
            && (reason.Contains("Recover Workspace", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("generated", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("runtime state is missing", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("runtime files need repair", StringComparison.OrdinalIgnoreCase)
            || string.Equals(health?.Repairability, WorkspaceRepairability.AutomaticRepair.ToString(), StringComparison.Ordinal)
                && string.Equals(health?.RecommendedAction, "Run Recover Workspace.", StringComparison.Ordinal));

    private static bool IsHostProblem(string reason, string evidence, string stage)
        => ContainsAny(reason, evidence, stage,
            "docker unavailable",
            "docker compose",
            "docker engine",
            "docker cli",
            "terminal launch is unavailable",
            "windows terminal",
            "platform unsupported",
            "unsupported platform",
            "host prerequisite",
            "wsl",
            "virtualization")
            || reason.Contains("Docker", StringComparison.OrdinalIgnoreCase) && reason.Contains("failed", StringComparison.OrdinalIgnoreCase);

    private static bool IsRuntimeProblem(string reason, string evidence, string stage)
        => ContainsAny(reason, evidence, stage,
            "xdb is invalid",
            "xdb status = invalid",
            "sysdba",
            "pluggable database",
            "not open for writes",
            "runtime volume",
            "managed runtime state is invalid",
            "partial initialization",
            "corrupt",
            "invalid database volume");

    private static bool IsWorkspaceProblem(string reason, string evidence, string stage)
        => ContainsAny(reason, evidence, stage,
            "already in use",
            "port conflict",
            "port ",
            "service unhealthy",
            "provisioning stopped",
            "failed provisioning",
            "partial provisioning",
            "template check",
            "runtime-state",
            "runtime state",
            "workspace runtime could not be validated",
            "workspace configuration");

    private static bool IsTerminalLaunchReadinessProblem(string reason)
        => reason.Contains("terminal-ready state", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("terminal launch readiness", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("could not finish preparing the terminal", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("terminal could not be prepared", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("attach scripts and runtime state", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFailureReason(string reason)
        => IsTerminalLaunchReadinessProblem(reason)
            ? "Terminal launch readiness failed. Troubleshoot Workspace can inspect attach scripts and runtime state."
            : reason.Replace("Run Recover Workspace.", "Open Workspace will try to repair safe runtime issues automatically.", StringComparison.Ordinal);

    private static bool ContainsAny(string reason, string evidence, string stage, params string[] patterns)
        => patterns.Any(pattern =>
            reason.Contains(pattern, StringComparison.OrdinalIgnoreCase)
            || evidence.Contains(pattern, StringComparison.OrdinalIgnoreCase)
            || stage.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static string FormatRepairOutcome(string outcome)
        => outcome switch
        {
            nameof(WorkspaceRepairOutcome.RepairNoEffect) => "No improvement detected.",
            nameof(WorkspaceRepairOutcome.RepairImproved) => "Issue changed after repair.",
            nameof(WorkspaceRepairOutcome.RepairPartiallySucceeded) => "Repair partially succeeded.",
            nameof(WorkspaceRepairOutcome.RepairFailed) => "Repair failed.",
            _ => "Problem resolved.",
        };

    private static string? TryMapPrimaryAction(string? recommendedAction, WorkspaceFailureProblemScope scope, bool canRetry, bool canRecover, bool canTroubleshoot, bool canCleanup, bool canOpenWorkspace)
    {
        if (string.IsNullOrWhiteSpace(recommendedAction))
        {
            return null;
        }

        if ((recommendedAction.Contains("Rebuild Runtime", StringComparison.OrdinalIgnoreCase)
                || recommendedAction.Contains("Reset Runtime", StringComparison.OrdinalIgnoreCase))
            && canCleanup)
        {
            return "Rebuild Runtime";
        }

        if (recommendedAction.Contains("Recover Workspace", StringComparison.OrdinalIgnoreCase) && canOpenWorkspace)
        {
            return "Open Workspace";
        }

        if (recommendedAction.Contains("Troubleshoot Workspace", StringComparison.OrdinalIgnoreCase) && canTroubleshoot)
        {
            return "Troubleshoot Workspace";
        }

        if (recommendedAction.Contains("Run Diagnostics", StringComparison.OrdinalIgnoreCase) && canTroubleshoot)
        {
            return scope == WorkspaceFailureProblemScope.HostProblem ? "Run Diagnostics" : "Troubleshoot Workspace";
        }

        if (recommendedAction.Contains("Retry", StringComparison.OrdinalIgnoreCase) && canRetry)
        {
            return "Retry";
        }

        if (recommendedAction.Contains("Open Workspace", StringComparison.OrdinalIgnoreCase) && canOpenWorkspace)
        {
            return "Open Workspace";
        }

        return null;
    }

    private static string FormatOperationTranscriptLine(OperationTranscriptLine line)
    {
        var kind = line.Kind switch
        {
            OperationTranscriptLineKind.Command => "cmd ",
            OperationTranscriptLineKind.StandardOutput => "out ",
            OperationTranscriptLineKind.StandardError => "err ",
            OperationTranscriptLineKind.Status => "stat",
            OperationTranscriptLineKind.Result => "res ",
            _ => "info",
        };

        return $"[{line.Timestamp:HH:mm:ss}] {kind} {line.Text}";
    }

    private ServiceHealthRowViewModel BuildServiceHealthRow(WorkspaceServiceHealthSnapshot service)
    {
        var applications = string.Join(Environment.NewLine, service.Applications);
        var highlights = string.Join(Environment.NewLine, service.Highlights.Select(item => $"{item.Label}: {item.Value}"));
        var details = string.Join(Environment.NewLine, service.Evidence.Select(item => $"{item.Label}: {item.Value}"));
        AsyncRelayCommand? openCommand = null;
        if (!string.IsNullOrWhiteSpace(service.OpenUrl))
        {
            openCommand = new AsyncRelayCommand(() => _desktopShellService.OpenPathAsync(service.OpenUrl));
        }

        return new ServiceHealthRowViewModel(
            service.Name,
            service.StatusLabel,
            service.Summary,
            applications,
            service.PrimaryUrl,
            highlights,
            details,
            string.IsNullOrWhiteSpace(service.ActionLabel) ? "Open" : service.ActionLabel,
            service.OpenUrl,
            openCommand);
    }

    private sealed class OperationTranscriptSink : IOperationLogSink
    {
        private readonly WorkspacesPageViewModel _owner;

        public OperationTranscriptSink(WorkspacesPageViewModel owner)
        {
            _owner = owner;
        }

        public void Append(OperationTranscriptLine line)
        {
            var latestStatusText = TryGetBufferedStatusText(line);
            if (!string.IsNullOrWhiteSpace(latestStatusText))
            {
                void ApplyStatus()
                {
                    if (_owner._isWorkspaceActionRunning || _owner.IsReprovisioning)
                    {
                        _owner.ApplyImmediateBufferedStatus(latestStatusText);
                    }
                }

                if (Dispatcher.UIThread.CheckAccess())
                {
                    ApplyStatus();
                }
                else
                {
                    Dispatcher.UIThread.InvokeAsync(ApplyStatus).GetAwaiter().GetResult();
                }
            }

            _owner.AppendOperationTranscriptLineCore(line, flushImmediately: false);
        }
    }

    private async Task RunWorkspaceOperationAsync(string operationName, string initialStatusMessage, Func<string, OpenCode.Workspace.Core.Models.WorkspaceSnapshot?, IOperationLogSink, Task<WorkspaceOperationResult>> operation, bool preserveExistingTranscript = false)
    {
        if (SelectedWorkspace is null)
        {
            Services.StartupLog.WriteGlobal($"Workspace operation '{operationName}' skipped because no workspace is selected.");
            return;
        }

        var operationFailed = false;

        try
        {
            Services.StartupLog.WriteGlobal($"Workspace operation '{operationName}' starting for '{SelectedWorkspace.Name}'.");
            _isWorkspaceActionRunning = true;
            _workspaceActionStatusMessage = initialStatusMessage;
            RaiseWorkspaceActionCommandStates();
            if (!preserveExistingTranscript)
            {
                StartOperationTranscript(operationName, SelectedWorkspace.Name);
            }
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = initialStatusMessage });
            DetailSummary = initialStatusMessage;
            RefreshWorkspacePresentations();
            UpdateDetailPanel();
            Services.StartupLog.WriteGlobal($"Workspace operation '{operationName}' updated UI status to '{initialStatusMessage}'.");
            var sink = new OperationTranscriptSink(this);
            var result = await operation(SelectedWorkspace.RootPath, SelectedWorkspace.Snapshot, sink);
            FlushPendingOperationLogToUi(forceDrainAll: true);
            Services.StartupLog.WriteGlobal($"Workspace operation '{operationName}' completed provider call with message '{result.Message}'.");
            ReplaceSelectedWorkspace(result.Snapshot);
            CompleteOperationTranscript(result.Transcript);
            _workspaceActionStatusMessage = result.Message;
            DetailSummary = result.Message;
            RefreshWorkspacePresentations();
            UpdateDetailPanel();
        }
        catch (Exception exception)
        {
            operationFailed = true;
            Services.StartupLog.WriteGlobalException($"Workspace operation '{operationName}' failed", exception);
            _workspaceActionStatusMessage = exception.Message;
            SelectedWorkspace?.SetOperationFailureState(exception.Message, operationName);
            DetailSummary = exception.Message;
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.StandardError, Text = exception.Message });
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Failed." });
            RefreshWorkspacePresentations();
            UpdateDetailPanel();
            throw;
        }
        finally
        {
            _isWorkspaceActionRunning = false;
            FlushPendingOperationLogToUi(forceDrainAll: true);
            RaiseWorkspaceActionCommandStates();
            if (operationFailed)
            {
                UpdateDetailPanel();
            }
            else
            {
                RefreshDetailActions();
            }
        }
    }

    private string GetCurrentWorkspaceActionStatusMessage()
        => string.IsNullOrWhiteSpace(_workspaceActionStatusMessage) ? "Workspace action in progress." : _workspaceActionStatusMessage;

    private void RaiseWorkspaceActionCommandStates()
    {
        CreateWorkspaceCommand.RaiseCanExecuteChanged();
        OpenExistingRepositoryCommand.RaiseCanExecuteChanged();
        RefreshWorkspacesCommand.RaiseCanExecuteChanged();
        OpenSelectedWorkspaceCommand.RaiseCanExecuteChanged();
        OpenWorkspaceFolderCommand.RaiseCanExecuteChanged();
        CreateSavePointCommand.RaiseCanExecuteChanged();
        CreateCheckpointCommand.RaiseCanExecuteChanged();
        StartWorkspaceCommand.RaiseCanExecuteChanged();
        RecoverWorkspaceCommand.RaiseCanExecuteChanged();
        ResetRuntimeCommand.RaiseCanExecuteChanged();
        AttachWorkspaceCommand.RaiseCanExecuteChanged();
        TroubleshootWorkspaceCommand.RaiseCanExecuteChanged();
        RemoveWorkspaceCommand.RaiseCanExecuteChanged();
        PublishWorkspaceCommand.RaiseCanExecuteChanged();
        BackupWorkspaceCommand.RaiseCanExecuteChanged();
        ReprovisionWorkspaceCommand.RaiseCanExecuteChanged();
        RetryWorkspaceCommand.RaiseCanExecuteChanged();
        RefreshWorkspacePresentations();
        if (SelectedWorkspace is not null)
        {
            RefreshDetailActions();
        }
    }

    private sealed record WorkspaceFailureGuidance(
        string Summary,
        string Stage,
        string Reason,
        string Evidence,
        WorkspaceFailureProblemScope Scope,
        string Repairability,
        string Confidence,
        string EstimatedDuration,
        string? PrimaryAction,
        string RecommendedAction,
        WorkspaceFailureSeverity Severity,
        bool CanRetry,
        bool CanRecover,
        bool CanTroubleshoot,
        bool CanCleanup);

    private enum WorkspaceFailureProblemScope
    {
        HostProblem,
        WorkspaceProblem,
        RuntimeProblem,
        Unknown,
    }

    private enum WorkspaceFailureSeverity
    {
        Info,
        Warning,
        Error,
    }

    private static string FormatManagedResources(IReadOnlyList<WorkspacePortAllocationRecord> ports)
        => string.Join(
            Environment.NewLine,
            ports.Select(port => port.PreferredPort == port.AllocatedPort
                ? $"{port.DisplayName}: {port.AllocatedPort}"
                : $"{port.DisplayName}: {port.AllocatedPort} (allocated automatically)"));
}
