using System.Collections.ObjectModel;
using Avalonia.Threading;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspacesPageViewModel : PageViewModel
{
    private readonly IDesktopShellService _desktopShellService;
    private WorkspaceSummaryViewModel? _selectedWorkspace;
    private string _emptyStateTitle = string.Empty;
    private string _emptyStateMessage = string.Empty;
    private WorkspaceLoadReport _workspaceLoadReport = new();
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

    public WorkspacesPageViewModel(IDesktopShellService desktopShellService)
        : base("Workspaces", "Inspect local workspaces, repository state, and runtime readiness.")
    {
        _desktopShellService = desktopShellService;
        OpenSelectedWorkspaceCommand = new AsyncRelayCommand(OpenSelectedWorkspaceAsync, () => SelectedWorkspace is not null);
        ValidateSelectedWorkspaceCommand = new AsyncRelayCommand(ValidateSelectedWorkspaceInternalAsync, () => SelectedWorkspace is not null);
        ReprovisionWorkspaceCommand = new AsyncRelayCommand(ReprovisionSelectedWorkspaceAsync, CanReprovisionSelectedWorkspace);
        CopyOperationLogCommand = new AsyncRelayCommand(CopyOperationLogAsync, () => HasOperationLog && _clipboardService is not null);
        ClearOperationLogCommand = new RelayCommand(ClearOperationLog, () => HasOperationLog);
        ToggleOperationLogVisibilityCommand = new RelayCommand(ToggleOperationLogVisibility);
        DisabledActionCommand = new RelayCommand(() => { });
        SetLoadingState();
    }

    public ObservableCollection<WorkspaceSummaryViewModel> Workspaces { get; } = [];
    public AsyncRelayCommand OpenSelectedWorkspaceCommand { get; }
    public AsyncRelayCommand ValidateSelectedWorkspaceCommand { get; }
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
            var loadResult = await _desktopShellService.LoadWorkspaceItemsAsync(includeRuntimeInspection: true, cancellationToken);
            WorkspaceLoadReport = loadResult.Report;
            foreach (var item in loadResult.Items.OrderBy(item => string.IsNullOrWhiteSpace(item.Record.Name) ? item.Record.RootPath : item.Record.Name, StringComparer.OrdinalIgnoreCase))
            {
                Workspaces.Add(new WorkspaceSummaryViewModel(item));
            }

            HasLoadError = false;
            LoadErrorMessage = string.Empty;
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
            SelectedWorkspace = Workspaces.FirstOrDefault();
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

    private void SetLoadingState()
    {
        IsLoading = true;
        HasLoadError = false;
        LoadErrorMessage = string.Empty;
        EmptyStateTitle = "Loading workspaces...";
        EmptyStateMessage = "Reading the shared workspace index and snapshot state.";
        DetailTitle = "Workspaces";
        DetailSummary = "Loading workspace index and snapshot state.";
        RaisePropertyChanged(nameof(ShowLoadingState));
        RaisePropertyChanged(nameof(ShowErrorState));
        RaisePropertyChanged(nameof(ShowEmptyState));
    }

    private async Task OpenSelectedWorkspaceAsync()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        await _desktopShellService.OpenPathAsync(SelectedWorkspace.RootPath);
    }

    private async Task ValidateSelectedWorkspaceInternalAsync()
    {
        if (SelectedWorkspace is null || ValidateWorkspaceAsync is null)
        {
            return;
        }

        await ValidateWorkspaceAsync(SelectedWorkspace.RootPath);
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
            ReprovisionStatusMessage = "Preparing workspace";
            StartOperationTranscript("Reprovision", SelectedWorkspace.Name);
            UpdateDetailPanel();

            var result = await _desktopShellService.ReprovisionWorkspaceAsync(
                SelectedWorkspace.RootPath,
                new OperationTranscriptSink(this));

            ReplaceSelectedWorkspace(result.Snapshot);
            ReprovisionStatusMessage = result.Message;
            CompleteOperationTranscript(result.Transcript);
            DetailSummary = result.Message;
        }
        catch (Exception exception)
        {
            ReprovisionStatusMessage = GetActionableReprovisionFailure(exception.Message);
            DetailSummary = ReprovisionStatusMessage;
            DetailItems.Clear();
            DetailItems.Add(new DetailItemViewModel("Root path", SelectedWorkspace.RootPath));
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

        DetailActions.Add(new ActionItemViewModel("Open", "Open the workspace folder with the host shell.", true, string.Empty, OpenSelectedWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Validate", "Run portable doctor and platform validation from the Diagnostics page.", true, string.Empty, ValidateSelectedWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Reprovision", BuildReprovisionDescription(SelectedWorkspace), CanReprovisionSelectedWorkspace(), GetReprovisionDisabledReason(SelectedWorkspace), ReprovisionWorkspaceCommand));
        DetailActions.Add(new ActionItemViewModel("Attach", string.Empty, false, "Unavailable in Avalonia preview. Use WPF or CLI for now.", DisabledActionCommand));
        DetailActions.Add(new ActionItemViewModel("Recover", string.Empty, false, SelectedWorkspace.HasError ? "Workspace must load successfully before recovery UI can be offered in Avalonia. Use WPF or CLI for now." : "Recovery actions are not ported yet. Use WPF or CLI for now.", DisabledActionCommand));
        DetailActions.Add(new ActionItemViewModel("Save Point", string.Empty, false, SelectedWorkspace.HasError ? "Workspace must load successfully before Save Point operations can run. Use WPF or CLI for now." : "Save Point creation is not implemented in Avalonia preview yet.", DisabledActionCommand));

        RaisePropertyChanged(nameof(SelectedWorkspace));
    }

    private bool CanReprovisionSelectedWorkspace()
        => SelectedWorkspace is { HasSnapshot: true } && !IsReprovisioning;

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

        if (workspace.Snapshot?.UpdateRequired == true || workspace.Snapshot?.AppliedState is null)
        {
            return "Workspace files are out of date. Reprovision to regenerate runtime files.";
        }

        return workspace.SafetyState;
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
                _owner.ReprovisionStatusMessage = line.Text;
                _owner.DetailSummary = line.Text;
                _owner.AppendOperationTranscriptLine(line);
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Apply();
                return;
            }

            Dispatcher.UIThread.InvokeAsync(Apply).GetAwaiter().GetResult();
        }
    }
}
