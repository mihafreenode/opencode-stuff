using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Manager.Services;

namespace OpenCode.Workspace.Manager.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly WorkspaceOrchestrator _workspaceOrchestrator;
    private readonly BuiltInCatalogProvider _catalogProvider;
    private readonly EnvironmentDiagnostics _environmentDiagnostics;
    private readonly PoLocalizationService _localization;
    private readonly WindowsHostCapabilities _windowsHostCapabilities;
    private readonly WindowsTerminalProfileManager _profileManager;
    private readonly DockerService _dockerService;
    private readonly NerdFontInstaller _nerdFontInstaller;
    private readonly WorkspaceSavePointMessageService _savePointMessageService;
    private readonly AgentProfileResolver _agentProfileResolver = new();
    private readonly Dictionary<string, List<WorkspaceLogLineViewModel>> _workspaceLogsByPath = new(StringComparer.OrdinalIgnoreCase);
    private WorkspaceListItemViewModel? _selectedWorkspace;
    private TemplateManifest? _selectedTemplate;
    private string _newWorkspaceName = "demo-workspace";
    private string _newWorkspacePath;
    private string _statusMessage;
    private bool _isBusy;
    private string _currentLogText = string.Empty;
    private string _selectedPromptProvider = "starship";
    private string _selectedFontFamily = "JetBrainsMono Nerd Font";
    private bool _installTerminalIfMissing = true;
    private bool _installZoxide;
    private bool _installFzf;

    public MainWindowViewModel(
        WorkspaceOrchestrator workspaceOrchestrator,
        BuiltInCatalogProvider catalogProvider,
        EnvironmentDiagnostics environmentDiagnostics,
        PoLocalizationService localization,
        WindowsHostCapabilities windowsHostCapabilities,
        WindowsTerminalProfileManager profileManager,
        DockerService dockerService,
        NerdFontInstaller nerdFontInstaller,
        WorkspaceSavePointMessageService savePointMessageService)
    {
        _workspaceOrchestrator = workspaceOrchestrator;
        _catalogProvider = catalogProvider;
        _environmentDiagnostics = environmentDiagnostics;
        _localization = localization;
        _windowsHostCapabilities = windowsHostCapabilities;
        _profileManager = profileManager;
        _dockerService = dockerService;
        _nerdFontInstaller = nerdFontInstaller;
        _savePointMessageService = savePointMessageService;

        var defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OpenCode Workspaces");
        _newWorkspacePath = Path.Combine(defaultRoot, _newWorkspaceName);
        _statusMessage = localization.Get("status.none");

        Workspaces = new ObservableCollection<WorkspaceListItemViewModel>();
        AvailableFeatures = new ObservableCollection<SelectableItemViewModel>();
        AvailableServices = new ObservableCollection<SelectableItemViewModel>();
        Diagnostics = new ObservableCollection<DiagnosticResult>();
        Templates = new ObservableCollection<TemplateManifest>(_catalogProvider.LoadTemplates());
        CurrentLogLines = new ObservableCollection<WorkspaceLogLineViewModel>();
        PromptProviders = new ObservableCollection<string>(["starship", "default-bash", "custom"]);
        FontFamilies = new ObservableCollection<string>(["JetBrainsMono Nerd Font", "CaskaydiaCove Nerd Font", "FiraCode Nerd Font"]);

        Title = localization.Get("app.title");
        DashboardTitle = localization.Get("dashboard.title");
        DashboardSubtitle = localization.Get("dashboard.subtitle");
        WorkspacesTitle = localization.Get("workspaces.title");
        HealthTitle = localization.Get("health.title");
        CreateLabel = localization.Get("actions.create");
        OpenWorkspaceLabel = localization.Get("actions.openWorkspace");
        PrepareWorkspaceLabel = localization.Get("actions.prepareWorkspace");
        PrepareUpdateLabel = localization.Get("actions.prepareUpdate");
        UpdateNowLabel = localization.Get("actions.updateNow");
        ShutDownLabel = localization.Get("actions.shutDown");
        CreateSavePointLabel = localization.Get("actions.createSavePoint");
        CreateCheckpointLabel = localization.Get("actions.createCheckpoint");
        PublishLabel = localization.Get("actions.publish");
        ExportBackupLabel = localization.Get("actions.exportBackup");
        ConfigureRemoteBackupLabel = localization.Get("actions.configureRemoteBackup");
        ContinueWorkingLabel = localization.Get("actions.continueWorking");
        OpenAdvancedGitViewLabel = localization.Get("actions.openAdvancedGitView");
        ExportPatchLabel = localization.Get("actions.exportPatch");
        UpdateWorkspaceLabel = localization.Get("actions.updateWorkspace");
        PublishReviewWorkingCopyLabel = localization.Get("actions.publishReviewWorkingCopy");
        DismissLabel = localization.Get("actions.dismiss");
        RemoveLabel = localization.Get("actions.remove");
        RefreshLabel = localization.Get("actions.refresh");
        OpenFolderLabel = localization.Get("actions.openFolder");
        CopyPathLabel = localization.Get("actions.copyPath");
        NameLabel = localization.Get("create.name");
        PathLabel = localization.Get("create.path");
        TemplateLabel = localization.Get("create.template");
        FeaturesLabel = localization.Get("create.features");
        ServicesLabel = localization.Get("create.services");
        TerminalSettingsTitle = localization.Get("terminal.title");
        PromptLabel = localization.Get("terminal.prompt");
        FontLabel = localization.Get("terminal.font");
        InstallIfMissingLabel = localization.Get("terminal.installIfMissing");
        ZoxideLabel = localization.Get("terminal.zoxide");
        FzfLabel = localization.Get("terminal.fzf");
        InstallFontLabel = localization.Get("terminal.installFont");
        CancelLabel = localization.Get("actions.cancel");
        SelectedWorkspaceTitle = localization.Get("selected.title");
        SelectedNameLabel = localization.Get("field.name");
        SelectedPathLabel = localization.Get("field.path");
        SelectedImageLabel = localization.Get("field.image");
        SelectedFeaturesLabel = localization.Get("field.features");
        SelectedServicesLabel = localization.Get("field.services");
        SelectedAgentLabel = localization.Get("field.agent");
        SelectedStatusLabel = localization.Get("field.status");
        SelectedLastOperationLabel = localization.Get("field.lastOperation");
        SelectedServicesStatusLabel = localization.Get("field.servicesStatus");
        EncodingValidationLabel = localization.Get("field.encodingValidation");
        SafetyTitle = localization.Get("safety.title");
        LocalRecoveryLabel = localization.Get("safety.localRecovery");
        BackupLabel = localization.Get("safety.offMachineBackup");
        WorkingCopyLabel = localization.Get("safety.workingCopy");
        RemoteNameLabel = localization.Get("safety.remoteName");
        RemoteBranchLabel = localization.Get("safety.remoteBranch");
        AheadBehindLabel = localization.Get("safety.aheadBehind");
        ConflictingFilesLabel = localization.Get("safety.conflictingFiles");
        LatestSavePointLabel = localization.Get("safety.latestSavePoint");
        LatestCheckpointLabel = localization.Get("safety.latestCheckpoint");
        UncommittedChangesLabel = localization.Get("safety.uncommittedChanges");
        UntrackedFilesLabel = localization.Get("safety.untrackedFiles");
        RemoteConfiguredLabel = localization.Get("safety.remoteConfigured");
        UnpublishedSavePointsLabel = localization.Get("safety.unpublishedSavePoints");
        WorkingCopyPublishedLabel = localization.Get("safety.workingCopyPublished");
        LastPublishLabel = localization.Get("safety.lastPublish");
        EncodingValidationSample = localization.Get("utf8.validation");
        EmptySelectionTitle = localization.Get("empty.selection.title");
        EmptySelectionDescription = localization.Get("empty.selection.description");
        EmptySelectionHintPrimary = localization.Get("empty.selection.hintPrimary");
        EmptySelectionHintSecondary = localization.Get("empty.selection.hintSecondary");
        OnboardingTitle = localization.Get("onboarding.title");
        OnboardingDescription = localization.Get("onboarding.description");
        OnboardingActionLabel = localization.Get("actions.create");
        CreateWorkspaceDialogTitle = localization.Get("create.dialog.title");
        CreateWorkspaceDialogDescription = localization.Get("create.dialog.description");

        CreateWorkspaceCommand = new AsyncRelayCommand(CreateWorkspaceAsync, CanCreateWorkspace);
        PrimaryWorkspaceActionCommand = new AsyncRelayCommand(ExecutePrimaryWorkspaceActionAsync, HasSelectedWorkspace);
        OpenWorkspaceCommand = new AsyncRelayCommand(OpenWorkspaceAsync, HasSelectedWorkspace);
        PrepareWorkspaceCommand = new AsyncRelayCommand(PrepareWorkspaceAsync, HasSelectedWorkspace);
        ShutDownWorkspaceCommand = new AsyncRelayCommand(ShutDownWorkspaceAsync, HasSelectedWorkspace);
        CreateSavePointCommand = new AsyncRelayCommand(CreateSavePointAsync, HasSelectedWorkspace);
        CreateCheckpointCommand = new AsyncRelayCommand(CreateCheckpointAsync, HasSelectedWorkspace);
        PublishWorkspaceCommand = new AsyncRelayCommand(PublishWorkspaceAsync, HasSelectedWorkspace);
        ExportBackupCommand = new AsyncRelayCommand(ExportBackupAsync, HasSelectedWorkspace);
        ConfigureRemoteBackupCommand = new RelayCommand(ConfigureRemoteBackup, HasSelectedWorkspace);
        ContinueWorkingCommand = new RelayCommand(ContinueWorking, HasSelectedWorkspace);
        OpenAdvancedGitViewCommand = new RelayCommand(OpenAdvancedGitView, HasSelectedWorkspace);
        ExportPatchCommand = new AsyncRelayCommand(ExportPatchAsync, HasSelectedWorkspace);
        UpdateWorkspaceFromRemoteCommand = new AsyncRelayCommand(UpdateWorkspaceFromRemoteAsync, HasSelectedWorkspace);
        PublishReviewWorkingCopyCommand = new AsyncRelayCommand(PublishReviewWorkingCopyAsync, HasSelectedWorkspace);
        DismissSafetyNoticeCommand = new RelayCommand(DismissSafetyNotice, HasSelectedWorkspace);
        RefreshCommand = new AsyncRelayCommand(InitializeAsync, () => !IsBusy);
        OpenFolderCommand = new RelayCommand(OpenSelectedWorkspaceFolder, () => HasSelectedWorkspace() && !IsBusy);
        CopyWorkspacePathCommand = new RelayCommand(CopySelectedWorkspacePath, () => HasSelectedWorkspace() && !IsBusy);
        RemoveWorkspaceCommand = new AsyncRelayCommand(RemoveWorkspaceAsync, HasSelectedWorkspace);
        InstallSelectedFontCommand = new AsyncRelayCommand(InstallSelectedFontAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(SelectedFontFamily));

        LoadCatalogSelections();
        SelectedTemplate = Templates.FirstOrDefault(template => string.Equals(template.Id, "general-development", StringComparison.OrdinalIgnoreCase))
            ?? Templates.FirstOrDefault();
    }

    public ObservableCollection<WorkspaceListItemViewModel> Workspaces { get; }
    public ObservableCollection<SelectableItemViewModel> AvailableFeatures { get; }
    public ObservableCollection<SelectableItemViewModel> AvailableServices { get; }
    public ObservableCollection<DiagnosticResult> Diagnostics { get; }
    public ObservableCollection<TemplateManifest> Templates { get; }
    public ObservableCollection<WorkspaceLogLineViewModel> CurrentLogLines { get; }
    public ObservableCollection<string> PromptProviders { get; }
    public ObservableCollection<string> FontFamilies { get; }

    public string Title { get; }
    public string DashboardTitle { get; }
    public string DashboardSubtitle { get; }
    public string WorkspacesTitle { get; }
    public string HealthTitle { get; }
    public string CreateLabel { get; }
    public string OpenWorkspaceLabel { get; }
    public string PrepareWorkspaceLabel { get; }
    public string PrepareUpdateLabel { get; }
    public string UpdateNowLabel { get; }
    public string ShutDownLabel { get; }
    public string CreateSavePointLabel { get; }
    public string CreateCheckpointLabel { get; }
    public string PublishLabel { get; }
    public string ExportBackupLabel { get; }
    public string ConfigureRemoteBackupLabel { get; }
    public string ContinueWorkingLabel { get; }
    public string OpenAdvancedGitViewLabel { get; }
    public string ExportPatchLabel { get; }
    public string UpdateWorkspaceLabel { get; }
    public string PublishReviewWorkingCopyLabel { get; }
    public string DismissLabel { get; }
    public string RemoveLabel { get; }
    public string RefreshLabel { get; }
    public string OpenFolderLabel { get; }
    public string CopyPathLabel { get; }
    public string NameLabel { get; }
    public string PathLabel { get; }
    public string TemplateLabel { get; }
    public string FeaturesLabel { get; }
    public string ServicesLabel { get; }
    public string TerminalSettingsTitle { get; }
    public string PromptLabel { get; }
    public string FontLabel { get; }
    public string InstallIfMissingLabel { get; }
    public string ZoxideLabel { get; }
    public string FzfLabel { get; }
    public string InstallFontLabel { get; }
    public string CancelLabel { get; }
    public string SelectedWorkspaceTitle { get; }
    public string SelectedNameLabel { get; }
    public string SelectedPathLabel { get; }
    public string SelectedImageLabel { get; }
    public string SelectedFeaturesLabel { get; }
    public string SelectedServicesLabel { get; }
    public string SelectedAgentLabel { get; }
    public string SelectedStatusLabel { get; }
    public string SelectedLastOperationLabel { get; }
    public string SelectedServicesStatusLabel { get; }
    public string EncodingValidationLabel { get; }
    public string SafetyTitle { get; }
    public string LocalRecoveryLabel { get; }
    public string BackupLabel { get; }
    public string WorkingCopyLabel { get; }
    public string RemoteNameLabel { get; }
    public string RemoteBranchLabel { get; }
    public string AheadBehindLabel { get; }
    public string ConflictingFilesLabel { get; }
    public string LatestSavePointLabel { get; }
    public string LatestCheckpointLabel { get; }
    public string UncommittedChangesLabel { get; }
    public string UntrackedFilesLabel { get; }
    public string RemoteConfiguredLabel { get; }
    public string UnpublishedSavePointsLabel { get; }
    public string WorkingCopyPublishedLabel { get; }
    public string LastPublishLabel { get; }
    public string EncodingValidationSample { get; }
    public string EmptySelectionTitle { get; }
    public string EmptySelectionDescription { get; }
    public string EmptySelectionHintPrimary { get; }
    public string EmptySelectionHintSecondary { get; }
    public string OnboardingTitle { get; }
    public string OnboardingDescription { get; }
    public string OnboardingActionLabel { get; }
    public string CreateWorkspaceDialogTitle { get; }
    public string CreateWorkspaceDialogDescription { get; }

    public AsyncRelayCommand CreateWorkspaceCommand { get; }
    public AsyncRelayCommand PrimaryWorkspaceActionCommand { get; }
    public AsyncRelayCommand OpenWorkspaceCommand { get; }
    public AsyncRelayCommand PrepareWorkspaceCommand { get; }
    public AsyncRelayCommand ShutDownWorkspaceCommand { get; }
    public AsyncRelayCommand CreateSavePointCommand { get; }
    public AsyncRelayCommand CreateCheckpointCommand { get; }
    public AsyncRelayCommand PublishWorkspaceCommand { get; }
    public AsyncRelayCommand ExportBackupCommand { get; }
    public RelayCommand ConfigureRemoteBackupCommand { get; }
    public RelayCommand ContinueWorkingCommand { get; }
    public RelayCommand OpenAdvancedGitViewCommand { get; }
    public AsyncRelayCommand ExportPatchCommand { get; }
    public AsyncRelayCommand UpdateWorkspaceFromRemoteCommand { get; }
    public AsyncRelayCommand PublishReviewWorkingCopyCommand { get; }
    public RelayCommand DismissSafetyNoticeCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand CopyWorkspacePathCommand { get; }
    public AsyncRelayCommand RemoveWorkspaceCommand { get; }
    public AsyncRelayCommand InstallSelectedFontCommand { get; }

    public WorkspaceListItemViewModel? SelectedWorkspace
    {
        get => _selectedWorkspace;
        set
        {
            if (SetProperty(ref _selectedWorkspace, value))
            {
                RaisePropertyChanged(nameof(SelectedWorkspaceName));
                RaisePropertyChanged(nameof(SelectedWorkspacePath));
                RaisePropertyChanged(nameof(SelectedWorkspaceImage));
                RaisePropertyChanged(nameof(SelectedWorkspaceFeatures));
                RaisePropertyChanged(nameof(SelectedWorkspaceServices));
                RaisePropertyChanged(nameof(SelectedWorkspaceAgent));
                RaisePropertyChanged(nameof(SelectedWorkspaceStatus));
                RaisePropertyChanged(nameof(SelectedWorkspaceLastOperation));
                RaisePropertyChanged(nameof(SelectedWorkspaceServicesStatus));
                RaisePropertyChanged(nameof(SelectedWorkspaceSafetyStatus));
                RaisePropertyChanged(nameof(SelectedWorkspaceSafetyMessage));
                RaisePropertyChanged(nameof(SelectedWorkspaceWorkingCopy));
                RaisePropertyChanged(nameof(SelectedWorkspaceRemoteName));
                RaisePropertyChanged(nameof(SelectedWorkspaceRemoteBranch));
                RaisePropertyChanged(nameof(SelectedWorkspaceAheadBehind));
                RaisePropertyChanged(nameof(SelectedWorkspaceConflictingFiles));
                RaisePropertyChanged(nameof(SelectedWorkspaceLatestSavePoint));
                RaisePropertyChanged(nameof(SelectedWorkspaceLatestCheckpoint));
                RaisePropertyChanged(nameof(SelectedWorkspaceUncommittedChanges));
                RaisePropertyChanged(nameof(SelectedWorkspaceUntrackedFiles));
                RaisePropertyChanged(nameof(SelectedWorkspaceRemoteConfigured));
                RaisePropertyChanged(nameof(SelectedWorkspaceUnpublishedSavePoints));
                RaisePropertyChanged(nameof(SelectedWorkspaceWorkingCopyPublished));
                RaisePropertyChanged(nameof(SelectedWorkspaceLastPublish));
                RaisePropertyChanged(nameof(SelectedWorkspaceShortPath));
                RaisePropertyChanged(nameof(SelectedPrimaryActionLabel));
                RaisePropertyChanged(nameof(HasSelectedWorkspaceItem));
                RaisePropertyChanged(nameof(HasAnyWorkspaces));
                RaisePropertyChanged(nameof(ShowOnboardingState));
                RaisePropertyChanged(nameof(ShowSelectionGuidanceState));
                RaisePropertyChanged(nameof(ShowWorkspaceDetails));
                RaisePropertyChanged(nameof(ShowNoRemoteActions));
                RaisePropertyChanged(nameof(ShowUnpublishedSavePointActions));
                RaisePropertyChanged(nameof(ShowNeedsReviewActions));
                LoadVisibleLogsForSelectedWorkspace();
                RaiseCommandStates();
            }
        }
    }

    public TemplateManifest? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetProperty(ref _selectedTemplate, value) && value is not null)
            {
                ApplyTemplate(value);
            }
        }
    }

    public string NewWorkspaceName
    {
        get => _newWorkspaceName;
        set
        {
            if (SetProperty(ref _newWorkspaceName, value))
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OpenCode Workspaces");
                    NewWorkspacePath = Path.Combine(defaultRoot, value.Trim());
                }

                RaisePropertyChanged(nameof(CanCreateWorkspaceForDialog));
                RaisePropertyChanged(nameof(WorkspaceNameValidationMessage));
                RaiseCommandStates();
            }
        }
    }

    public string NewWorkspacePath
    {
        get => _newWorkspacePath;
        set
        {
            if (SetProperty(ref _newWorkspacePath, value))
            {
                RaisePropertyChanged(nameof(CanCreateWorkspaceForDialog));
                RaisePropertyChanged(nameof(WorkspacePathValidationMessage));
                RaiseCommandStates();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string CurrentLogText
    {
        get => _currentLogText;
        private set => SetProperty(ref _currentLogText, value);
    }

    public string SelectedPromptProvider
    {
        get => _selectedPromptProvider;
        set => SetProperty(ref _selectedPromptProvider, value);
    }

    public string SelectedFontFamily
    {
        get => _selectedFontFamily;
        set => SetProperty(ref _selectedFontFamily, value);
    }

    public bool InstallTerminalIfMissing
    {
        get => _installTerminalIfMissing;
        set => SetProperty(ref _installTerminalIfMissing, value);
    }

    public bool InstallZoxide
    {
        get => _installZoxide;
        set => SetProperty(ref _installZoxide, value);
    }

    public bool InstallFzf
    {
        get => _installFzf;
        set => SetProperty(ref _installFzf, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string SelectedWorkspaceName => SelectedWorkspace?.Name ?? "-";
    public string SelectedWorkspacePath => SelectedWorkspace?.RootPath ?? "-";
    public string SelectedWorkspaceShortPath => SelectedWorkspace?.ShortRootPath ?? "-";
    public string SelectedWorkspaceImage => SelectedWorkspace?.Image ?? "-";
    public string SelectedWorkspaceFeatures => SelectedWorkspace?.FeaturesSummary ?? "-";
    public string SelectedWorkspaceServices => SelectedWorkspace?.ServicesSummary ?? "-";
    public string SelectedWorkspaceStatus => SelectedWorkspace?.StatusLabel ?? "-";
    public string SelectedWorkspaceLastOperation => SelectedWorkspace?.LastOperationResult ?? "-";
    public string SelectedWorkspaceServicesStatus => SelectedWorkspace?.ServicesStatusSummary ?? "-";
    public string SelectedWorkspaceSafetyStatus => SelectedWorkspace is null
        ? "-"
        : GetSafetyStatusLabel(SelectedWorkspace.Snapshot.Safety.OverallStatus);
    public string SelectedWorkspaceSafetyMessage => SelectedWorkspace?.SafetyMessage ?? "-";
    public string SelectedWorkspaceWorkingCopy => string.IsNullOrWhiteSpace(SelectedWorkspace?.Snapshot.Safety.WorkingCopyName)
        ? _localization.Get("safety.workingCopyUnavailable")
        : SelectedWorkspace.Snapshot.Safety.WorkingCopyName;
    public string SelectedWorkspaceRemoteName => string.IsNullOrWhiteSpace(SelectedWorkspace?.Snapshot.Safety.AdvancedGit.RemoteName)
        ? _localization.Get("safety.noneRecorded")
        : SelectedWorkspace.Snapshot.Safety.AdvancedGit.RemoteName;
    public string SelectedWorkspaceRemoteBranch => string.IsNullOrWhiteSpace(SelectedWorkspace?.Snapshot.Safety.AdvancedGit.RemoteBranch)
        ? _localization.Get("safety.noneRecorded")
        : SelectedWorkspace.Snapshot.Safety.AdvancedGit.RemoteBranch;
    public string SelectedWorkspaceAheadBehind => SelectedWorkspace is null
        ? "-"
        : string.Format(
            _localization.Get("safety.aheadBehindValue"),
            SelectedWorkspace.Snapshot.Safety.AdvancedGit.AheadCount,
            SelectedWorkspace.Snapshot.Safety.AdvancedGit.BehindCount);
    public string SelectedWorkspaceConflictingFiles => SelectedWorkspace is null
        ? "-"
        : SelectedWorkspace.Snapshot.Safety.AdvancedGit.ConflictingFiles.Count == 0
            ? _localization.Get("safety.noConflictingFiles")
            : string.Join(Environment.NewLine, SelectedWorkspace.Snapshot.Safety.AdvancedGit.ConflictingFiles);
    public string SelectedWorkspaceLatestSavePoint => FormatRelativeTime(SelectedWorkspace?.Snapshot.Safety.LocalRecovery.LatestSavePointUtc);
    public string SelectedWorkspaceLatestCheckpoint => FormatRelativeTime(SelectedWorkspace?.Snapshot.Safety.LocalRecovery.LatestCheckpointUtc);
    public string SelectedWorkspaceUncommittedChanges => SelectedWorkspace is null
        ? "-"
        : SelectedWorkspace.Snapshot.Safety.LocalRecovery.HasUncommittedChanges
            ? string.Format(_localization.Get("safety.changedFiles"), SelectedWorkspace.Snapshot.Safety.LocalRecovery.UncommittedChangeCount)
            : _localization.Get("safety.noUncommittedChanges");
    public string SelectedWorkspaceUntrackedFiles => SelectedWorkspace is null
        ? "-"
        : SelectedWorkspace.Snapshot.Safety.LocalRecovery.UntrackedFileCount == 0
            ? _localization.Get("safety.noUntrackedFiles")
            : SelectedWorkspace.Snapshot.Safety.LocalRecovery.AreUntrackedFilesProtected
                ? string.Format(_localization.Get("safety.untrackedProtected"), SelectedWorkspace.Snapshot.Safety.LocalRecovery.UntrackedFileCount)
                : string.Format(_localization.Get("safety.untrackedNotProtected"), SelectedWorkspace.Snapshot.Safety.LocalRecovery.UntrackedFileCount);
    public string SelectedWorkspaceRemoteConfigured => FormatYesNo(SelectedWorkspace?.Snapshot.Safety.Backup.HasRemoteConfigured);
    public string SelectedWorkspaceUnpublishedSavePoints => FormatYesNo(SelectedWorkspace?.Snapshot.Safety.Backup.HasUnpublishedSavePoints);
    public string SelectedWorkspaceWorkingCopyPublished => FormatYesNo(SelectedWorkspace?.Snapshot.Safety.Backup.IsCurrentWorkingCopyPublished);
    public string SelectedWorkspaceLastPublish => FormatRelativeTime(SelectedWorkspace?.Snapshot.Safety.Backup.LastSuccessfulPublishUtc);
    public string SelectedPrimaryActionLabel => SelectedWorkspace is null
        ? OpenWorkspaceLabel
        : SelectedWorkspace.HasError
            ? _localization.Get("actions.viewError")
            : OpenWorkspaceLabel;
    public string SelectedWorkspaceAgent => SelectedWorkspace is null
        ? "-"
        : _agentProfileResolver.Resolve(SelectedWorkspace.Snapshot.Definition).ProfileId;
    public bool HasAnyWorkspaces => Workspaces.Count > 0;
    public bool HasRunningWorkspace => Workspaces.Any(workspace => workspace.IsRunning);
    public bool HasSelectedWorkspaceItem => SelectedWorkspace is not null;
    public bool ShowOnboardingState => !HasAnyWorkspaces;
    public bool ShowSelectionGuidanceState => HasAnyWorkspaces && !HasSelectedWorkspaceItem;
    public bool ShowWorkspaceDetails => HasSelectedWorkspaceItem;
    public bool ShowNoRemoteActions => SelectedWorkspace?.Snapshot.Safety.Backup.HasRemoteConfigured == false;
    public bool ShowUnpublishedSavePointActions => SelectedWorkspace?.Snapshot.Safety.Backup.HasUnpublishedSavePoints == true;
    public bool ShowNeedsReviewActions => SelectedWorkspace?.Snapshot.Safety.Backup.NeedsReviewBeforePublish == true
        || SelectedWorkspace?.Snapshot.Safety.Backup.IsOnProtectedBranch == true;
    public bool CanShutDownSelectedWorkspace => SelectedWorkspace is not null && !IsBusy && SelectedWorkspace.Snapshot.RuntimeState == WorkspaceRuntimeState.Running;
    public bool CanStartCreateWorkspaceFlow => !IsBusy && !HasRunningWorkspace;
    public bool CanCreateWorkspaceForDialog => CanCreateWorkspace();
    public string CreateWorkspaceDisabledReason => HasRunningWorkspace
        ? _localization.Get("create.disabled.workspaceRunning")
        : string.Empty;
    public string WorkspaceNameValidationMessage => string.IsNullOrWhiteSpace(NewWorkspaceName)
        ? _localization.Get("validation.workspaceNameRequired")
        : string.Empty;
    public string WorkspacePathValidationMessage => string.IsNullOrWhiteSpace(NewWorkspacePath)
        ? _localization.Get("validation.workspacePathRequired")
        : string.Empty;

    public async Task InitializeAsync()
    {
        await RefreshHealthChecksAsync();
        await RefreshWorkspaceListAsync();
        StatusMessage = Workspaces.Count == 0
            ? _localization.Get("status.none")
            : string.Format(_localization.Get("status.loadedWorkspaces"), Workspaces.Count);
    }

    public async Task<bool> CreateWorkspaceFromDialogAsync()
    {
        if (!CanCreateWorkspace())
        {
            return false;
        }

        var existingCount = Workspaces.Count;
        await CreateWorkspaceAsync();
        return Workspaces.Count > existingCount || Workspaces.Any(item => string.Equals(item.Name, NewWorkspaceName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task CreateWorkspaceAsync()
    {
        await RunBusyAsync(
            async () =>
            {
                AppendCurrentLog("app", $"Creating workspace '{NewWorkspaceName.Trim()}'.");
                var definition = new WorkspaceDefinition
                {
                    Workspace = new WorkspaceMetadata
                    {
                        Name = NewWorkspaceName.Trim(),
                        Image = "ubuntu:24.04",
                    },
                    Features = AvailableFeatures.Where(item => item.IsSelected).Select(item => item.Id).ToList(),
                    Services = AvailableServices.Where(item => item.IsSelected).Select(item => item.Id).ToList(),
                    Skills = new List<string>(),
                    Mcp = new List<string>(),
                    Agent = new AgentPreferences
                    {
                        Profile = AgentProfileResolver.BuiltInDefault.ProfileId,
                    },
                    Terminal = new TerminalPreferences
                    {
                        InstallIfMissing = InstallTerminalIfMissing,
                        Font = new TerminalFontPreferences
                        {
                            Provider = "nerd-fonts",
                            Family = SelectedFontFamily,
                        },
                        Prompt = new TerminalPromptPreferences
                        {
                            Provider = SelectedPromptProvider,
                        },
                        Utilities = new TerminalUtilityPreferences
                        {
                            Zoxide = InstallZoxide,
                            Fzf = InstallFzf,
                        },
                    },
                };

                var snapshot = _workspaceOrchestrator.CreateWorkspace(NewWorkspacePath.Trim(), definition, CreateWorkspaceLogAppender(NewWorkspacePath.Trim()));
                var resolvedFace = _windowsHostCapabilities.ResolvePreferredTerminalFace(snapshot.Definition.Terminal.Font.Family);
                _profileManager.EnsureManagedProfile(snapshot.Definition, snapshot.Definition.Terminal.Font, resolvedFace);
                PersistWorkspaceRecord(snapshot, CreateLabel, _localization.Get("workspace.result.created"), succeeded: true);
                EnsureWorkspaceLogStore(snapshot.Paths.RootPath);
                AppendWorkspaceLog(snapshot.Paths.RootPath, "app", "Created workspace files and generated runtime artifacts.");
                AppendWorkspaceLog(snapshot.Paths.RootPath, "app", $"Resolved default agent profile '{AgentProfileResolver.BuiltInDefault.ProfileId}'.");
                AppendWorkspaceLog(snapshot.Paths.RootPath, "app", $"Managed Windows Terminal profile ensured for font '{snapshot.Definition.Terminal.Font.Family}'.");
                AppendWorkspaceLog(snapshot.Paths.RootPath, "terminal", $"Profile: {_profileManager.GetProfileName(snapshot.Definition)}");
                AppendWorkspaceLog(snapshot.Paths.RootPath, "terminal", $"Configured font: {resolvedFace}");
                AppendWorkspaceLog(snapshot.Paths.RootPath, "terminal", $"Terminal profile file: {_profileManager.GetFragmentFilePath()}");
                AppendWorkspaceLog(snapshot.Paths.RootPath, "terminal", $"Profile id: {_profileManager.GetProfileGuid(snapshot.Definition)}");
                await RefreshWorkspaceListAsync(snapshot.Paths.RootPath);
                StatusMessage = string.Format(_localization.Get("status.workspaceCreated"), snapshot.Definition.Workspace.Name);
                await Task.CompletedTask;
            });
    }

    private async Task InstallSelectedFontAsync()
    {
        await RunBusyAsync(async () =>
        {
            AppendCurrentLog("app", $"Installing Nerd Font '{SelectedFontFamily}' for the current user.");
            await _nerdFontInstaller.InstallAsync(SelectedFontFamily);
            StatusMessage = string.Format(_localization.Get("status.fontInstalled"), SelectedFontFamily);
            await RefreshHealthChecksAsync();
        });
    }

    private async Task CreateSavePointAsync()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        var suggestion = await _savePointMessageService.SuggestAsync(SelectedWorkspace.RootPath);
        var dialog = new SavePointDialog(_localization, suggestion)
        {
            Owner = Application.Current?.MainWindow,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunWorkspaceActionAsync(
            _localization.Get("operation.createSavePoint"),
            async snapshot =>
            {
                var created = await _workspaceOrchestrator.CreateSavePointAsync(snapshot, dialog.ViewModel.SavePointMessage.Trim(), CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                PersistWorkspaceRecord(snapshot, _localization.Get("operation.createSavePoint"), created ? _localization.Get("workspace.result.savePointCreated") : _localization.Get("workspace.result.savePointSkipped"), succeeded: true);
                StatusMessage = created ? _localization.Get("status.savePointCreated") : _localization.Get("status.savePointSkipped");
            });
    }

    private async Task CreateCheckpointAsync()
    {
        await RunWorkspaceActionAsync(
            _localization.Get("operation.createCheckpoint"),
            async snapshot =>
            {
                var checkpoint = await _workspaceOrchestrator.CreateCheckpointAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                PersistWorkspaceRecord(snapshot, _localization.Get("operation.createCheckpoint"), string.Format(_localization.Get("workspace.result.checkpointCreated"), checkpoint.Id), succeeded: true);
                StatusMessage = string.Format(_localization.Get("status.checkpointCreated"), checkpoint.Id);
            });
    }

    private async Task PublishWorkspaceAsync()
    {
        await RunWorkspaceActionAsync(
            _localization.Get("operation.publish"),
            async snapshot =>
            {
                var review = await _workspaceOrchestrator.PublishAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                PersistWorkspaceRecord(snapshot, _localization.Get("operation.publish"), review.Message, succeeded: true);
                StatusMessage = review.Message;
            });
    }

    private async Task ExportBackupAsync()
    {
        await RunWorkspaceActionAsync(
            _localization.Get("operation.exportBackup"),
            async snapshot =>
            {
                var exportDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OpenCode Workspace Backups");
                Directory.CreateDirectory(exportDirectory);
                var archivePath = Path.Combine(exportDirectory, $"{WorkspacePathBuilder.Slugify(snapshot.Definition.Workspace.Name)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip");
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                }

                ZipFile.CreateFromDirectory(snapshot.Paths.RootPath, archivePath, CompressionLevel.Fastest, includeBaseDirectory: false);
                PersistWorkspaceRecord(snapshot, _localization.Get("operation.exportBackup"), string.Format(_localization.Get("workspace.result.backupExported"), archivePath), succeeded: true);
                StatusMessage = string.Format(_localization.Get("status.backupExported"), archivePath);
                await Task.CompletedTask;
            });
    }

    private async Task ExportPatchAsync()
    {
        await RunWorkspaceActionAsync(
            _localization.Get("operation.exportPatch"),
            async snapshot =>
            {
                var patchPath = await _workspaceOrchestrator.ExportPatchAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                PersistWorkspaceRecord(snapshot, _localization.Get("operation.exportPatch"), string.Format(_localization.Get("workspace.result.patchExported"), patchPath), succeeded: true);
                StatusMessage = string.Format(_localization.Get("status.patchExported"), patchPath);
            });
    }

    private Task ExecutePrimaryWorkspaceActionAsync()
    {
        if (SelectedWorkspace?.HasError == true)
        {
            ShowWorkspaceError();
            return Task.CompletedTask;
        }

        return OpenWorkspaceAsync();
    }

    private void ConfigureRemoteBackup()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        MessageBox.Show(
            string.Format(_localization.Get("safety.configureRemoteBackup.message"), SelectedWorkspace.Snapshot.Paths.WorkspaceYamlPath),
            _localization.Get("safety.configureRemoteBackup.title"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ContinueWorking()
    {
        StatusMessage = _localization.Get("safety.continueWorking.message");
    }

    private void OpenAdvancedGitView()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        var git = SelectedWorkspace.Snapshot.Safety.AdvancedGit;
        var details = string.Join(Environment.NewLine, new[]
        {
            $"Working Copy: {SelectedWorkspaceWorkingCopy}",
            $"Branch: {git.CurrentBranch}",
            $"Remote: {git.RemoteName}",
            $"Tracking: {git.RemoteBranch}",
            $"Ahead/Behind: {git.AheadCount}/{git.BehindCount}",
            $"Latest SHA: {git.LatestCommitSha}",
            $"Status: {git.StatusSummary}",
            $"Protected branch: {(git.IsProtectedBranch ? "Yes" : "No")}",
            $"Conflicting files: {(git.ConflictingFiles.Count == 0 ? "None" : string.Join(", ", git.ConflictingFiles))}",
            $"Patch export: {(git.PatchExportSupported ? "Available" : "Unavailable")}",
            $"Latest patch export status: {SelectedWorkspace.LastOperationResult}",
        });

        MessageBox.Show(details, OpenAdvancedGitViewLabel, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task UpdateWorkspaceFromRemoteAsync()
    {
        await RunWorkspaceActionAsync(
            _localization.Get("operation.updateWorkingCopy"),
            async snapshot =>
            {
                var review = await _workspaceOrchestrator.UpdateWorkingCopyAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                PersistWorkspaceRecord(snapshot, _localization.Get("operation.updateWorkingCopy"), review.Message, succeeded: true);
                StatusMessage = review.Message;
            });
    }

    private async Task PublishReviewWorkingCopyAsync()
    {
        await RunWorkspaceActionAsync(
            _localization.Get("operation.publishReviewWorkingCopy"),
            async snapshot =>
            {
                var review = await _workspaceOrchestrator.PublishToReviewWorkingCopyAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                PersistWorkspaceRecord(snapshot, _localization.Get("operation.publishReviewWorkingCopy"), review.Message, succeeded: true);
                StatusMessage = review.IsBlocked
                    ? review.Message
                    : string.Format(_localization.Get("status.reviewWorkingCopyPublished"), review.ReviewWorkingCopyBranch);
            });
    }

    private void DismissSafetyNotice()
    {
        StatusMessage = _localization.Get("safety.dismiss.message");
    }

    private async Task PrepareWorkspaceAsync()
    {
        await RunWorkspaceActionAsync(
            _localization.Get("operation.update"),
            async snapshot =>
            {
                await UpdateWorkspaceAsync(snapshot, openAfterUpdate: false);
                StatusMessage = string.Format(_localization.Get("status.workspaceUpdated"), snapshot.Definition.Workspace.Name);
            });
    }

    private async Task OpenWorkspaceAsync()
    {
        await RunWorkspaceActionAsync(
            _localization.Get("operation.open"),
            async snapshot =>
            {
                if (SelectedWorkspace?.HasUpdateAvailable == true)
                {
                    if (snapshot.RuntimeState == WorkspaceRuntimeState.Running)
                    {
                        var restartChoice = MessageBox.Show(
                            string.Format(_localization.Get("dialog.restartForUpdate.message"), snapshot.Definition.Workspace.Name),
                            _localization.Get("dialog.restartForUpdate.title"),
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (restartChoice != MessageBoxResult.Yes)
                        {
                            StatusMessage = string.Format(_localization.Get("status.workspaceUpdateRequired"), snapshot.Definition.Workspace.Name);
                            return;
                        }
                    }

                    await UpdateWorkspaceAsync(snapshot, openAfterUpdate: true);
                    snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(snapshot.Paths.RootPath);
                }

                if (snapshot.RuntimeState != WorkspaceRuntimeState.Running)
                {
                    await _workspaceOrchestrator.StartAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                    snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(snapshot.Paths.RootPath);
                }

                await _workspaceOrchestrator.LaunchAttachForRunningWorkspaceAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                PersistWorkspaceRecord(snapshot, _localization.Get("operation.open"), _localization.Get("workspace.result.opened"), succeeded: true, lastOpenedUtc: DateTimeOffset.UtcNow);
                StatusMessage = string.Format(_localization.Get("status.workspaceOpened"), snapshot.Definition.Workspace.Name);
            });
    }

    private async Task UpdateWorkspaceAsync(WorkspaceSnapshot snapshot, bool openAfterUpdate)
    {
        var wasRunning = snapshot.RuntimeState == WorkspaceRuntimeState.Running;
        if (wasRunning)
        {
            await _workspaceOrchestrator.StopAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
            snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(snapshot.Paths.RootPath);
        }

        await _workspaceOrchestrator.ProvisionAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
        PersistWorkspaceRecord(snapshot, _localization.Get("operation.update"), _localization.Get("workspace.result.updated"), succeeded: true, lastPreparedUtc: DateTimeOffset.UtcNow);

        if (!openAfterUpdate && !wasRunning)
        {
            var refreshedSnapshot = await _workspaceOrchestrator.LoadSnapshotAsync(snapshot.Paths.RootPath);
            await _workspaceOrchestrator.StopAsync(refreshedSnapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
        }
    }

    private async Task ShutDownWorkspaceAsync()
    {
        await RunWorkspaceActionAsync(
            _localization.Get("operation.shutdown"),
            async snapshot =>
            {
                await _workspaceOrchestrator.StopAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                PersistWorkspaceRecord(snapshot, _localization.Get("operation.shutdown"), _localization.Get("workspace.result.stopped"), succeeded: true);
                StatusMessage = string.Format(_localization.Get("status.workspaceStopped"), snapshot.Definition.Workspace.Name);
            });
    }

    private async Task RemoveWorkspaceAsync()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        var dialog = new RemoveWorkspaceDialog(_localization, SelectedWorkspace.Name, SelectedWorkspace.RootPath)
        {
            Owner = Application.Current?.MainWindow,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunWorkspaceActionAsync(
            _localization.Get("operation.remove"),
            async snapshot =>
            {
                if (dialog.SelectedChoice is WorkspaceRemovalChoice.DockerResources or WorkspaceRemovalChoice.DeleteFiles)
                {
                    await _workspaceOrchestrator.RemoveDockerResourcesAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                }

                if (dialog.SelectedChoice == WorkspaceRemovalChoice.DeleteFiles && Directory.Exists(snapshot.Paths.RootPath))
                {
                    await DeleteWorkspaceFilesWithRepairAsync(snapshot);
                }

                _workspaceOrchestrator.DeleteWorkspaceRegistration(snapshot.Paths.RootPath);

                StatusMessage = string.Format(_localization.Get("status.workspaceRemoved"), snapshot.Definition.Workspace.Name);
            },
            ensureTerminalProfile: false,
            refreshSelectionPath: string.Empty);
    }

    private async Task DeleteWorkspaceFilesWithRepairAsync(WorkspaceSnapshot snapshot)
    {
        await _workspaceOrchestrator.RepairWorkspaceFilePermissionsAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));

        try
        {
            Directory.Delete(snapshot.Paths.RootPath, recursive: true);
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var failedPaths = GetDeletionFailurePaths(snapshot.Paths.RootPath);
            var retry = MessageBox.Show(
                string.Format(
                    _localization.Get("remove.deleteFailed.message"),
                    snapshot.Paths.RootPath,
                    string.Join(Environment.NewLine, failedPaths)),
                _localization.Get("remove.deleteFailed.title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (retry != MessageBoxResult.Yes)
            {
                throw new InvalidOperationException(string.Format(_localization.Get("remove.deleteCancelled.message"), snapshot.Paths.RootPath), exception);
            }

            await _workspaceOrchestrator.RepairWorkspaceFilePermissionsAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));

            try
            {
                Directory.Delete(snapshot.Paths.RootPath, recursive: true);
            }
            catch (Exception retryException) when (retryException is IOException or UnauthorizedAccessException)
            {
                var retryFailedPaths = GetDeletionFailurePaths(snapshot.Paths.RootPath);
                throw new InvalidOperationException(
                    string.Format(
                        _localization.Get("remove.deleteRetryFailed.message"),
                        snapshot.Paths.RootPath,
                        string.Join(Environment.NewLine, retryFailedPaths)),
                    retryException);
            }
        }
    }

    private async Task RunWorkspaceActionAsync(string operationName, Func<WorkspaceSnapshot, Task> action, bool ensureTerminalProfile = true, string? refreshSelectionPath = null)
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        await RunBusyAsync(
            async () =>
            {
                var snapshot = _workspaceOrchestrator.LoadSnapshot(SelectedWorkspace.RootPath);
                EnsureWorkspaceLogStore(snapshot.Paths.RootPath);
                AppendWorkspaceLog(snapshot.Paths.RootPath, "app", $"Selected workspace '{snapshot.Definition.Workspace.Name}'.");

                if (ensureTerminalProfile)
                {
                    var resolvedFace = _windowsHostCapabilities.ResolvePreferredTerminalFace(snapshot.Definition.Terminal.Font.Family);
                    _profileManager.EnsureManagedProfile(snapshot.Definition, snapshot.Definition.Terminal.Font, resolvedFace);
                    AppendWorkspaceLog(snapshot.Paths.RootPath, "app", $"Managed Windows Terminal profile ensured for font '{snapshot.Definition.Terminal.Font.Family}'.");
                    AppendWorkspaceLog(snapshot.Paths.RootPath, "terminal", $"Profile: {_profileManager.GetProfileName(snapshot.Definition)}");
                    AppendWorkspaceLog(snapshot.Paths.RootPath, "terminal", $"Configured font: {resolvedFace}");
                    AppendWorkspaceLog(snapshot.Paths.RootPath, "terminal", $"Terminal profile file: {_profileManager.GetFragmentFilePath()}");
                    AppendWorkspaceLog(snapshot.Paths.RootPath, "terminal", $"Profile id: {_profileManager.GetProfileGuid(snapshot.Definition)}");
                }

                try
                {
                    await action(snapshot);
                }
                catch (Exception exception)
                {
                    PersistWorkspaceRecord(snapshot, operationName, exception.Message, succeeded: false);
                    throw;
                }
                finally
                {
                    await RefreshWorkspaceListAsync(refreshSelectionPath ?? snapshot.Paths.RootPath);
                }
            });
    }

    private void OpenSelectedWorkspaceFolder()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{SelectedWorkspace.RootPath}\"",
            UseShellExecute = true,
        });
    }

    private void CopySelectedWorkspacePath()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        Clipboard.SetText(SelectedWorkspace.RootPath);
        StatusMessage = string.Format(_localization.Get("status.pathCopied"), SelectedWorkspace.Name);
    }

    private void ShowWorkspaceError()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        MessageBox.Show(
            SelectedWorkspace.LastOperationResult,
            string.Format(_localization.Get("dialog.workspaceError.title"), SelectedWorkspace.Name),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static IReadOnlyList<string> GetDeletionFailurePaths(string rootPath)
    {
        var failures = new List<string>();

        void Scan(string path)
        {
            if (failures.Count >= 5 || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                {
                    if (failures.Count >= 5)
                    {
                        break;
                    }

                    try
                    {
                        _ = File.GetAttributes(entry);
                        if (Directory.Exists(entry))
                        {
                            Scan(entry);
                        }
                    }
                    catch
                    {
                        failures.Add(entry);
                    }
                }
            }
            catch
            {
                failures.Add(path);
            }
        }

        Scan(rootPath);
        if (failures.Count == 0)
        {
            failures.Add(rootPath);
        }

        return failures;
    }

    private async Task RefreshHealthChecksAsync()
    {
        Diagnostics.Clear();
        foreach (var result in await _environmentDiagnostics.RunAsync())
        {
            Diagnostics.Add(result);
        }

        AddAgentDiagnostics();
        await AddTerminalDiagnosticsAsync();
    }

    private async Task RefreshWorkspaceListAsync(string? selectRootPath = null)
    {
        Workspaces.Clear();
        foreach (var record in _workspaceOrchestrator.LoadWorkspaceRecords())
        {
            if (!File.Exists(Path.Combine(record.RootPath, "workspace.yaml")))
            {
                continue;
            }

            var snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(record.RootPath);
            Workspaces.Add(new WorkspaceListItemViewModel(snapshot, _localization));
        }

        SelectedWorkspace = Workspaces.FirstOrDefault(item => string.Equals(item.RootPath, selectRootPath, StringComparison.OrdinalIgnoreCase))
            ?? Workspaces.FirstOrDefault();
        RaisePropertyChanged(nameof(HasRunningWorkspace));
        RaisePropertyChanged(nameof(HasAnyWorkspaces));
        RaisePropertyChanged(nameof(ShowOnboardingState));
        RaisePropertyChanged(nameof(ShowSelectionGuidanceState));
        RaisePropertyChanged(nameof(ShowWorkspaceDetails));
        RaisePropertyChanged(nameof(CanStartCreateWorkspaceFlow));
        RaisePropertyChanged(nameof(CreateWorkspaceDisabledReason));
        RaisePropertyChanged(nameof(CanCreateWorkspaceForDialog));
    }

    private void LoadCatalogSelections()
    {
        AvailableFeatures.Clear();
        foreach (var feature in _catalogProvider.LoadFeatures())
        {
            AvailableFeatures.Add(new SelectableItemViewModel
            {
                Id = feature.Id,
                DisplayName = feature.DisplayName,
                Description = feature.Description,
                IsLocked = feature.AlwaysEnabled,
                IsSelected = feature.AlwaysEnabled,
            });
        }

        AvailableServices.Clear();
        foreach (var service in _catalogProvider.LoadServices())
        {
            AvailableServices.Add(new SelectableItemViewModel
            {
                Id = service.Id,
                DisplayName = service.DisplayName,
                Description = service.Description,
                IsSelected = false,
            });
        }
    }

    private void ApplyTemplate(TemplateManifest template)
    {
        foreach (var feature in AvailableFeatures)
        {
            feature.IsSelected = feature.IsLocked || template.Features.Contains(feature.Id, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var service in AvailableServices)
        {
            service.IsSelected = template.Services.Contains(service.Id, StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task AddTerminalDiagnosticsAsync()
    {
        var fontCheck = _windowsHostCapabilities.CheckNerdFont(SelectedWorkspace?.Snapshot.Definition.Terminal.Font.Family ?? SelectedFontFamily);
        Diagnostics.Add(new DiagnosticResult
        {
            Code = "terminal.font",
            Title = "Selected Nerd Font",
            Message = fontCheck.Reason,
            Severity = fontCheck.IsAvailable ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
            IsSuccess = fontCheck.IsAvailable,
        });

        Diagnostics.Add(new DiagnosticResult
        {
            Code = "terminal.profile",
            Title = "OpenCode Stuff terminal profile",
            Message = _profileManager.ManagedProfileExists() ? "OpenCode Stuff managed Windows Terminal profile exists." : "OpenCode Stuff managed Windows Terminal profile has not been generated yet.",
            Severity = _profileManager.ManagedProfileExists() ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
            IsSuccess = _profileManager.ManagedProfileExists(),
        });

        if (SelectedWorkspace is null)
        {
            return;
        }

        var configuredFace = _profileManager.GetConfiguredFontFace(SelectedWorkspace.Snapshot.Definition);
        Diagnostics.Add(new DiagnosticResult
        {
            Code = "terminal.profile-font",
            Title = "Profile configured to use selected font",
            Message = string.IsNullOrWhiteSpace(configuredFace)
                ? "OpenCode Stuff profile exists but no configured font face was found."
                : $"OpenCode Stuff profile is configured to use '{configuredFace}'.",
            Severity = string.IsNullOrWhiteSpace(configuredFace) ? DiagnosticSeverity.Warning : DiagnosticSeverity.Information,
            IsSuccess = !string.IsNullOrWhiteSpace(configuredFace),
        });

        if (!string.Equals(SelectedWorkspace.Snapshot.Definition.Terminal.Prompt.Provider, "starship", StringComparison.OrdinalIgnoreCase))
        {
            Diagnostics.Add(new DiagnosticResult
            {
                Code = "terminal.starship",
                Title = "Starship installed",
                Message = "This workspace is configured to use the default Bash prompt instead of Starship.",
                Severity = DiagnosticSeverity.Information,
                IsSuccess = true,
            });
            return;
        }

        try
        {
            var containerName = DockerService.GetWorkspaceContainerName(SelectedWorkspace.Snapshot.Definition);
            var result = await _dockerService.RunSimpleDockerCommandAsync(["exec", containerName, "bash", "-lc", "command -v starship >/dev/null 2>&1 && starship --version"]);
            Diagnostics.Add(new DiagnosticResult
            {
                Code = "terminal.starship",
                Title = "Starship installed",
                Message = result.IsSuccess ? "Starship is installed in the selected workspace." : "Starship could not be verified in the selected workspace. Start and provision the workspace first.",
                Severity = result.IsSuccess ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                IsSuccess = result.IsSuccess,
                TechnicalDetails = result.IsSuccess ? result.StandardOutput : result.StandardError,
            });
        }
        catch (Exception exception)
        {
            Diagnostics.Add(new DiagnosticResult
            {
                Code = "terminal.starship",
                Title = "Starship installed",
                Message = "Starship could not be verified in the selected workspace. Start and provision the workspace first.",
                Severity = DiagnosticSeverity.Warning,
                IsSuccess = false,
                TechnicalDetails = exception.Message,
            });
        }
    }

    private void AddAgentDiagnostics()
    {
        var definition = SelectedWorkspace?.Snapshot.Definition ?? new WorkspaceDefinition();
        var resolved = _agentProfileResolver.Resolve(definition);

        Diagnostics.Add(new DiagnosticResult
        {
            Code = "agent.profile",
            Title = "Agent profile resolved",
            Message = $"Using profile '{resolved.ProfileId}' from {resolved.ResolutionSource}.",
            Severity = DiagnosticSeverity.Information,
            IsSuccess = true,
        });

        Diagnostics.Add(new DiagnosticResult
        {
            Code = "agent.provider",
            Title = "Provider available",
            Message = $"Provider '{resolved.Provider}' is configured.",
            Severity = DiagnosticSeverity.Information,
            IsSuccess = !string.IsNullOrWhiteSpace(resolved.Provider),
        });

        Diagnostics.Add(new DiagnosticResult
        {
            Code = "agent.connection",
            Title = "Connection configured",
            Message = $"Connection '{resolved.Connection}' is configured.",
            Severity = DiagnosticSeverity.Information,
            IsSuccess = !string.IsNullOrWhiteSpace(resolved.Connection),
        });

        Diagnostics.Add(new DiagnosticResult
        {
            Code = "agent.model",
            Title = "Model available",
            Message = $"Model '{resolved.Model}' is configured{(resolved.UsesBuiltInDefault ? " via the recommended default profile" : string.Empty)}.",
            Severity = DiagnosticSeverity.Information,
            IsSuccess = !string.IsNullOrWhiteSpace(resolved.Model),
        });
    }

    private bool CanCreateWorkspace() => CanStartCreateWorkspaceFlow && !string.IsNullOrWhiteSpace(NewWorkspaceName) && !string.IsNullOrWhiteSpace(NewWorkspacePath);
    private bool HasSelectedWorkspace() => SelectedWorkspace is not null && !IsBusy;

    private string FormatRelativeTime(DateTimeOffset? value)
    {
        if (value is null)
        {
            return _localization.Get("safety.noneRecorded");
        }

        var age = DateTimeOffset.UtcNow - value.Value;
        if (age.TotalMinutes < 1)
        {
            return _localization.Get("safety.justNow");
        }

        if (age.TotalHours < 1)
        {
            return string.Format(_localization.Get("safety.minutesAgo"), Math.Max(1, (int)Math.Floor(age.TotalMinutes)));
        }

        if (age.TotalDays < 1)
        {
            return string.Format(_localization.Get("safety.hoursAgo"), Math.Max(1, (int)Math.Floor(age.TotalHours)));
        }

        return string.Format(_localization.Get("safety.daysAgo"), Math.Max(1, (int)Math.Floor(age.TotalDays)));
    }

    private string FormatYesNo(bool? value)
    {
        return value switch
        {
            true => _localization.Get("safety.yes"),
            false => _localization.Get("safety.no"),
            _ => "-",
        };
    }

    private string GetSafetyStatusLabel(WorkspaceSafetyLevel value)
    {
        return value switch
        {
            WorkspaceSafetyLevel.Protected => _localization.Get("safety.status.protected"),
            WorkspaceSafetyLevel.PartiallyProtected => _localization.Get("safety.status.partiallyProtected"),
            WorkspaceSafetyLevel.AtRisk => _localization.Get("safety.status.atRisk"),
            WorkspaceSafetyLevel.NeedsReview => _localization.Get("safety.status.needsReview"),
            _ => value.ToString(),
        };
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception exception)
        {
            AppendCurrentLog("app", exception.Message);
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AppendCurrentLog(string source, string message)
    {
        if (SelectedWorkspace is not null)
        {
            AppendWorkspaceLog(SelectedWorkspace.RootPath, source, message);
            return;
        }

        AppendToVisibleLogCollection(new WorkspaceLogLineViewModel { Text = $"[{source}] {message}" });
    }

    private void AppendWorkspaceLog(string rootPath, string source, string message)
    {
        EnsureWorkspaceLogStore(rootPath);
        var logLine = new WorkspaceLogLineViewModel { Text = $"[{source}] {message}" };
        _workspaceLogsByPath[rootPath].Add(logLine);

        if (SelectedWorkspace is not null && string.Equals(SelectedWorkspace.RootPath, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            AppendToVisibleLogCollection(logLine);
        }
    }

    private void EnsureWorkspaceLogStore(string rootPath)
    {
        if (!_workspaceLogsByPath.ContainsKey(rootPath))
        {
            _workspaceLogsByPath[rootPath] = new List<WorkspaceLogLineViewModel>();
        }
    }

    private void LoadVisibleLogsForSelectedWorkspace()
    {
        CurrentLogLines.Clear();
        if (SelectedWorkspace is null)
        {
            return;
        }

        EnsureWorkspaceLogStore(SelectedWorkspace.RootPath);
        foreach (var line in _workspaceLogsByPath[SelectedWorkspace.RootPath])
        {
            CurrentLogLines.Add(line);
        }
        CurrentLogText = string.Join(Environment.NewLine, _workspaceLogsByPath[SelectedWorkspace.RootPath].Select(line => line.Text));
    }

    private void AppendToVisibleLogCollection(WorkspaceLogLineViewModel line)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            CurrentLogLines.Add(line);
            CurrentLogText = string.Join(Environment.NewLine, CurrentLogLines.Select(item => item.Text));
            return;
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            CurrentLogLines.Add(line);
            CurrentLogText = string.Join(Environment.NewLine, CurrentLogLines.Select(item => item.Text));
        });
    }

    private Action<CommandLogEntry> CreateWorkspaceLogAppender(string rootPath)
    {
        return entry => AppendWorkspaceLog(rootPath, entry.Source, entry.Message);
    }

    private void PersistWorkspaceRecord(
        WorkspaceSnapshot snapshot,
        string operationName,
        string operationResult,
        bool succeeded,
        DateTimeOffset? lastPreparedUtc = null,
        DateTimeOffset? lastOpenedUtc = null)
    {
        var updatedRecord = new WorkspaceRecord
        {
            Name = snapshot.Record.Name,
            RootPath = snapshot.Record.RootPath,
            CreatedUtc = snapshot.Record.CreatedUtc,
            LastOpenedUtc = lastOpenedUtc ?? snapshot.Record.LastOpenedUtc,
            LastPreparedUtc = lastPreparedUtc ?? snapshot.Record.LastPreparedUtc,
            LastOperationName = operationName,
            LastOperationResult = operationResult,
            LastOperationSucceeded = succeeded,
            LastOperationUtc = DateTimeOffset.UtcNow,
        };

        _workspaceOrchestrator.SaveRecord(updatedRecord);
    }

    private void RaiseCommandStates()
    {
        CreateWorkspaceCommand.RaiseCanExecuteChanged();
        PrimaryWorkspaceActionCommand.RaiseCanExecuteChanged();
        OpenWorkspaceCommand.RaiseCanExecuteChanged();
        PrepareWorkspaceCommand.RaiseCanExecuteChanged();
        ShutDownWorkspaceCommand.RaiseCanExecuteChanged();
        CreateSavePointCommand.RaiseCanExecuteChanged();
        CreateCheckpointCommand.RaiseCanExecuteChanged();
        PublishWorkspaceCommand.RaiseCanExecuteChanged();
        ExportBackupCommand.RaiseCanExecuteChanged();
        ExportPatchCommand.RaiseCanExecuteChanged();
        ConfigureRemoteBackupCommand.RaiseCanExecuteChanged();
        ContinueWorkingCommand.RaiseCanExecuteChanged();
        OpenAdvancedGitViewCommand.RaiseCanExecuteChanged();
        UpdateWorkspaceFromRemoteCommand.RaiseCanExecuteChanged();
        PublishReviewWorkingCopyCommand.RaiseCanExecuteChanged();
        DismissSafetyNoticeCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
        OpenFolderCommand.RaiseCanExecuteChanged();
        CopyWorkspacePathCommand.RaiseCanExecuteChanged();
        RemoveWorkspaceCommand.RaiseCanExecuteChanged();
        InstallSelectedFontCommand.RaiseCanExecuteChanged();
        RaisePropertyChanged(nameof(CanShutDownSelectedWorkspace));
        RaisePropertyChanged(nameof(SelectedPrimaryActionLabel));
        RaisePropertyChanged(nameof(HasRunningWorkspace));
        RaisePropertyChanged(nameof(CanStartCreateWorkspaceFlow));
        RaisePropertyChanged(nameof(CreateWorkspaceDisabledReason));
        RaisePropertyChanged(nameof(CanCreateWorkspaceForDialog));
    }
}
