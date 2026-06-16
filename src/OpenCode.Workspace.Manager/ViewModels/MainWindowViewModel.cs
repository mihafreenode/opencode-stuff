using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
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
    private readonly QuickTutorialService _tutorialService;
    private readonly TmpReprovisionWorkflowService _tmpReprovisionWorkflowService;
    private readonly AppBuildInfo _appBuildInfo;
    private readonly AgentProfileResolver _agentProfileResolver = new();
    private readonly Dictionary<string, List<WorkspaceLogLineViewModel>> _workspaceLogsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _oracleNoticeAcknowledgedWorkspacePaths = new(StringComparer.OrdinalIgnoreCase);
    private WorkspaceListItemViewModel? _selectedWorkspace;
    private TemplateManifest? _selectedTemplate;
    private string _newWorkspaceName = "demo-workspace";
    private string _newWorkspacePath;
    private string _existingRepositoryPath = string.Empty;
    private WorkspaceSourceType _selectedWorkspaceSourceType;
    private WorkspaceDefinition? _loadedRepositoryDefinition;
    private string _loadedRepositoryConfigurationPath = string.Empty;
    private string _loadedRepositoryConfigurationError = string.Empty;
    private bool _loadedRepositoryConfigurationIsInvalid;
    private string _statusMessage;
    private bool _isWorkspaceListLoading = true;
    private bool _workspaceListLoadFailed;
    private string _workspaceListErrorMessage = string.Empty;
    private bool _isBusy;
    private string _currentLogText = string.Empty;
    private string _selectedPromptProvider = "starship";
    private string _selectedFontFamily = "JetBrainsMono Nerd Font";
    private bool _installTerminalIfMissing = true;
    private bool _installZoxide;
    private bool _installFzf;
    private readonly string? _sqlDeveloperExecutablePath;
    private string? _oracleStartupStageOverride;
    private static readonly TimeSpan CreateWorkspaceTimeout = TimeSpan.FromSeconds(20);

    internal Func<string, AppDialogResult>? OracleNoticePromptOverrideForTests { get; set; }
    internal string? LastOracleNoticeMessageForTests { get; private set; }

    public MainWindowViewModel(
        WorkspaceOrchestrator workspaceOrchestrator,
        BuiltInCatalogProvider catalogProvider,
        EnvironmentDiagnostics environmentDiagnostics,
        PoLocalizationService localization,
        WindowsHostCapabilities windowsHostCapabilities,
        WindowsTerminalProfileManager profileManager,
        DockerService dockerService,
        NerdFontInstaller nerdFontInstaller,
        WorkspaceSavePointMessageService savePointMessageService,
        QuickTutorialService tutorialService,
        TmpReprovisionWorkflowService tmpReprovisionWorkflowService,
        AppBuildInfoService appBuildInfoService)
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
        _tutorialService = tutorialService;
        _tmpReprovisionWorkflowService = tmpReprovisionWorkflowService;
        _appBuildInfo = appBuildInfoService.GetCurrent();
        _sqlDeveloperExecutablePath = _windowsHostCapabilities.FindSqlDeveloperExecutablePath();

        var defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OpenCode Workspaces");
        _newWorkspacePath = Path.Combine(defaultRoot, _newWorkspaceName);
        _selectedWorkspaceSourceType = WorkspaceSourceType.NewWorkspace;
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
        StartWorkspaceLabel = localization.Get("actions.start");
        RecoverWorkspaceLabel = localization.Get("actions.recoverWorkspace");
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

        CreateWorkspaceCommand = new AsyncRelayCommand(() => CreateWorkspaceAsync(), CanCreateWorkspace);
        PrimaryWorkspaceActionCommand = new AsyncRelayCommand(ExecutePrimaryWorkspaceActionAsync, HasSelectedWorkspace);
        StartWorkspaceCommand = new AsyncRelayCommand(StartWorkspaceAsync, HasSelectedWorkspace);
        OpenWorkspaceCommand = new AsyncRelayCommand(OpenWorkspaceAsync, HasSelectedWorkspace);
        RecoverWorkspaceCommand = new AsyncRelayCommand(RecoverWorkspaceAsync, HasSelectedWorkspace);
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
        RefreshCommand = new AsyncRelayCommand(InitializeAsync, () => !IsBusy && !IsWorkspaceListLoading);
        OpenFolderCommand = new RelayCommand(OpenSelectedWorkspaceFolder, () => HasSelectedWorkspace() && !IsBusy);
        CopyWorkspacePathCommand = new RelayCommand(CopySelectedWorkspacePath, () => HasSelectedWorkspace() && !IsBusy);
        RemoveWorkspaceCommand = new AsyncRelayCommand(RemoveWorkspaceAsync, HasSelectedWorkspace);
        ViewWorkspaceErrorCommand = new RelayCommand(ShowWorkspaceError, HasSelectedWorkspace);
        InstallSelectedFontCommand = new AsyncRelayCommand(InstallSelectedFontAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(SelectedFontFamily));
        StartOracleDemoCommand = new AsyncRelayCommand(StartOracleDemoAsync, () => SelectedWorkspaceHasOracleDemo && !IsBusy);
        ResetOracleDemoCommand = new AsyncRelayCommand(ResetOracleDemoAsync, () => SelectedWorkspaceHasOracleDemo && !IsBusy);
        ViewOracleLogsCommand = new AsyncRelayCommand(ViewOracleLogsAsync, () => SelectedWorkspaceHasOracleDemo && !IsBusy);
        CopyOracleConnectionDetailsCommand = new RelayCommand(CopyOracleConnectionDetails, () => SelectedWorkspaceHasOracleDemo && !IsBusy);
        OpenOracleOrdsCommand = new RelayCommand(OpenOracleOrds, () => SelectedWorkspaceHasOracleApex && !IsBusy);
        OpenOracleApexCommand = new RelayCommand(OpenOracleApex, () => SelectedWorkspaceHasOracleApex && !IsBusy);
        OpenOracleSqlclCommand = new RelayCommand(OpenOracleSqlcl, () => SelectedWorkspaceHasOracleDemo && !IsBusy);
        OpenOracleSqlWorksheetCommand = new RelayCommand(OpenOracleSqlWorksheet, () => SelectedWorkspaceHasOracleApex && !IsBusy);
        RunOracleTutorialQueryCommand = new RelayCommand(RunOracleTutorialQuery, () => SelectedWorkspaceHasOracleDemo && !IsBusy);
        TestOracleConnectionCommand = new RelayCommand(TestOracleConnection, () => SelectedWorkspaceHasOracleDemo && !IsBusy);
        OpenSqlDeveloperCommand = new RelayCommand(OpenSqlDeveloper, () => SelectedWorkspaceHasOracleDemo && IsSqlDeveloperDetected && !IsBusy);
        RunTmpReprovisionWorkflowCommand = new AsyncRelayCommand(RunTmpReprovisionWorkflowAsync, HasSelectedWorkspace);

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
    public string StartWorkspaceLabel { get; }
    public string RecoverWorkspaceLabel { get; }
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
    public string CreateSavePointHelpText => "Use before a big change and after a good state. Save Points protect local work on this machine.";
    public string CreateCheckpointHelpText => "Checkpoint is extra local recovery when you want stronger protection than a normal Save Point.";
    public string PublishHelpText => "Publish sends the current Working Copy to remote backup. It is useful, but not required for normal local work.";

    public AsyncRelayCommand CreateWorkspaceCommand { get; }
    public AsyncRelayCommand PrimaryWorkspaceActionCommand { get; }
    public AsyncRelayCommand StartWorkspaceCommand { get; }
    public AsyncRelayCommand OpenWorkspaceCommand { get; }
    public AsyncRelayCommand RecoverWorkspaceCommand { get; }
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
    public RelayCommand ViewWorkspaceErrorCommand { get; }
    public AsyncRelayCommand InstallSelectedFontCommand { get; }
    public AsyncRelayCommand StartOracleDemoCommand { get; }
    public AsyncRelayCommand ResetOracleDemoCommand { get; }
    public AsyncRelayCommand ViewOracleLogsCommand { get; }
    public RelayCommand CopyOracleConnectionDetailsCommand { get; }
    public RelayCommand OpenOracleOrdsCommand { get; }
    public RelayCommand OpenOracleApexCommand { get; }
    public RelayCommand OpenOracleSqlclCommand { get; }
    public RelayCommand OpenOracleSqlWorksheetCommand { get; }
    public RelayCommand RunOracleTutorialQueryCommand { get; }
    public RelayCommand TestOracleConnectionCommand { get; }
    public RelayCommand OpenSqlDeveloperCommand { get; }
    public AsyncRelayCommand RunTmpReprovisionWorkflowCommand { get; }

    public WorkspaceListItemViewModel? SelectedWorkspace
    {
        get => _selectedWorkspace;
        set
        {
            if (SetProperty(ref _selectedWorkspace, value))
            {
                _oracleStartupStageOverride = null;
                RaisePropertyChanged(nameof(SelectedWorkspaceName));
                RaisePropertyChanged(nameof(SelectedWorkspacePath));
                RaisePropertyChanged(nameof(SelectedRepositoryPath));
                RaisePropertyChanged(nameof(SelectedWorkspaceImage));
                RaisePropertyChanged(nameof(SelectedWorkspaceNodeVersion));
                RaisePropertyChanged(nameof(SelectedWorkspaceFeatures));
                RaisePropertyChanged(nameof(SelectedWorkspaceServices));
                RaisePropertyChanged(nameof(SelectedWorkspaceAgent));
                RaisePropertyChanged(nameof(SelectedWorkspaceHasOracleDemo));
                RaisePropertyChanged(nameof(SelectedWorkspaceHasOracleApex));
                RaisePropertyChanged(nameof(SelectedWorkspaceHasOracleApexLang));
                RaisePropertyChanged(nameof(SelectedOracleDemoStatus));
                RaisePropertyChanged(nameof(OracleStartupStage));
                RaisePropertyChanged(nameof(OracleOpenCodeGuidance));
                RaisePropertyChanged(nameof(IsSqlDeveloperDetected));
                RaisePropertyChanged(nameof(SqlDeveloperStatus));
                RaisePropertyChanged(nameof(SelectedWorkspaceStatus));
                RaisePropertyChanged(nameof(SelectedWorkspaceLastOperation));
                RaisePropertyChanged(nameof(SelectedWorkspaceServicesStatus));
                RaisePropertyChanged(nameof(SelectedWorkspaceSafetyStatus));
                RaisePropertyChanged(nameof(SelectedWorkspaceSafetyStatusHelpText));
                RaisePropertyChanged(nameof(SelectedWorkspaceSafetyMessage));
                RaisePropertyChanged(nameof(SelectedWorkspaceWorkingCopy));
                RaisePropertyChanged(nameof(SelectedWorkspaceRemoteName));
                RaisePropertyChanged(nameof(SelectedWorkspaceCurrentBranch));
                RaisePropertyChanged(nameof(SelectedWorkspaceDefaultBranch));
                RaisePropertyChanged(nameof(SelectedWorkspaceRemoteOrigin));
                RaisePropertyChanged(nameof(SelectedWorkspaceDirtyStatus));
                RaisePropertyChanged(nameof(SelectedWorkspaceBranchModeStatus));
                RaisePropertyChanged(nameof(SelectedWorkspaceSessionStatus));
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
                RaisePropertyChanged(nameof(ShowRecoverWorkspaceAction));
                RaisePropertyChanged(nameof(ShowViewWorkspaceErrorAction));
                RaisePropertyChanged(nameof(ShowStartWorkspaceAction));
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
                if (_loadedRepositoryDefinition is null)
                {
                    ApplyTemplate(value);
                }

                RaisePropertyChanged(nameof(IsOracleDemoTemplateSelected));
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

    public string ExistingRepositoryPath
    {
        get => _existingRepositoryPath;
        set
        {
            if (SetProperty(ref _existingRepositoryPath, value))
            {
                ClearLoadedRepositoryConfiguration();
                if (string.IsNullOrWhiteSpace(_newWorkspaceName))
                {
                    var folderName = GetWorkspaceNameFromPath(value);
                    if (!string.IsNullOrWhiteSpace(folderName))
                    {
                        NewWorkspaceName = folderName;
                    }
                }

                RaisePropertyChanged(nameof(CanCreateWorkspaceForDialog));
                RaisePropertyChanged(nameof(ExistingRepositoryPathValidationMessage));
            }
        }
    }

    public WorkspaceSourceType SelectedWorkspaceSourceType
    {
        get => _selectedWorkspaceSourceType;
        set
        {
            if (SetProperty(ref _selectedWorkspaceSourceType, value))
            {
                RaisePropertyChanged(nameof(IsNewWorkspaceSource));
                RaisePropertyChanged(nameof(IsExistingGitCheckoutSource));
                RaisePropertyChanged(nameof(CanCreateWorkspaceForDialog));
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
    public string AppExecutablePath => _appBuildInfo.ExecutablePath;
    public string AppBuildConfiguration => _appBuildInfo.BuildConfiguration;
    public string AppAssemblyVersion => _appBuildInfo.AssemblyVersion;
    public string AppInformationalVersion => _appBuildInfo.InformationalVersion;
    public string AppGitCommitSha => _appBuildInfo.GitCommitSha;
    public string AppBuildTimestamp => _appBuildInfo.BuildTimestamp;
    public string WorkspaceGeneratorVersion => _appBuildInfo.WorkspaceGeneratorVersion;
    public string GeneratedSchemaVersion => _appBuildInfo.GeneratedSchemaVersion;
    public string SelectedRepositoryPath => string.IsNullOrWhiteSpace(SelectedWorkspace?.Snapshot.Record.RepositoryPath) ? SelectedWorkspacePath : SelectedWorkspace!.Snapshot.Record.RepositoryPath;
    public string SelectedWorkspaceShortPath => SelectedWorkspace?.ShortRootPath ?? "-";
    public string SelectedWorkspaceImage => SelectedWorkspace?.Image ?? "-";
    public string SelectedWorkspaceNodeVersion => SelectedWorkspace is null ? "-" : $"Node.js {SelectedWorkspace.Snapshot.Definition.Runtime.GetEffectiveNodeMajorVersion()} LTS";
    public string SelectedWorkspaceFeatures => SelectedWorkspace?.FeaturesSummary ?? "-";
    public string SelectedWorkspaceServices => SelectedWorkspace?.ServicesSummary ?? "-";
    public string SelectedWorkspaceStatus => SelectedWorkspace?.StatusLabel ?? "-";
    public string SelectedWorkspaceLastOperation => SelectedWorkspace?.LastOperationResult ?? "-";
    public string SelectedWorkspaceServicesStatus => SelectedWorkspace?.ServicesStatusSummary ?? "-";
    public string SelectedWorkspaceSafetyStatus => SelectedWorkspace is null
        ? "-"
        : GetSafetyStatusLabel(SelectedWorkspace.Snapshot.Safety.OverallStatus);
    public string SelectedWorkspaceSafetyStatusHelpText => SelectedWorkspace?.Snapshot.Safety.OverallStatus switch
    {
        WorkspaceSafetyLevel.Protected => "Protected means local recovery is in a strong state and normal work can continue safely.",
        WorkspaceSafetyLevel.PartiallyProtected => "Partially Protected means your work is still safe, but another Save Point, Checkpoint, or backup step would improve protection.",
        WorkspaceSafetyLevel.NeedsReview => "Needs Review means the app found something that should be checked before publishing. Local work is still kept safe.",
        WorkspaceSafetyLevel.AtRisk => "At Risk means local recovery is weaker than normal and you should protect the workspace before continuing major changes.",
        _ => "The safety panel explains how well the current workspace is protected."
    };
    public string SelectedWorkspaceSafetyMessage => SelectedWorkspace?.SafetyMessage ?? "-";
    public string SelectedWorkspaceCurrentBranch => string.IsNullOrWhiteSpace(SelectedWorkspace?.Snapshot.Safety.AdvancedGit.CurrentBranch) ? "-" : SelectedWorkspace.Snapshot.Safety.AdvancedGit.CurrentBranch;
    public string SelectedWorkspaceDefaultBranch => string.IsNullOrWhiteSpace(SelectedWorkspace?.Snapshot.Safety.AdvancedGit.DefaultBranch) ? "-" : SelectedWorkspace.Snapshot.Safety.AdvancedGit.DefaultBranch;
    public string SelectedWorkspaceRemoteOrigin => string.IsNullOrWhiteSpace(SelectedWorkspace?.Snapshot.Safety.AdvancedGit.RemoteUrl)
        ? "Remote origin not configured"
        : SelectedWorkspace.Snapshot.Safety.AdvancedGit.RemoteUrl;
    public string SelectedWorkspaceDirtyStatus => SelectedWorkspace is null
        ? "-"
        : SelectedWorkspace.Snapshot.Safety.LocalRecovery.HasUncommittedChanges || SelectedWorkspace.Snapshot.Safety.LocalRecovery.UntrackedFileCount > 0
            ? "Uncommitted local changes present"
            : "Working tree is clean";
    public string SelectedWorkspaceBranchModeStatus => SelectedWorkspace?.Snapshot.Safety.AdvancedGit.IsWorkspaceBranch == true
        ? "Working on isolated workspace branch"
        : SelectedWorkspace?.Snapshot.Safety.AdvancedGit.IsProtectedBranch == true
            ? "Working directly on protected branch"
            : "Working on current branch";
    public string SelectedWorkspaceSessionStatus => SelectedWorkspace?.Snapshot.Session.State switch
    {
        WorkspaceSessionState.Resumable => "Session: resumable",
        WorkspaceSessionState.NotRunning => "Session: not running",
        WorkspaceSessionState.Unknown => "Session: status unavailable",
        _ => "Session: status unavailable",
    };
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
        : OpenWorkspaceLabel;
    public string SelectedWorkspaceAgent => SelectedWorkspace is null
        ? "-"
        : _agentProfileResolver.Resolve(SelectedWorkspace.Snapshot.Definition).ProfileId;
    public bool SelectedWorkspaceHasOracleDemo => SelectedWorkspace is not null && OracleWorkspaceFamily.IsOracleWorkspace(SelectedWorkspace.Snapshot.Definition);
    public bool SelectedWorkspaceHasOracleApex => SelectedWorkspace is not null && OracleWorkspaceFamily.HasApex(SelectedWorkspace.Snapshot.Definition);
    public bool SelectedWorkspaceHasOracleApexLang => SelectedWorkspace is not null && OracleWorkspaceFamily.HasApexLang(SelectedWorkspace.Snapshot.Definition);
    public string SelectedOracleDemoStatus => SelectedWorkspace?.Snapshot.RuntimeState switch
    {
        WorkspaceRuntimeState.Running => "Running",
        WorkspaceRuntimeState.Stopped => "Stopped",
        _ => "Unknown",
    };
    public string OracleDemoHost => "localhost";
    public string OracleDemoPort => "1521";
    public string OracleDemoServiceName => "FREEPDB1";
    public string OracleDemoUsername => "demo_user";
    public string OracleDemoPassword => "demo_password";
    public string OracleDemoProvisioningNote => SelectedWorkspaceHasOracleApex
        ? "First provisioning downloads Oracle SQLcl from Oracle and prepares ORDS reachability checks. After that, the Oracle database, ORDS, and local APEX onboarding flow run locally. OpenCode inside the workspace uses demo_user/demo_password@//oracle-demo:1521/FREEPDB1."
        : "First provisioning downloads Oracle SQLcl from Oracle and requires internet access. After that, the demo database and tutorial run locally. OpenCode inside the workspace uses demo_user/demo_password@//oracle-demo:1521/FREEPDB1.";
    public string OracleStartupStage => SelectedWorkspaceHasOracleDemo && !string.IsNullOrWhiteSpace(_oracleStartupStageOverride)
        ? _oracleStartupStageOverride!
        : GetOracleStartupStageFromSnapshot();
    public string OracleOpenCodeGuidance => GetOracleOpenCodeGuidance();
    public bool IsSqlDeveloperDetected => !string.IsNullOrWhiteSpace(_sqlDeveloperExecutablePath);
    public string SqlDeveloperStatus => IsSqlDeveloperDetected ? "SQL Developer detected" : "SQL Developer not detected";
    public string SqlDeveloperGuidance => IsSqlDeveloperDetected
        ? "Use the copied localhost connection details to connect quickly during the demo."
        : "SQL Developer is optional. If it is not installed, continue with Open SQLcl and the local tutorial.";
    public bool IsOracleDemoTemplateSelected => SelectedTemplate is not null && OracleWorkspaceFamily.IsOracleWorkspace(SelectedTemplate);
    public string OracleTemplateIncludesSummary => string.Join(Environment.NewLine, GetOracleTemplateIncludesSummary());
    public bool IsWorkspaceListLoading
    {
        get => _isWorkspaceListLoading;
        private set
        {
            if (SetProperty(ref _isWorkspaceListLoading, value))
            {
                RaisePropertyChanged(nameof(ShowWorkspaceLoadingState));
                RaisePropertyChanged(nameof(ShowWorkspaceErrorState));
                RaisePropertyChanged(nameof(ShowWorkspaceReloadErrorBanner));
                RaisePropertyChanged(nameof(ShowWorkspaceListState));
                RaisePropertyChanged(nameof(ShowWorkspaceDetailsPane));
                RaisePropertyChanged(nameof(ShowOnboardingState));
                RaisePropertyChanged(nameof(ShowSelectionGuidanceState));
                RaisePropertyChanged(nameof(ShowWorkspaceDetails));
                RaisePropertyChanged(nameof(ShowWorkspaceSidePanels));
                RaisePropertyChanged(nameof(CanStartCreateWorkspaceFlow));
                RaisePropertyChanged(nameof(CreateWorkspaceDisabledReason));
                RaisePropertyChanged(nameof(CanCreateWorkspaceForDialog));
                RaiseCommandStates();
            }
        }
    }
    public bool WorkspaceListLoadFailed
    {
        get => _workspaceListLoadFailed;
        private set
        {
            if (SetProperty(ref _workspaceListLoadFailed, value))
            {
                RaisePropertyChanged(nameof(ShowWorkspaceErrorState));
                RaisePropertyChanged(nameof(ShowWorkspaceReloadErrorBanner));
                RaisePropertyChanged(nameof(ShowWorkspaceListState));
                RaisePropertyChanged(nameof(ShowWorkspaceDetailsPane));
                RaisePropertyChanged(nameof(ShowOnboardingState));
                RaisePropertyChanged(nameof(ShowSelectionGuidanceState));
            }
        }
    }
    public string WorkspaceListErrorMessage
    {
        get => _workspaceListErrorMessage;
        private set
        {
            if (SetProperty(ref _workspaceListErrorMessage, value))
            {
                RaisePropertyChanged(nameof(WorkspaceLoadFailedDescription));
                RaisePropertyChanged(nameof(WorkspaceReloadErrorBannerText));
            }
        }
    }
    public bool HasAnyWorkspaces => Workspaces.Count > 0;
    public bool HasRunningWorkspace => Workspaces.Any(workspace => workspace.IsRunning);
    public bool HasSelectedWorkspaceItem => SelectedWorkspace is not null;
    public bool ShowWorkspaceLoadingState => IsWorkspaceListLoading;
    public bool ShowWorkspaceErrorState => !IsWorkspaceListLoading && WorkspaceListLoadFailed && !HasAnyWorkspaces;
    public bool ShowWorkspaceReloadErrorBanner => !IsWorkspaceListLoading && WorkspaceListLoadFailed && HasAnyWorkspaces;
    public bool ShowWorkspaceListState => HasAnyWorkspaces;
    public bool ShowOnboardingState => !IsWorkspaceListLoading && !WorkspaceListLoadFailed && !HasAnyWorkspaces;
    public bool ShowSelectionGuidanceState => ShowWorkspaceListState && !HasSelectedWorkspaceItem;
    public bool ShowWorkspaceDetails => HasSelectedWorkspaceItem && !IsWorkspaceListLoading;
    public bool ShowWorkspaceDetailsPane => HasAnyWorkspaces;
    public bool ShowWorkspaceSidePanels => !IsWorkspaceListLoading;
    public bool ShowNoRemoteActions => SelectedWorkspace?.Snapshot.Safety.Backup.HasRemoteConfigured == false;
    public bool ShowUnpublishedSavePointActions => SelectedWorkspace?.Snapshot.Safety.Backup.HasUnpublishedSavePoints == true;
    public bool ShowNeedsReviewActions => SelectedWorkspace?.Snapshot.Safety.Backup.NeedsReviewBeforePublish == true
        || SelectedWorkspace?.Snapshot.Safety.Backup.IsOnProtectedBranch == true;
    public bool ShowRecoverWorkspaceAction => SelectedWorkspace?.HasError == true;
    public bool ShowViewWorkspaceErrorAction => SelectedWorkspace?.HasError == true;
    public bool ShowStartWorkspaceAction => SelectedWorkspace is not null && SelectedWorkspace.Snapshot.RuntimeState != WorkspaceRuntimeState.Running;
    public bool CanShutDownSelectedWorkspace => SelectedWorkspace is not null && !IsBusy && SelectedWorkspace.Snapshot.RuntimeState == WorkspaceRuntimeState.Running;
    public bool CanStartCreateWorkspaceFlow => !IsBusy && !IsWorkspaceListLoading;
    public bool IsBusyForDiagnostics => IsBusy;
    public bool CanCreateWorkspaceForDialog => CanCreateWorkspace();
    public bool IsNewWorkspaceSource => SelectedWorkspaceSourceType == WorkspaceSourceType.NewWorkspace;
    public bool IsExistingGitCheckoutSource => SelectedWorkspaceSourceType == WorkspaceSourceType.ExistingGitCheckout;
    public bool ShowCatalogSelectionOptions => true;
    public bool HasLoadedRepositoryConfiguration => _loadedRepositoryDefinition is not null;
    public bool HasInvalidRepositoryConfiguration => _loadedRepositoryConfigurationIsInvalid;
    public bool ShowRepositoryConfigurationBanner => HasLoadedRepositoryConfiguration;
    public string LoadedRepositoryConfigurationPath => _loadedRepositoryConfigurationPath;
    public string RepositoryConfigurationBannerTitle => "Existing workspace configuration found.";
    public string RepositoryConfigurationBannerMessage => "This repository already contains workspace settings. The configuration has been loaded and can be reviewed or modified.";
    public string InvalidRepositoryConfigurationMessage => _loadedRepositoryConfigurationError;
    public string CreateWorkspaceDisabledReason => string.Empty;
    public string WorkspaceNameValidationMessage => string.IsNullOrWhiteSpace(NewWorkspaceName)
        ? _localization.Get("validation.workspaceNameRequired")
        : string.Empty;
    public string WorkspacePathValidationMessage => string.IsNullOrWhiteSpace(NewWorkspacePath)
        ? _localization.Get("validation.workspacePathRequired")
        : string.Empty;
    public string ExistingRepositoryPathValidationMessage => SelectedWorkspaceSourceType == WorkspaceSourceType.ExistingGitCheckout && string.IsNullOrWhiteSpace(ExistingRepositoryPath)
        ? "Select an existing Git checkout."
        : SelectedWorkspaceSourceType == WorkspaceSourceType.ExistingGitCheckout && HasInvalidRepositoryConfiguration
            ? "Fix the existing repository configuration before continuing."
        : string.Empty;
    public string WorkspaceLoadingTitle => "Loading workspaces...";
    public string WorkspaceLoadingDescription => "Please wait while workspace status is refreshed.";
    public string WorkspaceLoadFailedTitle => "Workspace list could not be loaded";
    public string WorkspaceLoadFailedDescription => string.IsNullOrWhiteSpace(WorkspaceListErrorMessage)
        ? "Try refreshing the workspace list. The previous workspace list could not be loaded."
        : WorkspaceListErrorMessage;
    public string WorkspaceReloadErrorBannerText => string.IsNullOrWhiteSpace(WorkspaceListErrorMessage)
        ? "Workspace refresh failed. Showing the previous list. Retry when ready."
        : WorkspaceListErrorMessage;

    public async Task InitializeAsync()
    {
        await RefreshHealthChecksAsync();
        await ReloadWorkspaceListAsync();
        if (!WorkspaceListLoadFailed)
        {
            StatusMessage = Workspaces.Count == 0
                ? _localization.Get("status.none")
                : string.Format(_localization.Get("status.loadedWorkspaces"), Workspaces.Count);
        }
    }

    public async Task InitializeBackgroundAsync(StartupDiagnosticsService diagnostics)
    {
        diagnostics.Log("Background initialization begin.");
        diagnostics.Log("Workspace list loading start.");
        await ReloadWorkspaceListAsync(diagnosticsLog: diagnostics.Log);
        diagnostics.Log($"Workspace list loading complete. Count={Workspaces.Count}. Failed={WorkspaceListLoadFailed}.");

        try
        {
            await RefreshHealthChecksAsync();
            diagnostics.Log($"Health checks complete. Count={Diagnostics.Count}.");
        }
        catch (Exception exception)
        {
            diagnostics.Log($"Health checks failed: {exception.Message}");
            StatusMessage = string.IsNullOrWhiteSpace(StatusMessage) || string.Equals(StatusMessage, _localization.Get("status.none"), StringComparison.Ordinal)
                ? "Some health checks could not be completed."
                : StatusMessage;
        }

        if (string.IsNullOrWhiteSpace(StatusMessage) || string.Equals(StatusMessage, _localization.Get("status.none"), StringComparison.Ordinal))
        {
            StatusMessage = Workspaces.Count == 0
                ? _localization.Get("status.none")
                : string.Format(_localization.Get("status.loadedWorkspaces"), Workspaces.Count);
        }

        diagnostics.Log($"Background initialization finished. CanStartCreateWorkspaceFlow={CanStartCreateWorkspaceFlow}.");
    }

    public bool ShouldPromptForQuickTutorial()
        => _tutorialService.ShouldPromptForQuickTutorial();

    public void MarkQuickTutorialPromptHandled()
        => _tutorialService.MarkQuickTutorialPromptHandled();

    public QuickTutorialViewModel CreateQuickTutorialViewModel()
        => new(_tutorialService.LoadTutorial(SelectedWorkspace?.RootPath));

    public string GetText(string key)
        => _localization.Get(key);

    public void PrepareCreateWorkspaceDialog()
    {
        SelectedWorkspaceSourceType = WorkspaceSourceType.NewWorkspace;
        ExistingRepositoryPath = string.Empty;
        ClearLoadedRepositoryConfiguration();
        LoadCatalogSelections();

        var templateToApply = SelectedTemplate
            ?? Templates.FirstOrDefault(template => string.Equals(template.Id, "general-development", StringComparison.OrdinalIgnoreCase))
            ?? Templates.FirstOrDefault();

        if (templateToApply is null)
        {
            return;
        }

        if (!ReferenceEquals(SelectedTemplate, templateToApply))
        {
            SelectedTemplate = templateToApply;
            return;
        }

        ApplyTemplate(templateToApply);
        RaisePropertyChanged(nameof(IsOracleDemoTemplateSelected));
    }

    public async Task<bool> CreateWorkspaceFromDialogAsync(string buttonSource, Action<string>? diagnosticsLog = null)
    {
        diagnosticsLog?.Invoke($"Create Workspace input validation started via {buttonSource}.");
        if (!CanCreateWorkspace())
        {
            diagnosticsLog?.Invoke($"Create Workspace input validation failed via {buttonSource}.");
            return false;
        }

        diagnosticsLog?.Invoke($"Create Workspace input validated via {buttonSource}.");
        diagnosticsLog?.Invoke($"Create Workspace path resolution started via {buttonSource}. path='{NewWorkspacePath.Trim()}' name='{NewWorkspaceName.Trim()}' template='{SelectedTemplate?.Id ?? "none"}'.");
        diagnosticsLog?.Invoke($"Create Workspace selected features: {string.Join(", ", AvailableFeatures.Where(item => item.IsSelected).Select(item => item.Id))}.");
        diagnosticsLog?.Invoke($"Create Workspace selected services: {string.Join(", ", AvailableServices.Where(item => item.IsSelected).Select(item => item.Id))}.");
        diagnosticsLog?.Invoke($"Create Workspace path resolution completed via {buttonSource}.");

        var existingCount = Workspaces.Count;
        await CreateWorkspaceAsync(buttonSource, diagnosticsLog);
        diagnosticsLog?.Invoke($"Create Workspace workspace list check started via {buttonSource}.");
        return Workspaces.Count > existingCount || Workspaces.Any(item => string.Equals(item.Name, NewWorkspaceName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ExistingGitCheckoutPlan> InspectExistingGitCheckoutFromDialogAsync()
    {
        var plan = await _workspaceOrchestrator.InspectExistingGitCheckoutAsync(ExistingRepositoryPath.Trim(), NewWorkspaceName.Trim());
        ApplyRepositoryConfigurationFromPlan(plan);
        return plan;
    }

    public async Task<ExistingGitCheckoutPlan> LoadExistingRepositoryConfigurationAsync(string repositoryPath)
    {
        ExistingRepositoryPath = repositoryPath.Trim();
        var plan = await _workspaceOrchestrator.InspectExistingGitCheckoutAsync(ExistingRepositoryPath, NewWorkspaceName.Trim());
        ApplyRepositoryConfigurationFromPlan(plan);
        return plan;
    }

    public async Task<GitBranchValidationResult> ValidateExistingGitCheckoutBranchAsync(string repositoryPath, string branchName)
    {
        var plan = await _workspaceOrchestrator.InspectExistingGitCheckoutAsync(repositoryPath, NewWorkspaceName.Trim());
        var repositoryService = new GitRepositoryService(new ProcessRunner());
        return await repositoryService.ValidateBranchNameAsync(plan.RepositoryPath, branchName.Trim());
    }

    public async Task<bool> ImportExistingGitCheckoutFromDialogAsync(ExistingGitCheckoutImportRequest request)
    {
        await RunBusyAsync(async () =>
        {
            var snapshot = await _workspaceOrchestrator.ImportExistingGitCheckoutAsync(request, CreateWorkspaceLogAppender(request.RepositoryPath));
            EnsureWorkspaceLogStore(snapshot.Paths.RootPath);
            AppendWorkspaceLog(snapshot.Paths.RootPath, "app", $"Imported existing Git checkout on branch '{snapshot.Safety.AdvancedGit.CurrentBranch}'.");
            await ReloadWorkspaceListAsync(snapshot.Paths.RootPath);
            StatusMessage = $"Imported existing Git checkout '{snapshot.Definition.Workspace.Name}'.";
        });

        return Workspaces.Any(item => string.Equals(item.RootPath, request.RepositoryPath, StringComparison.OrdinalIgnoreCase));
    }

    public WorkspaceDefinition BuildWorkspaceDefinitionFromSelections(string workspaceName)
    {
        var baseDefinition = _loadedRepositoryDefinition;
        return new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Name = workspaceName,
                Id = string.IsNullOrWhiteSpace(baseDefinition?.Workspace.Id) ? WorkspacePathBuilder.Slugify(workspaceName) : baseDefinition.Workspace.Id,
                Image = baseDefinition?.Workspace.Image
                    ?? (string.IsNullOrWhiteSpace(SelectedTemplate?.WorkspaceImage) ? "ubuntu:24.04" : SelectedTemplate.WorkspaceImage),
            },
            Provider = new WorkspaceProviderDefinition
            {
                Type = string.IsNullOrWhiteSpace(baseDefinition?.Provider.Type) ? "git" : baseDefinition.Provider.Type,
                Url = baseDefinition?.Provider.Url,
            },
            Runtime = new WorkspaceRuntimeDefinition
            {
                Default = baseDefinition?.Runtime.Default ?? "default",
                Node = baseDefinition?.Runtime.Node ?? WorkspaceRuntimeDefinition.DefaultNodeMajorVersion,
            },
            Features = AvailableFeatures.Where(item => item.IsSelected).Select(item => item.Id).ToList(),
            Services = AvailableServices.Where(item => item.IsSelected).Select(item => item.Id).ToList(),
            Skills = baseDefinition?.Skills.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                ?? SelectedTemplate?.Skills.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                ?? new List<string>(),
            Mcp = baseDefinition?.Mcp.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                ?? SelectedTemplate?.Mcp.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                ?? new List<string>(),
            Agent = new AgentPreferences
            {
                Profile = string.IsNullOrWhiteSpace(baseDefinition?.Agent.Profile) ? AgentProfileResolver.BuiltInDefault.ProfileId : baseDefinition.Agent.Profile,
                Provider = baseDefinition?.Agent.Provider,
                Connection = baseDefinition?.Agent.Connection,
                Model = baseDefinition?.Agent.Model,
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
    }

    public void ClearLoadedRepositoryConfiguration()
    {
        _loadedRepositoryDefinition = null;
        _loadedRepositoryConfigurationPath = string.Empty;
        _loadedRepositoryConfigurationError = string.Empty;
        _loadedRepositoryConfigurationIsInvalid = false;
        RaisePropertyChanged(nameof(HasLoadedRepositoryConfiguration));
        RaisePropertyChanged(nameof(HasInvalidRepositoryConfiguration));
        RaisePropertyChanged(nameof(ShowRepositoryConfigurationBanner));
        RaisePropertyChanged(nameof(LoadedRepositoryConfigurationPath));
        RaisePropertyChanged(nameof(InvalidRepositoryConfigurationMessage));
        RaisePropertyChanged(nameof(ExistingRepositoryPathValidationMessage));
        RaisePropertyChanged(nameof(CanCreateWorkspaceForDialog));
    }

    public void ApplyInvalidRepositoryConfiguration(string configurationPath, string errorMessage)
    {
        _loadedRepositoryDefinition = null;
        _loadedRepositoryConfigurationPath = configurationPath;
        _loadedRepositoryConfigurationError = errorMessage;
        _loadedRepositoryConfigurationIsInvalid = true;
        RaisePropertyChanged(nameof(HasLoadedRepositoryConfiguration));
        RaisePropertyChanged(nameof(HasInvalidRepositoryConfiguration));
        RaisePropertyChanged(nameof(ShowRepositoryConfigurationBanner));
        RaisePropertyChanged(nameof(LoadedRepositoryConfigurationPath));
        RaisePropertyChanged(nameof(InvalidRepositoryConfigurationMessage));
        RaisePropertyChanged(nameof(ExistingRepositoryPathValidationMessage));
        RaisePropertyChanged(nameof(CanCreateWorkspaceForDialog));
    }

    public void ApplyRepositoryConfigurationFromPlan(ExistingGitCheckoutPlan plan)
    {
        if (plan.DiscoveryResult.Status == WorkspaceDiscoveryStatus.Invalid)
        {
            ApplyInvalidRepositoryConfiguration(
                plan.DiscoveryResult.ConfigurationPath ?? "workspace configuration",
                plan.DiscoveryResult.ErrorMessage ?? "The configuration could not be loaded.");
            return;
        }

        if (plan.LoadedDefinition is null || string.IsNullOrWhiteSpace(plan.DiscoveryResult.ConfigurationPath))
        {
            ClearLoadedRepositoryConfiguration();
            return;
        }

        _loadedRepositoryDefinition = plan.LoadedDefinition;
        _loadedRepositoryConfigurationPath = plan.DiscoveryResult.ConfigurationPath;
        _loadedRepositoryConfigurationError = string.Empty;
        _loadedRepositoryConfigurationIsInvalid = false;

        // Once a repository has declared its workspace configuration, those values
        // become the initial UI state instead of template defaults.
        NewWorkspaceName = plan.LoadedDefinition.Workspace.Name;
        SelectCatalogItems(plan.LoadedDefinition.Features, plan.LoadedDefinition.Services);
        SelectedPromptProvider = plan.LoadedDefinition.Terminal.Prompt.Provider;
        SelectedFontFamily = plan.LoadedDefinition.Terminal.Font.Family;
        InstallTerminalIfMissing = plan.LoadedDefinition.Terminal.InstallIfMissing;
        InstallZoxide = plan.LoadedDefinition.Terminal.Utilities.Zoxide;
        InstallFzf = plan.LoadedDefinition.Terminal.Utilities.Fzf;

        RaisePropertyChanged(nameof(HasLoadedRepositoryConfiguration));
        RaisePropertyChanged(nameof(HasInvalidRepositoryConfiguration));
        RaisePropertyChanged(nameof(ShowRepositoryConfigurationBanner));
        RaisePropertyChanged(nameof(LoadedRepositoryConfigurationPath));
        RaisePropertyChanged(nameof(InvalidRepositoryConfigurationMessage));
        RaisePropertyChanged(nameof(ExistingRepositoryPathValidationMessage));
        RaisePropertyChanged(nameof(CanCreateWorkspaceForDialog));
    }

    private async Task CreateWorkspaceAsync(string buttonSource = "HeaderCreateButton", Action<string>? diagnosticsLog = null)
    {
        var requestedWorkspacePath = NewWorkspacePath.Trim();
        var currentStep = "command setup";
        await RunBusyAsync(
            async () =>
            {
                using var createTimeout = new CancellationTokenSource(CreateWorkspaceTimeout);

                currentStep = "template application verification";
                diagnosticsLog?.Invoke($"Create Workspace template application verified via {buttonSource}. template='{SelectedTemplate?.Id ?? "none"}'.");

                currentStep = "workspace definition build";
                diagnosticsLog?.Invoke($"Create Workspace definition build started via {buttonSource}.");
                AppendCurrentLog("app", $"Creating workspace '{NewWorkspaceName.Trim()}'.");
                var definition = BuildWorkspaceDefinitionFromSelections(NewWorkspaceName.Trim());
                diagnosticsLog?.Invoke($"Create Workspace definition built via {buttonSource}.");

                currentStep = "workspace file generation and repository initialization";
                diagnosticsLog?.Invoke($"Create Workspace file generation started via {buttonSource}.");
                var snapshot = await _workspaceOrchestrator.CreateWorkspaceAsync(requestedWorkspacePath, definition, CreateWorkspaceLogAppender(requestedWorkspacePath), createTimeout.Token, includeRuntimeInspection: false);
                diagnosticsLog?.Invoke($"Create Workspace files generated via {buttonSource}.");

                currentStep = "terminal profile setup";
                var resolvedFace = _windowsHostCapabilities.ResolvePreferredTerminalFace(snapshot.Definition.Terminal.Font.Family);
                _profileManager.EnsureManagedProfile(snapshot.Definition, snapshot.Definition.Terminal.Font, resolvedFace);

                currentStep = "workspace registration metadata update";
                diagnosticsLog?.Invoke($"Create Workspace workspace registered via {buttonSource}.");
                await PersistWorkspaceRecordAsync(snapshot, CreateLabel, _localization.Get("workspace.result.created"), succeeded: true);
                diagnosticsLog?.Invoke($"Create Workspace workspaces.json saved via {buttonSource}.");

                currentStep = "workspace log initialization";
                EnsureWorkspaceLogStore(snapshot.Paths.RootPath);
                AppendWorkspaceLog(snapshot.Paths.RootPath, "app", "Created workspace files and generated runtime artifacts.");
                AppendWorkspaceLog(snapshot.Paths.RootPath, "app", $"Resolved default agent profile '{AgentProfileResolver.BuiltInDefault.ProfileId}'.");
                AppendWorkspaceLog(snapshot.Paths.RootPath, "app", $"Managed Windows Terminal profile ensured for font '{snapshot.Definition.Terminal.Font.Family}'.");
                AppendWorkspaceLog(snapshot.Paths.RootPath, "terminal", $"Profile: {_profileManager.GetProfileName(snapshot.Definition)}");
                AppendWorkspaceLog(snapshot.Paths.RootPath, "terminal", $"Configured font: {resolvedFace}");
                    AppendWorkspaceLog(snapshot.Paths.RootPath, "terminal", $"Terminal profile file: {_profileManager.GetFragmentFilePath()}");
                    AppendWorkspaceLog(snapshot.Paths.RootPath, "terminal", $"Profile id: {_profileManager.GetProfileGuid(snapshot.Definition)}");
                    if (IsOracleDemoWorkspace(snapshot.Definition))
                    {
                    AppendWorkspaceLog(snapshot.Paths.RootPath, "app", "Oracle PL/SQL Demo workspace created. Start Oracle first from the Oracle Demo Database panel. OpenCode verification should wait until the panel shows Running and Ready.");
                    }

                currentStep = "workspace list refresh";
                diagnosticsLog?.Invoke($"Create Workspace workspace list refresh started via {buttonSource}.");
                await ReloadWorkspaceListAsync(snapshot.Paths.RootPath, includeRuntimeInspection: false, cancellationToken: createTimeout.Token);
                diagnosticsLog?.Invoke($"Create Workspace workspace list refreshed via {buttonSource}.");

                currentStep = "status update";
                StatusMessage = string.Format(_localization.Get("status.workspaceCreated"), snapshot.Definition.Workspace.Name);
            },
            exception =>
            {
                var isTimeout = exception is OperationCanceledException;
                diagnosticsLog?.Invoke($"Create Workspace failed via {buttonSource} during '{currentStep}': {exception.Message}");
                AppDialogService.ShowOk(
                    TryGetDialogOwner(),
                    _localization,
                    "Create Workspace Failed",
                    $"What happened: the workspace could not be fully created during '{currentStep}'.{Environment.NewLine}{Environment.NewLine}Why: {(isTimeout ? "The operation timed out while waiting for a repository or snapshot step to finish." : exception.Message)}{Environment.NewLine}{Environment.NewLine}How to fix it: review the dialog log, then retry. Generated files may already exist at:{Environment.NewLine}{requestedWorkspacePath}");
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
        return OpenWorkspaceAsync();
    }

    private async Task StartWorkspaceAsync()
    {
        await RunWorkspaceActionAsync(
            StartWorkspaceLabel,
            async snapshot =>
            {
                if (snapshot.UpdateRequired || snapshot.AppliedState is null)
                {
                    if (!EnsureOracleSoftwareNoticeReviewed(snapshot))
                    {
                        return;
                    }

                    await _workspaceOrchestrator.ProvisionAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                    PersistWorkspaceRecord(snapshot, StartWorkspaceLabel, "Provisioned and started workspace.", succeeded: true, lastPreparedUtc: DateTimeOffset.UtcNow);
                    StatusMessage = $"Workspace '{snapshot.Definition.Workspace.Name}' was prepared and started.";
                    return;
                }

                if (snapshot.RuntimeState != WorkspaceRuntimeState.Running)
                {
                    await _workspaceOrchestrator.StartAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                }

                PersistWorkspaceRecord(snapshot, StartWorkspaceLabel, "Started workspace.", succeeded: true);
                StatusMessage = $"Workspace '{snapshot.Definition.Workspace.Name}' is running.";
            });
    }

    private async Task RecoverWorkspaceAsync()
    {
        await RunWorkspaceActionAsync(
            RecoverWorkspaceLabel,
            async snapshot =>
            {
                await _workspaceOrchestrator.RecoverAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                PersistWorkspaceRecord(snapshot, RecoverWorkspaceLabel, "Recovered workspace and validated generated files.", succeeded: true);
                StatusMessage = $"Workspace '{snapshot.Definition.Workspace.Name}' was recovered. Start is available again.";
            });
    }

    private void ConfigureRemoteBackup()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        AppDialogService.ShowOk(
            Application.Current?.MainWindow,
            _localization,
            _localization.Get("safety.configureRemoteBackup.title"),
            string.Format(_localization.Get("safety.configureRemoteBackup.message"), SelectedWorkspace.Snapshot.Paths.WorkspaceYamlPath));
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

        AppDialogService.ShowOk(Application.Current?.MainWindow, _localization, OpenAdvancedGitViewLabel, details);
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
                        var restartChoice = AppDialogService.ShowYesNo(
                            Application.Current?.MainWindow,
                            _localization,
                            _localization.Get("dialog.restartForUpdate.title"),
                            string.Format(_localization.Get("dialog.restartForUpdate.message"), snapshot.Definition.Workspace.Name));

                        if (restartChoice != AppDialogResult.Yes)
                        {
                            await _workspaceOrchestrator.LaunchAttachForRunningWorkspaceAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                            PersistWorkspaceRecord(snapshot, _localization.Get("operation.open"), _localization.Get("workspace.result.opened"), succeeded: true, lastOpenedUtc: DateTimeOffset.UtcNow);
                            StatusMessage = string.Format(_localization.Get("status.workspaceOpened"), snapshot.Definition.Workspace.Name);
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

        if (IsOracleDemoWorkspace(snapshot.Definition))
        {
            if (!EnsureOracleSoftwareNoticeReviewed(snapshot))
            {
                return;
            }

            SetOracleStartupStage("Provisioning SQLcl");
            StatusMessage = "Provisioning Oracle demo workspace. SQLcl is downloaded from Oracle on first run and requires internet access.";
            AppendWorkspaceLog(snapshot.Paths.RootPath, "app", "Provisioning Oracle demo workspace. SQLcl download requires internet access on first run.");
        }

        await _workspaceOrchestrator.ProvisionAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
        if (IsOracleDemoWorkspace(snapshot.Definition))
        {
            SetOracleStartupStage(openAfterUpdate ? "Ready" : "Start Oracle first");
        }
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

    private async Task StartOracleDemoAsync()
    {
        await RunWorkspaceActionAsync(
            "Start Oracle Demo",
            async snapshot =>
            {
                if (snapshot.UpdateRequired || snapshot.AppliedState is null)
                {
                    if (!EnsureOracleSoftwareNoticeReviewed(snapshot))
                    {
                        StatusMessage = "Oracle software notice was cancelled. Provisioning did not start.";
                        return;
                    }

                    SetOracleStartupStage("Provisioning SQLcl");
                    StatusMessage = "Provisioning Oracle demo workspace. SQLcl is downloaded from Oracle on first run and requires internet access.";
                    AppendWorkspaceLog(snapshot.Paths.RootPath, "app", "Starting Oracle demo workspace. SQLcl download requires internet access on first run.");
                    await _workspaceOrchestrator.ProvisionAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                    PersistWorkspaceRecord(snapshot, "Start Oracle Demo", "Provisioned and started Oracle demo workspace.", succeeded: true, lastPreparedUtc: DateTimeOffset.UtcNow);
                }
                else if (snapshot.RuntimeState != WorkspaceRuntimeState.Running)
                {
                    SetOracleStartupStage("Starting Oracle");
                    AppendWorkspaceLog(snapshot.Paths.RootPath, "app", "Starting local Oracle container.");
                    await _workspaceOrchestrator.StartAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                    PersistWorkspaceRecord(snapshot, "Start Oracle Demo", "Started Oracle demo workspace.", succeeded: true);
                }

                SetOracleStartupStage("Waiting For Health Check");
                AppendWorkspaceLog(snapshot.Paths.RootPath, "app", "Waiting for Oracle health check to report ready.");
                StatusMessage = $"Oracle demo database is ready for '{snapshot.Definition.Workspace.Name}'.";
                SetOracleStartupStage("Ready");
            });
    }

    private async Task ResetOracleDemoAsync()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        var confirmation = AppDialogService.ShowYesNo(
            TryGetDialogOwner(),
            _localization,
            "Reset Oracle Demo Database",
            $"Reset deletes the local Oracle demo data volume for '{SelectedWorkspace.Name}'.\n\nThis removes the local demo schema, sample data, and any local Oracle changes in this workspace. Continue?");

        if (confirmation != AppDialogResult.Yes)
        {
            StatusMessage = "Oracle demo reset was cancelled. Local Oracle demo data was kept.";
            return;
        }

        await RunWorkspaceActionAsync(
            "Reset Oracle Demo",
            async snapshot =>
            {
                SetOracleStartupStage("Not Provisioned");
                StatusMessage = "Resetting Oracle demo database. This deletes local Oracle demo data and recreates it from the generated initialization scripts.";
                await _workspaceOrchestrator.ResetRuntimeAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(snapshot.Paths.RootPath);
                SetOracleStartupStage("Provisioning SQLcl");
                await _workspaceOrchestrator.ProvisionAsync(snapshot, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                PersistWorkspaceRecord(snapshot, "Reset Oracle Demo", "Reset Oracle demo database volume and reprovisioned the workspace.", succeeded: true, lastPreparedUtc: DateTimeOffset.UtcNow);
                StatusMessage = $"Oracle demo database was reset for '{snapshot.Definition.Workspace.Name}'.";
                SetOracleStartupStage("Ready");
            });
    }

    private async Task ViewOracleLogsAsync()
    {
        await RunWorkspaceActionAsync(
            "View Oracle Logs",
            async snapshot =>
            {
                var result = await _dockerService.GetServiceLogsAsync(snapshot.Paths, snapshot.Definition, "oracle-demo", CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError);
                }

                StatusMessage = $"Loaded Oracle logs for '{snapshot.Definition.Workspace.Name}'.";
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
            var retry = AppDialogService.ShowYesNo(
                Application.Current?.MainWindow,
                _localization,
                _localization.Get("remove.deleteFailed.title"),
                string.Format(
                    _localization.Get("remove.deleteFailed.message"),
                    snapshot.Paths.RootPath,
                    string.Join(Environment.NewLine, failedPaths)));

            if (retry != AppDialogResult.Yes)
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
                    AppendWorkspaceLog(snapshot.Paths.RootPath, "runtime", $"Effective Node.js runtime: {snapshot.Definition.Runtime.GetEffectiveNodeMajorVersion()}.");
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
                    await ReloadWorkspaceListAsync(refreshSelectionPath ?? snapshot.Paths.RootPath);
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

    private void CopyOracleConnectionDetails()
    {
        if (!SelectedWorkspaceHasOracleDemo)
        {
            return;
        }

        try
        {
            Clipboard.SetText($"Host: {OracleDemoHost}{Environment.NewLine}Port: {OracleDemoPort}{Environment.NewLine}Service: {OracleDemoServiceName}{Environment.NewLine}Username: {OracleDemoUsername}{Environment.NewLine}Password: {OracleDemoPassword}");
            StatusMessage = $"Copied Oracle connection details for '{SelectedWorkspace?.Name}'.";
        }
        catch (Exception exception)
        {
            ShowOracleActionError("Copy Connection Details Failed", "The app could not copy the Oracle connection details to the clipboard.", exception.Message, "Try copying again. If clipboard access is blocked by the desktop session, copy the displayed localhost details manually.");
        }
    }

    private void OpenOracleSqlcl() => RunWorkspaceScript("open-sqlcl.ps1", "Opened SQLcl launcher.");

    private void OpenOracleOrds() => OpenOracleUrl("http://localhost:8181/ords", "Opened ORDS.");

    private void OpenOracleApex() => OpenOracleUrl("http://localhost:8181/ords/apex", "Opened APEX.");

    private void OpenOracleSqlWorksheet() => RunWorkspaceScript(Path.Combine("scripts", "open-sql-worksheet.ps1"), "Opened SQL worksheet launcher.");

    private void RunOracleTutorialQuery() => RunWorkspaceScript("run-tutorial-query.ps1", "Ran Oracle tutorial query.");

    private void TestOracleConnection() => RunWorkspaceScript("test-oracle-connection.ps1", "Tested Oracle SQLcl connection.");

    private void OpenSqlDeveloper()
    {
        if (string.IsNullOrWhiteSpace(_sqlDeveloperExecutablePath))
        {
            ShowOracleActionError("SQL Developer Not Detected", "SQL Developer was not detected on this Windows machine.", "The demo can still continue without it.", "Install SQL Developer or use Open SQLcl from the Oracle Demo Database panel.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _sqlDeveloperExecutablePath,
                UseShellExecute = true,
            });
            StatusMessage = "Opened SQL Developer.";
        }
        catch (Exception exception)
        {
            ShowOracleActionError("Open SQL Developer Failed", "The app could not open SQL Developer.", exception.Message, "Use Open SQLcl instead, or verify the SQL Developer installation path on Windows.");
        }
    }

    private async Task RunTmpReprovisionWorkflowAsync()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var snapshot = _workspaceOrchestrator.LoadSnapshot(SelectedWorkspace.RootPath);
            EnsureWorkspaceLogStore(snapshot.Paths.RootPath);
            AppendWorkspaceLog(snapshot.Paths.RootPath, "dev", $"Developer tmp reprovision requested for '{snapshot.Definition.Workspace.Name}'.");
            await _tmpReprovisionWorkflowService.RunAsync(snapshot.Paths.RootPath, CreateWorkspaceLogAppender(snapshot.Paths.RootPath));
            StatusMessage = $"Developer tmp reprovision completed for '{snapshot.Definition.Workspace.Name}'.";
            await ReloadWorkspaceListAsync(snapshot.Paths.RootPath);
        });
    }

    private void RunWorkspaceScript(string scriptFileName, string successMessage)
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        var snapshot = SelectedWorkspace.Snapshot;
        if (snapshot.RuntimeState != WorkspaceRuntimeState.Running)
        {
            StatusMessage = "Start Oracle first. Oracle demo verification is only available after the local runtime is Running and Ready.";
            AppDialogService.ShowOk(
                TryGetDialogOwner(),
                _localization,
                "Oracle Demo Database Not Running",
                "Start Oracle first.\n\nWhat happened: the local Oracle demo runtime is stopped.\nWhy: OpenCode verification and Oracle helper scripts need the running local demo database.\nHow to fix it: start Oracle Demo Database from the Oracle panel, wait for Running and Ready, then try again.");
            return;
        }

        if (snapshot.UpdateRequired || snapshot.AppliedState is null)
        {
            AppDialogService.ShowOk(
                TryGetDialogOwner(),
                _localization,
                "SQLcl Not Ready Yet",
                "SQLcl not downloaded yet.\n\nWhat happened: SQLcl is not installed in this workspace yet.\nWhy: the first Oracle provisioning run downloads SQLcl from Oracle.\nHow to fix it: start Oracle provisioning first with internet access, then try again.");
            StatusMessage = "SQLcl is not ready yet. Start Oracle with internet access first so provisioning can download SQLcl.";
            return;
        }

        var scriptPath = Path.Combine(SelectedWorkspace.RootPath, scriptFileName);
        if (!File.Exists(scriptPath))
        {
            ShowOracleActionError("Oracle Helper Script Missing", $"The Oracle action '{scriptFileName}' is not available in this workspace.", "The generated helper script is missing from the workspace folder.", "Recreate the workspace or regenerate it, then try the Oracle action again.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                WorkingDirectory = SelectedWorkspace.RootPath,
            });
            StatusMessage = successMessage;
        }
        catch (Exception exception)
        {
            ShowOracleActionError("Oracle Action Failed", $"The app could not start '{scriptFileName}'.", exception.Message, "Verify PowerShell is available on Windows and try the Oracle action again.");
        }
    }

    private void ShowWorkspaceError()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        AppDialogService.ShowOk(
            TryGetDialogOwner(),
            _localization,
            string.Format(_localization.Get("dialog.workspaceError.title"), SelectedWorkspace.Name),
            SelectedWorkspace.LastOperationResult);
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

    public void AppendCreateDialogDiagnostic(string message)
        => AppendCurrentLog("create", message);

    private async Task ReloadWorkspaceListAsync(string? selectRootPath = null, bool includeRuntimeInspection = true, CancellationToken cancellationToken = default, Action<string>? diagnosticsLog = null)
    {
        var preservedSelectionPath = selectRootPath ?? SelectedWorkspace?.RootPath;
        IsWorkspaceListLoading = true;
        WorkspaceListLoadFailed = false;
        WorkspaceListErrorMessage = string.Empty;

        try
        {
            var loadedWorkspaces = await LoadWorkspaceItemsAsync(includeRuntimeInspection, cancellationToken);
            ReplaceWorkspaceList(loadedWorkspaces, preservedSelectionPath);
            diagnosticsLog?.Invoke($"Workspace list refresh complete. Count={Workspaces.Count}.");
        }
        catch (Exception exception)
        {
            WorkspaceListLoadFailed = true;
            WorkspaceListErrorMessage = $"Workspace refresh failed. Showing the previous list. {exception.Message}";
            StatusMessage = "Workspace refresh failed. Showing the previous list.";
            diagnosticsLog?.Invoke($"Workspace list refresh failed: {exception.Message}");
        }
        finally
        {
            IsWorkspaceListLoading = false;
        }
    }

    private async Task<List<WorkspaceListItemViewModel>> LoadWorkspaceItemsAsync(bool includeRuntimeInspection, CancellationToken cancellationToken)
    {
        var loadedWorkspaces = new List<WorkspaceListItemViewModel>();
        foreach (var record in _workspaceOrchestrator.LoadWorkspaceRecords())
        {
            var configurationPath = WorkspacePathBuilder.NormalizeConfigurationRelativePath(record.ConfigurationPath);
            if (!File.Exists(Path.Combine(record.RootPath, configurationPath.Replace('/', Path.DirectorySeparatorChar))))
            {
                continue;
            }

            try
            {
                var snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(record.RootPath, cancellationToken, includeRuntimeInspection);
                var workspaceItem = new WorkspaceListItemViewModel(snapshot, _localization);
                loadedWorkspaces.Add(workspaceItem);
            }
            catch
            {
                continue;
            }
        }

        return loadedWorkspaces;
    }

    private void ReplaceWorkspaceList(IReadOnlyList<WorkspaceListItemViewModel> loadedWorkspaces, string? selectRootPath)
    {
        Workspaces.Clear();
        foreach (var workspaceItem in loadedWorkspaces)
        {
            Workspaces.Add(workspaceItem);
            if (workspaceItem.HasError)
            {
                AppendCurrentLog("status", $"Workspace '{workspaceItem.Name}' is in Error state. {workspaceItem.ErrorDetails}");
            }
            else
            {
                AppendCurrentLog("status", $"Workspace '{workspaceItem.Name}' status: {workspaceItem.StatusLabel}. {workspaceItem.StatusDetails}");
            }
        }

        SelectedWorkspace = Workspaces.FirstOrDefault(item => string.Equals(item.RootPath, selectRootPath, StringComparison.OrdinalIgnoreCase))
            ?? Workspaces.FirstOrDefault();
        RaisePropertyChanged(nameof(HasRunningWorkspace));
        RaisePropertyChanged(nameof(HasAnyWorkspaces));
        RaisePropertyChanged(nameof(ShowWorkspaceLoadingState));
        RaisePropertyChanged(nameof(ShowWorkspaceErrorState));
        RaisePropertyChanged(nameof(ShowWorkspaceReloadErrorBanner));
        RaisePropertyChanged(nameof(ShowWorkspaceListState));
        RaisePropertyChanged(nameof(ShowOnboardingState));
        RaisePropertyChanged(nameof(ShowSelectionGuidanceState));
        RaisePropertyChanged(nameof(ShowWorkspaceDetails));
        RaisePropertyChanged(nameof(ShowWorkspaceDetailsPane));
        RaisePropertyChanged(nameof(ShowWorkspaceSidePanels));
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

    private void SelectCatalogItems(IEnumerable<string> selectedFeatures, IEnumerable<string> selectedServices)
    {
        var featureIds = new HashSet<string>(selectedFeatures, StringComparer.OrdinalIgnoreCase);
        foreach (var feature in AvailableFeatures)
        {
            feature.IsSelected = feature.IsLocked || featureIds.Contains(feature.Id);
        }

        var serviceIds = new HashSet<string>(selectedServices, StringComparer.OrdinalIgnoreCase);
        foreach (var service in AvailableServices)
        {
            service.IsSelected = serviceIds.Contains(service.Id);
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

    private bool CanCreateWorkspace()
    {
        if (IsBusy || !CanStartCreateWorkspaceFlow || string.IsNullOrWhiteSpace(NewWorkspaceName))
        {
            return false;
        }

        return SelectedWorkspaceSourceType == WorkspaceSourceType.ExistingGitCheckout
            ? !string.IsNullOrWhiteSpace(ExistingRepositoryPath) && !HasInvalidRepositoryConfiguration
            : !string.IsNullOrWhiteSpace(NewWorkspacePath);
    }

    private static string GetWorkspaceNameFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.GetFileName(path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private bool HasSelectedWorkspace() => SelectedWorkspace is not null && !IsBusy && !IsWorkspaceListLoading;

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

    private async Task RunBusyAsync(Func<Task> action, Action<Exception>? onError = null)
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
            onError?.Invoke(exception);
            if (SelectedWorkspaceHasOracleDemo)
            {
                AppDialogService.ShowOk(
                    TryGetDialogOwner(),
                    _localization,
                    "Oracle Demo Action Failed",
                    $"What happened: {exception.Message}{Environment.NewLine}{Environment.NewLine}Why: the Oracle demo action could not complete with the current local workspace state.{Environment.NewLine}{Environment.NewLine}How to fix it: review the Oracle panel status and the log output below, then retry the action. If provisioning was incomplete, start Oracle again with internet access.");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string GetOracleStartupStageFromSnapshot()
    {
        if (!SelectedWorkspaceHasOracleDemo || SelectedWorkspace is null)
        {
            return string.Empty;
        }

        if (SelectedWorkspace.Snapshot.RuntimeState == WorkspaceRuntimeState.Running)
        {
            return "Ready";
        }

        return SelectedWorkspace.Snapshot.UpdateRequired || SelectedWorkspace.Snapshot.AppliedState is null
            ? "Not Provisioned"
            : "Start Oracle first";
    }

    private string GetOracleOpenCodeGuidance()
    {
        if (!SelectedWorkspaceHasOracleDemo || SelectedWorkspace is null)
        {
            return string.Empty;
        }

        if (SelectedWorkspace.Snapshot.RuntimeState != WorkspaceRuntimeState.Running)
        {
            return "Start Oracle first. OpenCode should not run Oracle verification until the Oracle Demo Database panel shows Running and Ready.";
        }

        if (SelectedWorkspace.Snapshot.UpdateRequired || SelectedWorkspace.Snapshot.AppliedState is null)
        {
            return "Finish Oracle provisioning first. OpenCode should use the known local demo connection and wait for the panel to show Running and Ready before verification.";
        }

        return SelectedWorkspaceHasOracleApex
            ? "OpenCode inside the workspace should use demo_user/demo_password@//oracle-demo:1521/FREEPDB1. Use the known local demo connection, do not ask for credentials, and validate database, ORDS, and APEX reachability before application work."
            : "OpenCode inside the workspace should use demo_user/demo_password@//oracle-demo:1521/FREEPDB1. Use the known local demo connection, do not ask for credentials, and run scripts/verify-oracle-demo.sh.";
    }

    private bool EnsureOracleSoftwareNoticeReviewed(WorkspaceSnapshot snapshot)
    {
        if (!IsOracleDemoWorkspace(snapshot.Definition)
            || snapshot.Record.OracleSoftwareNoticeShown
            || _oracleNoticeAcknowledgedWorkspacePaths.Contains(snapshot.Record.RootPath))
        {
            return true;
        }

        var message = string.Join(Environment.NewLine + Environment.NewLine, new[]
        {
            "This workspace provisions Oracle software from Oracle-provided sources.",
            "Oracle software is subject to Oracle licensing terms.",
            "Please review applicable Oracle licensing information before continuing.",
            "Oracle Database Free: https://www.oracle.com/database/free/",
            "Oracle APEX: https://apex.oracle.com/",
            "Oracle ORDS: https://www.oracle.com/database/technologies/appdev/rest.html",
            "Oracle Licensing Information: https://www.oracle.com/corporate/license/",
        });

        LastOracleNoticeMessageForTests = message;

        AppDialogResult result;
        if (OracleNoticePromptOverrideForTests is not null)
        {
            result = OracleNoticePromptOverrideForTests(message);
        }
        else
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA || Application.Current is null)
            {
                return true;
            }

            result = AppDialogService.ShowYesNo(
                TryGetDialogOwner(),
                _localization,
                "Oracle Software Notice",
                message,
                "Continue",
                "Cancel");
        }

        if (result != AppDialogResult.Yes)
        {
            return false;
        }

        _oracleNoticeAcknowledgedWorkspacePaths.Add(snapshot.Record.RootPath);

        _workspaceOrchestrator.SaveRecord(new WorkspaceRecord
        {
            Name = snapshot.Record.Name,
            RootPath = snapshot.Record.RootPath,
            RepositoryPath = snapshot.Record.RepositoryPath,
            ConfigurationPath = snapshot.Record.ConfigurationPath,
            SourceType = snapshot.Record.SourceType,
            ImportedFromExistingCheckout = snapshot.Record.ImportedFromExistingCheckout,
            OriginalDefaultBranch = snapshot.Record.OriginalDefaultBranch,
            SelectedWorkspaceBranch = snapshot.Record.SelectedWorkspaceBranch,
            RemoteOriginUrl = snapshot.Record.RemoteOriginUrl,
            CreatedUtc = snapshot.Record.CreatedUtc,
            LastOpenedUtc = snapshot.Record.LastOpenedUtc,
            LastPreparedUtc = snapshot.Record.LastPreparedUtc,
            OracleSoftwareNoticeShown = true,
            LastOperationName = snapshot.Record.LastOperationName,
            LastOperationResult = snapshot.Record.LastOperationResult,
            LastOperationSucceeded = snapshot.Record.LastOperationSucceeded,
            LastOperationUtc = snapshot.Record.LastOperationUtc,
        });

        return true;
    }

    internal bool EnsureOracleSoftwareNoticeReviewedForTests(WorkspaceSnapshot snapshot)
        => EnsureOracleSoftwareNoticeReviewed(snapshot);

    private void OpenOracleUrl(string url, string successMessage)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
            StatusMessage = successMessage;
        }
        catch (Exception exception)
        {
            ShowOracleActionError("Oracle URL Action Failed", $"The app could not open '{url}'.", exception.Message, "Verify the default browser configuration on Windows and try again.");
        }
    }

    private void SetOracleStartupStage(string stage)
    {
        _oracleStartupStageOverride = stage;
        RaisePropertyChanged(nameof(OracleStartupStage));
        RaisePropertyChanged(nameof(OracleOpenCodeGuidance));
    }

    private void ShowOracleActionError(string title, string whatHappened, string why, string howToFix)
    {
        var message = string.Join(Environment.NewLine + Environment.NewLine, new[]
        {
            $"What happened: {whatHappened}",
            $"Why: {why}",
            $"How to fix it: {howToFix}",
        });

        AppDialogService.ShowOk(TryGetDialogOwner(), _localization, title, message);
        StatusMessage = whatHappened;
    }

    private static Window? TryGetDialogOwner()
    {
        var application = Application.Current;
        if (application is null)
        {
            return null;
        }

        var dispatcher = application.Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || !dispatcher.CheckAccess())
        {
            return null;
        }

        return application.MainWindow;
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
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
        {
            CurrentLogLines.Add(line);
            CurrentLogText = string.Join(Environment.NewLine, CurrentLogLines.Select(item => item.Text));
            return;
        }

        dispatcher.Invoke(() =>
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
            RepositoryPath = snapshot.Record.RepositoryPath,
            SourceType = snapshot.Record.SourceType,
            ImportedFromExistingCheckout = snapshot.Record.ImportedFromExistingCheckout,
            OriginalDefaultBranch = snapshot.Record.OriginalDefaultBranch,
            SelectedWorkspaceBranch = snapshot.Record.SelectedWorkspaceBranch,
            RemoteOriginUrl = snapshot.Record.RemoteOriginUrl,
            CreatedUtc = snapshot.Record.CreatedUtc,
            LastOpenedUtc = lastOpenedUtc ?? snapshot.Record.LastOpenedUtc,
            LastPreparedUtc = lastPreparedUtc ?? snapshot.Record.LastPreparedUtc,
            OracleSoftwareNoticeShown = snapshot.Record.OracleSoftwareNoticeShown,
            LastOperationName = operationName,
            LastOperationResult = operationResult,
            LastOperationSucceeded = succeeded,
            LastOperationUtc = DateTimeOffset.UtcNow,
        };

        _workspaceOrchestrator.SaveRecord(updatedRecord);
    }

    private Task PersistWorkspaceRecordAsync(
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
            RepositoryPath = snapshot.Record.RepositoryPath,
            SourceType = snapshot.Record.SourceType,
            ImportedFromExistingCheckout = snapshot.Record.ImportedFromExistingCheckout,
            OriginalDefaultBranch = snapshot.Record.OriginalDefaultBranch,
            SelectedWorkspaceBranch = snapshot.Record.SelectedWorkspaceBranch,
            RemoteOriginUrl = snapshot.Record.RemoteOriginUrl,
            CreatedUtc = snapshot.Record.CreatedUtc,
            LastOpenedUtc = lastOpenedUtc ?? snapshot.Record.LastOpenedUtc,
            LastPreparedUtc = lastPreparedUtc ?? snapshot.Record.LastPreparedUtc,
            OracleSoftwareNoticeShown = snapshot.Record.OracleSoftwareNoticeShown,
            LastOperationName = operationName,
            LastOperationResult = operationResult,
            LastOperationSucceeded = succeeded,
            LastOperationUtc = DateTimeOffset.UtcNow,
        };

        return _workspaceOrchestrator.SaveRecordAsync(updatedRecord);
    }

    // UI thread safety rule:
    // - UI command flows must use async/await end to end.
    // - Do not use .Result, .Wait(), or GetAwaiter().GetResult() from dialog or command paths.
    // - Do not run Git, process, repository, or snapshot work directly on the UI thread.
    // - Report progress through status and log updates.
    // - Catch exceptions, show actionable error details, and leave the dialog usable after failure.

    private void RaiseCommandStates()
    {
        CreateWorkspaceCommand.RaiseCanExecuteChanged();
        PrimaryWorkspaceActionCommand.RaiseCanExecuteChanged();
        StartWorkspaceCommand.RaiseCanExecuteChanged();
        OpenWorkspaceCommand.RaiseCanExecuteChanged();
        RecoverWorkspaceCommand.RaiseCanExecuteChanged();
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
        ViewWorkspaceErrorCommand.RaiseCanExecuteChanged();
        InstallSelectedFontCommand.RaiseCanExecuteChanged();
        StartOracleDemoCommand.RaiseCanExecuteChanged();
        ResetOracleDemoCommand.RaiseCanExecuteChanged();
        ViewOracleLogsCommand.RaiseCanExecuteChanged();
        CopyOracleConnectionDetailsCommand.RaiseCanExecuteChanged();
        OpenOracleOrdsCommand.RaiseCanExecuteChanged();
        OpenOracleApexCommand.RaiseCanExecuteChanged();
        OpenOracleSqlclCommand.RaiseCanExecuteChanged();
        OpenOracleSqlWorksheetCommand.RaiseCanExecuteChanged();
        RunOracleTutorialQueryCommand.RaiseCanExecuteChanged();
        TestOracleConnectionCommand.RaiseCanExecuteChanged();
        OpenSqlDeveloperCommand.RaiseCanExecuteChanged();
        RunTmpReprovisionWorkflowCommand.RaiseCanExecuteChanged();
        RaisePropertyChanged(nameof(CanShutDownSelectedWorkspace));
        RaisePropertyChanged(nameof(SelectedPrimaryActionLabel));
        RaisePropertyChanged(nameof(ShowRecoverWorkspaceAction));
        RaisePropertyChanged(nameof(ShowViewWorkspaceErrorAction));
        RaisePropertyChanged(nameof(ShowStartWorkspaceAction));
        RaisePropertyChanged(nameof(HasRunningWorkspace));
        RaisePropertyChanged(nameof(CanStartCreateWorkspaceFlow));
        RaisePropertyChanged(nameof(CreateWorkspaceDisabledReason));
        RaisePropertyChanged(nameof(CanCreateWorkspaceForDialog));
    }

    private static bool IsOracleDemoWorkspace(WorkspaceDefinition definition)
        => OracleWorkspaceFamily.IsOracleWorkspace(definition);

    private IEnumerable<string> GetOracleTemplateIncludesSummary()
    {
        var kind = SelectedTemplate is null ? OracleWorkspaceKind.None : OracleWorkspaceFamily.Detect(SelectedTemplate);
        yield return "✓ Oracle Free Database";
        yield return "✓ SQLcl";

        if (kind is OracleWorkspaceKind.Apex or OracleWorkspaceKind.ApexLang)
        {
            yield return "✓ Oracle APEX";
            yield return "✓ ORDS";
            yield return "✓ Customers Sample Data";
        }
        else
        {
            yield return "✓ Tutorial";
            yield return "✓ Sample Schema";
        }

        if (kind == OracleWorkspaceKind.ApexLang)
        {
            yield return "✓ APEXlang Export/Import Scripts";
        }

        yield return "✓ AI Skills";
    }
}
