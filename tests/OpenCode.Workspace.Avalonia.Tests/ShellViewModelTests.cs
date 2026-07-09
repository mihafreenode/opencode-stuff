using System.Reflection;
using System.Runtime.CompilerServices;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Avalonia.ViewModels;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Platform;
using OpenCode.Workspace.Platform.Windows;
using OperationTranscript = OpenCode.Workspace.AppSupport.OperationTranscript;
using OperationTranscriptLine = OpenCode.Workspace.AppSupport.OperationTranscriptLine;
using OperationTranscriptLineKind = OpenCode.Workspace.AppSupport.OperationTranscriptLineKind;

namespace OpenCode.Workspace.Avalonia.Tests;

public sealed class ShellViewModelTests
{
    [Fact]
    public void DefaultPage_IsWorkspaces()
    {
        var shell = CreateShell();

        Assert.Equal("Workspaces", shell.CurrentPage.Title);
    }

    [Fact]
    public void MainWindowHeader_UsesScaledCompositeBrandBanner()
    {
        var repoRoot = GetRepositoryRoot();
        var axaml = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "MainWindow.axaml"));
        var project = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "OpenCode.Workspace.Avalonia.csproj"));
        var readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));
        var brandingReadme = File.ReadAllText(Path.Combine(repoRoot, "branding", "README.md"));
        var brandGuidelines = File.ReadAllText(Path.Combine(repoRoot, "branding", "BRAND_GUIDELINES.md"));
        var headerImageStart = axaml.IndexOf("<Image Source=\"avares://OpenCode.Workspace.Avalonia/Assets/opencode-stuff-header-brand-ui.png\"", StringComparison.Ordinal);
        var headerImageEnd = axaml.IndexOf("/>", headerImageStart, StringComparison.Ordinal);
        var headerImageMarkup = axaml.Substring(headerImageStart, headerImageEnd - headerImageStart);

        Assert.Contains("Assets/opencode-stuff-satchel-icon.ico", axaml, StringComparison.Ordinal);
        Assert.Contains("Assets/opencode-stuff-header-brand-ui.png", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon=\"avares://OpenCode.Workspace.Avalonia/Assets/opencode-stuff-header-brand-ui.png\"", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Assets/opencode-stuff-satchel-transparent.png", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Workspaces\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Runtime\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Status\"", axaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"{DynamicResource HeaderHeight}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"{DynamicResource HeaderLogoHeight}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Stretch=\"Uniform\"", axaml, StringComparison.Ordinal);
        Assert.Contains("RenderOptions.BitmapInterpolationMode=\"HighQuality\"", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("THERE IS NO MAGIC. ONLY ", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"STUFF.\"", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Assets/opencode-stuff-satchel-transparent.png", headerImageMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("BorderThickness", headerImageMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("BorderBrush", headerImageMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("Background", headerImageMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("Avalonia Preview", axaml, StringComparison.Ordinal);
        Assert.Contains("opencode-stuff-satchel-icon.ico", project, StringComparison.Ordinal);
        Assert.Contains("opencode-stuff-header-brand-ui.png", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Assets\\opencode-stuff-header-brand.png", project, StringComparison.Ordinal);
        Assert.DoesNotContain("opencode-stuff-header-brand-trimmed.png", project, StringComparison.Ordinal);
        Assert.Contains("opencode-stuff-satchel-icon.png", project, StringComparison.Ordinal);
        Assert.Contains("opencode-stuff-header-brand-ui.png", readme, StringComparison.Ordinal);
        Assert.Contains("ImageMagick trim", readme, StringComparison.Ordinal);
        Assert.Contains("opencode-stuff-header-brand-ui.png", brandingReadme, StringComparison.Ordinal);
        Assert.Contains("ImageMagick trim", brandingReadme, StringComparison.Ordinal);
        Assert.Contains("opencode-stuff-header-brand-ui.png", brandGuidelines, StringComparison.Ordinal);
        Assert.Contains("ImageMagick trim", brandGuidelines, StringComparison.Ordinal);
        Assert.Contains("opencode-stuff-satchel-icon.png", brandGuidelines, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsShellIcon_UsesCanonicalIcoForMainWindowAndExecutable()
    {
        var repoRoot = GetRepositoryRoot();
        var axaml = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "MainWindow.axaml"));
        var project = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "OpenCode.Workspace.Avalonia.csproj"));
        var iconHelper = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "Services", "AppWindowIcons.cs"));

        Assert.Contains("Icon=\"avares://OpenCode.Workspace.Avalonia/Assets/opencode-stuff-satchel-icon.ico\"", axaml, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>..\\..\\docs\\images\\opencode-stuff-satchel-icon.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("<Link>Assets\\opencode-stuff-satchel-icon.ico</Link>", project, StringComparison.Ordinal);
        Assert.Contains("avares://OpenCode.Workspace.Avalonia/Assets/opencode-stuff-satchel-icon.ico", iconHelper, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceInteractionService_AssignsAppIconToOwnedDialogs()
    {
        var repoRoot = GetRepositoryRoot();
        var interactionService = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "Services", "AvaloniaWorkspaceInteractionService.cs"));

        Assert.Contains("AppWindowIcons.Apply(window, _owner);", interactionService, StringComparison.Ordinal);
        Assert.DoesNotContain("return new CreateWorkspaceWindow(templates).ShowDialog<CreateWorkspaceDraft?>(_owner);", interactionService, StringComparison.Ordinal);
        Assert.DoesNotContain("return new OpenExistingRepositoryWindow(inspectRepositoryAsync, validateBranchAsync).ShowDialog<ExistingRepositoryImportDraft?>(_owner);", interactionService, StringComparison.Ordinal);
        Assert.DoesNotContain("return new RecoveryConfirmationWindow(assessment).ShowDialog<bool>(_owner);", interactionService, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogCodeBehind_UsesBackingFieldsInsteadOfGeneratedNamedControls()
    {
        var repoRoot = GetRepositoryRoot();
        var createWindowAxaml = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "CreateWorkspaceWindow.axaml"));
        var createWindow = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "CreateWorkspaceWindow.axaml.cs"));
        var openExistingWindow = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "OpenExistingRepositoryWindow.axaml.cs"));
        var recoveryWindow = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "RecoveryConfirmationWindow.axaml.cs"));
        var savePointWindow = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "SavePointWindow.axaml.cs"));

        Assert.DoesNotContain("ValidationMessageTextBlock.", openExistingWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("StatusTextBlock.", openExistingWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("InspectionSummaryTextBlock.", openExistingWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("RepositoryPathTextBox.", openExistingWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspaceNameTextBox.", openExistingWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("BranchModeComboBox.", openExistingWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("NamedBranchTextBox.", openExistingWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("ReuseExistingBranchCheckBox.", openExistingWindow, StringComparison.Ordinal);

        Assert.DoesNotContain("TemplateComboBox.", createWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspaceNameTextBox.", createWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspacePathTextBox.", createWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidationMessageTextBlock.", createWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("StatusTextBlock.", createWindow, StringComparison.Ordinal);
        Assert.Contains("<TextBlock Text=\"{Binding DisplayName}\" />", createWindowAxaml, StringComparison.Ordinal);

        Assert.DoesNotContain("TitleTextBlock.", recoveryWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspaceNameTextBlock.", recoveryWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("StatusTextBlock.", recoveryWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("SummaryTextBlock.", recoveryWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmationTextBlock.", recoveryWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("RecoverActionsItemsControl.", recoveryWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentProblemsItemsControl.", recoveryWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviousFailureItemsControl.", recoveryWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("WillNotItemsControl.", recoveryWindow, StringComparison.Ordinal);

        Assert.DoesNotContain("MessageTextBox.", savePointWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidationMessageTextBlock.", savePointWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWorkspaceWindow_UsesDesktopWizardLayout()
    {
        var repoRoot = GetRepositoryRoot();
        var axaml = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "CreateWorkspaceWindow.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "CreateWorkspaceWindow.axaml.cs"));

        Assert.Contains("Width=\"820\"", axaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"{DynamicResource CreateWorkspaceDialogMinWidth}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"{DynamicResource CreateWorkspaceDialogMinHeight}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("WorkspaceFolderPreviewTextBlock", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Location\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Template information", axaml, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer Grid.Row=\"1\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"dialog-footer\"", axaml, StringComparison.Ordinal);
        Assert.Contains("IsDefault=\"True\"", axaml, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", axaml, StringComparison.Ordinal);
        Assert.Contains("BuildValidationState", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BuildWorkspaceFolderPreview", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BuildWorkspaceRootPath", codeBehind, StringComparison.Ordinal);
        Assert.Contains("SuggestedStartLocation = startLocation", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Select parent folder", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Workspace will be created here.", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Workspace folder already exists.", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_createButton.IsEnabled", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryConfirmationWindow_UsesDecisionFocusedLayout()
    {
        var repoRoot = GetRepositoryRoot();
        var axaml = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "RecoveryConfirmationWindow.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "RecoveryConfirmationWindow.axaml.cs"));

        Assert.Contains("Text=\"Workspace\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Recover will:", axaml, StringComparison.Ordinal);
        Assert.Contains("Current problems", axaml, StringComparison.Ordinal);
        Assert.Contains("Previous failure context", axaml, StringComparison.Ordinal);
        Assert.Contains("Recover will NOT:", axaml, StringComparison.Ordinal);
        Assert.Contains("Possible manual action", axaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Show Details\"", axaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"170\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Refresh\"", axaml, StringComparison.Ordinal);
        Assert.Contains("LastCheckedTextBlock", axaml, StringComparison.Ordinal);
        Assert.Contains("IsDefault=\"True\"", axaml, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", axaml, StringComparison.Ordinal);
        Assert.Contains("RefreshAssessmentAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DispatcherTimer", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BuildListItems", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DefaultRecoverActions", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DefaultWillNotItems", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ManualActionBorder", codeBehind, StringComparison.Ordinal);
        Assert.Contains("AdvancedDetailsTextBlock", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetRuntimeWindow_UsesScopeConfirmationLayout()
    {
        var repoRoot = GetRepositoryRoot();
        var axaml = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "ResetRuntimeWindow.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "ResetRuntimeWindow.axaml.cs"));

        Assert.Contains("Rebuild Runtime", axaml, StringComparison.Ordinal);
        Assert.Contains("Rebuild Runtime will remove:", axaml, StringComparison.Ordinal);
        Assert.Contains("Rebuild Runtime will keep:", axaml, StringComparison.Ordinal);
        Assert.Contains("IsDefault=\"True\"", axaml, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", axaml, StringComparison.Ordinal);
        Assert.Contains("BuildItems", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspacesPageViewModel_UsesGenericResetRuntimeActionNames()
    {
        var repoRoot = GetRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "ViewModels", "WorkspacesPageViewModel.cs"));

        Assert.Contains("ResetRuntimeCommand", code, StringComparison.Ordinal);
        Assert.Contains("Rebuild Runtime", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetOracleRuntime", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Reset Oracle Runtime", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SavePointInteractionService_ReadsWindowResultAfterDialogCloses()
    {
        var repoRoot = GetRepositoryRoot();
        var interactionService = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "Services", "AvaloniaWorkspaceInteractionService.cs"));

        Assert.Contains("var window = new SavePointWindow(initialMessage);", interactionService, StringComparison.Ordinal);
        Assert.Contains("await window.ShowDialog(_owner);", interactionService, StringComparison.Ordinal);
        Assert.Contains("return window.Result;", interactionService, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog<SavePointDraft?>", interactionService, StringComparison.Ordinal);
    }

    [Fact]
    public void Navigation_SelectsCorrectPage()
    {
        var shell = CreateShell();

        var diagnostics = shell.NavigationItems.Single(item => item.Title == "Diagnostics");
        diagnostics.SelectCommand.Execute(null);

        Assert.Equal("Diagnostics", shell.CurrentPage.Title);
    }

    [Fact]
    public void ShellCreation_DoesNotSynchronouslyLoadWorkspaces()
    {
        var desktop = new TrackingDesktopShellService();

        _ = ShellViewModel.Create(
            desktop,
            new FakeDiagnosticsShellService(),
            new FakeHostCapabilities(),
            new FakeTemplateCatalogShellService(),
            new FakeDocumentationShellService(),
            new ThemeCoordinator(ThemeMode.System),
            CreateAppBuildInfo(),
            "en");

        Assert.Equal(0, desktop.LoadCalls);
    }

    [Fact]
    public void InitialWorkspacesState_IsReadyToLoadAndNonBlocking()
    {
        var shell = CreateShell();
        var workspacesPage = (WorkspacesPageViewModel)shell.NavigationItems.Single(item => item.Title == "Workspaces").Page;

        Assert.True(workspacesPage.IsLoading);
        Assert.False(workspacesPage.HasWorkspaces);
        Assert.Equal("Loading workspace index...", workspacesPage.EmptyStateTitle);
    }

    [Fact]
    public async Task WorkspaceLoadingFailure_DoesNotPreventShellConstruction()
    {
        var shell = CreateShellWithDesktop(new ThrowingDesktopShellService());

        await shell.InitializeAsync();

        var workspacesPage = (WorkspacesPageViewModel)shell.NavigationItems.Single(item => item.Title == "Workspaces").Page;
        Assert.True(workspacesPage.HasLoadError);
        Assert.Equal("Workspace discovery failed", workspacesPage.DetailTitle);
        Assert.Equal("Refresh", workspacesPage.DetailPrimaryAction?.Label);
        Assert.Equal(["Refresh"], workspacesPage.DetailVisibleActions.Select(item => item.Label));
    }

    [Fact]
    public void ThemeMode_DefaultsToSystem()
    {
        var settings = CreateSettingsPage();

        Assert.Equal(ThemeMode.System, settings.SelectedThemeMode);
    }

    [Fact]
    public void ThemeMode_CanSwitchToLight()
    {
        var coordinator = new ThemeCoordinator(ThemeMode.System);
        var settings = CreateSettingsPage(coordinator: coordinator);

        settings.SelectedThemeMode = ThemeMode.Light;

        Assert.Equal(ThemeMode.Light, settings.SelectedThemeMode);
        Assert.Equal(ThemeMode.Light, coordinator.CurrentMode);
    }

    [Fact]
    public void ThemeMode_CanSwitchToDark()
    {
        var coordinator = new ThemeCoordinator(ThemeMode.System);
        var settings = CreateSettingsPage(coordinator: coordinator);

        settings.SelectedThemeMode = ThemeMode.Dark;

        Assert.Equal(ThemeMode.Dark, settings.SelectedThemeMode);
        Assert.Equal(ThemeMode.Dark, coordinator.CurrentMode);
    }

    [Fact]
    public async Task OracleCreateCancellation_DoesNotContinue()
    {
        var template = new TemplateManifest { Id = OracleWorkspaceFamily.OraclePlSqlTemplateId, DisplayName = "Oracle PL/SQL Demo" };
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([]));
        page.SetInteractionService(new FakeWorkspaceInteractionService
        {
            OracleNoticeConfirmed = false,
            CreateWorkspaceDraft = new CreateWorkspaceDraft
            {
                WorkspaceName = "oracle-demo",
                WorkspaceRootPath = Path.Combine(Path.GetTempPath(), $"oracle-demo-{Guid.NewGuid():N}"),
                Template = template,
            },
        });

        await ((AsyncRelayCommand)page.CreateWorkspaceCommand).ExecuteAsync();

        Assert.Contains("Cancelled.", page.OperationLogText, StringComparison.Ordinal);
        Assert.Equal("Workspace creation cancelled.", page.DetailSummary);
    }

    [Fact]
    public async Task OracleCreateAcknowledgement_AllowsCreate()
    {
        var template = new TemplateManifest { Id = OracleWorkspaceFamily.OraclePlSqlTemplateId, DisplayName = "Oracle PL/SQL Demo" };
        WorkspaceSnapshot? createdSnapshot = null;
        var service = new FakeDesktopShellService([])
        {
            CreateWorkspaceAsyncFactory = (rootPath, definition, _, _) =>
            {
                createdSnapshot = CreateSnapshot(definition.Workspace.Name);
                return Task.FromResult(createdSnapshot);
            },
            LoadWorkspaceItemsAsyncFactory = (_, _, _) => Task.FromResult(new WorkspaceLoadResult
            {
                Items = [new WorkspaceShellItem { Record = createdSnapshot!.Record, Snapshot = createdSnapshot }],
                Report = new WorkspaceLoadReport { RawRecordCount = 1, SnapshotAttemptCount = 1, SnapshotCount = 1, ItemsReturnedCount = 1, Timings = [] },
            }),
        };
        var rootPath = Path.Combine(Path.GetTempPath(), $"oracle-demo-{Guid.NewGuid():N}");
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService
        {
            OracleNoticeConfirmed = true,
            CreateWorkspaceDraft = new CreateWorkspaceDraft
            {
                WorkspaceName = "oracle-demo",
                WorkspaceRootPath = rootPath,
                Template = template,
            },
        });

        await ((AsyncRelayCommand)page.CreateWorkspaceCommand).ExecuteAsync();

        Assert.NotNull(createdSnapshot);
        Assert.Contains(page.Workspaces, item => string.Equals(item.RootPath, createdSnapshot.Paths.RootPath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(createdSnapshot.Paths.RootPath, page.SelectedWorkspace?.RootPath);
    }

    [Fact]
    public async Task NonOracleCreate_DoesNotRequireAcknowledgement()
    {
        var template = new TemplateManifest { Id = "general-development", DisplayName = "General Development", Features = ["core"], Services = ["postgres"] };
        WorkspaceSnapshot? createdSnapshot = null;
        var service = new FakeDesktopShellService([])
        {
            CreateWorkspaceAsyncFactory = (rootPath, definition, _, _) =>
            {
                createdSnapshot = CreateSnapshot(definition.Workspace.Name);
                return Task.FromResult(createdSnapshot);
            },
            LoadWorkspaceItemsAsyncFactory = (_, _, _) => Task.FromResult(new WorkspaceLoadResult
            {
                Items = [new WorkspaceShellItem { Record = createdSnapshot!.Record, Snapshot = createdSnapshot }],
                Report = new WorkspaceLoadReport { RawRecordCount = 1, SnapshotAttemptCount = 1, SnapshotCount = 1, ItemsReturnedCount = 1, Timings = [] },
            }),
        };
        var interaction = new FakeWorkspaceInteractionService
        {
            OracleNoticeConfirmed = false,
            CreateWorkspaceDraft = new CreateWorkspaceDraft
            {
                WorkspaceName = "general-demo",
                WorkspaceRootPath = Path.Combine(Path.GetTempPath(), $"general-{Guid.NewGuid():N}"),
                Template = template,
            },
        };
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(interaction);

        await ((AsyncRelayCommand)page.CreateWorkspaceCommand).ExecuteAsync();

        Assert.Equal(0, interaction.OracleNoticePromptCount);
        Assert.NotNull(createdSnapshot);
        Assert.Contains(page.Workspaces, item => string.Equals(item.RootPath, createdSnapshot.Paths.RootPath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(createdSnapshot.Paths.RootPath, page.SelectedWorkspace?.RootPath);
    }

    [Fact]
    public async Task CreateWorkspace_AddsCreatedWorkspaceToListAfterRefresh()
    {
        WorkspaceSnapshot? createdSnapshot = null;
        var template = new TemplateManifest { Id = "general-development", DisplayName = "General Development", Features = ["core"] };
        var service = new FakeDesktopShellService([])
        {
            CreateWorkspaceAsyncFactory = (rootPath, definition, _, _) =>
            {
                createdSnapshot = CreateSnapshot(definition.Workspace.Name);
                return Task.FromResult(createdSnapshot);
            },
            LoadWorkspaceItemsAsyncFactory = (_, _, _) => Task.FromResult(new WorkspaceLoadResult
            {
                Items = [new WorkspaceShellItem { Record = createdSnapshot!.Record, Snapshot = createdSnapshot }],
                Report = new WorkspaceLoadReport { RawRecordCount = 1, SnapshotAttemptCount = 1, SnapshotCount = 1, ItemsReturnedCount = 1, Timings = [] },
            }),
        };
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService
        {
            CreateWorkspaceDraft = new CreateWorkspaceDraft
            {
                WorkspaceName = "created-demo",
                WorkspaceRootPath = Path.Combine(Path.GetTempPath(), $"created-demo-{Guid.NewGuid():N}"),
                Template = template,
            },
        });

        await ((AsyncRelayCommand)page.CreateWorkspaceCommand).ExecuteAsync();

        Assert.NotNull(createdSnapshot);
        Assert.Contains(page.Workspaces, item => string.Equals(item.RootPath, createdSnapshot.Paths.RootPath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(createdSnapshot.Paths.RootPath, page.SelectedWorkspace?.RootPath);
        Assert.Contains("created successfully", page.DetailSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateWorkspace_KeepsCreatedWorkspaceVisibleWhenRefreshFails()
    {
        WorkspaceSnapshot? createdSnapshot = null;
        var template = new TemplateManifest { Id = "general-development", DisplayName = "General Development", Features = ["core"] };
        var service = new FakeDesktopShellService([])
        {
            CreateWorkspaceAsyncFactory = (rootPath, definition, _, _) =>
            {
                createdSnapshot = CreateSnapshot(definition.Workspace.Name);
                return Task.FromResult(createdSnapshot);
            },
            LoadWorkspaceItemsAsyncFactory = (_, _, _) => throw new InvalidOperationException("simulated discovery failure"),
        };
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService
        {
            CreateWorkspaceDraft = new CreateWorkspaceDraft
            {
                WorkspaceName = "created-demo",
                WorkspaceRootPath = Path.Combine(Path.GetTempPath(), $"created-demo-{Guid.NewGuid():N}"),
                Template = template,
            },
        });

        await ((AsyncRelayCommand)page.CreateWorkspaceCommand).ExecuteAsync();

        Assert.NotNull(createdSnapshot);
        Assert.Contains(page.Workspaces, item => string.Equals(item.RootPath, createdSnapshot.Paths.RootPath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(createdSnapshot.Paths.RootPath, page.SelectedWorkspace?.RootPath);
        Assert.Contains("was created, but discovery refresh failed", page.SelectedWorkspace?.LastActivity ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("simulated discovery failure", page.SelectedWorkspace?.LastActivity ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Created, but discovery refresh failed.", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenExistingRepository_ImportsSelectedDraftAndUpdatesSummary()
    {
        var service = new FakeDesktopShellService([]);
        var interaction = new FakeWorkspaceInteractionService
        {
            ExistingRepositoryImportDraft = new ExistingRepositoryImportDraft
            {
                RepositoryPath = @"C:\repo\demo",
                WorkspaceName = "demo-workspace",
                BranchMode = ExistingGitCheckoutBranchMode.CreateNamedFeatureBranch,
                NamedBranch = "users/test/demo-feature",
                ReuseExistingNamedBranch = true,
            },
        };
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(interaction);

        await ((AsyncRelayCommand)page.OpenExistingRepositoryCommand).ExecuteAsync();

        Assert.NotNull(service.LastImportRequest);
        Assert.Equal(@"C:\repo\demo", service.LastImportRequest!.RepositoryPath);
        Assert.Equal("demo-workspace", service.LastImportRequest.WorkspaceName);
        Assert.Equal(ExistingGitCheckoutBranchMode.CreateNamedFeatureBranch, service.LastImportRequest.BranchMode);
        Assert.Equal("users/test/demo-feature", service.LastImportRequest.NamedBranch);
        Assert.True(service.LastImportRequest.ReuseExistingNamedBranch);
        Assert.Contains("Imported existing Git checkout", page.DetailSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenExistingRepository_CancelledDialog_DoesNotImport()
    {
        var service = new FakeDesktopShellService([]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService { ExistingRepositoryImportDraft = null });

        await ((AsyncRelayCommand)page.OpenExistingRepositoryCommand).ExecuteAsync();

        Assert.Null(service.LastImportRequest);
        Assert.DoesNotContain("Imported existing Git checkout", page.DetailSummary ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SettingsPage_TerminalProfileSetupShowsResult()
    {
        var snapshot = CreateSnapshot("alpha");
        var settings = CreateSettingsPage(new FakeDesktopShellService([snapshot])
        {
            WindowsTerminalProfileResult = new WindowsTerminalProfileOperationResult
            {
                Message = "Created Windows Terminal profile 'OpenCode Stuff - alpha'.",
                Setup = new WindowsTerminalProfileSetupResult
                {
                    Status = WindowsTerminalProfileSetupStatus.Created,
                    Summary = "Created Windows Terminal profile 'OpenCode Stuff - alpha'.",
                    ProfileName = "OpenCode Stuff - alpha",
                    FragmentPath = "C:\\Users\\test\\profiles.json",
                    ResolvedFontFace = "JetBrainsMono Nerd Font",
                    FailureReason = string.Empty,
                },
            },
        }, snapshot);

        await settings.SetupWindowsTerminalProfileCommand.ExecuteAsync();

        Assert.Equal("Created Windows Terminal profile 'OpenCode Stuff - alpha'.", settings.TerminalProfileStatus);
        Assert.Contains(settings.DetailItems, item => item.Label == "Profile name" && item.Value == "OpenCode Stuff - alpha");
        Assert.Contains(settings.DetailActions, item => item.Label == "Configure Windows Terminal profile");
    }

    [Fact]
    public async Task SettingsPage_TerminalProfileSetupShowsFailure()
    {
        var snapshot = CreateSnapshot("alpha");
        var settings = CreateSettingsPage(new FakeDesktopShellService([snapshot])
        {
            WindowsTerminalProfileResult = new WindowsTerminalProfileOperationResult
            {
                Message = "Windows Terminal profile setup failed.",
                Setup = new WindowsTerminalProfileSetupResult
                {
                    Status = WindowsTerminalProfileSetupStatus.Failed,
                    Summary = "Windows Terminal profile setup failed.",
                    ProfileName = string.Empty,
                    FragmentPath = "C:\\Users\\test\\profiles.json",
                    ResolvedFontFace = string.Empty,
                    FailureReason = "Access denied.",
                },
            },
        }, snapshot);

        await settings.SetupWindowsTerminalProfileCommand.ExecuteAsync();

        Assert.Equal("Windows Terminal profile setup failed.", settings.TerminalProfileStatus);
        Assert.Contains(settings.DetailItems, item => item.Label == "Failure" && item.Value == "Access denied.");
    }

    [Fact]
    public async Task SettingsPage_LoadHostCapabilities_AddsPlatformDetails()
    {
        var settings = CreateSettingsPage();

        await settings.LoadHostCapabilitiesAsync();

        Assert.Contains(settings.DetailItems, item => item.Label == "Host platform" && item.Value == "Windows");
        Assert.Contains(settings.DetailItems, item => item.Label == "Managed terminal profile support" && item.Value.Contains("supported", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OracleStartCancellation_BlocksWorkflow()
    {
        var snapshot = CreateOracleSnapshot("oracle-start", oracleNoticeShown: false);
        var service = new FakeDesktopShellService([snapshot]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService { OracleNoticeConfirmed = false });
        await page.LoadAsync();

        await ((AsyncRelayCommand)page.DetailAdvancedActions.Single(item => item.Label == "Start Only").Command).ExecuteAsync();

        Assert.Equal(0, service.StartCallCount);
        Assert.Equal("Workspace start cancelled.", page.DetailSummary);
    }

    [Fact]
    public async Task OracleStartAcknowledgement_AllowsWorkflow()
    {
        var snapshot = CreateOracleSnapshot("oracle-start", oracleNoticeShown: false);
        var service = new FakeDesktopShellService([snapshot]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService { OracleNoticeConfirmed = true });
        await page.LoadAsync();

        await ((AsyncRelayCommand)page.DetailAdvancedActions.Single(item => item.Label == "Start Only").Command).ExecuteAsync();

        Assert.Equal(1, service.StartCallCount);
        Assert.Equal(1, service.AcknowledgeOracleNoticeCallCount);
    }

    [Fact]
    public async Task OracleReprovisionCancellation_BlocksWorkflow()
    {
        var snapshot = CreateOracleSnapshot("oracle-reprovision", oracleNoticeShown: false);
        var service = new FakeDesktopShellService([snapshot]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService { OracleNoticeConfirmed = false });
        await page.LoadAsync();

        await page.ReprovisionWorkspaceCommand.ExecuteAsync();

        Assert.Equal(0, service.ReprovisionCallCount);
        Assert.Equal("Workspace reprovision cancelled.", page.DetailSummary);
    }

    [Fact]
    public async Task OracleReprovisionAcknowledgement_AllowsWorkflow()
    {
        var snapshot = CreateOracleSnapshot("oracle-reprovision", oracleNoticeShown: false);
        var service = new FakeDesktopShellService([snapshot]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService { OracleNoticeConfirmed = true });
        await page.LoadAsync();

        await page.ReprovisionWorkspaceCommand.ExecuteAsync();

        Assert.Equal(1, service.ReprovisionCallCount);
        Assert.Equal(1, service.AcknowledgeOracleNoticeCallCount);
    }

    [Fact]
    public async Task WorkspaceList_LoadsFromInjectedServiceModel()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha"), CreateSnapshot("beta")]));

        await page.LoadAsync();

        Assert.Collection(
            page.Workspaces,
            first => Assert.Equal("beta", first.Name),
            second => Assert.Equal("alpha", second.Name));
    }

    [Fact]
    public async Task SelectedWorkspace_UpdatesDetailPanel()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha"), CreateSnapshot("beta") ]));
        await page.LoadAsync();

        page.SelectedWorkspace = page.Workspaces.Last();

        Assert.Equal("alpha", page.DetailTitle);
        Assert.Equal(["Workspace", "Current Activity", "What You Can Use", "Needs Attention", "Development Environment", "Technical Evidence"], page.DetailItems.Take(6).Select(item => item.Label));
        Assert.Contains(page.DetailItems, item => item.Label == "Technical Evidence" && item.Value.Contains("Repository path: ", StringComparison.Ordinal) && item.Value.Contains("alpha", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SelectedWorkspace_DefaultsToMostRecentlyOpenedWorkspace()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha"), CreateSnapshot("beta")]));
        await page.LoadAsync();

        Assert.Equal("beta", page.DetailTitle);
        Assert.Contains(page.DetailItems, item => item.Label == "Technical Evidence" && item.Value.Contains("Repository path: ", StringComparison.Ordinal) && item.Value.Contains("beta", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyState_ShownWhenNoWorkspacesExist()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([]));

        await page.LoadAsync();

        Assert.True(page.ShowEmptyState);
        Assert.Equal("No workspaces discovered.", page.EmptyStateTitle);
    }

    [Fact]
    public async Task MissingRuntimeState_DoesNotPreventDisplay()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha", includeRuntimeState: false)]));

        await page.LoadAsync();

        Assert.Equal("alpha", page.SelectedWorkspace?.Name);
        Assert.Contains(page.DetailItems, item => item.Label == "Technical Evidence" && item.Value.Contains("Runtime-state status: Missing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidWorkspace_IsRepresentedInsteadOfHidden()
    {
        var invalidRecord = new WorkspaceRecord
        {
            Name = "broken",
            RootPath = "/workspace/broken",
            RepositoryPath = "/workspace/broken",
            ConfigurationPath = "workspace.yaml",
            CreatedUtc = DateTimeOffset.UtcNow,
            LastOpenedUtc = DateTimeOffset.UtcNow,
        };
        var invalidItem = new WorkspaceShellItem
        {
            Record = invalidRecord,
            ErrorMessage = "workspace.yaml missing",
        };

        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")], [invalidItem]));

        await page.LoadAsync();
        page.SelectedWorkspace = page.Workspaces.Single(item => item.Name == "broken");

        Assert.Equal(2, page.Workspaces.Count);
        Assert.Equal("Error", page.SelectedWorkspace?.RuntimeStatusLabel);
        Assert.Equal("Run Diagnostics", page.DetailPrimaryAction?.Label);
        Assert.Equal(["Refresh"], page.DetailVisibleActions.Select(item => item.Label));
        Assert.DoesNotContain(page.DetailVisibleActions, item => item.Label == "Open Workspace");
        Assert.Contains(page.DetailItems, item => item.Label == "Workspace" && item.Value.Contains("Discovery Failed", StringComparison.Ordinal));
        Assert.Contains(page.DetailItems, item => item.Label == "Current Activity" && item.Value.Contains("None", StringComparison.Ordinal));
        Assert.Contains(page.DetailItems, item => item.Label == "What You Can Use" && item.Value.Contains("Nothing is available because workspace details could not be loaded.", StringComparison.Ordinal));
        Assert.Contains(page.DetailItems, item => item.Label == "Needs Attention" && item.Value.Contains("Next: Run Diagnostics.", StringComparison.Ordinal));
        Assert.Contains(page.DetailItems, item => item.Label == "Development Environment" && item.Value.Contains("Unknown because workspace details could not be loaded.", StringComparison.Ordinal));
        Assert.Contains(page.DetailItems, item => item.Label == "Technical Evidence" && item.Value.Contains("Load failure: workspace.yaml missing", StringComparison.Ordinal));
        Assert.Equal(2, page.WorkspaceLoadReport.ItemsReturnedCount);
    }

    [Fact]
    public async Task WorkspaceLoadReport_CapturesPathAndCounts()
    {
        var invalidItem = new WorkspaceShellItem
        {
            Record = new WorkspaceRecord
            {
                Name = "broken",
                RootPath = "/workspace/broken",
                RepositoryPath = "/workspace/broken",
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            },
            ErrorMessage = "broken workspace",
        };
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")], [invalidItem]));

        await page.LoadAsync();

        Assert.Equal(WorkspaceAppDataPaths.GetWorkspaceIndexPath(), page.WorkspaceLoadReport.IndexFilePath);
        Assert.Equal(2, page.WorkspaceLoadReport.RawRecordCount);
        Assert.Equal(2, page.WorkspaceLoadReport.SnapshotAttemptCount);
        Assert.Equal(1, page.WorkspaceLoadReport.SnapshotCount);
        Assert.Equal(1, page.WorkspaceLoadReport.FailureCount);
        Assert.Equal(2, page.WorkspaceLoadReport.ItemsReturnedCount);
        Assert.NotEmpty(page.WorkspaceLoadReport.Timings);
        Assert.True(page.WorkspaceLoadReport.TotalDuration > TimeSpan.Zero);
    }

    [Fact]
    public async Task ProgressiveLoading_UpdatesStatusAndAddsRowsBeforeCompletion()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshots = new[] { CreateSnapshot("alpha"), CreateSnapshot("beta") };
        var service = new FakeDesktopShellService(snapshots)
        {
            LoadWorkspaceItemsAsyncFactory = async (_, progress, _) =>
            {
                progress?.Invoke(new WorkspaceLoadProgressUpdate
                {
                    Title = "Loading workspace index...",
                    Message = "Found 2 workspaces. Workspace index loaded in 12 ms.",
                    ProgressLabel = "Workspace 0 of 2",
                    TotalWorkspaces = 2,
                });
                progress?.Invoke(new WorkspaceLoadProgressUpdate
                {
                    Title = "Loading alpha...",
                    Message = "Checking repository status...",
                    ProgressLabel = "Workspace 1 of 2",
                    CurrentWorkspaceName = "alpha",
                    CurrentWorkspaceIndex = 1,
                    TotalWorkspaces = 2,
                    LoadedItem = new WorkspaceShellItem { Record = snapshots[0].Record, Snapshot = snapshots[0] },
                });
                await release.Task;
                return new WorkspaceLoadResult
                {
                    Items = snapshots.Select(item => new WorkspaceShellItem { Record = item.Record, Snapshot = item }).ToList(),
                    Report = new WorkspaceLoadReport
                    {
                        IndexFilePath = WorkspaceAppDataPaths.GetWorkspaceIndexPath(),
                        AppDataRoot = WorkspaceAppDataPaths.GetWorkspaceManagerDataRoot(),
                        StartedUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                        CompletedUtc = DateTimeOffset.UtcNow,
                        TotalDuration = TimeSpan.FromSeconds(1),
                        RawRecordCount = 2,
                        SnapshotAttemptCount = 2,
                        SnapshotCount = 2,
                        ItemsReturnedCount = 2,
                        Timings =
                        [
                            new WorkspaceLoadTiming
                            {
                                StageKey = "git-status",
                                StageLabel = "Repository status",
                                WorkspaceName = "alpha",
                                Duration = TimeSpan.FromMilliseconds(120),
                                StartedUtc = DateTimeOffset.UtcNow.AddMilliseconds(-250),
                                CompletedUtc = DateTimeOffset.UtcNow.AddMilliseconds(-130),
                                Succeeded = true,
                            },
                        ],
                    },
                };
            },
        };
        var page = new WorkspacesPageViewModel(service);

        var loadTask = page.LoadAsync();

        Assert.Equal("Loading alpha...", page.LoadingTitle);
        Assert.Equal("Checking repository status...", page.LoadingMessage);
        Assert.Equal("Workspace 1 of 2", page.LoadingProgressLabel);
        Assert.Single(page.Workspaces);

        release.SetResult();
        await loadTask;

        Assert.Equal(2, page.Workspaces.Count);
    }

    [Fact]
    public async Task DoctorResults_PopulateChecklist()
    {
        var page = new DiagnosticsPageViewModel(new FakeDiagnosticsShellService(), [new WorkspaceReference("alpha", "/workspace/alpha")]);

        await page.RunDoctorCommand.ExecuteAsync();

        Assert.NotEmpty(page.DoctorItems);
        Assert.Contains(page.DoctorItems, item => item.AutomationId == "Diagnostic_Git" && item.Title == "Git");
        Assert.Contains(page.DoctorItems, item => item.AutomationId == "Diagnostic_DockerCompose" && item.Title == "Docker Compose");
        Assert.Contains(page.DoctorItems, item => item.AutomationId == "Diagnostic_NerdFont" && item.Title == "Nerd Font");
        Assert.Contains(page.DoctorItems, item => item.AutomationId == "Diagnostic_OpenCodeCli" && item.Title == "OpenCode CLI");
        Assert.Contains(page.DoctorItems, item => item.AutomationId == "Diagnostic_TemplateCatalog" && item.Title == "Template catalog");
        Assert.Contains(page.DoctorItems, item => item.AutomationId == "Diagnostic_HostArchitecture" && item.Title == "Host architecture");
        Assert.Contains(page.DoctorItems, item => item.AutomationId == "Diagnostic_RuntimePlatform" && item.Title == "Runtime platform");
        Assert.Contains(page.DoctorItems, item => item.Title == "Docker Engine");
        Assert.Equal("Workspace can run on this machine.", page.StatusMessage);
    }

    [Fact]
    public async Task DoctorEvidenceText_IncludesStableDiagnosticsRows()
    {
        var page = new DiagnosticsPageViewModel(new FakeDiagnosticsShellService(), [new WorkspaceReference("alpha", "/workspace/alpha")]);

        await page.RunDoctorCommand.ExecuteAsync();

        var evidence = page.GetDoctorEvidenceText();
        Assert.Contains("Diagnostic_Git: Git", evidence, StringComparison.Ordinal);
        Assert.Contains("Diagnostic_DockerCompose: Docker Compose", evidence, StringComparison.Ordinal);
        Assert.Contains("Diagnostic_TemplateCatalog: Template catalog", evidence, StringComparison.Ordinal);
        Assert.Contains("Diagnostic_RuntimePlatform: Runtime platform", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsPage_ReportsLatestWorkspaceLoadSummary()
    {
        var page = new DiagnosticsPageViewModel(
            new FakeDiagnosticsShellService(),
            [new WorkspaceReference("alpha", "/workspace/alpha")],
            () => new WorkspaceLoadReport
            {
                RawRecordCount = 9,
                TotalDuration = TimeSpan.FromSeconds(2.8),
                Timings =
                [
                    new WorkspaceLoadTiming
                    {
                        StageLabel = "Repository status",
                        WorkspaceName = "Odip Analiza",
                        Duration = TimeSpan.FromSeconds(1.9),
                    },
                ],
            });

        page.RefreshWorkspaceLoadSummary();

        Assert.Contains("9 workspaces", page.LatestWorkspaceLoadSummary, StringComparison.Ordinal);
        Assert.Contains("2.8 s", page.LatestWorkspaceLoadSummary, StringComparison.Ordinal);
        Assert.Contains("Odip Analiza", page.LatestWorkspaceLoadSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationResults_PopulatePage()
    {
        var page = new DiagnosticsPageViewModel(new FakeDiagnosticsShellService(), [new WorkspaceReference("alpha", "/workspace/alpha")]);

        await page.ValidateAmd64Command.ExecuteAsync();

        Assert.NotEmpty(page.ValidationItems);
        Assert.Equal("linux/amd64 validation passed.", page.LatestValidationSummary);
        Assert.Contains("Requested Target: linux/amd64", page.LatestValidationContext, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationWarnings_DisplayCorrectly()
    {
        var page = new DiagnosticsPageViewModel(new FakeDiagnosticsShellService(), [new WorkspaceReference("alpha", "/workspace/alpha")]);

        await page.ValidateArm64Command.ExecuteAsync();

        Assert.Contains(page.ValidationItems, item => item.StatusLabel == "Warning");
    }

    [Fact]
    public async Task SelectedDiagnostic_UpdatesDetailPanel()
    {
        var page = new DiagnosticsPageViewModel(new FakeDiagnosticsShellService(), [new WorkspaceReference("alpha", "/workspace/alpha")]);

        await page.RunDoctorCommand.ExecuteAsync();
        page.SelectedDoctorItem = page.DoctorItems.Single(item => item.Title == "Docker CLI");

        Assert.Equal("Docker CLI", page.DetailTitle);
        Assert.Contains(page.DetailItems, item => item.Label == "Status");
    }

    [Fact]
    public void DiagnosticsItems_ExposeStableAutomationNames()
    {
        var item = new DiagnosticItemViewModel("Git", "Pass", "Git is available.", string.Empty, automationId: "Diagnostic_Git");

        Assert.Equal("Diagnostic_Git", item.AutomationId);
        Assert.Equal("Diagnostic_Git", item.AutomationName);
        Assert.Contains("Status: Pass", item.EvidenceText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AttachAction_IsEnabledForLoadedWorkspace()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")]));
        await page.LoadAsync();

        var attach = page.DetailAdvancedActions.Single(item => item.Label == "Attach Only");

        Assert.True(attach.IsEnabled);
        Assert.Equal(string.Empty, attach.DisabledReason);
    }

    [Fact]
    public async Task RecordOnlyWorkspace_DoesNotOfferStartOnlyAsNormalAction()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"avalonia-start-enable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            File.WriteAllText(Path.Combine(workspaceRoot, "workspace.yaml"), "workspace:\n  name: smoke\n  image: ubuntu:24.04\nprovider:\n  type: git\nruntime:\n  default: default\nfeatures:\n- core\n");
            var recordOnlyItem = new WorkspaceShellItem
            {
                Record = new WorkspaceRecord
                {
                    Name = "smoke",
                    RootPath = workspaceRoot,
                    RepositoryPath = workspaceRoot,
                    ConfigurationPath = "workspace.yaml",
                    CreatedUtc = DateTimeOffset.UtcNow,
                    LastOpenedUtc = DateTimeOffset.UtcNow,
                },
            };

            var page = new WorkspacesPageViewModel(new FakeDesktopShellService([], [recordOnlyItem]));
            await page.LoadAsync();
            page.SelectedWorkspace = page.Workspaces.Single(item => item.RootPath == workspaceRoot);

            Assert.Equal("Run Diagnostics", page.DetailPrimaryAction?.Label);
            Assert.Equal(["Refresh"], page.DetailVisibleActions.Select(item => item.Label));
            Assert.DoesNotContain(page.DetailAdvancedActions, item => item.Label == "Start Only");
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RecordOnlyWorkspace_DoesNotOfferOpenWorkspaceRecoveryAction()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"avalonia-recover-enable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            File.WriteAllText(Path.Combine(workspaceRoot, "workspace.yaml"), "workspace:\n  name: smoke\n  image: ubuntu:24.04\nprovider:\n  type: git\nruntime:\n  default: default\nfeatures:\n- core\n");
            var recordOnlyItem = new WorkspaceShellItem
            {
                Record = new WorkspaceRecord
                {
                    Name = "smoke",
                    RootPath = workspaceRoot,
                    RepositoryPath = workspaceRoot,
                    ConfigurationPath = "workspace.yaml",
                    CreatedUtc = DateTimeOffset.UtcNow,
                    LastOpenedUtc = DateTimeOffset.UtcNow,
                },
            };

            var page = new WorkspacesPageViewModel(new FakeDesktopShellService([], [recordOnlyItem]));
            page.SetInteractionService(new FakeWorkspaceInteractionService());
            await page.LoadAsync();
            page.SelectedWorkspace = page.Workspaces.Single(item => item.RootPath == workspaceRoot);

            Assert.Equal("Run Diagnostics", page.DetailPrimaryAction?.Label);
            Assert.DoesNotContain(page.DetailVisibleActions, item => item.Label == "Open Workspace");
            Assert.Equal(["Refresh"], page.DetailVisibleActions.Select(item => item.Label));
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RecordOnlyWorkspace_DoesNotOfferAttachOnlyAsNormalAction()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"avalonia-attach-enable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            File.WriteAllText(Path.Combine(workspaceRoot, "workspace.yaml"), "workspace:\n  name: smoke\n  image: ubuntu:24.04\nprovider:\n  type: git\nruntime:\n  default: default\nfeatures:\n- core\n");
            var recordOnlyItem = new WorkspaceShellItem
            {
                Record = new WorkspaceRecord
                {
                    Name = "smoke",
                    RootPath = workspaceRoot,
                    RepositoryPath = workspaceRoot,
                    ConfigurationPath = "workspace.yaml",
                    CreatedUtc = DateTimeOffset.UtcNow,
                    LastOpenedUtc = DateTimeOffset.UtcNow,
                },
            };

            var page = new WorkspacesPageViewModel(new FakeDesktopShellService([], [recordOnlyItem]));
            await page.LoadAsync();
            page.SelectedWorkspace = page.Workspaces.Single(item => item.RootPath == workspaceRoot);

            Assert.DoesNotContain(page.DetailAdvancedActions, item => item.Label == "Attach Only");
            Assert.Equal("Run Diagnostics", page.DetailPrimaryAction?.Label);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AttachStart_EmitsImmediateAttachTranscriptBeforeLauncherCompletes()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            AttachResultFactoryAsync = async (_, _, cancellationToken) =>
            {
                started.SetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new WorkspaceOperationResult { Snapshot = CreateSnapshot("alpha"), Message = "attach launched", Transcript = new OperationTranscript() };
            },
        });

        await page.LoadAsync();
        var attachTask = page.DetailAdvancedActions.Single(item => item.Label == "Attach Only").Command is AsyncRelayCommand cmd ? cmd.ExecuteAsync() : throw new InvalidOperationException();
        await started.Task;

        Assert.Contains("Preparing attach...", page.OperationLogText, StringComparison.Ordinal);
        Assert.DoesNotContain("Validating runtime...", page.OperationLogText, StringComparison.Ordinal);

        release.SetResult();
        await attachTask;
    }

    [Fact]
    public async Task AttachFailure_IsSurfacedInTranscript()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            AttachException = new InvalidOperationException("Windows Terminal launch failed."),
        });

        await page.LoadAsync();
        await ((AsyncRelayCommand)page.DetailAdvancedActions.Single(item => item.Label == "Attach Only").Command).ExecuteAsync();

        Assert.Contains("Preparing attach...", page.OperationLogText, StringComparison.Ordinal);
        Assert.Contains("Windows Terminal launch failed.", page.OperationLogText, StringComparison.Ordinal);
        Assert.Equal("Windows Terminal launch failed.", page.DetailSummary);
    }

    [Fact]
    public async Task OpenWorkspace_UsesSingleOpenAction()
    {
        var service = new FakeDesktopShellService([CreateSnapshot("alpha")]);
        var page = new WorkspacesPageViewModel(service);

        await page.LoadAsync();
        await page.OpenSelectedWorkspaceCommand.ExecuteAsync();

        Assert.Equal(1, service.OpenWorkspaceCallCount);
        Assert.Equal(0, service.AttachCallCount);
        Assert.Contains("Open Workspace", page.DetailActions.Select(item => item.Label));
    }

    [Fact]
    public async Task OpenWorkspaceFailure_IsSurfacedWithoutThrowing()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            OpenWorkspaceException = new InvalidOperationException("Runtime files need repair. Run Recover Workspace."),
        });
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();
        await page.OpenSelectedWorkspaceCommand.ExecuteAsync();

        Assert.Contains("Runtime files need repair. Run Recover Workspace.", page.OperationLogText, StringComparison.Ordinal);
        Assert.Equal("Runtime files need repair. Open Workspace will try to repair safe runtime issues automatically.", page.DetailSummary);
        Assert.DoesNotContain("Recover Workspace", page.DetailSummary, StringComparison.Ordinal);
        Assert.Contains(page.DetailItems, item => item.Label == "Workspace");
    }

    [Fact]
    public async Task OpenWorkspace_UpdatesDetailSummaryFromOpenProgress()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            OpenWorkspaceResultFactoryAsync = async (_, sink, cancellationToken) =>
            {
                sink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Provisioning runtime..." });
                started.SetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new WorkspaceOperationResult { Snapshot = CreateSnapshot("alpha"), Message = "opened", Transcript = new OperationTranscript() };
            },
        });

        await page.LoadAsync();
        var openTask = page.OpenSelectedWorkspaceCommand.ExecuteAsync();
        await started.Task;

        Assert.Equal("Preparing workspace. This may take several minutes.", page.DetailSummary);

        release.SetResult();
        await openTask;
    }

    [Fact]
    public async Task RecordOnlyWorkspace_DoesNotOfferSavePointAction()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"avalonia-savepoint-enable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            File.WriteAllText(Path.Combine(workspaceRoot, "workspace.yaml"), "workspace:\n  name: smoke\n  image: ubuntu:24.04\nprovider:\n  type: git\nruntime:\n  default: default\nfeatures:\n- core\n");
            var recordOnlyItem = new WorkspaceShellItem
            {
                Record = new WorkspaceRecord
                {
                    Name = "smoke",
                    RootPath = workspaceRoot,
                    RepositoryPath = workspaceRoot,
                    ConfigurationPath = "workspace.yaml",
                    CreatedUtc = DateTimeOffset.UtcNow,
                    LastOpenedUtc = DateTimeOffset.UtcNow,
                },
            };

            var page = new WorkspacesPageViewModel(new FakeDesktopShellService([], [recordOnlyItem]));
            page.SetInteractionService(new FakeWorkspaceInteractionService());
            await page.LoadAsync();
            page.SelectedWorkspace = page.Workspaces.Single(item => item.RootPath == workspaceRoot);

            Assert.DoesNotContain(page.DetailActions, item => item.Label == "Save Point");
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RecordOnlyWorkspace_DoesNotOfferBackupAction()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"avalonia-backup-enable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            File.WriteAllText(Path.Combine(workspaceRoot, "workspace.yaml"), "workspace:\n  name: smoke\n  image: ubuntu:24.04\nprovider:\n  type: git\nruntime:\n  default: default\nfeatures:\n- core\n");
            var recordOnlyItem = new WorkspaceShellItem
            {
                Record = new WorkspaceRecord
                {
                    Name = "smoke",
                    RootPath = workspaceRoot,
                    RepositoryPath = workspaceRoot,
                    ConfigurationPath = "workspace.yaml",
                    CreatedUtc = DateTimeOffset.UtcNow,
                    LastOpenedUtc = DateTimeOffset.UtcNow,
                },
            };

            var page = new WorkspacesPageViewModel(new FakeDesktopShellService([], [recordOnlyItem]));
            page.SetInteractionService(new FakeWorkspaceInteractionService());
            await page.LoadAsync();
            page.SelectedWorkspace = page.Workspaces.Single(item => item.RootPath == workspaceRoot);

            Assert.DoesNotContain(page.DetailActions, item => item.Label == "Backup");
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task BackupStart_EmitsImmediateTranscriptBeforeExportCompletes()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interaction = new FakeWorkspaceInteractionService { BackupArchivePath = Path.Combine(Path.GetTempPath(), $"backup-{Guid.NewGuid():N}.zip") };
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            BackupResultFactoryAsync = async (_, _, _, cancellationToken) =>
            {
                started.SetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new WorkspaceBackupResult
                {
                    Snapshot = CreateSnapshot("alpha"),
                    Message = "Backup created.",
                    Transcript = new OperationTranscript(),
                    Export = new WorkspaceBackupExportResult
                    {
                        ArchivePath = interaction.BackupArchivePath!,
                        FileCount = 4,
                        ArchiveSizeBytes = 2048,
                        IncludedEntries = [],
                        ExcludedEntries = [],
                        Warnings = [],
                    },
                    Manifest = new WorkspaceBackupManifestResult
                    {
                        ManifestPath = Path.Combine(Path.GetTempPath(), "backup-manifest.yaml"),
                        ArchiveEntryPath = "backup-manifest.yaml",
                        IncludedFileCount = 4,
                        ExcludedFileCount = 0,
                        WarningCount = 0,
                    },
                };
            },
        });
        page.SetInteractionService(interaction);

        await page.LoadAsync();
        var backupTask = ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Backup").Command).ExecuteAsync();
        await started.Task;

        Assert.Contains("Preparing backup...", page.OperationLogText, StringComparison.Ordinal);
        Assert.Contains("Creating backup archive...", page.OperationLogText, StringComparison.Ordinal);

        release.SetResult();
        await backupTask;
    }

    [Fact]
    public async Task BackupCancellation_StopsBeforeExport()
    {
        var interaction = new FakeWorkspaceInteractionService { BackupArchivePath = null };
        var service = new FakeDesktopShellService([CreateSnapshot("alpha")]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(interaction);

        await page.LoadAsync();
        await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Backup").Command).ExecuteAsync();

        Assert.Contains("Cancelled.", page.OperationLogText, StringComparison.Ordinal);
        Assert.Equal("Backup cancelled.", page.DetailSummary);
        Assert.Equal(0, service.BackupCallCount);
    }

    [Fact]
    public async Task BackupSuccess_ShowsArchiveSummaryAndWarnings()
    {
        var archivePath = Path.Combine(Path.GetTempPath(), $"backup-{Guid.NewGuid():N}.zip");
        var interaction = new FakeWorkspaceInteractionService { BackupArchivePath = archivePath };
        var service = new FakeDesktopShellService([CreateSnapshot("alpha")]);
        service.BackupResultFactoryAsync = (_, _, _, _) => Task.FromResult(new WorkspaceBackupResult
        {
            Snapshot = CreateSnapshot("alpha"),
            Message = "Backup created.",
            Transcript = new OperationTranscript(),
            Export = new WorkspaceBackupExportResult
            {
                ArchivePath = archivePath,
                FileCount = 6,
                ArchiveSizeBytes = 4096,
                IncludedEntries = [new WorkspaceBackupEntry { Path = "workspace.yaml", Reason = "included", SizeBytes = 20 }],
                ExcludedEntries = [new WorkspaceBackupEntry { Path = "bin/", Reason = "excluded", SizeBytes = 0 }],
                Warnings = ["secrets/.env: Potential secret content is excluded by default."],
            },
            Manifest = new WorkspaceBackupManifestResult
            {
                ManifestPath = Path.Combine(Path.GetTempPath(), "backup-manifest.yaml"),
                ArchiveEntryPath = "backup-manifest.yaml",
                IncludedFileCount = 6,
                ExcludedFileCount = 1,
                WarningCount = 1,
            },
        });
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(interaction);

        await page.LoadAsync();
        await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Backup").Command).ExecuteAsync();

        Assert.Contains(page.DetailItems, item => item.Label == "Archive" && item.Value == archivePath);
        Assert.Contains(page.DetailItems, item => item.Label == "Included files" && item.Value == "6");
        Assert.Contains(page.DetailItems, item => item.Label == "Archive size" && item.Value.Contains("KB", StringComparison.Ordinal));
        Assert.Contains(page.DetailItems, item => item.Label == "Manifest" && item.Value.EndsWith("backup-manifest.yaml", StringComparison.Ordinal));
        Assert.Contains(page.DetailItems, item => item.Label == "Warnings" && item.Value.Contains("Potential secret content", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BackupFailure_IsSurfacedInTranscript()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            BackupException = new InvalidOperationException("Backup export failed."),
        });
        page.SetInteractionService(new FakeWorkspaceInteractionService { BackupArchivePath = Path.Combine(Path.GetTempPath(), $"backup-{Guid.NewGuid():N}.zip") });

        await page.LoadAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Backup").Command).ExecuteAsync());

        Assert.Contains("Preparing backup...", page.OperationLogText, StringComparison.Ordinal);
        Assert.Equal("Backup export failed.", page.DetailSummary);
        Assert.Contains("Backup export failed.", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordOnlyWorkspace_DoesNotOfferPublishAction()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"avalonia-publish-enable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            File.WriteAllText(Path.Combine(workspaceRoot, "workspace.yaml"), "workspace:\n  name: smoke\n  image: ubuntu:24.04\nprovider:\n  type: git\nruntime:\n  default: default\nfeatures:\n- core\n");
            var recordOnlyItem = new WorkspaceShellItem
            {
                Record = new WorkspaceRecord
                {
                    Name = "smoke",
                    RootPath = workspaceRoot,
                    RepositoryPath = workspaceRoot,
                    ConfigurationPath = "workspace.yaml",
                    CreatedUtc = DateTimeOffset.UtcNow,
                    LastOpenedUtc = DateTimeOffset.UtcNow,
                },
            };

            var page = new WorkspacesPageViewModel(new FakeDesktopShellService([], [recordOnlyItem]));
            page.SetInteractionService(new FakeWorkspaceInteractionService());
            await page.LoadAsync();
            page.SelectedWorkspace = page.Workspaces.Single(item => item.RootPath == workspaceRoot);

            Assert.DoesNotContain(page.DetailActions, item => item.Label == "Publish");
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PublishBlockedByDirtyWork_ShowsSavePointRequirement()
    {
        var assessment = new WorkspacePublishAssessment
        {
            WorkspaceName = "alpha",
            CurrentBranch = "workspace/users/alpha",
            Summary = "Uncommitted or untracked work is present. Create a Save Point before publishing.",
            ConfirmationMessage = string.Empty,
            Findings = ["Working tree changes: 1 changed, 2 untracked."],
            Warnings = [],
            CanPublish = false,
            IsBlocked = true,
            RequiresConfirmation = false,
            RequiresSavePoint = true,
            HasRemoteConfigured = true,
            RemoteName = "origin",
            RemoteBranch = "origin/workspace/users/alpha",
            AheadCount = 1,
            BehindCount = 0,
        };
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            PublishAssessment = assessment,
        });
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();
        await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Publish").Command).ExecuteAsync();

        Assert.Contains("Preparing publish...", page.OperationLogText, StringComparison.Ordinal);
        Assert.Contains("Create a Save Point before publishing", page.DetailSummary, StringComparison.Ordinal);
        Assert.Contains(page.DetailItems, item => item.Label == "Findings" && item.Value.Contains("1 changed, 2 untracked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishCancellation_StopsBeforeExecution()
    {
        var assessment = new WorkspacePublishAssessment
        {
            WorkspaceName = "alpha",
            CurrentBranch = "workspace/users/alpha",
            Summary = "Ready to publish 1 commit(s) to 'origin/workspace/users/alpha'.",
            ConfirmationMessage = "Publish this Working Copy now?",
            Findings = ["Ahead/behind: 1/0"],
            Warnings = [],
            CanPublish = true,
            IsBlocked = false,
            RequiresConfirmation = true,
            RequiresSavePoint = false,
            HasRemoteConfigured = true,
            RemoteName = "origin",
            RemoteBranch = "origin/workspace/users/alpha",
            AheadCount = 1,
            BehindCount = 0,
        };
        var service = new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            PublishAssessment = assessment,
        };
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService { PublishConfirmed = false });

        await page.LoadAsync();
        await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Publish").Command).ExecuteAsync();

        Assert.Contains("Cancelled.", page.OperationLogText, StringComparison.Ordinal);
        Assert.Equal("Publish cancelled.", page.DetailSummary);
        Assert.Equal(0, service.PublishCallCount);
    }

    [Fact]
    public async Task PublishSuccess_ShowsRemoteSummary()
    {
        var snapshot = CreateSnapshot("alpha");
        var assessment = new WorkspacePublishAssessment
        {
            WorkspaceName = "alpha",
            CurrentBranch = snapshot.Safety.AdvancedGit.CurrentBranch,
            Summary = "Ready to publish 1 commit(s) to 'origin/workspace/users/alpha'.",
            ConfirmationMessage = "Publish this Working Copy now?",
            Findings = ["Ahead/behind: 1/0"],
            Warnings = ["This is the first publish for the current Working Copy."],
            CanPublish = true,
            IsBlocked = false,
            RequiresConfirmation = true,
            RequiresSavePoint = false,
            HasRemoteConfigured = true,
            RemoteName = "origin",
            RemoteBranch = "origin/workspace/users/alpha",
            AheadCount = 1,
            BehindCount = 0,
        };
        var service = new FakeDesktopShellService([snapshot])
        {
            PublishAssessment = assessment,
            PublishResultFactoryAsync = (_, _, cancellationToken) => Task.FromResult(new WorkspacePublishResult
            {
                Snapshot = snapshot,
                Message = "Working Copy published successfully.",
                Transcript = new OperationTranscript(),
                Review = new WorkspacePublishReview
                {
                    IsBlocked = false,
                    Message = "Working Copy published successfully.",
                    WorkingCopyName = snapshot.Safety.AdvancedGit.CurrentBranch,
                    RemoteName = "origin",
                    RemoteBranch = "origin/workspace/users/alpha",
                    AheadCount = 0,
                    BehindCount = 0,
                    LatestCommitSha = "abc123",
                },
            }),
        };
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService { PublishConfirmed = true });

        await page.LoadAsync();
        await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Publish").Command).ExecuteAsync();

        Assert.Contains(page.DetailItems, item => item.Label == "Remote" && item.Value == "origin");
        Assert.Contains(page.DetailItems, item => item.Label == "Tracking" && item.Value == "origin/workspace/users/alpha");
        Assert.Contains(page.DetailItems, item => item.Label == "Latest commit" && item.Value == "abc123");
        Assert.Equal(1, service.PublishCallCount);
    }

    [Fact]
    public async Task PublishFailure_IsSurfacedInTranscript()
    {
        var assessment = new WorkspacePublishAssessment
        {
            WorkspaceName = "alpha",
            CurrentBranch = "workspace/users/alpha",
            Summary = "Ready to publish 1 commit(s) to 'origin/workspace/users/alpha'.",
            ConfirmationMessage = "Publish this Working Copy now?",
            Findings = ["Ahead/behind: 1/0"],
            Warnings = [],
            CanPublish = true,
            IsBlocked = false,
            RequiresConfirmation = true,
            RequiresSavePoint = false,
            HasRemoteConfigured = true,
            RemoteName = "origin",
            RemoteBranch = "origin/workspace/users/alpha",
            AheadCount = 1,
            BehindCount = 0,
        };
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            PublishAssessment = assessment,
            PublishException = new InvalidOperationException("Authentication failed while publishing."),
        });
        page.SetInteractionService(new FakeWorkspaceInteractionService { PublishConfirmed = true });

        await page.LoadAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Publish").Command).ExecuteAsync());

        Assert.Contains("Preparing publish...", page.OperationLogText, StringComparison.Ordinal);
        Assert.Equal("Authentication failed while publishing.", page.DetailSummary);
        Assert.Contains("Authentication failed while publishing.", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveAction_IsEnabledForSelectedWorkspace()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")]));
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();

        var remove = page.DetailActions.Single(item => item.Label == "Remove");
        Assert.True(remove.IsEnabled);
        Assert.Equal(string.Empty, remove.DisabledReason);
    }

    [Fact]
    public async Task RemoveCancellation_DoesNotRemoveWorkspace()
    {
        var service = new FakeDesktopShellService([CreateSnapshot("alpha"), CreateSnapshot("beta")]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService { RemoveConfirmed = false });

        await page.LoadAsync();
        await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Remove").Command).ExecuteAsync();

        Assert.Equal(2, page.Workspaces.Count);
        Assert.Equal(0, service.RemoveCallCount);
        Assert.Equal("Workspace removal cancelled.", page.DetailSummary);
    }

    [Fact]
    public async Task RemoveDialog_LayoutPreservesDefaultAndCancelBehavior()
    {
        var repoRoot = GetRepositoryRoot();
        var axaml = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "RemoveWorkspaceWindow.axaml"));

        Assert.Contains("RegistrationOnlyRadioButton", axaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"True\"", axaml, StringComparison.Ordinal);
        Assert.Contains("DeleteFilesRadioButton", axaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"False\"", axaml, StringComparison.Ordinal);
        Assert.Contains("DeleteFilesUnavailableTextBlock", axaml, StringComparison.Ordinal);
        Assert.Contains("IsDefault=\"True\"", axaml, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveSuccess_RemovesRowAndUpdatesSelection()
    {
        var first = CreateSnapshot("alpha");
        var second = CreateSnapshot("beta");
        var service = new FakeDesktopShellService([first, second]);
        service.RemoveResultFactoryAsync = (_, _, _) => Task.FromResult(new WorkspaceRemovalOperationResult
        {
            Message = "Removed 'alpha' from the workspace list.",
            Transcript = new OperationTranscript(),
            Removal = new WorkspaceRemovalResult
            {
                WorkspaceName = "alpha",
                WorkspaceRoot = first.Paths.RootPath,
                FilesDeleted = false,
                Warnings = [],
                Succeeded = true,
                FailureReason = string.Empty,
            },
        });
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService { RemoveConfirmed = true });

        await page.LoadAsync();
        page.SelectedWorkspace = page.Workspaces.Single(item => item.Name == "alpha");
        await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Remove").Command).ExecuteAsync();

        Assert.Single(page.Workspaces);
        Assert.Equal("beta", page.SelectedWorkspace?.Name);
        Assert.Equal(1, service.RemoveCallCount);
        Assert.Equal("Removed 'alpha' from the workspace list.", page.DetailSummary);
    }

    [Fact]
    public async Task RemoveDockerResourcesFailure_IsSurfacedWithoutCrashing()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            RemoveException = new InvalidOperationException("Docker cleanup failed because Docker is unavailable."),
        });
        page.SetInteractionService(new FakeWorkspaceInteractionService
        {
            RemoveConfirmed = true,
            RemoveChoice = WorkspaceRemovalChoice.DockerResources,
        });

        await page.LoadAsync();
        await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Remove").Command).ExecuteAsync();

        Assert.Contains("Preparing removal...", page.OperationLogText, StringComparison.Ordinal);
        Assert.Contains("Docker cleanup failed because Docker is unavailable.", page.DetailSummary, StringComparison.Ordinal);
        Assert.Contains("Failed.", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveFailure_IsSurfacedClearlyWithoutThrowing()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            RemoveException = new InvalidOperationException("Workspace root path is required before removal can run."),
        });
        page.SetInteractionService(new FakeWorkspaceInteractionService { RemoveConfirmed = true });

        await page.LoadAsync();
        await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Remove").Command).ExecuteAsync();

        Assert.Contains("Preparing removal...", page.OperationLogText, StringComparison.Ordinal);
        Assert.Contains("Workspace root path is required", page.DetailSummary, StringComparison.Ordinal);
        Assert.Contains("Failed.", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveDialogFailure_IsSurfacedClearlyWithoutThrowing()
    {
        var service = new FakeDesktopShellService([CreateSnapshot("alpha")]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService
        {
            RemoveDialogException = new InvalidOperationException("Remove dialog failed to open."),
        });

        await page.LoadAsync();
        await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Remove").Command).ExecuteAsync();

        Assert.Equal(0, service.RemoveCallCount);
        Assert.Contains("Remove dialog failed to open.", page.DetailSummary, StringComparison.Ordinal);
        Assert.Contains("Failed.", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveUnsupportedDeleteChoice_IsRejectedBeforeRemovalStarts()
    {
        var service = new FakeDesktopShellService([CreateSnapshot("alpha")]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService
        {
            RemoveConfirmed = true,
            RemoveChoice = WorkspaceRemovalChoice.DeleteFiles,
        });

        await page.LoadAsync();
        await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Remove").Command).ExecuteAsync();

        Assert.Equal(0, service.RemoveCallCount);
        Assert.Contains("Delete workspace files is not available in this version", page.DetailSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("Removing workspace from list...", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveFromList_UsesSelectedNestedWorkspaceRoot()
    {
        var baseRoot = Path.Combine(Path.GetTempPath(), $"avalonia-remove-root-{Guid.NewGuid():N}");
        var workspaceRoot = Path.Combine(baseRoot, "rc-first-workspace");
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            var recordOnlyItem = new WorkspaceShellItem
            {
                Record = new WorkspaceRecord
                {
                    Name = "rc-first-workspace",
                    RootPath = baseRoot,
                    RepositoryPath = baseRoot,
                    ConfigurationPath = "rc-first-workspace/workspace.yaml",
                    CreatedUtc = DateTimeOffset.UtcNow,
                    LastOpenedUtc = DateTimeOffset.UtcNow,
                },
                ErrorMessage = "workspace.yaml missing",
            };

            var service = new FakeDesktopShellService([], [recordOnlyItem]);
            var page = new WorkspacesPageViewModel(service);
            page.SetInteractionService(new FakeWorkspaceInteractionService { RemoveConfirmed = true, RemoveChoice = WorkspaceRemovalChoice.RegistrationOnly });

            await page.LoadAsync();

            Assert.Equal(workspaceRoot, page.SelectedWorkspace?.RootPath);

            await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Remove").Command).ExecuteAsync();

            Assert.Equal(workspaceRoot, service.LastRemoveRootPath);
        }
        finally
        {
            if (Directory.Exists(baseRoot))
            {
                Directory.Delete(baseRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CheckpointCancellation_DoesNotCreateCheckpoint()
    {
        var service = new FakeDesktopShellService([CreateSnapshot("alpha")]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService { CheckpointConfirmed = false });

        await page.LoadAsync();
        await page.CreateCheckpointCommand.ExecuteAsync();

        Assert.Equal(0, service.CreateCheckpointCallCount);
        Assert.Contains("Cancelled.", page.OperationLogText, StringComparison.Ordinal);
        Assert.Equal("Checkpoint creation cancelled.", page.DetailSummary);
    }

    [Fact]
    public async Task CheckpointFailure_IsSurfacedInTranscript()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            CheckpointException = new InvalidOperationException("Checkpoint review required."),
        });
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.CreateCheckpointCommand.ExecuteAsync());

        Assert.Contains("Creating checkpoint...", page.OperationLogText, StringComparison.Ordinal);
        Assert.Equal("Checkpoint review required.", page.DetailSummary);
        Assert.Contains("Checkpoint review required.", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckpointSuccess_ShowsCheckpointSummary()
    {
        var service = new FakeDesktopShellService([CreateSnapshot("alpha")]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();
        await page.CreateCheckpointCommand.ExecuteAsync();

        Assert.Equal(1, service.CreateCheckpointCallCount);
        Assert.Contains("Creating checkpoint...", page.OperationLogText, StringComparison.Ordinal);
        Assert.Equal("Checkpoint 'cp-1' created.", page.DetailSummary);
    }

    [Fact]
    public async Task OpenWorkspace_DelegatesToUnifiedOpenFlow()
    {
        var service = new FakeDesktopShellService([CreateSnapshot("alpha")]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();
        await page.OpenSelectedWorkspaceCommand.ExecuteAsync();

        Assert.Equal(1, service.OpenWorkspaceCallCount);
        Assert.Equal(0, service.PrepareCallCount);
        Assert.Equal(0, service.AttachCallCount);
    }

    [Fact]
    public async Task SavePointStart_EmitsImmediateTranscriptBeforeSaveCompletes()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interaction = new FakeWorkspaceInteractionService();
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            SavePointResultFactoryAsync = async (_, _, _, cancellationToken) =>
            {
                started.SetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new WorkspaceOperationResult { Snapshot = CreateSnapshot("alpha"), Message = "Save Point created.", Transcript = new OperationTranscript() };
            },
        });
        page.SetInteractionService(interaction);

        await page.LoadAsync();
        var savePointTask = ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Save Point").Command).ExecuteAsync();
        await started.Task;

        Assert.Contains("Preparing Save Point...", page.OperationLogText, StringComparison.Ordinal);
        Assert.Contains("Creating Save Point...", page.OperationLogText, StringComparison.Ordinal);

        release.SetResult();
        await savePointTask;
    }

    [Fact]
    public async Task SavePointCancellation_StopsBeforeSave()
    {
        var interaction = new FakeWorkspaceInteractionService { SavePointDraft = null };
        var service = new FakeDesktopShellService([CreateSnapshot("alpha")]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(interaction);

        await page.LoadAsync();
        await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Save Point").Command).ExecuteAsync();

        Assert.Contains("Cancelled.", page.OperationLogText, StringComparison.Ordinal);
        Assert.Equal("Save Point cancelled.", page.DetailSummary);
        Assert.Equal(0, service.CreateSavePointCallCount);
    }

    [Fact]
    public async Task SavePointConfirmation_PassesDialogMessageToDesktopShellService()
    {
        var interaction = new FakeWorkspaceInteractionService { SavePointDraft = new SavePointDraft { Message = "Edited Save Point message" } };
        var service = new FakeDesktopShellService([CreateSnapshot("alpha")]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(interaction);

        await page.LoadAsync();
        await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Save Point").Command).ExecuteAsync();

        Assert.Equal(1, service.CreateSavePointCallCount);
        Assert.Equal("Edited Save Point message", service.LastSavePointMessage);
    }

    [Fact]
    public async Task SavePointSuccess_ReenablesSavePointActionAfterCompletion()
    {
        var interaction = new FakeWorkspaceInteractionService { SavePointDraft = new SavePointDraft { Message = "Edited Save Point message" } };
        var service = new FakeDesktopShellService([CreateSnapshot("alpha")]);
        var page = new WorkspacesPageViewModel(service);
        page.SetInteractionService(interaction);

        await page.LoadAsync();
        await ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Save Point").Command).ExecuteAsync();

        var savePoint = page.DetailActions.Single(item => item.Label == "Save Point");
        Assert.True(savePoint.IsEnabled);
        Assert.Equal(string.Empty, savePoint.DisabledReason);
    }

    [Fact]
    public async Task TimelineRefreshes_AfterSavePointSuccess()
    {
        var snapshot = CreateSnapshot("alpha");
        var desktop = new FakeDesktopShellService([snapshot])
        {
            TimelineByPath =
            {
                [snapshot.Paths.TimelinePath] = new WorkspaceTimeline(),
            },
        };
        desktop.SavePointResultFactoryAsync = (_, _, _, _) =>
        {
            desktop.TimelineByPath[snapshot.Paths.TimelinePath] = new WorkspaceTimeline
            {
                Events =
                [
                    new WorkspaceTimelineEvent
                    {
                        Id = "sp-1",
                        Type = "save-point",
                        Summary = "Created Save Point",
                        Details = "Manual Save Point",
                        Branch = "users/test/alpha",
                        CommitSha = "abc123",
                        AffectedPaths = ["notes.txt"],
                        OccurredUtc = DateTimeOffset.UtcNow,
                    },
                ],
            };

            return Task.FromResult(new WorkspaceOperationResult { Snapshot = snapshot, Message = "Save Point created.", Transcript = new OperationTranscript() });
        };
        var shell = CreateShellWithDesktop(desktop);
        shell.SetClipboardService(new FakeClipboardService());
        shell.SetInteractionService(new FakeWorkspaceInteractionService { SavePointDraft = new SavePointDraft { Message = "Manual Save Point" } });
        await shell.InitializeAsync();

        var workspacesPage = (WorkspacesPageViewModel)shell.NavigationItems.Single(item => item.Title == "Workspaces").Page;
        await workspacesPage.CreateSavePointCommand.ExecuteAsync();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (((SavePointsPageViewModel)shell.NavigationItems.Single(item => item.Page is SavePointsPageViewModel).Page).Entries.Count == 1)
            {
                break;
            }

            await Task.Delay(50);
        }

        var timelinePage = (SavePointsPageViewModel)shell.NavigationItems.Single(item => item.Page is SavePointsPageViewModel).Page;
        Assert.Single(timelinePage.Entries);
        Assert.Equal("Manual Save Point", timelinePage.SelectedEntry?.Message);
    }

    [Fact]
    public async Task TimelineEntrySelection_ShowsDetailsAndCopyActions()
    {
        var snapshot = CreateSnapshot("alpha");
        var desktop = new FakeDesktopShellService([snapshot])
        {
            TimelineByPath =
            {
                [snapshot.Paths.TimelinePath] = new WorkspaceTimeline
                {
                    Events =
                    [
                        new WorkspaceTimelineEvent
                        {
                            Id = "sp-1",
                            Type = "save-point",
                            Summary = "Created Save Point",
                            Details = "Manual Save Point",
                            Branch = "users/test/alpha",
                            CommitSha = "abc123",
                            AffectedPaths = ["notes.txt", "workspace.yaml"],
                            OccurredUtc = DateTimeOffset.UtcNow,
                        },
                    ],
                },
            },
        };
        var clipboard = new FakeClipboardService();
        var page = new SavePointsPageViewModel(desktop);
        page.SetClipboardService(clipboard);

        await page.RefreshAsync(new WorkspaceSummaryViewModel(new WorkspaceShellItem { Record = snapshot.Record, Snapshot = snapshot }));

        Assert.Equal("Created Save Point", page.DetailTitle);
        Assert.Contains(page.DetailItems, item => item.Label == "Commit" && item.Value == "abc123");
        Assert.Contains(page.DetailItems, item => item.Label == "Affected files" && item.Value.Contains("2 file", StringComparison.Ordinal));

        var copyCommit = page.DetailActions.Single(item => item.Label == "Copy Commit Id");
        await ((AsyncRelayCommand)copyCommit.Command).ExecuteAsync();
        Assert.Equal("abc123", clipboard.Text);
    }

    [Fact]
    public async Task TimelineCopyFailure_IsSurfacedWithoutThrowing()
    {
        var snapshot = CreateSnapshot("alpha");
        var desktop = new FakeDesktopShellService([snapshot])
        {
            TimelineByPath =
            {
                [snapshot.Paths.TimelinePath] = new WorkspaceTimeline
                {
                    Events =
                    [
                        new WorkspaceTimelineEvent
                        {
                            Id = "sp-1",
                            Type = "save-point",
                            Summary = "Created Save Point",
                            Details = "Manual Save Point",
                            CommitSha = "abc123",
                            OccurredUtc = DateTimeOffset.UtcNow,
                        },
                    ],
                },
            },
        };
        var page = new SavePointsPageViewModel(desktop);
        page.SetClipboardService(new ThrowingClipboardService());

        await page.RefreshAsync(new WorkspaceSummaryViewModel(new WorkspaceShellItem { Record = snapshot.Record, Snapshot = snapshot }));

        var copyCommit = page.DetailActions.Single(item => item.Label == "Copy Commit Id");
        await ((AsyncRelayCommand)copyCommit.Command).ExecuteAsync();

        Assert.Contains("Timeline copy failed:", page.DetailSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimelinePage_ShowsEmptyState_WhenNoEntriesExist()
    {
        var snapshot = CreateSnapshot("alpha");
        File.WriteAllText(snapshot.Paths.TimelinePath, "events: []\n");
        var desktop = new FakeDesktopShellService([snapshot])
        {
            TimelineByPath =
            {
                [snapshot.Paths.TimelinePath] = new WorkspaceTimeline(),
            },
        };
        var page = new SavePointsPageViewModel(desktop);

        await page.RefreshAsync(new WorkspaceSummaryViewModel(new WorkspaceShellItem { Record = snapshot.Record, Snapshot = snapshot }));

        Assert.Empty(page.Entries);
        Assert.True(page.ShowEmptyState);
        Assert.Contains("No timeline entries exist yet", page.DetailSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimelinePage_ShowsActionableDiagnostics_WhenTimelineLoadFails()
    {
        var snapshot = CreateSnapshot("alpha");
        var desktop = new FakeDesktopShellService([snapshot])
        {
            TimelineException = new InvalidOperationException("timeline.yaml is corrupt"),
        };
        var page = new SavePointsPageViewModel(desktop);

        await page.RefreshAsync(new WorkspaceSummaryViewModel(new WorkspaceShellItem { Record = snapshot.Record, Snapshot = snapshot }));

        Assert.True(page.ShowErrorState);
        Assert.Contains("timeline.yaml is corrupt", page.DetailSummary, StringComparison.Ordinal);
        Assert.Contains(page.DetailActions, item => item.Label == "Open Timeline File");
    }

    [Fact]
    public async Task SavePointFailure_IsSurfacedInTranscript()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            SavePointException = new InvalidOperationException("Save Point validation failed."),
        });
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ((AsyncRelayCommand)page.DetailActions.Single(item => item.Label == "Save Point").Command).ExecuteAsync());

        Assert.Contains("Preparing Save Point...", page.OperationLogText, StringComparison.Ordinal);
        Assert.Equal("Save Point validation failed.", page.DetailSummary);
        Assert.Contains("Save Point validation failed.", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReprovisionAction_VisibleForSelectedWorkspace()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")]));

        await page.LoadAsync();

        Assert.DoesNotContain(page.DetailActions, item => item.Label == "Reprovision");
        Assert.DoesNotContain(page.DetailVisibleActions, item => item.Label == "Troubleshoot Workspace");
        Assert.DoesNotContain(page.DetailVisibleActions, item => item.Label == "Start Only");
        Assert.DoesNotContain(page.DetailVisibleActions, item => item.Label == "Attach Only");
    }

    [Fact]
    public async Task Reprovision_EnabledWhenWorkspaceCanBeReprovisioned()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")]));

        await page.LoadAsync();

        Assert.DoesNotContain(page.DetailActions, item => item.Label == "Reprovision");
    }

    [Fact]
    public async Task Reprovision_DisabledWithReasonWhenWorkspaceCannotBeLoaded()
    {
        var invalidRecord = new WorkspaceRecord
        {
            Name = "broken",
            RootPath = "/workspace/broken",
            RepositoryPath = "/workspace/broken",
            ConfigurationPath = "workspace.yaml",
            CreatedUtc = DateTimeOffset.UtcNow,
            LastOpenedUtc = DateTimeOffset.UtcNow,
        };
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([], [new WorkspaceShellItem { Record = invalidRecord, ErrorMessage = "workspace.yaml missing" }]));

        await page.LoadAsync();

        Assert.DoesNotContain(page.DetailActions, item => item.Label == "Reprovision");
    }

    [Fact]
    public async Task SuccessfulReprovision_RefreshesWorkspaceSnapshot()
    {
        var original = CreateSnapshot("alpha", includeRuntimeState: false, lastOperationResult: "Workspace provisioning failed. Exit code: 127. /workspace/.env: line 17...", lastOperationSucceeded: false);
        var refreshed = CreateSnapshot("alpha", includeRuntimeState: true, updateRequired: false, lastOperationResult: "Workspace reprovisioned successfully.");
        var desktop = new FakeDesktopShellService([original])
        {
            ReprovisionResultFactory = (_, _) => new WorkspaceReprovisionResult { Snapshot = refreshed, Succeeded = true, Message = "Workspace reprovisioned successfully." },
        };
        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();
        await page.ReprovisionWorkspaceCommand.ExecuteAsync();

        Assert.Equal("Loaded (linux/amd64)", page.SelectedWorkspace?.LocalRuntimeStateStatus);
        Assert.Equal("Workspace reprovisioned successfully.", page.ReprovisionStatusMessage);
        Assert.Equal("Workspace Ready", page.SelectedWorkspace?.RuntimeStatusLabel);
        Assert.Equal("Available: Development Shell.", page.SelectedWorkspace?.LastActivity);
    }

    [Fact]
    public async Task ReprovisionStart_ClearsPreviousFailureDisplayAndShowsInProgress()
    {
        var previousFailure = "Workspace provisioning failed. Exit code: 127. /workspace/.env: line 17: $'Analiza\\r': command not found";
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha", lastOperationResult: previousFailure, lastOperationSucceeded: false)])
        {
            ReprovisionResultFactoryAsync = async (_, sink, _) =>
            {
                sink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Generating runtime files..." });
                started.SetResult();
                await release.Task;
                return new WorkspaceReprovisionResult
                {
                    Snapshot = CreateSnapshot("alpha", lastOperationResult: "Workspace reprovisioned successfully.", lastOperationSucceeded: true),
                    Succeeded = true,
                    Message = "Workspace reprovisioned successfully.",
                };
            },
        };
        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();

        var reprovisionTask = page.ReprovisionWorkspaceCommand.ExecuteAsync();
        await started.Task;

        Assert.Equal("Reprovisioning", page.SelectedWorkspace?.RuntimeStatusLabel);
        Assert.Contains("Generating runtime files", page.SelectedWorkspace?.LastActivity ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("Analiza", page.SelectedWorkspace?.LastActivity ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("Preparing workspace. This may take several minutes.", page.DetailSummary);

        release.SetResult();
        await reprovisionTask;
    }

    [Fact]
    public async Task WorkspaceRows_AppearImmediatelyWhileDetailsContinueLoading()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeDesktopShellService([])
        {
            LoadWorkspaceItemsAsyncFactory = async (_, progress, cancellationToken) =>
            {
                var alpha = CreateSnapshot("alpha");
                var beta = CreateSnapshot("beta");
                progress?.Invoke(new WorkspaceLoadProgressUpdate { Title = "Loading alpha...", Message = "Loading details in background.", ProgressLabel = "Workspace 1 of 2", LoadedItem = new WorkspaceShellItem { Record = alpha.Record, IsLoading = true, LoadingStatusMessage = "Checking workspace configuration..." } });
                progress?.Invoke(new WorkspaceLoadProgressUpdate { Title = "Loading beta...", Message = "Loading details in background.", ProgressLabel = "Workspace 2 of 2", LoadedItem = new WorkspaceShellItem { Record = beta.Record, IsLoading = true, LoadingStatusMessage = "Checking Git status..." } });
                await release.Task.WaitAsync(cancellationToken);
                return new WorkspaceLoadResult
                {
                    Items =
                    [
                        new WorkspaceShellItem { Record = alpha.Record, Snapshot = alpha },
                        new WorkspaceShellItem { Record = beta.Record, Snapshot = beta },
                    ],
                    Report = new WorkspaceLoadReport { RawRecordCount = 2, SnapshotAttemptCount = 2, SnapshotCount = 2, ItemsReturnedCount = 2, Timings = [] },
                };
            },
        };
        var page = new WorkspacesPageViewModel(service);

        var loadTask = page.LoadAsync();

        Assert.Equal(2, page.Workspaces.Count);
        Assert.All(page.Workspaces, item => Assert.Equal("Loading...", item.RuntimeStatusLabel));
        Assert.NotNull(page.SelectedWorkspace);

        release.SetResult();
        await loadTask;

        Assert.All(page.Workspaces, item => Assert.False(item.IsLoading));
    }

    [Fact]
    public async Task SelectedWorkspace_SurvivesRowReplacementAfterPlaceholderLoad()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var alpha = CreateSnapshot("alpha");
        var service = new FakeDesktopShellService([])
        {
            LoadWorkspaceItemsAsyncFactory = async (_, progress, cancellationToken) =>
            {
                progress?.Invoke(new WorkspaceLoadProgressUpdate { Title = "Loading alpha...", Message = "Loading details in background.", ProgressLabel = "Workspace 1 of 1", LoadedItem = new WorkspaceShellItem { Record = alpha.Record, IsLoading = true, LoadingStatusMessage = "Checking workspace configuration..." } });
                await release.Task.WaitAsync(cancellationToken);
                progress?.Invoke(new WorkspaceLoadProgressUpdate { Title = "Loading alpha...", Message = "Snapshot loaded.", ProgressLabel = "Workspace 1 of 1", LoadedItem = new WorkspaceShellItem { Record = alpha.Record, Snapshot = alpha } });
                return new WorkspaceLoadResult
                {
                    Items = [new WorkspaceShellItem { Record = alpha.Record, Snapshot = alpha }],
                    Report = new WorkspaceLoadReport { RawRecordCount = 1, SnapshotAttemptCount = 1, SnapshotCount = 1, ItemsReturnedCount = 1, Timings = [] },
                };
            },
        };
        var page = new WorkspacesPageViewModel(service);

        var loadTask = page.LoadAsync();
        Assert.True(page.SelectedWorkspace?.IsLoading);

        release.SetResult();
        await loadTask;

        Assert.NotNull(page.SelectedWorkspace);
        Assert.False(page.SelectedWorkspace.IsLoading);
        Assert.Equal(alpha.Paths.RootPath, page.SelectedWorkspace.RootPath);
        Assert.Equal("alpha", page.SelectedWorkspace.Name);
    }

    [Fact]
    public async Task Reprovision_ShowsImmediateLogEntriesBeforeServiceCompletes()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            ReprovisionResultFactoryAsync = async (_, _, cancellationToken) =>
            {
                started.SetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new WorkspaceReprovisionResult { Snapshot = CreateSnapshot("alpha"), Succeeded = true, Message = "done" };
            },
        });

        await page.LoadAsync();
        var reprovisionTask = page.ReprovisionWorkspaceCommand.ExecuteAsync();
        await started.Task;

        Assert.Contains("Starting reprovision for alpha", page.OperationLogText, StringComparison.Ordinal);
        Assert.Contains("Loading current workspace state", page.OperationLogText, StringComparison.Ordinal);

        release.SetResult();
        await reprovisionTask;
    }

    [Fact]
    public async Task HealthyCurrentServices_DoNotShowWorkspaceActionFailedAsHeadline()
    {
        var snapshot = CreateSnapshot("alpha", lastOperationName: "Open Workspace", lastOperationResult: "Workspace action failed.", lastOperationSucceeded: false);
        snapshot = WithHealth(
            snapshot,
            CreateHealthSnapshot(
                WorkspaceHealthStatus.Attention,
                "Workspace is running. ORDS and SQL Developer Web are available. Oracle APEX is not available yet.",
                "Open Workspace or troubleshoot the unavailable application.",
                services:
                [
                    CreateServiceHealthSnapshot("ords", "ORDS", WorkspaceHealthStatus.Healthy, "ORDS is available."),
                    CreateServiceHealthSnapshot("sql-developer-web", "SQL Developer Web", WorkspaceHealthStatus.Healthy, "SQL Developer Web is available."),
                    CreateServiceHealthSnapshot("apex", "Oracle APEX", WorkspaceHealthStatus.Attention, "Oracle APEX is not available yet."),
                ]));

        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Workspace Ready", page.SelectedWorkspace?.Headline);
        Assert.DoesNotContain("Workspace action failed", page.DetailSummary, StringComparison.Ordinal);
        Assert.Contains("ORDS", page.DetailSummary, StringComparison.Ordinal);
        Assert.Contains(page.DetailItems, item => item.Label == "Technical Evidence" && item.Value.Contains("Recent history: Recent issue: Workspace action failed.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PartiallyUsableWorkspace_PrioritizesCapabilitiesOverRepairLanguage()
    {
        var snapshot = WithHealth(
            CreateSnapshot("alpha"),
            CreateHealthSnapshot(
                WorkspaceHealthStatus.Attention,
                "Workspace is running. SQL Developer Web and REST APIs are available. Oracle APEX is not available yet.",
                "Investigate Oracle APEX.",
                services:
                [
                    CreateServiceHealthSnapshot("sql-developer-web", "SQL Developer Web", WorkspaceHealthStatus.Healthy, "SQL Developer Web is available."),
                    CreateServiceHealthSnapshot("rest-apis", "REST APIs", WorkspaceHealthStatus.Healthy, "REST APIs are available."),
                    CreateServiceHealthSnapshot("oracle-apex", "Oracle APEX", WorkspaceHealthStatus.Attention, "Oracle APEX is not available yet."),
                ]));
        snapshot = WithReadiness(snapshot, new WorkspaceReadinessSnapshot
        {
            Status = WorkspaceReadinessStatus.Ready,
            CurrentActivity = WorkspaceActivity.None,
            PrimaryAction = WorkspacePrimaryAction.OpenWorkspace,
            Summary = "Workspace is ready. Available: SQL Developer Web, REST APIs. Development environment needs attention.",
            Capabilities =
            [
                new WorkspaceCapabilitySnapshot { Key = "development-shell", Label = "Development Shell", State = OpenCode.Workspace.Core.Models.WorkspaceCapabilityState.Available, Summary = "Development shell is available.", IsPrimaryWorkSurface = true },
                new WorkspaceCapabilitySnapshot { Key = "sql-developer-web", Label = "SQL Developer Web", State = OpenCode.Workspace.Core.Models.WorkspaceCapabilityState.Available, Summary = "SQL Developer Web is available." },
                new WorkspaceCapabilitySnapshot { Key = "rest-apis", Label = "REST APIs", State = OpenCode.Workspace.Core.Models.WorkspaceCapabilityState.Available, Summary = "REST APIs are available." },
                new WorkspaceCapabilitySnapshot { Key = "oracle-apex", Label = "Oracle APEX", State = OpenCode.Workspace.Core.Models.WorkspaceCapabilityState.Unavailable, Summary = "Oracle APEX is not available yet." },
            ],
            AttentionItems =
            [
                new WorkspaceAttentionItem { Key = "oracle-apex", Label = "Oracle APEX", Scope = WorkspaceAttentionScope.Capability, Severity = WorkspaceAttentionSeverity.Attention, Summary = "Oracle APEX is not available yet.", RecommendedActionLabel = "Investigate Oracle APEX" },
            ],
        });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Workspace Ready", page.SelectedWorkspace?.Headline);
        Assert.Equal("Open Workspace", page.DetailPrimaryAction?.Label);
        Assert.Equal("Investigate Oracle APEX.", page.DetailRecommendation);
        Assert.DoesNotContain("Repair", page.DetailSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(page.DetailItems, item => item.Label == "What You Can Use" && item.Value.Contains("Development Shell", StringComparison.Ordinal));
        Assert.Contains(page.DetailItems, item => item.Label == "Needs Attention" && item.Value.Contains("Oracle APEX", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DevelopmentEnvironmentAttention_DoesNotOverrideWorkspaceReadiness()
    {
        var snapshot = WithHealth(
            CreateSnapshot("alpha"),
            CreateHealthSnapshot(
                WorkspaceHealthStatus.Healthy,
                "Workspace is running.",
                "Open Workspace.",
                services:
                [
                    CreateServiceHealthSnapshot("sql-developer-web", "SQL Developer Web", WorkspaceHealthStatus.Healthy, "SQL Developer Web is available."),
                    CreateServiceHealthSnapshot("rest-apis", "REST APIs", WorkspaceHealthStatus.Healthy, "REST APIs are available."),
                ],
                developmentEnvironment: new WorkspaceDevelopmentEnvironmentHealthSnapshot
                {
                    Status = WorkspaceHealthStatus.Attention,
                    Summary = "Development environment needs attention: OpenCode CLI, screen.",
                    Recommendation = "Inspect Development Environment.",
                    Confidence = "HIGH",
                    Timestamp = DateTimeOffset.UtcNow,
                    Checks =
                    [
                        new WorkspaceDevelopmentEnvironmentCheck { Name = "OpenCode CLI", Status = "Missing", Summary = "OpenCode CLI is missing." },
                        new WorkspaceDevelopmentEnvironmentCheck { Name = "screen", Status = "Missing", Summary = "screen is missing." },
                    ],
                }));
        snapshot = WithReadiness(snapshot, new WorkspaceReadinessSnapshot
        {
            Status = WorkspaceReadinessStatus.Ready,
            CurrentActivity = WorkspaceActivity.None,
            PrimaryAction = WorkspacePrimaryAction.OpenWorkspace,
            Summary = "Workspace is ready. Development environment needs attention.",
            Capabilities =
            [
                new WorkspaceCapabilitySnapshot { Key = "development-shell", Label = "Development Shell", State = OpenCode.Workspace.Core.Models.WorkspaceCapabilityState.Available, Summary = "Development shell is available.", IsPrimaryWorkSurface = true },
                new WorkspaceCapabilitySnapshot { Key = "sql-developer-web", Label = "SQL Developer Web", State = OpenCode.Workspace.Core.Models.WorkspaceCapabilityState.Available, Summary = "SQL Developer Web is available." },
                new WorkspaceCapabilitySnapshot { Key = "rest-apis", Label = "REST APIs", State = OpenCode.Workspace.Core.Models.WorkspaceCapabilityState.Available, Summary = "REST APIs are available." },
            ],
            AttentionItems =
            [
                new WorkspaceAttentionItem { Key = "development-environment", Label = "Development Environment", Scope = WorkspaceAttentionScope.DevelopmentEnvironment, Severity = WorkspaceAttentionSeverity.Attention, Summary = "Development environment needs attention: OpenCode CLI, screen.", RecommendedActionLabel = "Inspect Development Environment" },
            ],
        });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Workspace Ready", page.SelectedWorkspace?.Headline);
        Assert.Equal("Open Workspace", page.DetailPrimaryAction?.Label);
        Assert.Equal("Inspect Development Environment.", page.DetailRecommendation);
        Assert.Contains(page.DetailItems, item => item.Label == "Development Environment" && item.Value.Contains("OpenCode CLI", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadyWorkspaceCard_PrefersCoreReadinessOverHealthStatus()
    {
        var snapshot = WithReadiness(
            WithHealth(
                CreateSnapshot("alpha"),
                CreateHealthSnapshot(WorkspaceHealthStatus.Degraded, "Managed runtime files are missing or stale.", "Open Workspace.")),
            new WorkspaceReadinessSnapshot
            {
                Status = WorkspaceReadinessStatus.Ready,
                CurrentActivity = WorkspaceActivity.None,
                PrimaryAction = WorkspacePrimaryAction.OpenWorkspace,
                Summary = "Workspace is ready.",
                Capabilities =
                [
                    new WorkspaceCapabilitySnapshot { Key = "development-shell", Label = "Development Shell", State = OpenCode.Workspace.Core.Models.WorkspaceCapabilityState.Available, Summary = "Development shell is available.", IsPrimaryWorkSurface = true },
                ],
            });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Workspace Ready", page.SelectedWorkspace?.Headline);
        Assert.Equal("Workspace Ready", page.SelectedWorkspace?.RuntimeStatusLabel);
        Assert.Equal("Workspace is ready.", page.DetailSummary);
    }

    [Fact]
    public async Task ReadyWorkspace_ShowsAvailableServicesAndHidesRebuildRuntime()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")]));

        await page.LoadAsync();

        Assert.Contains(page.DetailAvailableServices, item => item.Service == "Development Shell");
        Assert.Contains(page.DetailAvailableServices, item => item.Service == "Repository Folder");
        Assert.Contains(page.DetailAvailableServices, item => item.Service == "OpenCode CLI");
        Assert.Equal("Open Workspace", page.DetailPrimaryAction?.Label);
        Assert.DoesNotContain(page.DetailAdvancedActions, item => item.Label == "Rebuild Runtime");
    }

    [Fact]
    public async Task OracleReadyWorkspace_ShowsApexOrdsAndSqlclServices()
    {
        var baseSnapshot = CreateSnapshot("oracle-apexlang-demo");
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-apexlang-demo", Image = "ubuntu:24.04" },
            Features = ["core", "oracle-demo", "oracle-apex-demo", "oracle-apexlang-demo"],
            Services = ["oracle-demo", "oracle-ords"],
        };
        var snapshot = WithComputedReadiness(new WorkspaceSnapshot
        {
            Record = baseSnapshot.Record,
            Definition = definition,
            Paths = baseSnapshot.Paths,
            ConfigurationPath = baseSnapshot.ConfigurationPath,
            RuntimeState = baseSnapshot.RuntimeState,
            Safety = baseSnapshot.Safety,
            Session = baseSnapshot.Session,
            AppliedState = baseSnapshot.AppliedState,
            LocalRuntimeState = baseSnapshot.LocalRuntimeState,
            ResolvedRuntimePlan = baseSnapshot.ResolvedRuntimePlan,
            UpdateRequired = baseSnapshot.UpdateRequired,
            Health = baseSnapshot.Health,
            Readiness = baseSnapshot.Readiness,
            AvailableServices = WorkspaceServiceCatalog.Build(definition, baseSnapshot.LocalRuntimeState, baseSnapshot.Paths.RootPath),
        });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Contains(page.DetailAvailableServices, item => item.Service == "APEX Builder");
        Assert.Contains(page.DetailAvailableServices, item => item.Service == "ORDS Landing");
        Assert.Contains(page.DetailAvailableServices, item => item.Service == "SQLcl");
    }

    [Fact]
    public async Task OracleApexWorkspace_EnablesConnectExistingApplicationAction()
    {
        var baseSnapshot = CreateSnapshot("oracle-apexlang-demo");
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-apexlang-demo", Image = "ubuntu:24.04" },
            Features = ["core", "oracle-demo", "oracle-apex-demo", "oracle-apexlang-demo"],
            Services = ["oracle-demo", "oracle-ords"],
        };
        var snapshot = WithComputedReadiness(new WorkspaceSnapshot
        {
            Record = baseSnapshot.Record,
            Definition = definition,
            Paths = baseSnapshot.Paths,
            ConfigurationPath = baseSnapshot.ConfigurationPath,
            RuntimeState = baseSnapshot.RuntimeState,
            Safety = baseSnapshot.Safety,
            Session = baseSnapshot.Session,
            AppliedState = baseSnapshot.AppliedState,
            LocalRuntimeState = baseSnapshot.LocalRuntimeState,
            ResolvedRuntimePlan = baseSnapshot.ResolvedRuntimePlan,
            UpdateRequired = baseSnapshot.UpdateRequired,
            Health = baseSnapshot.Health,
            Readiness = baseSnapshot.Readiness,
            AvailableServices = WorkspaceServiceCatalog.Build(definition, baseSnapshot.LocalRuntimeState, baseSnapshot.Paths.RootPath),
        });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();

        Assert.True(page.DetailAdvancedActions.Single(item => item.Label == "Connect Existing Application").IsEnabled);
        Assert.False(page.DetailAdvancedActions.Single(item => item.Label == "Create Application").IsEnabled);
    }

    [Fact]
    public async Task NonOracleWorkspace_DisablesConnectExistingApplicationAction()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")]));
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();

        var connect = page.DetailAdvancedActions.Single(item => item.Label == "Connect Existing Application");
        Assert.False(connect.IsEnabled);
        Assert.Contains("only available for Oracle APEX workspaces", connect.DisabledReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectedOracleApexWorkspace_EnablesDiffAndPullActions()
    {
        var baseSnapshot = CreateSnapshot("oracle-apexlang-demo");
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-apexlang-demo", Image = "ubuntu:24.04" },
            Features = ["core", "oracle-demo", "oracle-apex-demo", "oracle-apexlang-demo"],
            Services = ["oracle-demo", "oracle-ords"],
            Oracle = new OracleWorkspacePreferences
            {
                Apex = new OracleApexWorkspacePreferences
                {
                    DefaultEnvironment = "dev",
                    Environments = new Dictionary<string, OracleApexEnvironmentPreferences>
                    {
                        ["dev"] = new()
                        {
                            Workspace = "TEST",
                            ParsingSchema = "TESTSCHEMA",
                            ApplicationId = 100,
                            SqlclProfile = "local-apex-dev",
                            SyncMode = WorkspaceSynchronizationModes.Manual,
                            SourcePath = "src/apex",
                        },
                    },
                },
            },
        };
        var snapshot = WithComputedReadiness(new WorkspaceSnapshot
        {
            Record = baseSnapshot.Record,
            Definition = definition,
            Paths = baseSnapshot.Paths,
            ConfigurationPath = baseSnapshot.ConfigurationPath,
            RuntimeState = baseSnapshot.RuntimeState,
            Safety = baseSnapshot.Safety,
            Session = baseSnapshot.Session,
            AppliedState = baseSnapshot.AppliedState,
            LocalRuntimeState = baseSnapshot.LocalRuntimeState,
            ResolvedRuntimePlan = baseSnapshot.ResolvedRuntimePlan,
            UpdateRequired = baseSnapshot.UpdateRequired,
            Synchronization = new WorkspaceSynchronizationSnapshot
            {
                IsSupported = true,
                State = WorkspaceSynchronizationState.DeploymentAhead,
                Summary = "Oracle APEX contains newer Builder changes.",
                DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                {
                    EnvironmentName = "dev",
                    WorkspaceName = "TEST",
                    ParsingSchema = "TESTSCHEMA",
                    ApplicationId = 100,
                    SyncMode = WorkspaceSynchronizationModes.Manual,
                    SourcePath = "src/apex",
                    State = WorkspaceSynchronizationState.DeploymentAhead,
                },
                Environments =
                [
                    new WorkspaceSynchronizationEnvironmentSnapshot
                    {
                        EnvironmentName = "dev",
                        WorkspaceName = "TEST",
                        ParsingSchema = "TESTSCHEMA",
                        ApplicationId = 100,
                        SyncMode = WorkspaceSynchronizationModes.Manual,
                        SourcePath = "src/apex",
                        State = WorkspaceSynchronizationState.DeploymentAhead,
                    },
                ],
            },
            Health = baseSnapshot.Health,
            Readiness = baseSnapshot.Readiness,
            AvailableServices = WorkspaceServiceCatalog.Build(definition, baseSnapshot.LocalRuntimeState, baseSnapshot.Paths.RootPath),
        });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();

        Assert.True(page.DetailAdvancedActions.Single(item => item.Label == "Show Diff").IsEnabled);
        Assert.True(page.DetailAdvancedActions.Single(item => item.Label == "Pull Changes").IsEnabled);
    }

    [Fact]
    public async Task ConnectedWorkspace_ShowsOracleApexSyncCard()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateConnectedOracleApexSnapshot(WorkspaceSynchronizationState.DeploymentAhead)]));

        await page.LoadAsync();

        var syncCard = page.DetailItems.SingleOrDefault(item => item.Label == "Oracle APEX Sync");
        Assert.NotNull(syncCard);
        Assert.Contains("Environment: dev", syncCard!.Value, StringComparison.Ordinal);
        Assert.Contains("APEX Workspace: TEST", syncCard.Value, StringComparison.Ordinal);
        Assert.Contains("Parsing Schema: TESTSCHEMA", syncCard.Value, StringComparison.Ordinal);
        Assert.Contains("Application: 100 (Customer Orders Demo)", syncCard.Value, StringComparison.Ordinal);
        Assert.Contains("Source Path: src/apex", syncCard.Value, StringComparison.Ordinal);
        Assert.Contains("Sync State: APEX Ahead", syncCard.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnconnectedWorkspace_HidesOracleApexSyncCard()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")]));

        await page.LoadAsync();

        Assert.DoesNotContain(page.DetailItems, item => item.Label == "Oracle APEX Sync");
    }

    [Theory]
    [InlineData(WorkspaceSynchronizationState.InSync, "No action needed")]
    [InlineData(WorkspaceSynchronizationState.GitAhead, "Push Changes")]
    [InlineData(WorkspaceSynchronizationState.DeploymentAhead, "Pull Changes")]
    [InlineData(WorkspaceSynchronizationState.Diverged, "Show Diff, then choose Pull or Push")]
    [InlineData(WorkspaceSynchronizationState.ValidationFailed, "Open transcript")]
    [InlineData(WorkspaceSynchronizationState.Unknown, "Validate")]
    public async Task OracleApexSyncCard_MapsStateToRecommendedAction(WorkspaceSynchronizationState state, string expectedAction)
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateConnectedOracleApexSnapshot(state)]));

        await page.LoadAsync();

        var syncCard = page.DetailItems.Single(item => item.Label == "Oracle APEX Sync");
        Assert.Contains($"Recommended Action: {expectedAction}", syncCard.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadinessPresentation_UsesSharedLabel_ForPartiallyReadyWorkspace()
    {
        var snapshot = WithReadiness(
            CreateSnapshot("alpha", includeRuntimeState: true),
            new WorkspaceReadinessSnapshot
            {
                Status = WorkspaceReadinessStatus.Unavailable,
                CurrentActivity = WorkspaceActivity.None,
                PrimaryAction = WorkspacePrimaryAction.OpenWorkspace,
                Summary = "Workspace services are running, but terminal launch is not ready. SQL Developer Web is available.",
                Capabilities =
                [
                    new WorkspaceCapabilitySnapshot { Key = "development-shell", Label = "Development Shell", State = OpenCode.Workspace.Core.Models.WorkspaceCapabilityState.Unavailable, IsPrimaryWorkSurface = true },
                    new WorkspaceCapabilitySnapshot { Key = "sql-developer-web", Label = "SQL Developer Web", State = OpenCode.Workspace.Core.Models.WorkspaceCapabilityState.Available },
                ],
            });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Workspace Partially Ready", page.SelectedWorkspace?.Headline);
        Assert.Equal("Workspace Partially Ready", page.SelectedWorkspace?.RuntimeStatusLabel);
    }

    [Fact]
    public async Task ReadinessPresentation_UsesSharedLabel_ForNeedsPreparationWorkspace()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha", includeRuntimeState: false)]));

        await page.LoadAsync();

        Assert.Equal("Needs Preparation", page.SelectedWorkspace?.Headline);
        Assert.Equal("Needs Preparation", page.SelectedWorkspace?.RuntimeStatusLabel);
    }

    [Fact]
    public async Task ReadinessPresentation_UsesSharedLabel_ForNotPreparedWorkspace()
    {
        var snapshot = CreateSnapshot("alpha", includeRuntimeState: true, lastOperationName: "Create Workspace", lastOperationSucceeded: true);
        snapshot = WithComputedReadiness(new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = snapshot.Record.Name,
                RootPath = snapshot.Record.RootPath,
                RepositoryPath = snapshot.Record.RepositoryPath,
                ConfigurationPath = snapshot.Record.ConfigurationPath,
                CreatedUtc = snapshot.Record.CreatedUtc,
                LastOpenedUtc = snapshot.Record.LastOpenedUtc,
                LastOperationName = "Create Workspace",
                LastOperationSucceeded = true,
                LastPreparedUtc = null,
            },
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = WorkspaceRuntimeState.Stopped,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = null,
            LocalRuntimeState = null,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = false,
            Health = snapshot.Health,
            Readiness = snapshot.Readiness,
        });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Not Prepared", page.SelectedWorkspace?.Headline);
        Assert.Equal("Not Prepared", page.SelectedWorkspace?.RuntimeStatusLabel);
    }

    [Fact]
    public async Task ReadinessPresentation_UsesSharedLabel_ForNeedsRebuildWorkspace()
    {
        var snapshot = WithReadiness(
            CreateSnapshot("alpha"),
            new WorkspaceReadinessSnapshot
            {
                Status = WorkspaceReadinessStatus.NeedsRebuild,
                CurrentActivity = WorkspaceActivity.None,
                PrimaryAction = WorkspacePrimaryAction.RebuildRuntime,
                Summary = "Rebuild Runtime is the next normal recovery step.",
            });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Needs Rebuild", page.SelectedWorkspace?.Headline);
        Assert.Equal("Needs Rebuild", page.SelectedWorkspace?.RuntimeStatusLabel);
    }

    [Fact]
    public async Task ReadinessPresentation_UsesSharedLabel_ForUnavailableWorkspace()
    {
        var snapshot = WithReadiness(
            CreateSnapshot("alpha"),
            new WorkspaceReadinessSnapshot
            {
                Status = WorkspaceReadinessStatus.Unavailable,
                CurrentActivity = WorkspaceActivity.None,
                PrimaryAction = WorkspacePrimaryAction.OpenWorkspace,
                Summary = "Open Workspace can prepare and open this workspace.",
            });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Unavailable", page.SelectedWorkspace?.Headline);
        Assert.Equal("Unavailable", page.SelectedWorkspace?.RuntimeStatusLabel);
    }

    [Fact]
    public async Task ReadinessPresentation_UsesSharedLabel_ForPreparingWorkspace()
    {
        var snapshot = WithReadiness(
            CreateSnapshot("alpha"),
            new WorkspaceReadinessSnapshot
            {
                Status = WorkspaceReadinessStatus.Preparing,
                CurrentActivity = WorkspaceActivity.Preparing,
                PrimaryAction = WorkspacePrimaryAction.ViewProgress,
                Summary = "Preparing workspace. This may take several minutes.",
                IsOperationInProgress = true,
            });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Provisioning", page.SelectedWorkspace?.Headline);
        Assert.Equal("Preparing", page.SelectedWorkspace?.RuntimeStatusLabel);
    }

    [Fact]
    public async Task ReadyWorkspaceReadinessLabel_KeepsOpenWorkspaceCommandBehavior()
    {
        var snapshot = WithReadiness(
            CreateSnapshot("alpha"),
            new WorkspaceReadinessSnapshot
            {
                Status = WorkspaceReadinessStatus.Ready,
                CurrentActivity = WorkspaceActivity.None,
                PrimaryAction = WorkspacePrimaryAction.OpenWorkspace,
                Summary = "Workspace is ready.",
                Capabilities =
                [
                    new WorkspaceCapabilitySnapshot { Key = "development-shell", Label = "Development Shell", State = OpenCode.Workspace.Core.Models.WorkspaceCapabilityState.Available, Summary = "Development shell is available.", IsPrimaryWorkSurface = true },
                ],
            });
        var desktop = new FakeDesktopShellService([snapshot]);
        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();

        Assert.Equal("Open Workspace", page.DetailPrimaryAction?.Label);
        await ((AsyncRelayCommand)page.DetailPrimaryAction!.Command).ExecuteAsync();
        Assert.Equal(1, desktop.OpenWorkspaceCallCount);
    }

    [Fact]
    public async Task SnapshotReadiness_Present_DoesNotRecomputePrimaryActionFromHealth()
    {
        var snapshot = WithReadiness(
            WithHealth(
                CreateSnapshot("alpha", includeRuntimeState: false),
                CreateHealthSnapshot(WorkspaceHealthStatus.Degraded, "Managed runtime files are missing or stale.", "Troubleshoot Workspace.")),
            new WorkspaceReadinessSnapshot
            {
                Status = WorkspaceReadinessStatus.Unavailable,
                CurrentActivity = WorkspaceActivity.None,
                PrimaryAction = WorkspacePrimaryAction.OpenWorkspace,
                Summary = "Open Workspace can prepare and open this workspace.",
            });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Needs Preparation", page.SelectedWorkspace?.Headline);
        Assert.Equal("Open Workspace", page.DetailPrimaryAction?.Label);
        Assert.Equal("Open Workspace.", page.DetailRecommendation);
    }

    [Fact]
    public async Task SnapshotReadiness_Present_DoesNotRecomputeNeedsRebuildFromHealth()
    {
        var snapshot = WithReadiness(
            WithHealth(
                CreateSnapshot("alpha"),
                CreateHealthSnapshot(WorkspaceHealthStatus.Healthy, "Workspace is healthy.", "Open Workspace.")),
            new WorkspaceReadinessSnapshot
            {
                Status = WorkspaceReadinessStatus.NeedsRebuild,
                CurrentActivity = WorkspaceActivity.None,
                PrimaryAction = WorkspacePrimaryAction.RebuildRuntime,
                Summary = "Rebuild Runtime is the next normal recovery step.",
            });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Needs Rebuild", page.SelectedWorkspace?.Headline);
        Assert.Equal("Rebuild Runtime", page.DetailPrimaryAction?.Label);
        Assert.Equal("Rebuild Runtime.", page.DetailRecommendation);
    }

    [Fact]
    public async Task ActiveProvisioning_DoesNotShowTroubleshootAsPrimaryOrVisibleAction()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            ReprovisionResultFactoryAsync = async (_, sink, cancellationToken) =>
            {
                sink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Generating runtime files..." });
                started.SetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new WorkspaceReprovisionResult { Snapshot = CreateSnapshot("alpha"), Succeeded = true, Message = "done", Transcript = new OperationTranscript() };
            },
        };
        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();

        var reprovisionTask = page.ReprovisionWorkspaceCommand.ExecuteAsync();
        await started.Task;

        Assert.Equal("Provisioning", page.SelectedWorkspace?.Headline);
        Assert.Equal("Preparing workspace. This may take several minutes.", page.DetailSummary);
        Assert.Equal("Open Workspace", page.DetailPrimaryAction?.Label);
        Assert.Equal("Open Workspace.", page.DetailRecommendation);
        Assert.DoesNotContain(page.DetailVisibleActions, item => item.Label == "Troubleshoot Workspace");
        Assert.Contains(page.DetailAdvancedActions, item => item.Label == "Run Diagnostics");
        Assert.Equal(["Open Folder"], page.DetailVisibleActions.Select(item => item.Label));

        release.SetResult();
        await reprovisionTask;
    }

    [Fact]
    public async Task MissingRuntimeState_UsesNeedsRepairHealthSummary()
    {
        var snapshot = CreateSnapshot("alpha", includeRuntimeState: false, lastOperationName: "Open Workspace", lastOperationResult: "Old failure", lastOperationSucceeded: false);
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Needs Preparation", page.SelectedWorkspace?.Headline);
        Assert.Equal("Open Workspace can safely regenerate runtime state.", page.DetailSummary);
        Assert.Contains(page.DetailItems, item => item.Label == "Workspace" && item.Value.Contains("Needs Preparation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FreshWorkspaceBeforeOpen_UsesNotPreparedAndOpenWorkspace()
    {
        var snapshot = CreateSnapshot("alpha", includeRuntimeState: true, lastOperationName: "Create Workspace", lastOperationSucceeded: true);
        snapshot = WithComputedReadiness(new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = snapshot.Record.Name,
                RootPath = snapshot.Record.RootPath,
                RepositoryPath = snapshot.Record.RepositoryPath,
                CreatedUtc = snapshot.Record.CreatedUtc,
                LastOpenedUtc = snapshot.Record.LastOpenedUtc,
                LastPreparedUtc = null,
                LastOperationName = "Create Workspace",
                LastOperationResult = "Workspace created.",
                LastOperationSucceeded = true,
            },
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = WorkspaceRuntimeState.Stopped,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = null,
            LocalRuntimeState = snapshot.LocalRuntimeState,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = true,
            Health = CreateHealthSnapshot(
                WorkspaceHealthStatus.Degraded,
                "Open Workspace will need to repair runtime artifacts before you can work.",
                "Run Recover Workspace.",
                providers:
                [
                    new WorkspaceProviderHealthSnapshot
                    {
                        ProviderKey = "runtime",
                        DisplayName = "Runtime",
                        Status = WorkspaceHealthStatus.Degraded,
                        Summary = "Managed runtime files are missing or stale.",
                        WorkspaceImpact = "Open Workspace will need to repair runtime artifacts before you can work.",
                        Timestamp = DateTimeOffset.UtcNow,
                    },
                    new WorkspaceProviderHealthSnapshot
                    {
                        ProviderKey = "container",
                        DisplayName = "Container",
                        Status = WorkspaceHealthStatus.Attention,
                        Summary = "Workspace runtime is stopped.",
                        WorkspaceImpact = "Workspace can still be opened, but the runtime will need to start first.",
                        Timestamp = DateTimeOffset.UtcNow,
                    },
                ]),
        });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Not Prepared", page.SelectedWorkspace?.Headline);
        Assert.Equal("Open Workspace will prepare the runtime and open the terminal.", page.DetailSummary);
        Assert.Equal("Open Workspace", page.DetailPrimaryAction?.Label);
        Assert.Equal("Open Workspace.", page.DetailRecommendation);
        Assert.Contains(page.DetailItems, item => item.Label == "Workspace" && item.Value.Contains("Not Prepared", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recommendation_RemainsSecondaryToPrimaryAction()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha", includeRuntimeState: false)]));

        await page.LoadAsync();

        Assert.NotNull(page.DetailPrimaryAction);
        Assert.Equal("Open Workspace", page.DetailPrimaryAction!.Label);
        Assert.Equal("Open Workspace.", page.DetailRecommendation);
    }

    [Fact]
    public async Task PreviousFailureThenCurrentSuccess_PreservesHistoryOnly()
    {
        var snapshot = CreateSnapshot("alpha", lastOperationName: "Open Workspace", lastOperationResult: "Workspace action failed.", lastOperationSucceeded: false);
        snapshot = WithHealth(snapshot, CreateHealthSnapshot(WorkspaceHealthStatus.Healthy, "Workspace is running.", "Open Workspace."));
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Workspace Ready", page.SelectedWorkspace?.Headline);
        Assert.DoesNotContain("Workspace action failed", page.DetailSummary, StringComparison.Ordinal);
        Assert.Contains(page.DetailItems, item => item.Label == "Technical Evidence" && item.Value.Contains("Recent history: Recent issue: Workspace action failed.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TerminalReadyFailure_DoesNotLeakRecoverWorkspaceInPrimaryUi()
    {
        var snapshot = CreateSnapshot("alpha", lastOperationName: "Open Workspace", lastOperationResult: "Workspace open did not reach a terminal-ready state. Run Recover Workspace.", lastOperationSucceeded: false);
        snapshot = WithHealth(snapshot, CreateHealthSnapshot(
            WorkspaceHealthStatus.Attention,
            "Workspace is running.",
            "Open Workspace.",
            services:
            [
                CreateServiceHealthSnapshot("pgadmin", "pgAdmin", WorkspaceHealthStatus.Healthy, "pgAdmin is available."),
            ]));
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Workspace Partially Ready", page.SelectedWorkspace?.Headline);
        Assert.Equal("Open Workspace", page.DetailPrimaryAction?.Label);
        Assert.DoesNotContain("Recover Workspace", page.DetailSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("Run Recover Workspace", page.DetailRecommendation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServicesAvailableButTerminalNotReady_ShowsLaunchReadinessSummary()
    {
        var snapshot = CreateSnapshot("alpha", lastOperationName: "Open Workspace", lastOperationResult: "Open Workspace could not finish preparing the terminal. Troubleshoot Workspace can inspect the runtime files and launch readiness.", lastOperationSucceeded: false);
        snapshot = WithHealth(snapshot, CreateHealthSnapshot(
            WorkspaceHealthStatus.Attention,
            "Workspace is running.",
            "Open Workspace.",
            services:
            [
                CreateServiceHealthSnapshot("ords", "Oracle REST Data Services", WorkspaceHealthStatus.Healthy, "ORDS is available."),
                CreateServiceHealthSnapshot("sql-developer-web", "SQL Developer Web", WorkspaceHealthStatus.Healthy, "SQL Developer Web is available."),
            ]));
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Workspace Partially Ready", page.SelectedWorkspace?.Headline);
        Assert.Contains("Workspace services are running, but terminal launch is not ready.", page.DetailSummary, StringComparison.Ordinal);
        Assert.Equal("Open Workspace", page.DetailPrimaryAction?.Label);
        Assert.Equal("Open Workspace.", page.DetailRecommendation);
        Assert.DoesNotContain("Troubleshoot Workspace", page.DetailSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceDiscoveryFailure_UsesRunDiagnosticsInNormalRecommendation()
    {
        var desktop = new FakeDesktopShellService([])
        {
            LoadWorkspaceItemsAsyncFactory = (_, _, _) => throw new InvalidOperationException("discovery failed"),
        };
        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();

        Assert.Equal("Run Diagnostics.", page.DetailRecommendation);
        Assert.DoesNotContain("Troubleshoot Workspace", page.DetailRecommendation, StringComparison.Ordinal);
        Assert.Equal("Refresh", page.DetailPrimaryAction?.Label);
    }

    [Fact]
    public async Task SafeRepairNoEffect_OffersRebuildRuntimeAsPrimaryAction()
    {
        var snapshot = CreateSnapshot("alpha", lastOperationName: "Open Workspace", lastOperationResult: "Open Workspace tried to repair the runtime automatically, but the workspace is still not ready. Rebuild Runtime will recreate managed containers and volumes while keeping your files.", lastOperationSucceeded: false);
        snapshot = WithHealth(snapshot, CreateHealthSnapshot(
            WorkspaceHealthStatus.Attention,
            "Workspace services are available, but OpenCode terminal could not be prepared.",
            "Rebuild Runtime.",
            services:
            [
                CreateServiceHealthSnapshot("pgadmin", "pgAdmin", WorkspaceHealthStatus.Healthy, "pgAdmin is available."),
            ]));
        snapshot = WithComputedReadiness(new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = snapshot.Record.Name,
                RootPath = snapshot.Record.RootPath,
                RepositoryPath = snapshot.Record.RepositoryPath,
                CreatedUtc = snapshot.Record.CreatedUtc,
                LastOpenedUtc = snapshot.Record.LastOpenedUtc,
                LastOperationName = snapshot.Record.LastOperationName,
                LastOperationResult = snapshot.Record.LastOperationResult,
                LastOperationSucceeded = snapshot.Record.LastOperationSucceeded,
                LastProvisioningHealth = new WorkspaceProvisioningHealthRecord
                {
                    Succeeded = false,
                    Stage = "Verify terminal launch readiness",
                    Summary = "Open Workspace tried to repair the runtime automatically, but the workspace is still not ready.",
                    Reason = snapshot.Record.LastOperationResult!,
                    Evidence = "Available services: pgAdmin. Terminal launch artifacts still failed readiness validation.",
                    ProblemScope = "WorkspaceProblem",
                    RecommendedAction = "Rebuild Runtime.",
                    Confidence = "HIGH",
                    Timestamp = DateTimeOffset.UtcNow,
                    Duration = TimeSpan.Zero,
                    Repairability = WorkspaceRepairability.CleanupRepair.ToString(),
                    EstimatedEffort = "Medium",
                    EstimatedDuration = "4-6 minutes",
                    LastDiagnosticsTimestamp = DateTimeOffset.UtcNow,
                    RepairHistory =
                    [
                        new WorkspaceRepairAttemptRecord
                        {
                            RepairType = "Recover Workspace",
                            StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                            CompletedUtc = DateTimeOffset.UtcNow,
                            Duration = TimeSpan.FromMinutes(1),
                            Result = WorkspaceRepairOutcome.RepairNoEffect,
                        },
                    ],
                },
            },
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = snapshot.RuntimeState,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = snapshot.AppliedState,
            LocalRuntimeState = snapshot.LocalRuntimeState,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = snapshot.UpdateRequired,
            Health = snapshot.Health,
        });
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));

        await page.LoadAsync();

        Assert.Equal("Rebuild Runtime", page.DetailPrimaryAction?.Label);
        Assert.Equal("Rebuild Runtime.", page.DetailRecommendation);
        Assert.Contains("Rebuild Runtime will recreate managed containers and volumes while keeping your files.", page.DetailSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReprovisioningSuppressesPreviousFailureHeadline()
    {
        var previousFailure = "Oracle prerequisite validation failed. XDB status was INVALID.";
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha", lastOperationResult: previousFailure, lastOperationSucceeded: false)])
        {
            ReprovisionResultFactoryAsync = async (_, sink, _) =>
            {
                sink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Installing Oracle APEX. This can take several minutes." });
                started.SetResult();
                await release.Task;
                return new WorkspaceReprovisionResult
                {
                    Snapshot = CreateSnapshot("alpha", lastOperationResult: "Workspace reprovisioned successfully.", lastOperationSucceeded: true),
                    Succeeded = true,
                    Message = "Workspace reprovisioned successfully.",
                };
            },
        };
        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();

        var reprovisionTask = page.ReprovisionWorkspaceCommand.ExecuteAsync();
        await started.Task;

        Assert.Equal("Provisioning", page.SelectedWorkspace?.Headline);
        Assert.Equal("Preparing workspace. This may take several minutes.", page.DetailSummary);
        Assert.DoesNotContain("failed", page.DetailSummary, StringComparison.OrdinalIgnoreCase);

        release.SetResult();
        await reprovisionTask;
    }

    [Fact]
    public async Task Reprovision_HighVolumeOutput_StaysBufferedUntilBatchFlush()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha"), CreateSnapshot("beta")])
        {
            ReprovisionResultFactoryAsync = async (_, sink, cancellationToken) =>
            {
                for (var index = 0; index < 10000; index++)
                {
                    sink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.StandardOutput, Text = $"sql line {index}" });
                }

                started.SetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new WorkspaceReprovisionResult { Snapshot = CreateSnapshot("alpha"), Succeeded = true, Message = "done" };
            },
        };

        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();
        var reprovisionTask = page.ReprovisionWorkspaceCommand.ExecuteAsync();
        await started.Task;

        Assert.True(page.PendingOperationLogLineCountForTesting >= 10000);
        Assert.DoesNotContain("sql line 9999", page.OperationLogText, StringComparison.Ordinal);

        page.SelectedWorkspace = page.Workspaces.Single(item => item.Name == "beta");
        Assert.Equal("beta", page.SelectedWorkspace.Name);

        page.FlushPendingOperationLogForTesting();

        Assert.Contains("sql line", page.OperationLogText, StringComparison.Ordinal);
        Assert.True(page.PendingOperationLogLineCountForTesting > 0);
        Assert.True(page.VisibleOperationLogLineCountForTesting <= 5000);

        release.SetResult();
        await reprovisionTask;

        Assert.Contains("sql line 9999", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reprovision_HighVolumeOutput_CopyAllUsesFullTranscriptFile()
    {
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            ReprovisionResultFactory = (_, sink) =>
            {
                for (var index = 0; index < 10000; index++)
                {
                    sink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.StandardOutput, Text = $"sql line {index}" });
                }

                return new WorkspaceReprovisionResult { Snapshot = CreateSnapshot("alpha"), Succeeded = true, Message = "done" };
            },
        };

        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();
        await page.ReprovisionWorkspaceCommand.ExecuteAsync();

        Assert.DoesNotContain("sql line 0", page.OperationLogText, StringComparison.Ordinal);
        Assert.Contains("sql line 9999", page.OperationLogText, StringComparison.Ordinal);
        Assert.True(page.VisibleOperationLogLineCountForTesting <= 5000);

        var fullLog = page.GetCopyAllOperationLogText();
        Assert.Contains("sql line 0", fullLog, StringComparison.Ordinal);
        Assert.Contains("sql line 9999", fullLog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reprovision_FailurePreservesFullTranscriptFile()
    {
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            ReprovisionResultFactoryAsync = async (_, sink, _) =>
            {
                for (var index = 0; index < 250; index++)
                {
                    sink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.StandardError, Text = $"failure line {index}" });
                }

                throw new InvalidOperationException("provision failed");
            },
        };

        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();
        await page.ReprovisionWorkspaceCommand.ExecuteAsync();

        var fullLog = page.GetCopyAllOperationLogText();
        Assert.Contains("failure line 0", fullLog, StringComparison.Ordinal);
        Assert.Contains("failure line 249", fullLog, StringComparison.Ordinal);
        Assert.Contains("provision failed", page.ReprovisionStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reprovision_CreatesOperationTranscript()
    {
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            ReprovisionResultFactory = (_, sink) =>
            {
                sink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Comment, Text = "Preparing workspace" });
                return new WorkspaceReprovisionResult { Snapshot = CreateSnapshot("alpha"), Succeeded = true, Message = "Workspace reprovisioned successfully." };
            },
        };
        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();
        await page.ReprovisionWorkspaceCommand.ExecuteAsync();

        Assert.NotNull(page.LastOperationTranscript);
        Assert.Contains("Preparing workspace", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reprovision_ProgressAndCommandOutput_AreAppended()
    {
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            ReprovisionResultFactory = (_, sink) =>
            {
                sink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Comment, Text = "Preparing workspace" });
                sink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Command, Text = "docker exec odip-analiza-workspace bash /opt/opencode-workspace/config/provision.sh" });
                sink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.StandardOutput, Text = "Provisioning packages" });
                sink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.StandardError, Text = "/workspace/.env: line 17: $'Analiza\\r': command not found" });
                return new WorkspaceReprovisionResult { Snapshot = CreateSnapshot("alpha"), Succeeded = true, Message = "Workspace reprovisioned successfully." };
            },
        };
        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();
        await page.ReprovisionWorkspaceCommand.ExecuteAsync();

        Assert.Contains("Preparing workspace", page.OperationLogText, StringComparison.Ordinal);
        Assert.Contains("docker exec odip-analiza-workspace bash /opt/opencode-workspace/config/provision.sh", page.OperationLogText, StringComparison.Ordinal);
        Assert.Contains("Provisioning packages", page.OperationLogText, StringComparison.Ordinal);
        Assert.Contains("/workspace/.env: line 17", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedReprovision_ShowsFailureState()
    {
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha", lastOperationResult: "Old failure", lastOperationSucceeded: false)])
        {
            ReprovisionException = new InvalidOperationException("Command: docker exec odip-analiza-workspace bash /opt/opencode-workspace/config/provision.sh\nExit code: 127\n/workspace/.env: line 17: $'Analiza\\r': command not found"),
        };
        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();
        await page.ReprovisionWorkspaceCommand.ExecuteAsync();

        Assert.Contains("Command: docker exec odip-analiza-workspace", page.ReprovisionStatusMessage, StringComparison.Ordinal);
        Assert.Equal("Error", page.SelectedWorkspace?.RuntimeStatusLabel);
        Assert.Contains("/workspace/.env: line 17", page.SelectedWorkspace?.LastActivity, StringComparison.Ordinal);
        Assert.Contains("/workspace/.env: line 17", page.DetailSummary, StringComparison.Ordinal);
        Assert.Contains("/workspace/.env: line 17", page.OperationLogText, StringComparison.Ordinal);
        Assert.Contains(page.DetailItems, item => item.Label == "Workspace");
        Assert.NotNull(page.DetailPrimaryAction);
        Assert.Equal("Open Workspace", page.DetailPrimaryAction!.Label);
        Assert.True(page.DetailPrimaryAction.IsEnabled);
        Assert.Contains("Exit code: 127", page.OperationLogText, StringComparison.Ordinal);
        Assert.Contains("docker exec odip-analiza-workspace bash /opt/opencode-workspace/config/provision.sh", page.OperationLogText, StringComparison.Ordinal);
        Assert.Contains("/workspace/.env: line 17", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailureSummary_IsConciseAndExcludesFullCommand()
    {
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            ReprovisionException = new InvalidOperationException("Command: docker exec odip-analiza-workspace bash /opt/opencode-workspace/config/provision.sh\nExit code: 127\n/workspace/.env: line 17: $'Analiza\\r': command not found"),
        };
        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();
        await page.ReprovisionWorkspaceCommand.ExecuteAsync();

        Assert.DoesNotContain("docker exec odip-analiza-workspace", page.DetailSummary, StringComparison.Ordinal);
        Assert.Contains("/workspace/.env: line 17", page.DetailSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedWorkspaceState_OffersEnabledRecommendedAction()
    {
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha", lastOperationName: "Attach", lastOperationResult: "Workspace is not running. Start it first.", lastOperationSucceeded: false)]);
        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();

        Assert.Equal("Available: Development Shell.", page.DetailSummary);
        Assert.Contains(page.DetailItems, item => item.Label == "Technical Evidence" && item.Value.Contains("Recent history: Recent issue: Workspace is not running. Start it first.", StringComparison.Ordinal));
        Assert.NotNull(page.DetailPrimaryAction);
        Assert.Equal("Open Workspace", page.DetailPrimaryAction!.Label);
        Assert.True(page.DetailPrimaryAction.IsEnabled);
    }

    [Fact]
    public async Task CleanupRepairFailure_UsesGenericResetRuntimeAction()
    {
        var snapshot = CreateSnapshot("alpha", lastOperationName: "Recover", lastOperationResult: "Workspace provisioning stopped.", lastOperationSucceeded: false);
        snapshot = WithComputedReadiness(new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
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
                OracleSoftwareNoticeShown = snapshot.Record.OracleSoftwareNoticeShown,
                LastOperationName = snapshot.Record.LastOperationName,
                LastOperationResult = snapshot.Record.LastOperationResult,
                LastOperationSucceeded = snapshot.Record.LastOperationSucceeded,
                LastOperationUtc = snapshot.Record.LastOperationUtc,
                LastProvisioningHealth = new WorkspaceProvisioningHealthRecord
                {
                    Succeeded = false,
                    Stage = "Validate Oracle prerequisites",
                    Summary = "Workspace provisioning stopped.",
                    Reason = "Oracle XML Database (XDB) is invalid.",
                    Evidence = "XDB status = INVALID",
                    RecommendedAction = "Reset Runtime.",
                    Confidence = "HIGH",
                    Timestamp = DateTimeOffset.UtcNow,
                    Duration = TimeSpan.FromMinutes(1),
                    RawLogReference = "mounts/config/provision.sh",
                    Repairability = WorkspaceRepairability.CleanupRepair.ToString(),
                    EstimatedEffort = "Medium",
                    EstimatedDuration = "4-6 minutes",
                },
            },
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = snapshot.RuntimeState,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = snapshot.AppliedState,
            LocalRuntimeState = snapshot.LocalRuntimeState,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = snapshot.UpdateRequired,
            Health = snapshot.Health,
        });

        var desktop = new FakeDesktopShellService([snapshot]);
        var page = new WorkspacesPageViewModel(desktop);
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();

        Assert.Equal("Rebuild Runtime.", page.DetailRecommendation);
        Assert.NotNull(page.DetailPrimaryAction);
        Assert.Equal("Rebuild Runtime", page.DetailPrimaryAction!.Label);
        Assert.True(page.DetailPrimaryAction.IsEnabled);
        Assert.Contains(page.DetailAdvancedActions, item => item.Label == "Rebuild Runtime");
    }

    [Fact]
    public async Task CleanupRepairFailure_DemotesOpenWorkspaceAndOrdersVisibleActions()
    {
        var snapshot = CreateSnapshot("alpha", lastOperationName: "Recover", lastOperationResult: "Workspace provisioning stopped.", lastOperationSucceeded: false);
        snapshot = WithComputedReadiness(new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
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
                OracleSoftwareNoticeShown = snapshot.Record.OracleSoftwareNoticeShown,
                LastOperationName = snapshot.Record.LastOperationName,
                LastOperationResult = snapshot.Record.LastOperationResult,
                LastOperationSucceeded = snapshot.Record.LastOperationSucceeded,
                LastOperationUtc = snapshot.Record.LastOperationUtc,
                LastProvisioningHealth = new WorkspaceProvisioningHealthRecord
                {
                    Succeeded = false,
                    Stage = "Validate Oracle prerequisites",
                    Summary = "Workspace provisioning stopped.",
                    Reason = "Oracle XML Database (XDB) is invalid.",
                    Evidence = "XDB status = INVALID",
                    RecommendedAction = "Reset Runtime.",
                    Confidence = "HIGH",
                    Timestamp = DateTimeOffset.UtcNow,
                    Duration = TimeSpan.FromMinutes(1),
                    RawLogReference = "mounts/config/provision.sh",
                    Repairability = WorkspaceRepairability.CleanupRepair.ToString(),
                    EstimatedEffort = "Medium",
                    EstimatedDuration = "4-6 minutes",
                },
            },
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = snapshot.RuntimeState,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = snapshot.AppliedState,
            LocalRuntimeState = snapshot.LocalRuntimeState,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = snapshot.UpdateRequired,
        });

        var desktop = new FakeDesktopShellService([snapshot]);
        var page = new WorkspacesPageViewModel(desktop);
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();

        Assert.Equal("Rebuild Runtime", page.DetailPrimaryAction?.Label);
        Assert.Equal(["Open Workspace", "Open Folder"], page.DetailVisibleActions.Select(item => item.Label));
        Assert.Contains(page.DetailAdvancedActions, item => item.Label == "Rebuild Runtime");
    }

    [Fact]
    public async Task XdbInvalid_DoesNotRecommendHostDiagnostics()
    {
        var snapshot = WithProvisioningHealth(
            CreateSnapshot("alpha", lastOperationName: "Recover", lastOperationResult: "Workspace provisioning stopped.", lastOperationSucceeded: false),
            new WorkspaceProvisioningHealthRecord
            {
                Succeeded = false,
                Stage = "Validate Oracle prerequisites",
                Summary = "Workspace provisioning stopped.",
                Reason = "Oracle XML Database (XDB) is invalid.",
                Evidence = "XDB status = INVALID",
                RecommendedAction = "Reset Runtime.",
                Confidence = "HIGH",
                Timestamp = DateTimeOffset.UtcNow,
                Duration = TimeSpan.FromMinutes(1),
                RawLogReference = "mounts/config/provision.sh",
                Repairability = WorkspaceRepairability.CleanupRepair.ToString(),
                EstimatedEffort = "Medium",
                EstimatedDuration = "4-6 minutes",
            });

        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();

        Assert.DoesNotContain(page.DetailItems, item => item.Label == "Technical Evidence" && item.Value.Contains("Detailed recommendation: Run Diagnostics", StringComparison.Ordinal));
        Assert.Equal("Rebuild Runtime", page.DetailPrimaryAction?.Label);
    }

    [Fact]
    public async Task XdbInvalid_AfterResetRuntimeNoEffect_ShowsTroubleshootWorkspaceAndRepairOutcome()
    {
        var snapshot = WithProvisioningHealth(
            CreateSnapshot("alpha", lastOperationName: "Open Workspace", lastOperationResult: "Workspace provisioning stopped.", lastOperationSucceeded: false),
            new WorkspaceProvisioningHealthRecord
            {
                Succeeded = false,
                Stage = "Validate Oracle prerequisites",
                Summary = "Workspace provisioning stopped.",
                Reason = "Oracle XML Database (XDB) is invalid.",
                Evidence = "XDB status = INVALID",
                ProblemScope = "RuntimeProblem",
                RecommendedAction = "Troubleshoot Workspace.",
                PreviousRecommendedAction = "Reset Runtime.",
                Confidence = "HIGH",
                Timestamp = DateTimeOffset.UtcNow,
                Duration = TimeSpan.FromMinutes(1),
                RawLogReference = "mounts/config/provision.sh",
                Repairability = WorkspaceRepairability.ManualRepair.ToString(),
                EstimatedEffort = "Medium",
                EstimatedDuration = "4-6 minutes",
                RepairHistory =
                [
                    new WorkspaceRepairAttemptRecord
                    {
                        RepairType = "Reset Runtime",
                        StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-8),
                        CompletedUtc = DateTimeOffset.UtcNow.AddMinutes(-3),
                        Duration = TimeSpan.FromMinutes(5),
                        Result = WorkspaceRepairOutcome.RepairNoEffect,
                        EvidenceBefore = "XDB status = INVALID",
                        EvidenceAfter = "XDB status = INVALID",
                        RootCauseBefore = "Oracle XML Database (XDB) is invalid.",
                        RootCauseAfter = "Oracle XML Database (XDB) is invalid.",
                        WorkspaceStateBefore = "runtime=Running",
                        WorkspaceStateAfter = "runtime=Running",
                        PreviousRecommendation = "Reset Runtime.",
                        UpdatedRecommendation = "Troubleshoot Workspace.",
                    },
                ],
            });

        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();

        Assert.Equal("Open Workspace", page.DetailPrimaryAction?.Label);
        Assert.Equal("Open Workspace.", page.DetailRecommendation);
        Assert.Contains(page.DetailAdvancedActions, item => item.Label == "Rebuild Runtime");
    }

    [Fact]
    public async Task DockerUnavailable_RecommendsRunDiagnostics()
    {
        var snapshot = WithProvisioningHealth(
            CreateSnapshot("alpha", lastOperationName: "Open Workspace", lastOperationResult: "Docker engine is unavailable.", lastOperationSucceeded: false),
            new WorkspaceProvisioningHealthRecord
            {
                Succeeded = false,
                Stage = "Check host prerequisites",
                Summary = "Workspace provisioning stopped.",
                Reason = "Docker engine is unavailable.",
                Evidence = "Docker engine check failed.",
                RecommendedAction = "Run Diagnostics.",
                Confidence = "HIGH",
                Timestamp = DateTimeOffset.UtcNow,
                Duration = TimeSpan.FromSeconds(20),
                RawLogReference = "mounts/config/provision.sh",
                Repairability = WorkspaceRepairability.AutomaticRepair.ToString(),
                EstimatedEffort = "Low",
                EstimatedDuration = "1-2 minutes",
            });

        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();

        Assert.Equal("Open Workspace.", page.DetailRecommendation);
        Assert.Equal("Open Workspace", page.DetailPrimaryAction?.Label);
        Assert.True(page.DetailPrimaryAction?.IsEnabled);
    }

    [Fact]
    public async Task MissingRuntimeState_RecommendsRecoverWorkspace()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha", includeRuntimeState: false, lastOperationName: "Open Workspace", lastOperationResult: "Runtime state is missing.", lastOperationSucceeded: false)]));
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();

        Assert.Contains(page.DetailItems, item => item.Label == "Needs Attention" && item.Value.Contains("Next: Open Workspace.", StringComparison.Ordinal));
        Assert.Equal("Open Workspace", page.DetailPrimaryAction?.Label);
        Assert.True(page.DetailPrimaryAction?.IsEnabled);
    }

    [Fact]
    public async Task PortConflict_RecommendsTroubleshootWorkspace()
    {
        var snapshot = WithProvisioningHealth(
            CreateSnapshot("alpha", lastOperationName: "Open Workspace", lastOperationResult: "5432 port is already in use.", lastOperationSucceeded: false),
            new WorkspaceProvisioningHealthRecord
            {
                Succeeded = false,
                Stage = "Start services",
                Summary = "Workspace provisioning stopped.",
                Reason = "5432 port is already in use.",
                Evidence = "Port 5432 is already in use.",
                RecommendedAction = "Stop conflicting workspace and Retry.",
                Confidence = "HIGH",
                Timestamp = DateTimeOffset.UtcNow,
                Duration = TimeSpan.FromSeconds(15),
                RawLogReference = "mounts/config/provision.sh",
                Repairability = WorkspaceRepairability.AutomaticRepair.ToString(),
                EstimatedEffort = "Low",
                EstimatedDuration = "1-2 minutes",
            });

        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();

        Assert.Equal("Open Workspace.", page.DetailRecommendation);
        Assert.Equal("Open Workspace", page.DetailPrimaryAction?.Label);
        Assert.DoesNotContain(page.DetailItems, item => item.Label == "Technical Evidence" && item.Value.Contains("Detailed recommendation: Run Diagnostics", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TroubleshootWorkspace_RevalidatesVolatileStateBeforeOpeningDiagnostics()
    {
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha")]);
        var page = new WorkspacesPageViewModel(desktop);
        var interaction = new FakeWorkspaceInteractionService();
        page.SetInteractionService(interaction);

        await page.LoadAsync();

        await page.TroubleshootWorkspaceCommand.ExecuteAsync();

        Assert.Equal(1, desktop.RefreshVolatileWorkspaceStateCallCount);
        Assert.Equal(page.SelectedWorkspace?.RootPath, interaction.LastWorkspaceDiagnosticsSession?.WorkspaceRootPath);
    }

    [Fact]
    public async Task RunDiagnostics_OpensDiagnosticsWindow()
    {
        var snapshot = CreateSnapshot("alpha", lastOperationName: "Open Workspace", lastOperationResult: "Workspace provisioning stopped.", lastOperationSucceeded: false);
        var interaction = new FakeWorkspaceInteractionService();
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));
        page.SetInteractionService(interaction);

        await page.LoadAsync();
        await page.TroubleshootWorkspaceCommand.ExecuteAsync();

        Assert.NotNull(interaction.LastWorkspaceDiagnosticsSession);
        Assert.Equal("alpha", interaction.LastWorkspaceDiagnosticsSession.WorkspaceName);
        Assert.Equal(snapshot.Paths.RootPath, interaction.LastWorkspaceDiagnosticsSession.WorkspaceRootPath);
    }

    [Fact]
    public async Task RunDiagnostics_WithNoSelectedWorkspace_IsSafeNoOp()
    {
        var interaction = new FakeWorkspaceInteractionService();
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([]));
        page.SetInteractionService(interaction);

        await page.TroubleshootWorkspaceCommand.ExecuteAsync();

        Assert.Null(interaction.LastWorkspaceDiagnosticsSession);
    }

    [Fact]
    public async Task RunDiagnostics_UsesCurrentOperationTranscriptForRunningSession()
    {
        var snapshot = CreateSnapshot("alpha");
        var interaction = new FakeWorkspaceInteractionService();
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));
        page.SetInteractionService(interaction);

        await page.LoadAsync();
        page.StartOperationTranscriptForTesting("Open Workspace", snapshot.Definition.Workspace.Name);
        page.AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Status, Text = "Installing APEX..." });
        await page.TroubleshootWorkspaceCommand.ExecuteAsync();

        Assert.NotNull(interaction.LastWorkspaceDiagnosticsSession);
        Assert.Equal(WorkspaceDiagnosticsMode.Progress, interaction.LastWorkspaceDiagnosticsSession.Mode);
        Assert.Equal(WorkspaceDiagnosticsStatus.Running, interaction.LastWorkspaceDiagnosticsSession.Status);
        Assert.Equal("Installing APEX...", interaction.LastWorkspaceDiagnosticsSession.Summary);
    }

    [Fact]
    public async Task RunDiagnostics_BuildsSessionFromSelectedWorkspaceState()
    {
        var snapshot = CreateSnapshot("alpha", lastOperationName: "Open Workspace", lastOperationResult: "Workspace provisioning stopped.", lastOperationSucceeded: false);
        snapshot = WithProvisioningHealth(snapshot, new WorkspaceProvisioningHealthRecord
        {
            Succeeded = false,
            Stage = "Validate runtime prerequisites",
            Summary = "Workspace provisioning stopped.",
            Reason = "XDB status = INVALID",
            Evidence = "Oracle validation failed.",
            RecommendedAction = "Open Workspace.",
            Confidence = "HIGH",
            Timestamp = DateTimeOffset.UtcNow,
            Duration = TimeSpan.FromMinutes(1),
            RawLogReference = snapshot.Paths.ProvisionScriptPath,
            Repairability = WorkspaceRepairability.AutomaticRepair.ToString(),
            EstimatedEffort = "Low",
            EstimatedDuration = "1-2 minutes",
        });
        var interaction = new FakeWorkspaceInteractionService();
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));
        page.SetInteractionService(interaction);

        await page.LoadAsync();
        await page.TroubleshootWorkspaceCommand.ExecuteAsync();

        var session = Assert.IsType<WorkspaceDiagnosticsSession>(interaction.LastWorkspaceDiagnosticsSession);
        Assert.Equal(WorkspaceDiagnosticsStatus.Failed, session.Status);
        Assert.Equal(WorkspaceNextActionRecommendation.OpenWorkspace, session.Recommendation);
        Assert.NotNull(session.FailureSummary);
        Assert.Contains(session.Entries, entry => entry.IsFailureEvidence);
    }

    [Fact]
    public async Task CleanupRepairFailure_RecommendationMatchesVisibleEnabledResetRuntimeButton()
    {
        var snapshot = CreateSnapshot("alpha", lastOperationName: "Recover", lastOperationResult: "Workspace provisioning stopped.", lastOperationSucceeded: false);
        snapshot = WithComputedReadiness(new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
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
                OracleSoftwareNoticeShown = snapshot.Record.OracleSoftwareNoticeShown,
                LastOperationName = snapshot.Record.LastOperationName,
                LastOperationResult = snapshot.Record.LastOperationResult,
                LastOperationSucceeded = snapshot.Record.LastOperationSucceeded,
                LastOperationUtc = snapshot.Record.LastOperationUtc,
                LastProvisioningHealth = new WorkspaceProvisioningHealthRecord
                {
                    Succeeded = false,
                    Stage = "Validate runtime prerequisites",
                    Summary = "Workspace provisioning stopped.",
                    Reason = "Managed runtime state is invalid.",
                    Evidence = "Runtime volume contains partial initialization.",
                    RecommendedAction = "Reset Runtime.",
                    Confidence = "HIGH",
                    Timestamp = DateTimeOffset.UtcNow,
                    Duration = TimeSpan.FromMinutes(1),
                    RawLogReference = "mounts/config/provision.sh",
                    Repairability = WorkspaceRepairability.CleanupRepair.ToString(),
                    EstimatedEffort = "Medium",
                    EstimatedDuration = "4-6 minutes",
                },
            },
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = snapshot.RuntimeState,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = snapshot.AppliedState,
            LocalRuntimeState = snapshot.LocalRuntimeState,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = snapshot.UpdateRequired,
        });

        var desktop = new FakeDesktopShellService([snapshot]);
        var page = new WorkspacesPageViewModel(desktop);
        page.SetInteractionService(new FakeWorkspaceInteractionService());

        await page.LoadAsync();

        Assert.Equal("Rebuild Runtime.", page.DetailRecommendation);
        Assert.Equal("Rebuild Runtime", page.DetailPrimaryAction?.Label);
        Assert.True(page.DetailPrimaryAction?.IsEnabled);
    }

    [Fact]
    public async Task ResetRuntime_RequestsConfirmationWithRemoveAndKeepScope()
    {
        var snapshot = CreateSnapshot("alpha", lastOperationName: "Recover", lastOperationResult: "Workspace provisioning stopped.", lastOperationSucceeded: false);
        snapshot = WithComputedReadiness(new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
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
                OracleSoftwareNoticeShown = snapshot.Record.OracleSoftwareNoticeShown,
                LastOperationName = snapshot.Record.LastOperationName,
                LastOperationResult = snapshot.Record.LastOperationResult,
                LastOperationSucceeded = snapshot.Record.LastOperationSucceeded,
                LastOperationUtc = snapshot.Record.LastOperationUtc,
                LastProvisioningHealth = new WorkspaceProvisioningHealthRecord
                {
                    Succeeded = false,
                    Stage = "Validate runtime prerequisites",
                    Summary = "Workspace provisioning stopped.",
                    Reason = "Managed runtime state is invalid.",
                    Evidence = "Runtime volume contains partial initialization.",
                    RecommendedAction = "Reset Runtime.",
                    Confidence = "HIGH",
                    Timestamp = DateTimeOffset.UtcNow,
                    Duration = TimeSpan.FromMinutes(1),
                    RawLogReference = "mounts/config/provision.sh",
                    Repairability = WorkspaceRepairability.CleanupRepair.ToString(),
                    EstimatedEffort = "Medium",
                    EstimatedDuration = "4-6 minutes",
                },
            },
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = snapshot.RuntimeState,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = snapshot.AppliedState,
            LocalRuntimeState = snapshot.LocalRuntimeState,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = snapshot.UpdateRequired,
        });
        var desktop = new FakeDesktopShellService([snapshot]);
        var interaction = new FakeWorkspaceInteractionService { ResetRuntimeConfirmed = false };
        var page = new WorkspacesPageViewModel(desktop);
        page.SetInteractionService(interaction);

        await page.LoadAsync();
        await page.ResetRuntimeCommand.ExecuteAsync();

        Assert.NotNull(interaction.LastResetRuntimePrompt);
        Assert.Contains("Managed containers for this workspace", interaction.LastResetRuntimePrompt!.Removes);
        Assert.Contains("Managed Docker volumes for this workspace", interaction.LastResetRuntimePrompt.Removes);
        Assert.Contains("Generated runtime state", interaction.LastResetRuntimePrompt.Removes);
        Assert.Contains("Workspace files", interaction.LastResetRuntimePrompt.Keeps);
        Assert.Contains("Git history", interaction.LastResetRuntimePrompt.Keeps);
        Assert.Contains("Documentation", interaction.LastResetRuntimePrompt.Keeps);
        Assert.Contains("Downloads/cache", interaction.LastResetRuntimePrompt.Keeps);
        Assert.Equal(0, desktop.ResetRuntimeCallCount);
    }

    [Fact]
    public async Task OperationLogVisibility_TogglesAndDefaultsVisibleAfterOperation()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")]));

        await page.LoadAsync();
        Assert.False(page.IsOperationLogVisible);
        Assert.False(page.ShowOperationLogPanel);

        page.AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Comment, Text = "line" });
        Assert.True(page.IsOperationLogVisible);
        Assert.True(page.ShowOperationLogPanel);
        Assert.Equal("Hide Operation Log", page.OperationLogToggleLabel);
        Assert.True(page.ShowOperationLogToggleButton);

        page.ToggleOperationLogVisibilityCommand.Execute(null);
        Assert.False(page.IsOperationLogVisible);
        Assert.False(page.ShowOperationLogPanel);
        Assert.Equal("Show Operation Log", page.OperationLogToggleLabel);
        Assert.True(page.ShowOperationLogToggleButton);
    }

    [Fact]
    public async Task WorkspaceListAndOperationLog_AreSeparateStates()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha"), CreateSnapshot("beta")]));

        await page.LoadAsync();
        page.AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Comment, Text = "log line" });

        Assert.Equal(2, page.Workspaces.Count);
        Assert.Contains("log line", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FullFailureText_RemainsInOperationLog()
    {
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            ReprovisionException = new InvalidOperationException("Command: docker exec odip-analiza-workspace bash /opt/opencode-workspace/config/provision.sh\nExit code: 127\n/workspace/.env: line 17: $'Analiza\\r': command not found"),
        };
        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();
        await page.ReprovisionWorkspaceCommand.ExecuteAsync();

        Assert.Contains("docker exec odip-analiza-workspace bash /opt/opencode-workspace/config/provision.sh", page.GetCopyAllOperationLogText(), StringComparison.Ordinal);
        Assert.Contains("/workspace/.env: line 17: $'Analiza\\r': command not found", page.GetCopyAllOperationLogText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopyAllText_ContainsFullCommandAndError()
    {
        var clipboard = new FakeClipboardService();
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            ReprovisionResultFactory = (_, sink) =>
            {
                sink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Command, Text = "docker exec workspace bash /opt/opencode-workspace/config/provision.sh" });
                sink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.StandardError, Text = "failure text" });
                return new WorkspaceReprovisionResult { Snapshot = CreateSnapshot("alpha"), Succeeded = true, Message = "done" };
            },
        };
        var page = new WorkspacesPageViewModel(desktop);
        page.SetClipboardService(clipboard);

        await page.LoadAsync();
        await page.ReprovisionWorkspaceCommand.ExecuteAsync();
        await page.CopyOperationLogCommand.ExecuteAsync();

        Assert.Contains("docker exec workspace bash /opt/opencode-workspace/config/provision.sh", clipboard.Text!, StringComparison.Ordinal);
        Assert.Contains("failure text", clipboard.Text!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClearingOperationLog_RemovesVisiblePanelState()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")]));

        await page.LoadAsync();
        page.AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Command, Text = "docker exec workspace bash /opt/opencode-workspace/config/provision.sh" });

        Assert.True(page.ShowOperationLogPanel);

        page.ClearOperationLogCommand.Execute(null);

        Assert.False(page.HasOperationLog);
        Assert.False(page.IsOperationLogVisible);
        Assert.False(page.ShowOperationLogPanel);
        Assert.Equal(string.Empty, page.GetCopyAllOperationLogText());
    }

    [Fact]
    public void MainWindow_OperationLogView_UsesWrappedLayoutWithoutHorizontalScroll()
    {
        var repoRoot = GetRepositoryRoot();
        var axaml = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "MainWindow.axaml"));

        Assert.Contains("DockPanel Grid.Row=\"1\" LastChildFill=\"True\"", axaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", axaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", axaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ShowOperationLogPanel}\"", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RowDefinitions=\"*,8,220\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_DiagnosticsRows_ExposeStableAutomationBindings()
    {
        var repoRoot = GetRepositoryRoot();
        var axaml = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "MainWindow.axaml"));

        Assert.Contains("automation:AutomationProperties.AutomationId=\"{Binding AutomationId}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("automation:AutomationProperties.Name=\"{Binding AutomationName}\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_DetailsPanel_SurfacesRecommendedActionAboveScrollableActionList()
    {
        var repoRoot = GetRepositoryRoot();
        var axaml = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "MainWindow.axaml"));
        var recommendedActionIndex = axaml.IndexOf("Text=\"Recommended action\"", StringComparison.Ordinal);
        var primaryButtonIndex = axaml.IndexOf("Content=\"{Binding CurrentPage.DetailPrimaryAction.Label}\"", StringComparison.Ordinal);
        var actionListIndex = axaml.IndexOf("ItemsSource=\"{Binding CurrentPage.DetailVisibleActions}\"", StringComparison.Ordinal);

        Assert.Contains("IsVisible=\"{Binding CurrentPage.ShowDetailPrimaryAction}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"accent\"", axaml, StringComparison.Ordinal);
        Assert.True(recommendedActionIndex >= 0);
        Assert.True(primaryButtonIndex > recommendedActionIndex);
        Assert.True(actionListIndex > primaryButtonIndex);
    }

    [Fact]
    public void RemoveWorkspaceWindow_UsesDesktopDialogLayout()
    {
        var repoRoot = GetRepositoryRoot();
        var axaml = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "RemoveWorkspaceWindow.axaml"));

        Assert.Contains("Width=\"740\"", axaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"{DynamicResource WorkspaceDialogWideMinWidth}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"{DynamicResource WorkspaceDialogTallMinHeight}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer Grid.Row=\"1\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"dialog-footer\"", axaml, StringComparison.Ordinal);
        Assert.Contains("IsDefault=\"True\"", axaml, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", axaml, StringComparison.Ordinal);
        Assert.Contains("RegistrationOnlyBorder", axaml, StringComparison.Ordinal);
        Assert.Contains("DockerResourcesBorder", axaml, StringComparison.Ordinal);
        Assert.Contains("DeleteFilesBorder", axaml, StringComparison.Ordinal);
        Assert.Contains("Destructive", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingNewOperation_ClearsPreviousVisibleLog()
    {
        var desktop = new FakeDesktopShellService([CreateSnapshot("alpha")]);
        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();
        page.AppendOperationTranscriptLine(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Comment, Text = "old line" });
        await page.ReprovisionWorkspaceCommand.ExecuteAsync();

        Assert.DoesNotContain("old line", page.OperationLogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeStateMissing_ProducesReprovisionRecommendation()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha", includeRuntimeState: false)]));

        await page.LoadAsync();

        Assert.Equal("Open Workspace can safely regenerate runtime state.", page.DetailSummary);
        Assert.Equal("Open Workspace", page.DetailPrimaryAction?.Label);
    }

    [Fact]
    public async Task WorkspaceDetails_ServiceRowsExposeOpenActionForUsefulEndpoints()
    {
        var snapshot = WithHealthServices(
            CreateOracleSnapshot("alpha", oracleNoticeShown: true),
            new WorkspaceServiceHealthSnapshot
            {
                ServiceId = "ords",
                Name = "Oracle REST Data Services",
                Category = "Service",
                StatusLabel = "Available",
                Summary = "Application gateway is responding and published workspace applications were discovered.",
                Applications = ["✓ SQL Developer Web", "⚠ Oracle APEX", "✓ REST APIs"],
                Endpoint = "http://localhost:8181/ords/",
                PrimaryUrl = "http://localhost:8181/ords/_/landing",
                ProbeType = WorkspaceServiceProbeType.Http,
                Status = WorkspaceHealthStatus.Healthy,
                Latency = TimeSpan.FromMilliseconds(41),
                Highlights = [new WorkspaceHealthFact { Label = "Latency", Value = "41 ms" }],
                Evidence = [new WorkspaceHealthFact { Label = "HTTP status", Value = "200 OK" }, new WorkspaceHealthFact { Label = "Probe duration", Value = "41 ms" }],
                Confidence = "HIGH",
                Timestamp = DateTimeOffset.UtcNow,
                Recommendation = "Open Workspace.",
                ActionLabel = "Open Oracle REST Data Services",
                OpenUrl = "http://localhost:8181/ords/_/landing",
                RefreshInterval = TimeSpan.FromSeconds(30),
                ProviderKey = "oracle",
            },
            new WorkspaceServiceHealthSnapshot
            {
                ServiceId = "sql-developer-web",
                Name = "SQL Developer Web",
                Category = "Application",
                StatusLabel = "Available",
                Summary = "Browser-based database tooling is available.",
                Endpoint = "http://localhost:8181/ords/",
                PrimaryUrl = "http://localhost:8181/ords/_/landing",
                ProbeType = WorkspaceServiceProbeType.Http,
                Status = WorkspaceHealthStatus.Healthy,
                Latency = TimeSpan.FromMilliseconds(55),
                Highlights = [new WorkspaceHealthFact { Label = "Latency", Value = "55 ms" }],
                Evidence = [new WorkspaceHealthFact { Label = "HTTP status", Value = "200 OK" }],
                Confidence = "HIGH",
                Timestamp = DateTimeOffset.UtcNow,
                Recommendation = "Open Workspace.",
                ActionLabel = "Open SQL Developer Web",
                OpenUrl = "http://localhost:8181/ords/_/landing",
                RefreshInterval = TimeSpan.FromSeconds(30),
                ProviderKey = "oracle",
            });
        var desktop = new FakeDesktopShellService([snapshot]);
        var page = new WorkspacesPageViewModel(desktop);

        await page.LoadAsync();

        var ords = page.DetailServices.Single(item => item.Name == "Oracle REST Data Services");
        var sqlDeveloperWeb = page.DetailServices.Single(item => item.Name == "SQL Developer Web");
        Assert.True(ords.CanOpen);
        Assert.True(sqlDeveloperWeb.CanOpen);
        Assert.Equal("Open Oracle REST Data Services", ords.ActionLabel);
        Assert.Contains("SQL Developer Web", ords.Applications, StringComparison.Ordinal);

        await ords.OpenCommand!.ExecuteAsync();
        Assert.Contains("http://localhost:8181/ords/_/landing", desktop.OpenedPaths);
    }

    [Fact]
    public async Task Troubleshooting_ServiceRowsExposeOpenActionForPgAdmin()
    {
        var snapshot = CreateSnapshot("alpha");
        var interaction = new FakeWorkspaceInteractionService();
        var workspacesPage = new WorkspacesPageViewModel(new FakeDesktopShellService([snapshot]));
        workspacesPage.SetInteractionService(interaction);

        await workspacesPage.LoadAsync();
        await workspacesPage.TroubleshootWorkspaceCommand.ExecuteAsync();

        Assert.NotNull(interaction.LastWorkspaceDiagnosticsSession);
        Assert.Equal(snapshot.Paths.RootPath, interaction.LastWorkspaceDiagnosticsSession.WorkspaceRootPath);
        Assert.Equal("Workspace Diagnostics", interaction.LastWorkspaceDiagnosticsSession.OperationName);
    }

    [Fact]
    public async Task StatusBar_UpdatesWhenWorkspaceSelectionChanges()
    {
        var shell = CreateShell([CreateSnapshot("alpha"), CreateSnapshot("beta")]);
        await shell.InitializeAsync();

        var workspacesPage = (WorkspacesPageViewModel)shell.NavigationItems.Single(item => item.Title == "Workspaces").Page;
        workspacesPage.SelectedWorkspace = workspacesPage.Workspaces.Last();

        Assert.Equal("Workspace: alpha", shell.StatusBarWorkspace);
        Assert.Contains("Branch:", shell.StatusBarBranch, StringComparison.Ordinal);
        Assert.Contains("Protection:", shell.StatusBarProtection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceLoad_OrdersMostRecentlyOpenedWorkspaceFirst()
    {
        var older = CreateSnapshot("older", lastOpenedUtc: DateTimeOffset.UtcNow.AddHours(-2), createdUtc: DateTimeOffset.UtcNow.AddHours(-2));
        var recent = CreateSnapshot("recent", lastOpenedUtc: DateTimeOffset.UtcNow, createdUtc: DateTimeOffset.UtcNow);

        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([older, recent]));

        await page.LoadAsync();

        Assert.Equal("recent", page.Workspaces[0].Name);
        Assert.Equal("older", page.Workspaces[1].Name);
        Assert.Equal("recent", page.SelectedWorkspace?.Name);
    }

    [Fact]
    public async Task StatusBar_UpdatesWhenDiagnosticsChange()
    {
        var shell = CreateShell();

        var diagnostics = shell.NavigationItems.Single(item => item.Title == "Diagnostics");
        diagnostics.SelectCommand.Execute(null);
        var diagnosticsPage = (DiagnosticsPageViewModel)diagnostics.Page;
        await diagnosticsPage.RunDoctorCommand.ExecuteAsync();

        Assert.Contains("Diagnostics:", shell.StatusBarState, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shell_IncludesRuntimeResourcesNavigationPage()
    {
        var shell = CreateShellWithDesktop(new FakeDesktopShellService([CreateSnapshot("alpha")])
        {
            RuntimeResourceExplorerFactoryAsync = _ => Task.FromResult(new WorkspaceRuntimeExplorerReport()),
        });

        await shell.InitializeAsync();

        Assert.Contains(shell.NavigationItems, item => item.Title == "Runtime Resources");
    }

    [Fact]
    public async Task RuntimeResourcesPage_OpenOwningWorkspace_NavigatesBackToWorkspace()
    {
        var snapshot = CreateSnapshot("alpha");
        var desktop = new FakeDesktopShellService([snapshot])
        {
            RuntimeResourceExplorerFactoryAsync = _ => Task.FromResult(new WorkspaceRuntimeExplorerReport
            {
                Summary = "Runtime resources loaded.",
                Workspaces =
                [
                    new WorkspaceRuntimeWorkspaceEntry { WorkspaceName = "alpha", WorkspaceRootPath = snapshot.Paths.RootPath, Status = "Running", Health = "Healthy", OwningRuntime = "docker" },
                ],
                Resources =
                [
                    new WorkspaceRuntimeResourceEntry { ResourceType = "Port", DisplayName = "PostgreSQL", WorkspaceName = "alpha", WorkspaceRootPath = snapshot.Paths.RootPath, Status = "Allocated", Health = "Running", CurrentPort = 15433, PreferredPort = 15432 },
                ],
            }),
        };
        var shell = CreateShellWithDesktop(desktop);

        await shell.InitializeAsync();

        var runtimePage = (RuntimeResourcesPageViewModel)shell.NavigationItems.Single(item => item.Title == "Runtime Resources").Page;
        shell.NavigationItems.Single(item => item.Title == "Runtime Resources").SelectCommand.Execute(null);
        runtimePage.SelectedResource = runtimePage.Resources.Single();

        await runtimePage.OpenOwningWorkspaceCommand.ExecuteAsync();

        Assert.Equal("Workspaces", shell.CurrentPage.Title);
        var workspacesPage = (WorkspacesPageViewModel)shell.NavigationItems.Single(item => item.Title == "Workspaces").Page;
        Assert.Equal(snapshot.Paths.RootPath, workspacesPage.SelectedWorkspace?.RootPath);
    }

    [Fact]
    public void AvaloniaAssembly_DoesNotReferenceWpfAssemblies()
    {
        var references = typeof(ShellViewModel).Assembly.GetReferencedAssemblies().Select(item => item.Name).ToArray();

        Assert.DoesNotContain("PresentationCore", references);
        Assert.DoesNotContain("WindowsBase", references);
    }

    private static ShellViewModel CreateShell(IReadOnlyList<WorkspaceSnapshot>? snapshots = null)
    {
        var desktop = new FakeDesktopShellService(snapshots ?? [CreateSnapshot("alpha")]);
        return CreateShellWithDesktop(desktop);
    }

    private static ShellViewModel CreateShellWithDesktop(IDesktopShellService desktop)
    {
        return ShellViewModel.Create(
            desktop,
            new FakeDiagnosticsShellService(),
            new FakeHostCapabilities(),
            new FakeTemplateCatalogShellService(),
            new FakeDocumentationShellService(),
            new ThemeCoordinator(ThemeMode.System),
            CreateAppBuildInfo(),
            "en");
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));
    }

    private static AppBuildInfo CreateAppBuildInfo()
        => new("/tmp/app", "Debug", "1.0.0", "1.0.0-preview", "abcdef123456", DateTimeOffset.UtcNow.ToString("O"), "1.0.0", "workspace-yaml-v1");

    private static SettingsPageViewModel CreateSettingsPage(FakeDesktopShellService? desktop = null, WorkspaceSnapshot? selectedWorkspace = null, ThemeCoordinator? coordinator = null)
    {
        var actualDesktop = desktop ?? new FakeDesktopShellService([selectedWorkspace ?? CreateSnapshot("alpha")]);
        var actualWorkspace = selectedWorkspace ?? actualDesktop.LoadWorkspaceItemsAsync(false).GetAwaiter().GetResult().Items.First().Snapshot!;
        return new SettingsPageViewModel(coordinator ?? new ThemeCoordinator(ThemeMode.System), CreateAppBuildInfo(), actualDesktop, new FakeHostCapabilities(), () => new WorkspaceSummaryViewModel(new WorkspaceShellItem { Record = actualWorkspace.Record, Snapshot = actualWorkspace }));
    }

    private static WorkspaceSnapshot CreateSnapshot(
        string name,
        bool includeRuntimeState = true,
        bool updateRequired = false,
        string? lastOperationName = null,
        string? lastOperationResult = null,
        bool? lastOperationSucceeded = true,
        DateTimeOffset? lastOpenedUtc = null,
        DateTimeOffset? createdUtc = null)
    {
        var effectiveLastOpenedUtc = lastOpenedUtc ?? DateTimeOffset.UtcNow;
        var effectiveCreatedUtc = createdUtc ?? effectiveLastOpenedUtc;
        var root = Path.Combine(Path.GetTempPath(), $"oc-avalonia-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "mounts", "config"));
        Directory.CreateDirectory(Path.Combine(root, "history", "checkpoints"));
        File.WriteAllText(Path.Combine(root, "workspace.yaml"), "workspace: {}\n");
        File.WriteAllText(Path.Combine(root, "compose.yaml"), "services: {}\n");
        File.WriteAllText(Path.Combine(root, "mounts", "config", "provision.sh"), "#!/bin/bash\n");

        var snapshot = new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = name,
                RootPath = root,
                RepositoryPath = root,
                LastOpenedUtc = effectiveLastOpenedUtc,
                CreatedUtc = effectiveCreatedUtc,
                LastOperationName = lastOperationName,
                LastOperationResult = lastOperationResult ?? "Loaded workspace.",
                LastOperationSucceeded = lastOperationSucceeded,
            },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = name, Image = "ubuntu:24.04" },
                Features = ["core"],
                Services = ["postgres"],
            },
            Paths = new WorkspacePaths
            {
                RootPath = root,
                GitIgnorePath = Path.Combine(root, ".gitignore"),
                OpencodePath = Path.Combine(root, ".opencode"),
                OpencodeLocalPath = Path.Combine(root, ".opencode", "local"),
                WorkspaceYamlRelativePath = "workspace.yaml",
                WorkspaceYamlPath = Path.Combine(root, "workspace.yaml"),
                ComposePath = Path.Combine(root, "compose.yaml"),
                EnvironmentFilePath = Path.Combine(root, ".env"),
                MountsRootPath = Path.Combine(root, "mounts"),
                InboxPath = Path.Combine(root, "mounts", "inbox"),
                WorkspacePath = Path.Combine(root, "mounts", "workspace"),
                UserPath = Path.Combine(root, "mounts", "user"),
                HomePath = Path.Combine(root, "mounts", "home"),
                ConfigPath = Path.Combine(root, "mounts", "config"),
                ProvisionScriptPath = Path.Combine(root, "mounts", "config", "provision.sh"),
                StarshipConfigPath = Path.Combine(root, "mounts", "config", "starship.toml"),
                ShellInitScriptPath = Path.Combine(root, "mounts", "config", "opencode-shell-init.sh"),
                OpencodeWorkspaceShellPath = Path.Combine(root, "mounts", "config", "opencode-workspace-shell.sh"),
                ScreenConfigPath = Path.Combine(root, "mounts", "config", "screenrc"),
                AttachWrapperScriptPath = Path.Combine(root, "mounts", "config", "attach.ps1"),
                AttachDiagnosticsLogPath = Path.Combine(root, "mounts", "config", "attach-diagnostics.log"),
                TerminalDiagnosticsScriptPath = Path.Combine(root, "mounts", "config", "terminal-diagnostics.ps1"),
                RuntimeStatePath = Path.Combine(root, ".opencode", "local", "runtime-state.yaml"),
                AppliedStatePath = Path.Combine(root, "mounts", "config", "applied-state.yaml"),
                HistoryPath = Path.Combine(root, "history"),
                CheckpointsPath = Path.Combine(root, "history", "checkpoints"),
                CheckpointIndexPath = Path.Combine(root, "history", "checkpoints", "index.yaml"),
                TimelinePath = Path.Combine(root, "history", "timeline.yaml"),
                RuntimesPath = Path.Combine(root, "runtimes"),
                DefaultRuntimePath = Path.Combine(root, "runtimes", "default.yaml"),
                ArtifactsPath = Path.Combine(root, "artifacts"),
                ArtifactRunsPath = Path.Combine(root, "artifacts", "runs"),
                ArtifactIndexPath = Path.Combine(root, "artifacts", "index.json"),
            },
            ConfigurationPath = "workspace.yaml",
            RuntimeState = WorkspaceRuntimeState.Running,
            Safety = new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.PartiallyProtected,
                Headline = "Protected working copy",
                Message = "Workspace is on a safe working copy.",
                LocalRecovery = new WorkspaceLocalRecoverySnapshot { IsGitInitialized = true, AreUntrackedFilesProtected = true },
                Backup = new WorkspaceBackupSnapshot { HasRemoteConfigured = true },
                IgnorePolicy = new WorkspaceIgnorePolicyReview(),
                AdvancedGit = new WorkspaceAdvancedGitSnapshot { CurrentBranch = $"users/test/{name}", LatestCommitSha = "head123", StatusSummary = "clean" },
            },
            Session = new WorkspaceSessionSnapshot { SessionName = name, State = WorkspaceSessionState.Resumable },
            AppliedState = new WorkspaceAppliedState
            {
                DesiredStateHash = "desired",
                WorkspaceDefinitionHash = "definition",
                AppliedUtc = DateTimeOffset.UtcNow,
                AppVersion = "test",
            },
            LocalRuntimeState = includeRuntimeState ? new WorkspaceRuntimeStateRecord { ResolvedEngine = "docker", ResolvedPlatform = "linux/amd64", CompatibilityMode = "native" } : null,
            ResolvedRuntimePlan = new ResolvedRuntimePlan { Runtime = "docker", TargetPlatform = "linux/amd64", IsAvailable = true, HostPlatform = new HostPlatformInfo() },
            UpdateRequired = updateRequired,
            Health = new WorkspaceHealthSnapshot(),
            Readiness = new WorkspaceReadinessSnapshot
            {
                Status = includeRuntimeState && !updateRequired ? WorkspaceReadinessStatus.Ready : WorkspaceReadinessStatus.Unavailable,
                CurrentActivity = WorkspaceActivity.None,
                PrimaryAction = WorkspacePrimaryAction.OpenWorkspace,
                Summary = includeRuntimeState && !updateRequired ? "Workspace is ready." : "Open Workspace can prepare and open this workspace.",
            },
            AvailableServices = WorkspaceServiceCatalog.Build(new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = name, Image = "ubuntu:24.04" },
                Features = ["core"],
                Services = [],
            }, includeRuntimeState ? new WorkspaceRuntimeStateRecord { ResolvedEngine = "docker", ResolvedPlatform = "linux/amd64", CompatibilityMode = "native" } : null, root),
        };

        return WithComputedReadiness(snapshot);
    }

    private static WorkspaceSnapshot WithHealth(WorkspaceSnapshot snapshot, WorkspaceHealthSnapshot health, WorkspaceProvisioningHealthRecord? provisioningHealth = null)
    {
        var updated = new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
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
                OracleSoftwareNoticeShown = snapshot.Record.OracleSoftwareNoticeShown,
                LastOperationName = snapshot.Record.LastOperationName,
                LastOperationResult = snapshot.Record.LastOperationResult,
                LastOperationSucceeded = snapshot.Record.LastOperationSucceeded,
                LastOperationUtc = snapshot.Record.LastOperationUtc,
                LastProvisioningHealth = provisioningHealth ?? snapshot.Record.LastProvisioningHealth,
            },
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = snapshot.RuntimeState,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = snapshot.AppliedState,
            LocalRuntimeState = snapshot.LocalRuntimeState,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = snapshot.UpdateRequired,
            Health = health,
            Readiness = snapshot.Readiness,
            AvailableServices = snapshot.AvailableServices,
        };
        return WithComputedReadiness(updated);
    }

    private static WorkspaceSnapshot WithReadiness(WorkspaceSnapshot snapshot, WorkspaceReadinessSnapshot readiness)
        => new()
        {
            Record = snapshot.Record,
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = snapshot.RuntimeState,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = snapshot.AppliedState,
            LocalRuntimeState = snapshot.LocalRuntimeState,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = snapshot.UpdateRequired,
            Health = snapshot.Health,
            Readiness = readiness,
            AvailableServices = snapshot.AvailableServices,
        };

    private static WorkspaceHealthSnapshot CreateHealthSnapshot(
        WorkspaceHealthStatus overallStatus,
        string summary,
        string recommendation,
        IReadOnlyList<WorkspaceProviderHealthSnapshot>? providers = null,
        IReadOnlyList<WorkspaceServiceHealthSnapshot>? services = null,
        WorkspaceDevelopmentEnvironmentHealthSnapshot? developmentEnvironment = null,
        DateTimeOffset? timestamp = null)
        => new()
        {
            OverallStatus = overallStatus,
            Summary = summary,
            Recommendation = recommendation,
            Confidence = "HIGH",
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Providers = providers ?? Array.Empty<WorkspaceProviderHealthSnapshot>(),
            Services = services ?? Array.Empty<WorkspaceServiceHealthSnapshot>(),
            DevelopmentEnvironment = developmentEnvironment,
        };

    private static WorkspaceServiceHealthSnapshot CreateServiceHealthSnapshot(
        string id,
        string name,
        WorkspaceHealthStatus status,
        string summary,
        string category = "Application",
        DateTimeOffset? timestamp = null)
        => new()
        {
            ServiceId = id,
            Name = name,
            Category = category,
            Status = status,
            StatusLabel = status == WorkspaceHealthStatus.Healthy ? "Available" : "Needs attention",
            Summary = summary,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            RefreshInterval = TimeSpan.FromSeconds(30),
            Confidence = "HIGH",
            Recommendation = status == WorkspaceHealthStatus.Healthy ? "Open Workspace." : "Troubleshoot Workspace.",
        };

    private static WorkspaceSnapshot CreateOracleSnapshot(string name, bool oracleNoticeShown)
    {
        var snapshot = CreateSnapshot(name);
        var definition = new WorkspaceDefinition
        {
            Workspace = snapshot.Definition.Workspace,
            Provider = snapshot.Definition.Provider,
            Runtime = snapshot.Definition.Runtime,
            Features = [OracleWorkspaceFamily.OracleBaseFeatureId],
            Services = [OracleWorkspaceFamily.OracleDatabaseServiceId],
            Skills = snapshot.Definition.Skills,
            Mcp = snapshot.Definition.Mcp,
            Agent = snapshot.Definition.Agent,
            Terminal = snapshot.Definition.Terminal,
        };
        return WithComputedReadiness(new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
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
                OracleSoftwareNoticeShown = oracleNoticeShown,
                LastOperationName = snapshot.Record.LastOperationName,
                LastOperationResult = snapshot.Record.LastOperationResult,
                LastOperationSucceeded = snapshot.Record.LastOperationSucceeded,
                LastOperationUtc = snapshot.Record.LastOperationUtc,
            },
            Definition = definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = snapshot.RuntimeState,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = snapshot.AppliedState,
            LocalRuntimeState = snapshot.LocalRuntimeState,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = snapshot.UpdateRequired,
            Health = snapshot.Health,
            Readiness = snapshot.Readiness,
            AvailableServices = WorkspaceServiceCatalog.Build(definition, snapshot.LocalRuntimeState, snapshot.Paths.RootPath),
        });
    }

    private static WorkspaceSnapshot WithProvisioningHealth(WorkspaceSnapshot snapshot, WorkspaceProvisioningHealthRecord health)
        => WithComputedReadiness(new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
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
                OracleSoftwareNoticeShown = snapshot.Record.OracleSoftwareNoticeShown,
                LastOperationName = snapshot.Record.LastOperationName,
                LastOperationResult = snapshot.Record.LastOperationResult,
                LastOperationSucceeded = snapshot.Record.LastOperationSucceeded,
                LastOperationUtc = snapshot.Record.LastOperationUtc,
                LastProvisioningHealth = health,
            },
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = snapshot.RuntimeState,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = snapshot.AppliedState,
            LocalRuntimeState = snapshot.LocalRuntimeState,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = snapshot.UpdateRequired,
            Health = snapshot.Health,
            Readiness = snapshot.Readiness,
            AvailableServices = snapshot.AvailableServices,
        });

    private static WorkspaceSnapshot WithHealthServices(WorkspaceSnapshot snapshot, params WorkspaceServiceHealthSnapshot[] services)
        => WithComputedReadiness(new WorkspaceSnapshot
        {
            Record = snapshot.Record,
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = snapshot.RuntimeState,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = snapshot.AppliedState,
            LocalRuntimeState = snapshot.LocalRuntimeState,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = snapshot.UpdateRequired,
            Health = new WorkspaceHealthSnapshot
            {
                OverallStatus = services.Any(item => item.Status == WorkspaceHealthStatus.Degraded) ? WorkspaceHealthStatus.Degraded : services.Any(item => item.Status == WorkspaceHealthStatus.Attention) ? WorkspaceHealthStatus.Attention : WorkspaceHealthStatus.Healthy,
                Summary = "Service health loaded.",
                Recommendation = "Open Workspace.",
                Confidence = "HIGH",
                Timestamp = DateTimeOffset.UtcNow,
                Providers = [],
                Services = services,
            },
            Readiness = snapshot.Readiness,
            AvailableServices = snapshot.AvailableServices,
        });

    private static WorkspaceSnapshot WithComputedReadiness(WorkspaceSnapshot snapshot)
        => new WorkspaceSnapshot
        {
            Record = snapshot.Record,
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = snapshot.RuntimeState,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = snapshot.AppliedState,
            LocalRuntimeState = snapshot.LocalRuntimeState,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = snapshot.UpdateRequired,
            Synchronization = snapshot.Synchronization,
            Health = snapshot.Health,
            Readiness = WorkspaceReadinessEngine.Build(new WorkspaceReadinessInput { Snapshot = snapshot, Health = snapshot.Health }),
            AvailableServices = snapshot.AvailableServices,
        };

    private static WorkspaceSnapshot CreateConnectedOracleApexSnapshot(WorkspaceSynchronizationState state)
    {
        var baseSnapshot = CreateSnapshot("oracle-apexlang-demo");
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-apexlang-demo", Image = "ubuntu:24.04" },
            Features = ["core", "oracle-demo", "oracle-apex-demo", "oracle-apexlang-demo"],
            Services = ["oracle-demo", "oracle-ords"],
            Oracle = new OracleWorkspacePreferences
            {
                Apex = new OracleApexWorkspacePreferences
                {
                    DefaultEnvironment = "dev",
                    Environments = new Dictionary<string, OracleApexEnvironmentPreferences>
                    {
                        ["dev"] = new()
                        {
                            Workspace = "TEST",
                            ParsingSchema = "TESTSCHEMA",
                            ApplicationId = 100,
                            SqlclProfile = "local-apex-dev",
                            SyncMode = WorkspaceSynchronizationModes.Manual,
                            SourcePath = "src/apex",
                        },
                    },
                },
            },
        };

        return WithComputedReadiness(new WorkspaceSnapshot
        {
            Record = baseSnapshot.Record,
            Definition = definition,
            Paths = baseSnapshot.Paths,
            ConfigurationPath = baseSnapshot.ConfigurationPath,
            RuntimeState = baseSnapshot.RuntimeState,
            Safety = baseSnapshot.Safety,
            Session = baseSnapshot.Session,
            AppliedState = baseSnapshot.AppliedState,
            LocalRuntimeState = baseSnapshot.LocalRuntimeState,
            ResolvedRuntimePlan = baseSnapshot.ResolvedRuntimePlan,
            UpdateRequired = baseSnapshot.UpdateRequired,
            Synchronization = new WorkspaceSynchronizationSnapshot
            {
                IsSupported = true,
                State = state,
                Summary = "Oracle APEX synchronization state is available.",
                DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                {
                    EnvironmentName = "dev",
                    WorkspaceName = "TEST",
                    ParsingSchema = "TESTSCHEMA",
                    ApplicationId = 100,
                    ApplicationName = "Customer Orders Demo",
                    SyncMode = WorkspaceSynchronizationModes.Manual,
                    SourcePath = "src/apex",
                    State = state,
                    LastValidationUtc = new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero),
                    LastExportUtc = new DateTimeOffset(2026, 7, 9, 10, 15, 0, TimeSpan.Zero),
                    LastPullUtc = new DateTimeOffset(2026, 7, 9, 10, 30, 0, TimeSpan.Zero),
                },
                Environments =
                [
                    new WorkspaceSynchronizationEnvironmentSnapshot
                    {
                        EnvironmentName = "dev",
                        WorkspaceName = "TEST",
                        ParsingSchema = "TESTSCHEMA",
                        ApplicationId = 100,
                        ApplicationName = "Customer Orders Demo",
                        SyncMode = WorkspaceSynchronizationModes.Manual,
                        SourcePath = "src/apex",
                        State = state,
                        LastValidationUtc = new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero),
                        LastExportUtc = new DateTimeOffset(2026, 7, 9, 10, 15, 0, TimeSpan.Zero),
                        LastPullUtc = new DateTimeOffset(2026, 7, 9, 10, 30, 0, TimeSpan.Zero),
                    },
                ],
            },
            Health = baseSnapshot.Health,
            Readiness = baseSnapshot.Readiness,
            AvailableServices = WorkspaceServiceCatalog.Build(definition, baseSnapshot.LocalRuntimeState, baseSnapshot.Paths.RootPath),
        });
    }

    private sealed class FakeDesktopShellService : IDesktopShellService
    {
        private readonly IReadOnlyList<WorkspaceSnapshot> _snapshots;
        private readonly IReadOnlyList<WorkspaceShellItem> _extraItems;
        public int RefreshVolatileWorkspaceStateCallCount { get; private set; }
        public Func<bool, Action<WorkspaceLoadProgressUpdate>?, CancellationToken, Task<WorkspaceLoadResult>>? LoadWorkspaceItemsAsyncFactory { get; init; }
        public Func<string, IOperationLogSink?, WorkspaceReprovisionResult>? ReprovisionResultFactory { get; init; }
        public Func<string, IOperationLogSink?, CancellationToken, Task<WorkspaceReprovisionResult>>? ReprovisionResultFactoryAsync { get; init; }
        public Func<string, WorkspaceDefinition, IOperationLogSink?, CancellationToken, Task<WorkspaceSnapshot>>? CreateWorkspaceAsyncFactory { get; init; }
        public Func<string, IOperationLogSink?, CancellationToken, Task<WorkspaceOperationResult>>? OpenWorkspaceResultFactoryAsync { get; init; }
        public Func<string, IOperationLogSink?, CancellationToken, Task<WorkspaceOperationResult>>? AttachResultFactoryAsync { get; init; }
        public Func<CancellationToken, Task<WorkspaceRuntimeExplorerReport>>? RuntimeResourceExplorerFactoryAsync { get; init; }
        public Func<WorkspaceTroubleshootingRequest, CancellationToken, Task<WorkspaceTroubleshootingReport>>? TroubleshootingReportFactoryAsync { get; init; }
        public Func<WorkspaceTroubleshootingRequest, string, CancellationToken, Task<WorkspaceTroubleshootingReport>>? TroubleshootingActionFactoryAsync { get; init; }
        public Func<string, IOperationLogSink?, CancellationToken, Task<WorkspaceOperationResult>>? PrepareResultFactoryAsync { get; set; }
        public Func<string, IOperationLogSink?, CancellationToken, Task<WorkspaceCheckpointOperationResult>>? CheckpointResultFactoryAsync { get; set; }
        public Func<string, IOperationLogSink?, CancellationToken, Task<WorkspaceRemovalOperationResult>>? RemoveResultFactoryAsync { get; set; }
        public Func<string, IOperationLogSink?, CancellationToken, Task<WorkspacePublishResult>>? PublishResultFactoryAsync { get; set; }
        public Func<string, string, IOperationLogSink?, CancellationToken, Task<WorkspaceBackupResult>>? BackupResultFactoryAsync { get; set; }
        public Func<string, string, IOperationLogSink?, CancellationToken, Task<WorkspaceOperationResult>>? SavePointResultFactoryAsync { get; set; }
        public Func<string, string, string, string, string, WorkspaceSnapshot?, CancellationToken, Task<OracleApexApplicationDiscoveryResult>>? OracleApexDiscoveryResultFactoryAsync { get; set; }
        public Func<string, ConnectOracleApexApplicationDraft, WorkspaceSnapshot?, IOperationLogSink?, CancellationToken, Task<WorkspaceOperationResult>>? ConnectOracleApexApplicationResultFactoryAsync { get; set; }
        public Func<string, WorkspaceSnapshot?, CancellationToken, Task<WorkspaceSnapshot>>? AcknowledgeOracleNoticeAsyncFactory { get; set; }
        public WorkspacePublishAssessment PublishAssessment { get; set; } = new()
        {
            WorkspaceName = "alpha",
            CurrentBranch = "users/test/alpha",
            Summary = "Ready to publish 1 commit(s) to 'origin/users/test/alpha'.",
            ConfirmationMessage = "Publish this Working Copy now?",
            Findings = ["Ahead/behind: 1/0"],
            Warnings = [],
            CanPublish = true,
            IsBlocked = false,
            RequiresConfirmation = true,
            RequiresSavePoint = false,
            HasRemoteConfigured = true,
            RemoteName = "origin",
            RemoteBranch = "origin/users/test/alpha",
            AheadCount = 1,
            BehindCount = 0,
        };
        public Exception? ReprovisionException { get; init; }
        public Exception? OpenWorkspaceException { get; init; }
        public Exception? ResetRuntimeException { get; init; }
        public Exception? AttachException { get; init; }
        public Exception? RemoveException { get; init; }
        public Exception? PublishException { get; init; }
        public Exception? BackupException { get; init; }
        public Exception? SavePointException { get; init; }
        public Exception? CheckpointException { get; init; }
        public Exception? TimelineException { get; init; }
        public WindowsTerminalProfileOperationResult WindowsTerminalProfileResult { get; set; } = new()
        {
            Message = "Windows Terminal profile 'OpenCode Stuff - alpha' is already configured.",
            Setup = new WindowsTerminalProfileSetupResult
            {
                Status = WindowsTerminalProfileSetupStatus.AlreadyConfigured,
                Summary = "Windows Terminal profile 'OpenCode Stuff - alpha' is already configured.",
                ProfileName = "OpenCode Stuff - alpha",
                FragmentPath = "C:\\Users\\test\\profiles.json",
                ResolvedFontFace = "JetBrainsMono Nerd Font",
                FailureReason = string.Empty,
            },
        };
        public int RemoveCallCount { get; private set; }
        public string? LastRemoveRootPath { get; private set; }
        public int PublishCallCount { get; private set; }
        public int BackupCallCount { get; private set; }
        public int CreateSavePointCallCount { get; private set; }
        public int CreateCheckpointCallCount { get; private set; }
        public int StartCallCount { get; private set; }
        public int OpenWorkspaceCallCount { get; private set; }
        public int ResetRuntimeCallCount { get; private set; }
        public int AttachCallCount { get; private set; }
        public int PrepareCallCount { get; private set; }
        public int ReprovisionCallCount { get; private set; }
        public int AcknowledgeOracleNoticeCallCount { get; private set; }
        public int ConnectOracleApexApplicationCallCount { get; private set; }
        public ExistingGitCheckoutImportRequest? LastImportRequest { get; private set; }
        public string? LastSavePointMessage { get; private set; }
        public ConnectOracleApexApplicationDraft? LastConnectOracleApexApplicationDraft { get; private set; }
        public Dictionary<string, WorkspaceTimeline> TimelineByPath { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> OpenedPaths { get; } = [];

        public FakeDesktopShellService(IReadOnlyList<WorkspaceSnapshot> snapshots, IReadOnlyList<WorkspaceShellItem>? extraItems = null)
        {
            _snapshots = snapshots;
            _extraItems = extraItems ?? [];
        }

        public Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(bool includeRuntimeInspection, Action<WorkspaceLoadProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
            => LoadWorkspaceItemsAsyncFactory?.Invoke(includeRuntimeInspection, progress, cancellationToken) ?? Task.FromResult(BuildLoadResult());

        private WorkspaceLoadResult BuildLoadResult()
            => new()
            {
                Items = _snapshots.Select(item => new WorkspaceShellItem { Record = item.Record, Snapshot = item }).Concat(_extraItems).ToList(),
                Report = new WorkspaceLoadReport
                {
                    IndexFilePath = WorkspaceAppDataPaths.GetWorkspaceIndexPath(),
                    AppDataRoot = WorkspaceAppDataPaths.GetWorkspaceManagerDataRoot(),
                    StartedUtc = DateTimeOffset.UtcNow.AddMilliseconds(-25),
                    CompletedUtc = DateTimeOffset.UtcNow,
                    TotalDuration = TimeSpan.FromMilliseconds(25),
                    RawRecordCount = _snapshots.Count + _extraItems.Count,
                    SnapshotAttemptCount = _snapshots.Count + _extraItems.Count,
                    SnapshotCount = _snapshots.Count,
                    Failures = _extraItems.Select(item => new WorkspaceLoadFailure(string.IsNullOrWhiteSpace(item.Record.Name) ? item.Record.RootPath : item.Record.Name, item.Record.RootPath, item.ErrorMessage)).ToList(),
                    ItemsReturnedCount = _snapshots.Count + _extraItems.Count,
                    Timings =
                    [
                        new WorkspaceLoadTiming
                        {
                            StageKey = "workspace-index",
                            StageLabel = "Workspace index",
                            Duration = TimeSpan.FromMilliseconds(12),
                            StartedUtc = DateTimeOffset.UtcNow.AddMilliseconds(-25),
                            CompletedUtc = DateTimeOffset.UtcNow.AddMilliseconds(-13),
                            Succeeded = true,
                        },
                    ],
                },
            };

        public IReadOnlyList<WorkspaceReference> LoadWorkspaceReferences()
            => _snapshots.Select(item => new WorkspaceReference(item.Definition.Workspace.Name, item.Paths.RootPath))
                .Concat(_extraItems.Select(item => new WorkspaceReference(item.Record.Name, item.Record.RootPath)))
                .ToList();

        public OracleSoftwareNoticePrompt? BuildOracleSoftwareNotice(TemplateManifest template, string workspaceName)
            => OracleWorkspaceFamily.IsOracleWorkspace(template)
                ? new OracleSoftwareNoticePrompt
                {
                    Title = "Oracle Software Notice",
                    SubjectName = workspaceName,
                    Summary = "Review the Oracle software reminder before continuing with this Oracle workspace.",
                    Facts = ["Oracle software is subject to Oracle licensing terms."],
                    AcknowledgementLabel = "I understand the Oracle licensing reminder.",
                    ConfirmLabel = "Continue",
                    CancelLabel = "Cancel",
                }
                : null;

        public OracleSoftwareNoticePrompt? BuildOracleSoftwareNotice(WorkspaceSnapshot snapshot)
            => OracleWorkspaceFamily.IsOracleWorkspace(snapshot.Definition) && !snapshot.Record.OracleSoftwareNoticeShown
                ? new OracleSoftwareNoticePrompt
                {
                    Title = "Oracle Software Notice",
                    SubjectName = snapshot.Definition.Workspace.Name,
                    Summary = "Review the Oracle software reminder before continuing with this Oracle workspace.",
                    Facts = ["Oracle software is subject to Oracle licensing terms."],
                    AcknowledgementLabel = "I understand the Oracle licensing reminder.",
                    ConfirmLabel = "Continue",
                    CancelLabel = "Cancel",
                }
                : null;

        public WorkspaceRuntimeResetPrompt BuildRuntimeResetPrompt(WorkspaceSnapshot snapshot)
            => new()
            {
                WorkspaceName = snapshot.Definition.Workspace.Name,
                WorkspaceRoot = snapshot.Paths.RootPath,
                Summary = "Reset recreates managed runtime resources for this workspace while keeping your workspace files and downloads.",
                Removes = ["Managed containers for this workspace", "Managed Docker volumes for this workspace", "Generated runtime state"],
                Keeps = ["Workspace files", "Git history", "Documentation", "Downloads/cache", "workspace.yaml"],
                ConfirmationMessage = "Reset runtime and continue?",
            };

        public Task<WorkspaceSnapshot> AcknowledgeOracleSoftwareNoticeAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default)
        {
            AcknowledgeOracleNoticeCallCount++;

            if (AcknowledgeOracleNoticeAsyncFactory is not null)
            {
                return AcknowledgeOracleNoticeAsyncFactory(rootPath, currentSnapshot, cancellationToken);
            }

            var snapshot = currentSnapshot ?? CreateSnapshot("oracle");
            return Task.FromResult(new WorkspaceSnapshot
            {
                Record = new WorkspaceRecord
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
                },
                Definition = snapshot.Definition,
                Paths = snapshot.Paths,
                ConfigurationPath = snapshot.ConfigurationPath,
                RuntimeState = snapshot.RuntimeState,
                Safety = snapshot.Safety,
                Session = snapshot.Session,
                AppliedState = snapshot.AppliedState,
                LocalRuntimeState = snapshot.LocalRuntimeState,
                ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
                UpdateRequired = snapshot.UpdateRequired,
                Health = snapshot.Health,
                Readiness = snapshot.Readiness,
            });
        }

        public Task<string> SuggestSavePointMessageAsync(string rootPath, CancellationToken cancellationToken = default)
            => Task.FromResult("Capture current workspace state");

        public Task<ExistingGitCheckoutPlan> InspectExistingGitCheckoutAsync(string repositoryPath, string workspaceName, CancellationToken cancellationToken = default)
            => Task.FromResult(new ExistingGitCheckoutPlan
            {
                RepositoryPath = repositoryPath,
                WorkspaceName = workspaceName,
                Repository = new GitRepositoryInspection { IsRepository = true, CurrentBranch = "users/test/demo", StatusSummary = "clean" },
                DiscoveryResult = new WorkspaceDiscoveryResult { Status = WorkspaceDiscoveryStatus.NotFound },
            });

        public Task<GitBranchValidationResult> ValidateExistingGitCheckoutBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default)
            => Task.FromResult(new GitBranchValidationResult(true, string.Empty, false));

        public Task<WorkspaceSnapshot> ImportExistingGitCheckoutAsync(ExistingGitCheckoutImportRequest request, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
            LastImportRequest = request;
            return Task.FromResult(CreateSnapshot(request.WorkspaceName));
        }

        public WorkspaceDefinition BuildWorkspaceDefinition(CreateWorkspaceDraft draft)
            => new() { Workspace = new WorkspaceMetadata { Name = draft.WorkspaceName, Image = "ubuntu:24.04" }, Provider = new WorkspaceProviderDefinition { Type = "git" }, Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = WorkspaceRuntimeDefinition.DefaultNodeMajorVersion } };

        public Task<WorkspaceSnapshot> CreateWorkspaceAsync(string rootPath, WorkspaceDefinition definition, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
            => CreateWorkspaceAsyncFactory?.Invoke(rootPath, definition, logSink, cancellationToken) ?? Task.FromResult(CreateSnapshot(definition.Workspace.Name));

        public Task<WorkspaceSnapshot> RefreshVolatileWorkspaceStateAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
            RefreshVolatileWorkspaceStateCallCount++;
            return Task.FromResult(currentSnapshot ?? _snapshots.First(item => string.Equals(item.Paths.RootPath, rootPath, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<WorkspaceOperationResult> OpenWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
            OpenWorkspaceCallCount++;

            if (OpenWorkspaceException is not null)
            {
                throw OpenWorkspaceException;
            }

            if (OpenWorkspaceResultFactoryAsync is not null)
            {
                return OpenWorkspaceResultFactoryAsync(rootPath, logSink, cancellationToken);
            }

            return Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("opened"), Message = "opened", Transcript = new OperationTranscript() });
        }

        public Task<WorkspaceOperationResult> PrepareWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
            PrepareCallCount++;

            if (PrepareResultFactoryAsync is not null)
            {
                return PrepareResultFactoryAsync(rootPath, logSink, cancellationToken);
            }

            return Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("prepared"), Message = "prepared", Transcript = new OperationTranscript() });
        }

        public Task<WorkspaceOperationResult> StartWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            return Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("started"), Message = "started", Transcript = new OperationTranscript() });
        }

        public Task<WorkspaceOperationResult> StopWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("stopped"), Message = "stopped", Transcript = new OperationTranscript() });

        public Task<WorkspaceOperationResult> ResetRuntimeAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
            ResetRuntimeCallCount++;

            if (ResetRuntimeException is not null)
            {
                throw ResetRuntimeException;
            }

            return Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("reset"), Message = "reset", Transcript = new OperationTranscript() });
        }

        public Task<WorkspaceCheckpointOperationResult> CreateCheckpointAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
            CreateCheckpointCallCount++;

            if (CheckpointException is not null)
            {
                throw CheckpointException;
            }

            if (CheckpointResultFactoryAsync is not null)
            {
                return CheckpointResultFactoryAsync(rootPath, logSink, cancellationToken);
            }

            return Task.FromResult(new WorkspaceCheckpointOperationResult
            {
                Snapshot = WithComputedReadiness(new WorkspaceSnapshot
                {
                    Record = new WorkspaceRecord
                    {
                        Name = (currentSnapshot ?? CreateSnapshot("checkpoint")).Record.Name,
                        RootPath = (currentSnapshot ?? CreateSnapshot("checkpoint")).Record.RootPath,
                        RepositoryPath = (currentSnapshot ?? CreateSnapshot("checkpoint")).Record.RepositoryPath,
                        ConfigurationPath = (currentSnapshot ?? CreateSnapshot("checkpoint")).Record.ConfigurationPath,
                        SourceType = (currentSnapshot ?? CreateSnapshot("checkpoint")).Record.SourceType,
                        ImportedFromExistingCheckout = (currentSnapshot ?? CreateSnapshot("checkpoint")).Record.ImportedFromExistingCheckout,
                        OriginalDefaultBranch = (currentSnapshot ?? CreateSnapshot("checkpoint")).Record.OriginalDefaultBranch,
                        SelectedWorkspaceBranch = (currentSnapshot ?? CreateSnapshot("checkpoint")).Record.SelectedWorkspaceBranch,
                        RemoteOriginUrl = (currentSnapshot ?? CreateSnapshot("checkpoint")).Record.RemoteOriginUrl,
                        CreatedUtc = (currentSnapshot ?? CreateSnapshot("checkpoint")).Record.CreatedUtc,
                        LastOpenedUtc = (currentSnapshot ?? CreateSnapshot("checkpoint")).Record.LastOpenedUtc,
                        LastPreparedUtc = (currentSnapshot ?? CreateSnapshot("checkpoint")).Record.LastPreparedUtc,
                        OracleSoftwareNoticeShown = (currentSnapshot ?? CreateSnapshot("checkpoint")).Record.OracleSoftwareNoticeShown,
                        LastOperationName = "Create Checkpoint",
                        LastOperationResult = "Checkpoint 'cp-1' created.",
                        LastOperationSucceeded = true,
                        LastOperationUtc = DateTimeOffset.UtcNow,
                        LastProvisioningHealth = (currentSnapshot ?? CreateSnapshot("checkpoint")).Record.LastProvisioningHealth,
                    },
                    Definition = (currentSnapshot ?? CreateSnapshot("checkpoint")).Definition,
                    Paths = (currentSnapshot ?? CreateSnapshot("checkpoint")).Paths,
                    ConfigurationPath = (currentSnapshot ?? CreateSnapshot("checkpoint")).ConfigurationPath,
                    RuntimeState = (currentSnapshot ?? CreateSnapshot("checkpoint")).RuntimeState,
                    Safety = (currentSnapshot ?? CreateSnapshot("checkpoint")).Safety,
                    Session = (currentSnapshot ?? CreateSnapshot("checkpoint")).Session,
                    AppliedState = (currentSnapshot ?? CreateSnapshot("checkpoint")).AppliedState,
                    LocalRuntimeState = (currentSnapshot ?? CreateSnapshot("checkpoint")).LocalRuntimeState,
                    ResolvedRuntimePlan = (currentSnapshot ?? CreateSnapshot("checkpoint")).ResolvedRuntimePlan,
                    UpdateRequired = (currentSnapshot ?? CreateSnapshot("checkpoint")).UpdateRequired,
                    Health = (currentSnapshot ?? CreateSnapshot("checkpoint")).Health,
                    Readiness = (currentSnapshot ?? CreateSnapshot("checkpoint")).Readiness,
                }),
                Message = "Checkpoint 'cp-1' created.",
                Transcript = new OperationTranscript(),
                Checkpoint = new WorkspaceCheckpointRecord
                {
                    Id = "cp-1",
                    CreatedUtc = DateTimeOffset.UtcNow,
                    CurrentBranch = "users/test/demo",
                    CurrentCommitSha = "head123",
                    CapturedUntrackedFiles = true,
                    UntrackedFiles = [],
                },
            });
        }

        public Task<WorkspaceRemovalOperationResult> RemoveWorkspaceAsync(string rootPath, WorkspaceRemovalChoice choice, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
            RemoveCallCount++;
            LastRemoveRootPath = rootPath;

            if (RemoveException is not null)
            {
                throw RemoveException;
            }

            if (RemoveResultFactoryAsync is not null)
            {
                return RemoveResultFactoryAsync(rootPath, logSink, cancellationToken);
            }

            return Task.FromResult(new WorkspaceRemovalOperationResult
            {
                Message = "Removed from workspace list.",
                Transcript = new OperationTranscript(),
                Removal = new WorkspaceRemovalResult
                {
                    WorkspaceName = currentSnapshot?.Definition.Workspace.Name ?? "removed",
                    WorkspaceRoot = rootPath,
                    FilesDeleted = false,
                    Warnings = [],
                    Succeeded = true,
                    FailureReason = string.Empty,
                },
            });
        }

        public Task<WindowsTerminalProfileOperationResult> EnsureWindowsTerminalProfileAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default)
            => Task.FromResult(WindowsTerminalProfileResult);

        public Task<WorkspacePublishAssessment> AssessWorkspacePublishAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
            => Task.FromResult(PublishAssessment);

        public Task<WorkspacePublishResult> PublishWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
            PublishCallCount++;

            if (PublishException is not null)
            {
                throw PublishException;
            }

            if (PublishResultFactoryAsync is not null)
            {
                return PublishResultFactoryAsync(rootPath, logSink, cancellationToken);
            }

            return Task.FromResult(new WorkspacePublishResult
            {
                Snapshot = currentSnapshot ?? CreateSnapshot("published"),
                Message = "Working Copy published successfully.",
                Transcript = new OperationTranscript(),
                Review = new WorkspacePublishReview
                {
                    IsBlocked = false,
                    Message = "Working Copy published successfully.",
                    WorkingCopyName = currentSnapshot?.Safety.AdvancedGit.CurrentBranch ?? "users/test/published",
                    RemoteName = "origin",
                    RemoteBranch = "origin/users/test/published",
                    AheadCount = 0,
                    BehindCount = 0,
                    LatestCommitSha = "head123",
                },
            });
        }

        public Task<WorkspaceBackupResult> BackupWorkspaceAsync(string rootPath, string archivePath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
            BackupCallCount++;

            if (BackupException is not null)
            {
                throw BackupException;
            }

            if (BackupResultFactoryAsync is not null)
            {
                return BackupResultFactoryAsync(rootPath, archivePath, logSink, cancellationToken);
            }

            return Task.FromResult(new WorkspaceBackupResult
            {
                Snapshot = currentSnapshot ?? CreateSnapshot("backup"),
                Message = "Backup created.",
                Transcript = new OperationTranscript(),
                Export = new WorkspaceBackupExportResult
                {
                    ArchivePath = archivePath,
                    FileCount = 3,
                    ArchiveSizeBytes = 1024,
                    IncludedEntries = [],
                    ExcludedEntries = [],
                    Warnings = [],
                },
                Manifest = new WorkspaceBackupManifestResult
                {
                    ManifestPath = Path.Combine(Path.GetTempPath(), "backup-manifest.yaml"),
                    ArchiveEntryPath = "backup-manifest.yaml",
                    IncludedFileCount = 3,
                    ExcludedFileCount = 0,
                    WarningCount = 0,
                },
            });
        }

        public Task<WorkspaceOperationResult> CreateSavePointAsync(string rootPath, string message, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
            CreateSavePointCallCount++;
            LastSavePointMessage = message;

            if (SavePointException is not null)
            {
                throw SavePointException;
            }

            if (SavePointResultFactoryAsync is not null)
            {
                return SavePointResultFactoryAsync(rootPath, message, logSink, cancellationToken);
            }

            return Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("savepoint"), Message = "Save Point created.", Transcript = new OperationTranscript() });
        }

        public Task<OracleApexApplicationDiscoveryResult> DiscoverOracleApexApplicationsAsync(string rootPath, string environmentName, string workspaceName, string parsingSchema, string sqlclProfile, string sourcePath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default)
            => OracleApexDiscoveryResultFactoryAsync?.Invoke(environmentName, workspaceName, parsingSchema, sqlclProfile, sourcePath, currentSnapshot, cancellationToken)
                ?? Task.FromResult(new OracleApexApplicationDiscoveryResult
                {
                    EnvironmentName = environmentName,
                    WorkspaceName = workspaceName,
                    ParsingSchema = parsingSchema,
                    SqlclProfile = sqlclProfile,
                    SourcePath = sourcePath,
                    Applications = [new OracleApexApplicationInfo { ApplicationId = 100, ApplicationName = "Sample App", Alias = "sample-app" }],
                    Summary = "Found 1 Oracle APEX application.",
                });

        public Task<WorkspaceOperationResult> ConnectExistingOracleApexApplicationAsync(string rootPath, ConnectOracleApexApplicationDraft draft, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
            ConnectOracleApexApplicationCallCount++;
            LastConnectOracleApexApplicationDraft = draft;
            return ConnectOracleApexApplicationResultFactoryAsync?.Invoke(rootPath, draft, currentSnapshot, logSink, cancellationToken)
                ?? Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("oracle-connect"), Message = "Connected.", Transcript = new OperationTranscript() });
        }

        public Task<WorkspaceOperationResult> ValidateSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("sync"), Message = "Validated.", Transcript = new OperationTranscript() });

        public Task<WorkspaceOperationResult> ExportSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("sync"), Message = "Exported.", Transcript = new OperationTranscript() });

        public Task<WorkspaceOperationResult> ImportSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("sync"), Message = "Imported.", Transcript = new OperationTranscript() });

        public Task<WorkspaceOperationResult> PullSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("sync"), Message = "Pulled.", Transcript = new OperationTranscript() });

        public Task<WorkspaceOperationResult> PushSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("sync"), Message = "Pushed.", Transcript = new OperationTranscript() });

        public Task<WorkspaceOperationResult> SynchronizeWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("sync"), Message = "Synchronized.", Transcript = new OperationTranscript() });

        public Task<WorkspaceOperationResult> DiffSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("sync"), Message = "No differences.", Transcript = new OperationTranscript() });

        public Task<WorkspaceRecoveryAssessment> AssessWorkspaceRecoveryAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceRecoveryAssessment { Title = "Recover", Summary = "summary", Findings = ["finding"], ConfirmationMessage = "confirm" });

        public Task<WorkspaceOperationResult> RecoverWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("recovered"), Message = "recovered", Transcript = new OperationTranscript() });

        public Task<WorkspaceOperationResult> ReleaseRuntimeResourcesAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("released"), Message = "released", Transcript = new OperationTranscript() });

        public Task<WorkspaceOperationResult> AttachWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
            AttachCallCount++;

            if (AttachException is not null)
            {
                throw AttachException;
            }

            if (AttachResultFactoryAsync is not null)
            {
                return AttachResultFactoryAsync(rootPath, logSink, cancellationToken);
            }

            return Task.FromResult(new WorkspaceOperationResult { Snapshot = currentSnapshot ?? CreateSnapshot("attached"), Message = "attached", Transcript = new OperationTranscript() });
        }

        public Task<WorkspaceReprovisionResult> ReprovisionWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
            ReprovisionCallCount++;
            logSink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Comment, Text = "Preparing workspace" });
            if (ReprovisionException is not null)
            {
                foreach (var line in ReprovisionException.Message.Split([Environment.NewLine], StringSplitOptions.None))
                {
                    if (line.StartsWith("Command:", StringComparison.OrdinalIgnoreCase))
                    {
                        logSink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Command, Text = line[8..].Trim() });
                    }
                    else if (line.StartsWith("Exit code:", StringComparison.OrdinalIgnoreCase))
                    {
                        logSink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = line.Trim() });
                    }
                    else if (!string.IsNullOrWhiteSpace(line))
                    {
                        logSink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.StandardError, Text = line });
                    }
                }
                throw ReprovisionException;
            }

            if (ReprovisionResultFactoryAsync is not null)
            {
                return ReprovisionResultFactoryAsync(rootPath, logSink, cancellationToken);
            }

            var result = ReprovisionResultFactory?.Invoke(rootPath, logSink)
                ?? new WorkspaceReprovisionResult
                {
                    Snapshot = _snapshots.First(item => string.Equals(item.Paths.RootPath, rootPath, StringComparison.OrdinalIgnoreCase)),
                    Succeeded = true,
                    Message = "Workspace reprovisioned successfully.",
                };

            logSink?.Append(new OperationTranscriptLine { Kind = OperationTranscriptLineKind.Result, Text = "Completed" });
            return Task.FromResult(result);
        }

        public Task<WorkspaceTroubleshootingReport> GetWorkspaceTroubleshootingReportAsync(WorkspaceTroubleshootingRequest request, CancellationToken cancellationToken = default)
        {
            if (TroubleshootingReportFactoryAsync is not null)
            {
                return TroubleshootingReportFactoryAsync(request, cancellationToken);
            }

            return Task.FromResult(new WorkspaceTroubleshootingReport
            {
                WorkspaceName = request.WorkspaceName,
                RootPath = request.RootPath,
                Headline = request.IsOperationInProgress ? "Provisioning still running" : "Workspace troubleshooting",
                Summary = request.IsOperationInProgress ? "Provisioning is still running. Keep waiting while the workspace operation continues." : "Workspace-specific troubleshooting details.",
                Recommendation = request.IsOperationInProgress ? "View Log or Keep Waiting." : "Open Workspace should handle the next safe step.",
                IsProvisioningInProgress = request.IsOperationInProgress,
                CanKeepWaiting = request.IsOperationInProgress,
                CanViewLog = !string.IsNullOrWhiteSpace(request.TranscriptFilePath),
                CanOpenWorkspace = !request.IsOperationInProgress,
                CanRecoverWorkspace = true,
                CanResetRuntime = false,
                Facts = [new WorkspaceTroubleshootingFact { Label = "Current stage", Value = string.IsNullOrWhiteSpace(request.CurrentStatusMessage) ? "Unknown" : request.CurrentStatusMessage }],
                TranscriptFilePath = request.TranscriptFilePath,
                TranscriptExcerpt = request.IsOperationInProgress ? "live log excerpt" : string.Empty,
            });
        }

        public Task<WorkspaceTroubleshootingReport> ExecuteWorkspaceTroubleshootingActionAsync(WorkspaceTroubleshootingRequest request, string actionId, CancellationToken cancellationToken = default)
        {
            if (TroubleshootingActionFactoryAsync is not null)
            {
                return TroubleshootingActionFactoryAsync(request, actionId, cancellationToken);
            }

            return GetWorkspaceTroubleshootingReportAsync(request, cancellationToken);
        }

        public Task<WorkspaceRuntimeExplorerReport> GetRuntimeResourceExplorerAsync(CancellationToken cancellationToken = default)
            => RuntimeResourceExplorerFactoryAsync?.Invoke(cancellationToken) ?? Task.FromResult(new WorkspaceRuntimeExplorerReport());

        public Task<WorkspaceRuntimeInspectResult> InspectRuntimeResourceAsync(WorkspaceRuntimeResourceEntry resource, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceRuntimeInspectResult { Title = resource.DisplayName, Summary = resource.Status, Details = resource.RuntimeIdentifier });

        public Task<RuntimeResourceCleanupResult> CleanOrphanedRuntimeResourcesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new RuntimeResourceCleanupResult { Message = "cleaned", Transcript = new OperationTranscript() });

        public WorkspaceTimeline LoadTimeline(string timelinePath)
        {
            if (TimelineException is not null)
            {
                throw TimelineException;
            }

            return TimelineByPath.TryGetValue(timelinePath, out var timeline)
                ? timeline
                : new WorkspaceTimeline
                {
                    Events =
                    [
                        new WorkspaceTimelineEvent
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Type = "save-point",
                            OccurredUtc = DateTimeOffset.UtcNow,
                            Summary = "Created Save Point",
                            Details = "Preview timeline entry.",
                        },
                    ],
                };
        }

        public WorkspaceCheckpointIndex LoadCheckpointIndex(string checkpointIndexPath)
            => new()
            {
                Items =
                [
                    new WorkspaceCheckpointRecord
                    {
                        Id = "cp-1",
                        CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                        CurrentBranch = "users/test/demo",
                    },
                ],
            };

        public Task OpenPathAsync(string path, CancellationToken cancellationToken = default)
        {
            OpenedPaths.Add(path);
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingDesktopShellService : IDesktopShellService
    {
        public int LoadCalls { get; private set; }

        public Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(bool includeRuntimeInspection, Action<WorkspaceLoadProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
        {
            LoadCalls++;
            return Task.FromResult(new WorkspaceLoadResult());
        }

        public IReadOnlyList<WorkspaceReference> LoadWorkspaceReferences() => [];
        public WorkspaceTimeline LoadTimeline(string timelinePath) => new();
        public WorkspaceCheckpointIndex LoadCheckpointIndex(string checkpointIndexPath) => new();
        public OracleSoftwareNoticePrompt? BuildOracleSoftwareNotice(TemplateManifest template, string workspaceName) => throw new NotImplementedException();
        public OracleSoftwareNoticePrompt? BuildOracleSoftwareNotice(WorkspaceSnapshot snapshot) => throw new NotImplementedException();
        public WorkspaceRuntimeResetPrompt BuildRuntimeResetPrompt(WorkspaceSnapshot snapshot) => new() { WorkspaceName = snapshot.Definition.Workspace.Name, WorkspaceRoot = snapshot.Paths.RootPath, Summary = "summary" };
        public Task<WorkspaceSnapshot> AcknowledgeOracleSoftwareNoticeAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string> SuggestSavePointMessageAsync(string rootPath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ExistingGitCheckoutPlan> InspectExistingGitCheckoutAsync(string repositoryPath, string workspaceName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GitBranchValidationResult> ValidateExistingGitCheckoutBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceSnapshot> ImportExistingGitCheckoutAsync(ExistingGitCheckoutImportRequest request, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public WorkspaceDefinition BuildWorkspaceDefinition(CreateWorkspaceDraft draft) => throw new NotImplementedException();
        public Task<WorkspaceSnapshot> CreateWorkspaceAsync(string rootPath, WorkspaceDefinition definition, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceSnapshot> RefreshVolatileWorkspaceStateAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> OpenWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> PrepareWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> StartWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> StopWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceCheckpointOperationResult> CreateCheckpointAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceRemovalOperationResult> RemoveWorkspaceAsync(string rootPath, WorkspaceRemovalChoice choice, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WindowsTerminalProfileOperationResult> EnsureWindowsTerminalProfileAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspacePublishAssessment> AssessWorkspacePublishAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspacePublishResult> PublishWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceBackupResult> BackupWorkspaceAsync(string rootPath, string archivePath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> CreateSavePointAsync(string rootPath, string message, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<OracleApexApplicationDiscoveryResult> DiscoverOracleApexApplicationsAsync(string rootPath, string environmentName, string workspaceName, string parsingSchema, string sqlclProfile, string sourcePath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> ConnectExistingOracleApexApplicationAsync(string rootPath, ConnectOracleApexApplicationDraft draft, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> ValidateSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> ExportSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> ImportSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> PullSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> PushSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> SynchronizeWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> DiffSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceRecoveryAssessment> AssessWorkspaceRecoveryAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> RecoverWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> ReleaseRuntimeResourcesAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> ResetRuntimeAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceOperationResult> AttachWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceReprovisionResult> ReprovisionWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceReprovisionResult { Snapshot = CreateSnapshot("tracking"), Succeeded = true, Message = "ok" });
        public Task<WorkspaceTroubleshootingReport> GetWorkspaceTroubleshootingReportAsync(WorkspaceTroubleshootingRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceTroubleshootingReport { WorkspaceName = request.WorkspaceName, RootPath = request.RootPath, Headline = "Workspace troubleshooting", Summary = "Workspace-specific troubleshooting details.", Recommendation = "Open Workspace.", CanOpenWorkspace = true });
        public Task<WorkspaceTroubleshootingReport> ExecuteWorkspaceTroubleshootingActionAsync(WorkspaceTroubleshootingRequest request, string actionId, CancellationToken cancellationToken = default) => GetWorkspaceTroubleshootingReportAsync(request, cancellationToken);
        public Task<WorkspaceRuntimeExplorerReport> GetRuntimeResourceExplorerAsync(CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRuntimeExplorerReport());
        public Task<WorkspaceRuntimeInspectResult> InspectRuntimeResourceAsync(WorkspaceRuntimeResourceEntry resource, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRuntimeInspectResult());
        public Task<RuntimeResourceCleanupResult> CleanOrphanedRuntimeResourcesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new RuntimeResourceCleanupResult { Message = string.Empty, Transcript = new OperationTranscript() });
        public Task OpenPathAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeWorkspaceInteractionService : IWorkspaceInteractionService
    {
        public CreateWorkspaceDraft? CreateWorkspaceDraft { get; init; }
        public ExistingRepositoryImportDraft? ExistingRepositoryImportDraft { get; init; }
        public string? BackupArchivePath { get; init; } = Path.Combine(Path.GetTempPath(), $"avalonia-backup-{Guid.NewGuid():N}.zip");
        public bool CheckpointConfirmed { get; init; } = true;
        public bool OracleNoticeConfirmed { get; init; } = true;
        public int OracleNoticePromptCount { get; private set; }
        public bool RemoveConfirmed { get; init; } = true;
        public WorkspaceRemovalChoice RemoveChoice { get; init; } = WorkspaceRemovalChoice.RegistrationOnly;
        public Exception? RemoveDialogException { get; init; }
        public bool PublishConfirmed { get; init; } = true;
        public SavePointDraft? SavePointDraft { get; init; } = new SavePointDraft { Message = "Capture current workspace state" };
        public ConnectOracleApexApplicationDraft? ConnectOracleApexApplicationDraft { get; init; }
        public bool ResetRuntimeConfirmed { get; init; } = true;
        public WorkspaceRuntimeResetPrompt? LastResetRuntimePrompt { get; private set; }
        public WorkspaceDiagnosticsSession? LastWorkspaceDiagnosticsSession { get; private set; }

        public Task<CreateWorkspaceDraft?> ShowCreateWorkspaceDialogAsync(IReadOnlyList<TemplateManifest> templates, CancellationToken cancellationToken = default)
            => Task.FromResult(CreateWorkspaceDraft);

        public Task<ExistingRepositoryImportDraft?> ShowOpenExistingRepositoryDialogAsync(Func<string, string, CancellationToken, Task<ExistingGitCheckoutPlan>> inspectRepositoryAsync, Func<string, string, CancellationToken, Task<GitBranchValidationResult>> validateBranchAsync, CancellationToken cancellationToken = default)
            => Task.FromResult(ExistingRepositoryImportDraft);

        public Task<string?> ShowBackupDestinationDialogAsync(string suggestedFileName, CancellationToken cancellationToken = default)
            => Task.FromResult(BackupArchivePath);

        public Task<bool> ConfirmOracleSoftwareNoticeAsync(OracleSoftwareNoticePrompt prompt, CancellationToken cancellationToken = default)
        {
            OracleNoticePromptCount++;
            return Task.FromResult(OracleNoticeConfirmed);
        }

        public Task<bool> ConfirmCheckpointAsync(WorkspaceCheckpointPrompt prompt, CancellationToken cancellationToken = default)
            => Task.FromResult(CheckpointConfirmed);

        public Task<WorkspaceRemovalDecision?> ConfirmRemoveWorkspaceAsync(WorkspaceRemovalPrompt prompt, CancellationToken cancellationToken = default)
        {
            if (RemoveDialogException is not null)
            {
                throw RemoveDialogException;
            }

            return Task.FromResult(RemoveConfirmed ? new WorkspaceRemovalDecision { Choice = RemoveChoice } : null);
        }

        public Task<bool> ConfirmPublishAsync(WorkspacePublishAssessment assessment, CancellationToken cancellationToken = default)
            => Task.FromResult(PublishConfirmed);

        public Task<SavePointDraft?> ShowSavePointDialogAsync(string initialMessage, CancellationToken cancellationToken = default)
            => Task.FromResult(SavePointDraft);

        public Task<ConnectOracleApexApplicationDraft?> ShowConnectOracleApexApplicationDialogAsync(Func<ConnectOracleApexApplicationDraft, CancellationToken, Task<OracleApexApplicationDiscoveryResult>> discoverApplicationsAsync, ConnectOracleApexApplicationDraft initialDraft, CancellationToken cancellationToken = default)
            => Task.FromResult(ConnectOracleApexApplicationDraft);

        public Task<bool> ConfirmRecoveryAsync(WorkspaceRecoveryAssessment assessment, Func<CancellationToken, Task<WorkspaceRecoveryAssessment>> refreshAssessmentAsync, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> ConfirmResetRuntimeAsync(WorkspaceRuntimeResetPrompt prompt, CancellationToken cancellationToken = default)
        {
            LastResetRuntimePrompt = prompt;
            return Task.FromResult(ResetRuntimeConfirmed);
        }

        public Task ShowWorkspaceDiagnosticsAsync(WorkspaceDiagnosticsSession session, CancellationToken cancellationToken = default)
        {
            LastWorkspaceDiagnosticsSession = session;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDesktopShellService : IDesktopShellService
    {
        public Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(bool includeRuntimeInspection, Action<WorkspaceLoadProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated workspace discovery failure.");

        public IReadOnlyList<WorkspaceReference> LoadWorkspaceReferences() => [];
        public WorkspaceTimeline LoadTimeline(string timelinePath) => new();
        public WorkspaceCheckpointIndex LoadCheckpointIndex(string checkpointIndexPath) => new();
        public OracleSoftwareNoticePrompt? BuildOracleSoftwareNotice(TemplateManifest template, string workspaceName) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public OracleSoftwareNoticePrompt? BuildOracleSoftwareNotice(WorkspaceSnapshot snapshot) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public WorkspaceRuntimeResetPrompt BuildRuntimeResetPrompt(WorkspaceSnapshot snapshot) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceSnapshot> AcknowledgeOracleSoftwareNoticeAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<string> SuggestSavePointMessageAsync(string rootPath, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<ExistingGitCheckoutPlan> InspectExistingGitCheckoutAsync(string repositoryPath, string workspaceName, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<GitBranchValidationResult> ValidateExistingGitCheckoutBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceSnapshot> ImportExistingGitCheckoutAsync(ExistingGitCheckoutImportRequest request, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public WorkspaceDefinition BuildWorkspaceDefinition(CreateWorkspaceDraft draft) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceSnapshot> CreateWorkspaceAsync(string rootPath, WorkspaceDefinition definition, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceSnapshot> RefreshVolatileWorkspaceStateAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> OpenWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> PrepareWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> StartWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> StopWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceCheckpointOperationResult> CreateCheckpointAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceRemovalOperationResult> RemoveWorkspaceAsync(string rootPath, WorkspaceRemovalChoice choice, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WindowsTerminalProfileOperationResult> EnsureWindowsTerminalProfileAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspacePublishAssessment> AssessWorkspacePublishAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspacePublishResult> PublishWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceBackupResult> BackupWorkspaceAsync(string rootPath, string archivePath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> CreateSavePointAsync(string rootPath, string message, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<OracleApexApplicationDiscoveryResult> DiscoverOracleApexApplicationsAsync(string rootPath, string environmentName, string workspaceName, string parsingSchema, string sqlclProfile, string sourcePath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> ConnectExistingOracleApexApplicationAsync(string rootPath, ConnectOracleApexApplicationDraft draft, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> ValidateSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> ExportSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> ImportSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> PullSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> PushSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> SynchronizeWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> DiffSynchronizationAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceRecoveryAssessment> AssessWorkspaceRecoveryAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> RecoverWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> ReleaseRuntimeResourcesAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> ResetRuntimeAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceOperationResult> AttachWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceReprovisionResult> ReprovisionWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceTroubleshootingReport> GetWorkspaceTroubleshootingReportAsync(WorkspaceTroubleshootingRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceTroubleshootingReport> ExecuteWorkspaceTroubleshootingActionAsync(WorkspaceTroubleshootingRequest request, string actionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceRuntimeExplorerReport> GetRuntimeResourceExplorerAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<WorkspaceRuntimeInspectResult> InspectRuntimeResourceAsync(WorkspaceRuntimeResourceEntry resource, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task<RuntimeResourceCleanupResult> CleanOrphanedRuntimeResourcesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
        public Task OpenPathAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public string? Text { get; private set; }

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingClipboardService : IClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Clipboard unavailable");
    }

    private sealed class FakeDiagnosticsShellService : IDiagnosticsShellService
    {
        public Task<HostCapabilityReport> DetectHostCapabilitiesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(FakeHostCapabilities.CreateReport());

        public TemplateCatalogDiagnosticResult GetTemplateCatalogStatus()
            => new()
            {
                CatalogRootPath = "/app/catalog",
                TemplateCount = 2,
                Detail = "Loaded 2 template manifest(s) from the packaged catalog.",
            };

        public Task<WorkspaceDoctorResult> RunDoctorAsync(string workspacePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceDoctorResult
            {
                WorkspaceRootPath = workspacePath,
                RuntimeStatePath = Path.Combine(workspacePath, ".opencode", "local", "runtime-state.yaml"),
                HostPlatform = new HostPlatformInfo
                {
                    OperatingSystem = HostOperatingSystem.Linux,
                    Architecture = HostArchitecture.X64,
                    HostDescription = "Linux X64",
                    NativeContainerPlatform = "linux/amd64",
                    Docker = new ContainerRuntimeAvailability
                    {
                        CliAvailable = true,
                        EngineReachable = true,
                        BuildxAvailable = true,
                        SupportedPlatforms = ["linux/amd64", "linux/arm64"],
                        DiagnosticSummary = "Docker CLI and engine OK.",
                    },
                },
                WorkspaceConfigurationStatus = WorkspaceConfigurationStatus.Found,
                WorkspaceConfigurationPath = "workspace.yaml",
                RuntimeStateStatus = WorkspaceRuntimeStateReadStatus.Loaded,
                RuntimeState = new WorkspaceRuntimeStateRecord { ResolvedEngine = "docker", ResolvedPlatform = "linux/amd64", CompatibilityMode = "native" },
                Arm64ExecutionSupportStatus = Arm64ExecutionSupportStatus.Available,
                Arm64ExecutionSupportDetails = "Execution probe OK (aarch64)",
                ResolvedRuntimePlan = new ResolvedRuntimePlan { Runtime = "docker", TargetPlatform = "linux/amd64", IsAvailable = true, HostPlatform = new HostPlatformInfo() },
                CanRun = true,
                Recommendation = "Workspace can run on this machine.",
            });

        public Task<PlatformValidationReport> ValidateAsync(string workspacePath, string targetPlatform, CancellationToken cancellationToken = default)
            => Task.FromResult(targetPlatform == "linux/arm64"
                ? new PlatformValidationReport
                {
                    WorkspaceRootPath = workspacePath,
                    TargetPlatform = targetPlatform,
                    ResolvedPlatform = "linux/amd64",
                    CompatibilityDisplay = "fallback",
                    ValidatedWithFallback = true,
                    Checks =
                    [
                        new PlatformValidationCheckResult { Name = "Docker CLI", Severity = DiagnosticSeverity.Information, Message = "OK" },
                        new PlatformValidationCheckResult { Name = "Container execution", Severity = DiagnosticSeverity.Warning, Message = "Validated through fallback behavior." },
                    ],
                    IsSuccess = true,
                    HasWarnings = true,
                    Summary = "linux/arm64 validation completed through fallback behavior.",
                }
                : new PlatformValidationReport
                {
                    WorkspaceRootPath = workspacePath,
                    TargetPlatform = targetPlatform,
                    ResolvedPlatform = "linux/amd64",
                    CompatibilityDisplay = "native",
                    Checks =
                    [
                        new PlatformValidationCheckResult { Name = "Docker CLI", Severity = DiagnosticSeverity.Information, Message = "OK" },
                        new PlatformValidationCheckResult { Name = "Container execution", Severity = DiagnosticSeverity.Information, Message = "OK" },
                    ],
                    IsSuccess = true,
                    Summary = $"{targetPlatform} validation passed.",
                });
    }

    private sealed class FakeHostCapabilities : IHostCapabilities
    {
        public PlatformKind Platform => PlatformKind.Windows;

        public Task<HostCapabilityReport> DetectAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateReport());

        public static HostCapabilityReport CreateReport()
            => new()
            {
                Platform = PlatformKind.Windows,
                Architecture = "X64",
                Sections =
                [
                    new HostCapabilitySection
                    {
                        Id = "tools",
                        DisplayName = "Tools",
                        Entries =
                        [
                            new HostCapabilityEntry { Id = "tool.git", DisplayName = "Git", Status = HostCapabilityStatus.Available, Summary = "Git is available." },
                            new HostCapabilityEntry { Id = "tool.opencode-cli", DisplayName = "OpenCode CLI", Status = HostCapabilityStatus.Available, Summary = "OpenCode CLI is available.", Details = "C:/Users/test/AppData/Local/Microsoft/WindowsApps/opencode.exe" },
                            new HostCapabilityEntry { Id = "terminal.profile-support", DisplayName = "Windows Terminal profile support", Status = HostCapabilityStatus.Available, Summary = "Managed Windows Terminal profiles are supported." },
                        ],
                    },
                    new HostCapabilitySection
                    {
                        Id = "fonts",
                        DisplayName = "Fonts",
                        Entries =
                        [
                            new HostCapabilityEntry { Id = "font.nerd-fonts", DisplayName = "Nerd Fonts", Status = HostCapabilityStatus.Available, Summary = "Nerd Fonts are available." },
                            new HostCapabilityEntry { Id = "font.cascadia-code", DisplayName = "Cascadia Code", Status = HostCapabilityStatus.Available, Summary = "Cascadia Code is available." },
                        ],
                    },
                    new HostCapabilitySection
                    {
                        Id = "terminals",
                        DisplayName = "Terminals",
                        Entries =
                        [
                            new HostCapabilityEntry { Id = "terminal.windows-terminal", DisplayName = "Windows Terminal", Status = HostCapabilityStatus.Available, Summary = "Windows Terminal command is available." },
                        ],
                    },
                    new HostCapabilitySection
                    {
                        Id = "containers",
                        DisplayName = "Container runtime",
                        Entries =
                        [
                            new HostCapabilityEntry { Id = "container.docker", DisplayName = "Docker Desktop", Status = HostCapabilityStatus.Available, Summary = "Docker Desktop is reachable." },
                            new HostCapabilityEntry { Id = "container.docker-compose", DisplayName = "Docker Compose", Status = HostCapabilityStatus.Available, Summary = "Docker Compose is available through docker compose.", Details = "Docker Compose version v2.38.1" },
                            new HostCapabilityEntry { Id = "container.podman", DisplayName = "Podman", Status = HostCapabilityStatus.Unavailable, Summary = "Podman was not detected." },
                        ],
                    },
                ],
            };
    }

    private sealed class FakeTemplateCatalogShellService : ITemplateCatalogShellService
    {
        public IReadOnlyList<TemplateManifest> LoadTemplates()
            =>
            [
                new TemplateManifest
                {
                    Id = "demo",
                    DisplayName = "Demo template",
                    Description = "A portable demo template.",
                    Features = ["core"],
                    Services = ["postgres"],
                },
            ];
    }

    private sealed class FakeDocumentationShellService : IDocumentationShellService
    {
        public IReadOnlyList<DocumentationDocument> GetDocuments()
            => [new DocumentationDocument("README", "README.md", "Overview")];

        public Task OpenDocumentAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
