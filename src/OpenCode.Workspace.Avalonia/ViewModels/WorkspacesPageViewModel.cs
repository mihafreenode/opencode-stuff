using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspacesPageViewModel : PageViewModel
{
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
        ValidateSelectedWorkspaceCommand = new AsyncRelayCommand(ValidateSelectedWorkspaceInternalAsync, () => SelectedWorkspace is not null);
        CreateSavePointCommand = new AsyncRelayCommand(CreateSavePointAsync, CanCreateSavePointSelectedWorkspace);
        StartWorkspaceCommand = new AsyncRelayCommand(StartSelectedWorkspaceAsync, CanStartSelectedWorkspace);
        RecoverWorkspaceCommand = new AsyncRelayCommand(RecoverSelectedWorkspaceAsync, CanRecoverSelectedWorkspace);
        AttachWorkspaceCommand = new AsyncRelayCommand(AttachSelectedWorkspaceAsync, CanAttachSelectedWorkspace);
        ReprovisionWorkspaceCommand = new AsyncRelayCommand(ReprovisionSelectedWorkspaceAsync, CanReprovisionSelectedWorkspace);
        CopyOperationLogCommand = new AsyncRelayCommand(CopyOperationLogAsync, () => HasOperationLog && _clipboardService is not null);
        ClearOperationLogCommand = new RelayCommand(ClearOperationLog, () => HasOperationLog);
        ToggleOperationLogVisibilityCommand = new RelayCommand(ToggleOperationLogVisibility);
        DisabledActionCommand = new RelayCommand(() => { });
        SetLoadingState();
    }

    public ObservableCollection<WorkspaceSummaryViewModel> Workspaces { get; } = [];
    public AsyncRelayCommand CreateWorkspaceCommand { get; }
    public AsyncRelayCommand OpenExistingRepositoryCommand { get; }
    public AsyncRelayCommand RefreshWorkspacesCommand { get; }
    public AsyncRelayCommand OpenSelectedWorkspaceCommand { get; }
    public AsyncRelayCommand ValidateSelectedWorkspaceCommand { get; }
    public AsyncRelayCommand CreateSavePointCommand { get; }
    public AsyncRelayCommand StartWorkspaceCommand { get; }
    public AsyncRelayCommand RecoverWorkspaceCommand { get; }
    public AsyncRelayCommand AttachWorkspaceCommand { get; }
    public AsyncRelayCommand ReprovisionWorkspaceCommand { get; }
    public AsyncRelayCommand CopyOperationLogCommand { get; }
    public RelayCommand ClearOperationLogCommand { get; }
    public RelayCommand ToggleOperationLogVisibilityCommand { get; }
    public RelayCommand DisabledActionCommand { get; }
    public Func<string, Task>? ValidateWorkspaceAsync { get; set; }
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
                UpdateDetailPanel();
                OpenSelectedWorkspaceCommand.RaiseCanExecuteChanged();
                ValidateSelectedWorkspaceCommand.RaiseCanExecuteChanged();
                StartWorkspaceCommand.RaiseCanExecuteChanged();
                RecoverWorkspaceCommand.RaiseCanExecuteChanged();
                AttachWorkspaceCommand.RaiseCanExecuteChanged();
                ReprovisionWorkspaceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
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
            foreach (var item in loadResult.Items.OrderBy(item => string.IsNullOrWhiteSpace(item.Record.Name) ? item.Record.RootPath : item.Record.Name, StringComparer.OrdinalIgnoreCase))
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
            DetailItems.Clear();
            DetailItems.Add(new DetailItemViewModel("Error", exception.Message));
            DetailActions.Clear();
            DetailActions.Add(new ActionItemViewModel("Open", string.Empty, false, "Workspace discovery failed.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Attach", string.Empty, false, "Workspace discovery failed.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Validate", string.Empty, false, "Workspace discovery failed.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Recover", string.Empty, false, "Workspace discovery failed.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Save Point", string.Empty, false, "Workspace discovery failed.", DisabledActionCommand));
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
            if (SelectedWorkspace is null)
            {
                SelectedWorkspace = Workspaces.FirstOrDefault();
            }
            else
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
            DetailItems.Clear();
            DetailActions.Clear();
            DetailActions.Add(new ActionItemViewModel("Open", string.Empty, false, "No workspace selected.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Attach", string.Empty, false, "Unavailable in Avalonia preview. Use WPF or CLI for now.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Validate", string.Empty, false, "No workspace selected.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Recover", string.Empty, false, "No workspace selected. Use WPF or CLI for now.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Save Point", string.Empty, false, "No workspace selected. Use WPF or CLI for now.", DisabledActionCommand));
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
            RaisePropertyChanged(nameof(HasWorkspaces));
            if (SelectedWorkspace is null)
            {
                SelectedWorkspace = summary;
            }

            return;
        }

        var wasSelected = ReferenceEquals(SelectedWorkspace, existingIndex.item)
            || string.Equals(SelectedWorkspace?.RootPath, summary.RootPath, StringComparison.OrdinalIgnoreCase);
        Workspaces[existingIndex.index] = summary;
        if (wasSelected)
        {
            SelectedWorkspace = summary;
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
            ? $"{duration.TotalSeconds:F1} s"
            : $"{Math.Max(1, duration.TotalMilliseconds):F0} ms";

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
        var snapshot = await _desktopShellService.CreateWorkspaceAsync(draft.WorkspaceRootPath, definition, new OperationTranscriptSink(this));
        await LoadAsync();
        SelectWorkspace(snapshot.Paths.RootPath);
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
        await LoadAsync();
        SelectWorkspace(snapshot.Paths.RootPath);
        DetailSummary = $"Imported existing Git checkout '{snapshot.Definition.Workspace.Name}'.";
    }

    private async Task OpenSelectedWorkspaceAsync()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        await _desktopShellService.OpenPathAsync(SelectedWorkspace.RootPath);
    }

    private void SelectWorkspace(string rootPath)
    {
        SelectedWorkspace = Workspaces.FirstOrDefault(item => string.Equals(item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ValidateSelectedWorkspaceInternalAsync()
    {
        if (SelectedWorkspace is null || ValidateWorkspaceAsync is null)
        {
            return;
        }

        await ValidateWorkspaceAsync(SelectedWorkspace.RootPath);
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
            SelectedWorkspace.SetOperationFailureState(exception.Message);
            throw;
        }

        var draft = await _interactionService.ShowSavePointDialogAsync(suggestion);
        if (draft is null)
        {
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Cancelled." });
            DetailSummary = "Save Point cancelled.";
            return;
        }

        await RunWorkspaceOperationAsync(
            "Create Save Point",
            "Creating Save Point...",
            (rootPath, snapshot, sink) => _desktopShellService.CreateSavePointAsync(rootPath, draft.Message, snapshot, sink),
            preserveExistingTranscript: true);
    }

    private async Task StartSelectedWorkspaceAsync()
    {
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

        WorkspaceRecoveryAssessment assessment;
        try
        {
            assessment = await _desktopShellService.AssessWorkspaceRecoveryAsync(SelectedWorkspace.RootPath, SelectedWorkspace.Snapshot);
        }
        catch (Exception exception)
        {
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.StandardError, Text = exception.Message });
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Failed." });
            DetailSummary = exception.Message;
            SelectedWorkspace.SetOperationFailureState(exception.Message);
            throw;
        }

        if (!await _interactionService.ConfirmRecoveryAsync(assessment))
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

    private async Task AttachSelectedWorkspaceAsync()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        StartOperationTranscript("Attach", SelectedWorkspace.Name);
        AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Preparing attach..." });
        DetailSummary = "Preparing attach...";

        await RunWorkspaceOperationAsync(
            "Attach",
            "Validating runtime...",
            (rootPath, snapshot, sink) => _desktopShellService.AttachWorkspaceAsync(rootPath, snapshot, sink),
            preserveExistingTranscript: true);
    }

    private async Task ReprovisionSelectedWorkspaceAsync()
    {
        if (!CanReprovisionSelectedWorkspace() || SelectedWorkspace is null)
        {
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

            var result = await _desktopShellService.ReprovisionWorkspaceAsync(
                SelectedWorkspace.RootPath,
                SelectedWorkspace.Snapshot,
                new OperationTranscriptSink(this));

            ReplaceSelectedWorkspace(result.Snapshot);
            ReprovisionStatusMessage = result.Message;
            CompleteOperationTranscript(result.Transcript);
            DetailSummary = result.Message;
        }
        catch (Exception exception)
        {
            var selectedWorkspaceRootPath = SelectedWorkspace?.RootPath ?? string.Empty;
            ReprovisionStatusMessage = GetActionableReprovisionFailure(exception.Message);
            SelectedWorkspace?.SetOperationFailureState(ReprovisionStatusMessage);
            DetailSummary = ReprovisionStatusMessage;
            DetailItems.Clear();
            DetailItems.Add(new DetailItemViewModel("Root path", selectedWorkspaceRootPath));
            DetailItems.Add(new DetailItemViewModel("Failure", ReprovisionStatusMessage));
            DetailActions.Clear();
            DetailActions.Add(new ActionItemViewModel("Reprovision", "Retry workspace regeneration and runtime provisioning.", CanReprovisionSelectedWorkspace(), string.Empty, ReprovisionWorkspaceCommand));
            DetailActions.Add(new ActionItemViewModel("Attach", string.Empty, false, "Unavailable in Avalonia preview. Use WPF or CLI for now.", DisabledActionCommand));
            DetailActions.Add(new ActionItemViewModel("Validate", "Run portable doctor and platform validation from the Diagnostics page.", true, string.Empty, ValidateSelectedWorkspaceCommand));
        }
        finally
        {
            IsReprovisioning = false;
            ReprovisionWorkspaceCommand.RaiseCanExecuteChanged();
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
    }

    private void ToggleOperationLogVisibility()
    {
        IsOperationLogVisible = !IsOperationLogVisible;
    }

    public void AppendOperationTranscriptLine(OperationTranscriptLine line)
    {
        if (LastOperationTranscript is null)
        {
            LastOperationTranscript = new OperationTranscript
            {
                OperationName = "Workspace operation",
                WorkspaceName = SelectedWorkspace?.Name ?? string.Empty,
                StartedUtc = line.Timestamp,
            };
        }

        LastOperationTranscript.Lines.Add(line);
        RefreshOperationLogText();
    }

    private void StartOperationTranscript(string operationName, string workspaceName)
    {
        LastOperationTranscript = new OperationTranscript
        {
            OperationName = operationName,
            WorkspaceName = workspaceName,
            StartedUtc = DateTimeOffset.UtcNow,
        };
        RefreshOperationLogText();
    }

    private async Task CopyOperationLogAsync()
    {
        if (_clipboardService is null || !HasOperationLog)
        {
            return;
        }

        await _clipboardService.SetTextAsync(GetCopyAllOperationLogText());
    }

    private void ClearOperationLog()
    {
        LastOperationTranscript = null;
        OperationLogText = string.Empty;
        IsOperationLogVisible = false;
        CopyOperationLogCommand.RaiseCanExecuteChanged();
        ClearOperationLogCommand.RaiseCanExecuteChanged();
    }

    public string GetCopyAllOperationLogText() => BuildOperationTranscriptText();

    private void RefreshOperationLogText()
    {
        OperationLogText = BuildOperationTranscriptText();
    }

    private string BuildOperationTranscriptText()
    {
        if (LastOperationTranscript is null || LastOperationTranscript.Lines.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, LastOperationTranscript.Lines.Select(FormatOperationTranscriptLine));
    }

    private void CompleteOperationTranscript(OperationTranscript transcript)
    {
        if (LastOperationTranscript is null)
        {
            LastOperationTranscript = transcript;
            RefreshOperationLogText();
            return;
        }

        LastOperationTranscript.CompletedUtc = transcript.CompletedUtc;
        LastOperationTranscript.Succeeded = transcript.Succeeded;
    }

    private void UpdateDetailPanel()
    {
        DetailItems.Clear();
        DetailActions.Clear();

        if (SelectedWorkspace is null)
        {
            DetailTitle = "No workspace selected";
            DetailSummary = "Select a workspace to inspect repository and runtime details.";
            return;
        }

        DetailTitle = SelectedWorkspace.Name;
        DetailSummary = BuildWorkspaceSummary(SelectedWorkspace);
        DetailItems.Add(new DetailItemViewModel("Root path", SelectedWorkspace.RootPath));
        DetailItems.Add(new DetailItemViewModel("Repository path", SelectedWorkspace.RepositoryPath));
        DetailItems.Add(new DetailItemViewModel("Current branch", SelectedWorkspace.CurrentBranch));
        DetailItems.Add(new DetailItemViewModel("Protection state", SelectedWorkspace.ProtectionLabel));
        DetailItems.Add(new DetailItemViewModel("Repository status", SelectedWorkspace.RepositoryStatus));
        DetailItems.Add(new DetailItemViewModel("Runtime-state status", SelectedWorkspace.LocalRuntimeStateStatus));
        DetailItems.Add(new DetailItemViewModel("Last activity", SelectedWorkspace.LastActivity));
        DetailItems.Add(new DetailItemViewModel("Services", SelectedWorkspace.Services));
        DetailItems.Add(new DetailItemViewModel("Features", SelectedWorkspace.Features));
        DetailItems.Add(new DetailItemViewModel("Runtime target", SelectedWorkspace.RuntimeTarget));
        if (SelectedWorkspace.HasError)
        {
            DetailItems.Add(new DetailItemViewModel("Load failure", SelectedWorkspace.ErrorMessage));
        }

        DetailActions.Add(new ActionItemViewModel("Open Folder", "Open the workspace folder with the host shell.", true, string.Empty, OpenSelectedWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Start", BuildStartDescription(SelectedWorkspace), CanStartSelectedWorkspace(), GetStartDisabledReason(SelectedWorkspace), StartWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Attach", BuildAttachDescription(SelectedWorkspace), CanAttachSelectedWorkspace(), GetAttachDisabledReason(SelectedWorkspace), AttachWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Recover", BuildRecoverDescription(SelectedWorkspace), CanRecoverSelectedWorkspace(), GetRecoverDisabledReason(SelectedWorkspace), RecoverWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Validate", BuildValidateDescription(SelectedWorkspace), CanValidateSelectedWorkspace(), GetValidateDisabledReason(SelectedWorkspace), ValidateSelectedWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Reprovision", BuildReprovisionDescription(SelectedWorkspace), CanReprovisionSelectedWorkspace(), GetReprovisionDisabledReason(SelectedWorkspace), ReprovisionWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Save Point", BuildSavePointDescription(SelectedWorkspace), CanCreateSavePointSelectedWorkspace(), GetSavePointDisabledReason(SelectedWorkspace), CreateSavePointCommand));

        RaisePropertyChanged(nameof(SelectedWorkspace));
    }

    private bool CanReprovisionSelectedWorkspace()
        => SelectedWorkspace is { HasSnapshot: true } && !IsReprovisioning;

    private bool CanStartSelectedWorkspace()
        => CanStartWorkspace(SelectedWorkspace);

    private bool CanRecoverSelectedWorkspace()
        => CanRecoverWorkspace(SelectedWorkspace);

    private bool CanAttachSelectedWorkspace()
        => CanAttachWorkspace(SelectedWorkspace);

    private bool CanValidateSelectedWorkspace()
        => SelectedWorkspace is { IsLoading: false };

    private bool CanCreateSavePointSelectedWorkspace()
        => CanCreateSavePointWorkspace(SelectedWorkspace);

    private string GetValidateDisabledReason(WorkspaceSummaryViewModel workspace)
        => workspace.IsLoading
            ? "Workspace details are still loading. Validation will be available when background checks finish."
            : string.Empty;

    private string GetStartDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanStartWorkspace(workspace) ? string.Empty : "Workspace root or configuration file is missing, so start cannot run.";

    private string GetRecoverDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanRecoverWorkspace(workspace) ? string.Empty : _interactionService is null ? "Workspace interaction services are unavailable." : "Workspace root or configuration file is missing, so recovery cannot run.";

    private string GetAttachDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanAttachWorkspace(workspace) ? string.Empty : "Workspace root or configuration file is missing, so attach cannot run.";

    private string GetSavePointDisabledReason(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanCreateSavePointWorkspace(workspace) ? string.Empty : _interactionService is null ? "Workspace interaction services are unavailable." : "Workspace root or configuration file is missing, so Save Point creation cannot run.";

    private static string BuildValidateDescription(WorkspaceSummaryViewModel workspace)
        => workspace.IsLoading
            ? "Loading workspace details before validation becomes available."
            : "Run portable doctor and platform validation from the Diagnostics page.";

    private string BuildStartDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : workspace.Snapshot?.RuntimeState == OpenCode.Workspace.Core.Models.WorkspaceRuntimeState.Running
                ? "Workspace runtime is already running. Start will re-check runtime readiness."
                : workspace.Snapshot?.LocalRuntimeState is null
                    ? "Runtime state is missing. Start will regenerate runtime files and bring the workspace online."
                    : "Start the workspace runtime and provision it if generated files are out of date.";

    private string BuildRecoverDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanRecoverWorkspace(workspace)
                ? "Assess and repair generated runtime files and container readiness without deleting user work."
                : "Workspace root or configuration file is missing, so recovery cannot run.";

    private string BuildAttachDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanAttachWorkspace(workspace)
                ? "Launch a terminal attach session after validating runtime readiness."
                : "Workspace root or configuration file is missing, so attach cannot run.";

    private string BuildSavePointDescription(WorkspaceSummaryViewModel workspace)
        => IsBusyForWorkspaceActions
            ? GetCurrentWorkspaceActionStatusMessage()
            : CanCreateSavePointWorkspace(workspace)
                ? "Capture the current local milestone for recovery using the shared Git-backed Save Point flow."
                : "Workspace root or configuration file is missing, so Save Point creation cannot run.";

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
            return "Runtime state is missing. Reprovision will regenerate local runtime state.";
        }

        if (workspace.IsLoading)
        {
            return string.IsNullOrWhiteSpace(workspace.LastActivity) ? "Loading details..." : workspace.LastActivity;
        }

        if (workspace.Snapshot?.UpdateRequired == true || workspace.Snapshot?.AppliedState is null)
        {
            return "Workspace files are out of date. Reprovision to regenerate runtime files.";
        }

        return "Regenerate runtime files, validate compose, and reprovision the workspace runtime.";
    }

    private string BuildWorkspaceSummary(WorkspaceSummaryViewModel workspace)
    {
        if (IsReprovisioning)
        {
            return string.IsNullOrWhiteSpace(ReprovisionStatusMessage) ? "Reprovision in progress." : ReprovisionStatusMessage;
        }

        if (workspace.Snapshot?.LocalRuntimeState is null)
        {
            return "Runtime state is missing. Reprovision will regenerate local runtime state.";
        }

        if (workspace.IsLoading)
        {
            return string.IsNullOrWhiteSpace(workspace.LastActivity) ? "Loading details..." : workspace.LastActivity;
        }

        if (workspace.Snapshot?.UpdateRequired == true || workspace.Snapshot?.AppliedState is null)
        {
            return "Workspace files are out of date. Reprovision to regenerate runtime files.";
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

        var relativeConfigurationPath = string.IsNullOrWhiteSpace(workspace.Record.ConfigurationPath)
            ? "workspace.yaml"
            : workspace.Record.ConfigurationPath.Replace('/', Path.DirectorySeparatorChar);
        var fullConfigurationPath = Path.Combine(workspace.RootPath, relativeConfigurationPath);
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
            Workspaces[index] = replacement;
            SelectedWorkspace = replacement;
        }
    }

    private static string GetActionableReprovisionFailure(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "Workspace reprovision failed. See Operation Log panel.";
        }

        var lines = error.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var exitCode = lines.FirstOrDefault(line => line.StartsWith("Exit code:", StringComparison.OrdinalIgnoreCase));
        return exitCode is null
            ? "Workspace reprovision failed. See Operation Log panel."
            : $"Workspace reprovision failed. {exitCode}. See Operation Log panel.";
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

    private sealed class OperationTranscriptSink : IOperationLogSink
    {
        private readonly WorkspacesPageViewModel _owner;

        public OperationTranscriptSink(WorkspacesPageViewModel owner)
        {
            _owner = owner;
        }

        public void Append(OperationTranscriptLine line)
        {
            void Apply()
            {
                if (line.Kind is OperationTranscriptLineKind.Status or OperationTranscriptLineKind.Comment)
                {
                    _owner.ReprovisionStatusMessage = line.Text;
                    _owner.DetailSummary = line.Text;
                    _owner.SelectedWorkspace?.SetReprovisioningState(line.Text);
                }
                _owner.AppendOperationTranscriptLine(line);
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Apply();
                return;
            }

            Dispatcher.UIThread.Post(Apply);
        }
    }

    private async Task RunWorkspaceOperationAsync(string operationName, string initialStatusMessage, Func<string, OpenCode.Workspace.Core.Models.WorkspaceSnapshot?, IOperationLogSink, Task<WorkspaceOperationResult>> operation, bool preserveExistingTranscript = false)
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        try
        {
            _isWorkspaceActionRunning = true;
            _workspaceActionStatusMessage = initialStatusMessage;
            RaiseWorkspaceActionCommandStates();
            if (!preserveExistingTranscript)
            {
                StartOperationTranscript(operationName, SelectedWorkspace.Name);
            }
            AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = initialStatusMessage });
            DetailSummary = initialStatusMessage;
            var sink = new OperationTranscriptSink(this);
            var result = await operation(SelectedWorkspace.RootPath, SelectedWorkspace.Snapshot, sink);
            ReplaceSelectedWorkspace(result.Snapshot);
            CompleteOperationTranscript(result.Transcript);
            _workspaceActionStatusMessage = result.Message;
            DetailSummary = result.Message;
        }
        catch (Exception exception)
        {
            _workspaceActionStatusMessage = exception.Message;
            SelectedWorkspace?.SetOperationFailureState(exception.Message);
            DetailSummary = exception.Message;
            throw;
        }
        finally
        {
            _isWorkspaceActionRunning = false;
            RaiseWorkspaceActionCommandStates();
        }
    }

    private string GetCurrentWorkspaceActionStatusMessage()
        => string.IsNullOrWhiteSpace(_workspaceActionStatusMessage) ? "Workspace action in progress." : _workspaceActionStatusMessage;

    private void RaiseWorkspaceActionCommandStates()
    {
        CreateWorkspaceCommand.RaiseCanExecuteChanged();
        OpenExistingRepositoryCommand.RaiseCanExecuteChanged();
        RefreshWorkspacesCommand.RaiseCanExecuteChanged();
        CreateSavePointCommand.RaiseCanExecuteChanged();
        StartWorkspaceCommand.RaiseCanExecuteChanged();
        RecoverWorkspaceCommand.RaiseCanExecuteChanged();
        AttachWorkspaceCommand.RaiseCanExecuteChanged();
        ValidateSelectedWorkspaceCommand.RaiseCanExecuteChanged();
        ReprovisionWorkspaceCommand.RaiseCanExecuteChanged();
    }
}
