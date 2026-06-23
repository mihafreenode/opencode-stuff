using System.Reflection;
using System.Runtime.CompilerServices;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Avalonia.ViewModels;
using OpenCode.Workspace.Core.Models;

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
    public void MainWindowHeader_UsesTrimmedBrandBannerMarkup()
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

        Assert.Contains("Assets/opencode-stuff-satchel-icon.png", axaml, StringComparison.Ordinal);
        Assert.Contains("Assets/opencode-stuff-header-brand-ui.png", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon=\"avares://OpenCode.Workspace.Avalonia/Assets/opencode-stuff-header-brand-ui.png\"", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Assets/opencode-stuff-satchel-transparent.png", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Workspaces\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Runtime\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Status\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"112\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Stretch=\"Uniform\"", axaml, StringComparison.Ordinal);
        Assert.Contains("RenderOptions.BitmapInterpolationMode=\"HighQuality\"", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BorderThickness", headerImageMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("BorderBrush", headerImageMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("Background", headerImageMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("Avalonia Preview", axaml, StringComparison.Ordinal);
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
    }

    [Fact]
    public void ThemeMode_DefaultsToSystem()
    {
        var settings = new SettingsPageViewModel(new ThemeCoordinator(ThemeMode.System), CreateAppBuildInfo());

        Assert.Equal(ThemeMode.System, settings.SelectedThemeMode);
    }

    [Fact]
    public void ThemeMode_CanSwitchToLight()
    {
        var coordinator = new ThemeCoordinator(ThemeMode.System);
        var settings = new SettingsPageViewModel(coordinator, CreateAppBuildInfo());

        settings.SelectedThemeMode = ThemeMode.Light;

        Assert.Equal(ThemeMode.Light, settings.SelectedThemeMode);
        Assert.Equal(ThemeMode.Light, coordinator.CurrentMode);
    }

    [Fact]
    public void ThemeMode_CanSwitchToDark()
    {
        var coordinator = new ThemeCoordinator(ThemeMode.System);
        var settings = new SettingsPageViewModel(coordinator, CreateAppBuildInfo());

        settings.SelectedThemeMode = ThemeMode.Dark;

        Assert.Equal(ThemeMode.Dark, settings.SelectedThemeMode);
        Assert.Equal(ThemeMode.Dark, coordinator.CurrentMode);
    }

    [Fact]
    public async Task WorkspaceList_LoadsFromInjectedServiceModel()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha"), CreateSnapshot("beta")]));

        await page.LoadAsync();

        Assert.Collection(
            page.Workspaces,
            first => Assert.Equal("alpha", first.Name),
            second => Assert.Equal("beta", second.Name));
    }

    [Fact]
    public async Task SelectedWorkspace_UpdatesDetailPanel()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha"), CreateSnapshot("beta") ]));
        await page.LoadAsync();

        page.SelectedWorkspace = page.Workspaces.Last();

        Assert.Equal("beta", page.DetailTitle);
        Assert.Contains(page.DetailItems, item => item.Label == "Repository path" && item.Value.Contains("beta", StringComparison.Ordinal));
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
        Assert.Contains(page.DetailItems, item => item.Label == "Runtime-state status" && item.Value == "Missing");
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
        Assert.Contains(page.DetailItems, item => item.Label == "Load failure" && item.Value.Contains("workspace.yaml missing", StringComparison.Ordinal));
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
        Assert.Contains(page.DoctorItems, item => item.Title == "Docker Engine");
        Assert.Equal("Workspace can run on this machine.", page.StatusMessage);
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
    public async Task DisabledActions_ExposeReasonText()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")]));
        await page.LoadAsync();

        var attach = page.DetailActions.Single(item => item.Label == "Attach");

        Assert.False(attach.IsEnabled);
        Assert.Equal("Unavailable in Avalonia preview. Use WPF or CLI for now.", attach.DisabledReason);
    }

    [Fact]
    public async Task ReprovisionAction_VisibleForSelectedWorkspace()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")]));

        await page.LoadAsync();

        Assert.Contains(page.DetailActions, item => item.Label == "Reprovision");
    }

    [Fact]
    public async Task Reprovision_EnabledWhenWorkspaceCanBeReprovisioned()
    {
        var page = new WorkspacesPageViewModel(new FakeDesktopShellService([CreateSnapshot("alpha")]));

        await page.LoadAsync();

        var reprovision = page.DetailActions.Single(item => item.Label == "Reprovision");
        Assert.True(reprovision.IsEnabled);
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

        var reprovision = page.DetailActions.Single(item => item.Label == "Reprovision");
        Assert.False(reprovision.IsEnabled);
        Assert.Contains("configuration must load", reprovision.DisabledReason, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal("Running", page.SelectedWorkspace?.RuntimeStatusLabel);
        Assert.Equal("Workspace reprovisioned successfully.", page.SelectedWorkspace?.LastActivity);
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
        Assert.Contains("Generating runtime files", page.DetailSummary, StringComparison.Ordinal);

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

        Assert.Equal("Workspace reprovision failed. Exit code: 127. See Operation Log panel.", page.ReprovisionStatusMessage);
        Assert.Equal("Error", page.SelectedWorkspace?.RuntimeStatusLabel);
        Assert.Equal("Workspace reprovision failed. Exit code: 127. See Operation Log panel.", page.SelectedWorkspace?.LastActivity);
        Assert.Contains(page.DetailItems, item => item.Label == "Failure");
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
        Assert.DoesNotContain("/workspace/.env: line 17", page.DetailSummary, StringComparison.Ordinal);
        Assert.Contains("See Operation Log panel.", page.DetailSummary, StringComparison.Ordinal);
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

        Assert.Contains("Runtime state is missing", page.DetailSummary, StringComparison.Ordinal);
        var reprovision = page.DetailActions.Single(item => item.Label == "Reprovision");
        Assert.Contains("Runtime state is missing", reprovision.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusBar_UpdatesWhenWorkspaceSelectionChanges()
    {
        var shell = CreateShell([CreateSnapshot("alpha"), CreateSnapshot("beta")]);
        await shell.InitializeAsync();

        var workspacesPage = (WorkspacesPageViewModel)shell.NavigationItems.Single(item => item.Title == "Workspaces").Page;
        workspacesPage.SelectedWorkspace = workspacesPage.Workspaces.Last();

        Assert.Equal("Workspace: beta", shell.StatusBarWorkspace);
        Assert.Contains("Branch:", shell.StatusBarBranch, StringComparison.Ordinal);
        Assert.Contains("Protection:", shell.StatusBarProtection, StringComparison.Ordinal);
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
    public void AvaloniaAssembly_DoesNotReferenceWpfAssemblies()
    {
        var references = typeof(ShellViewModel).Assembly.GetReferencedAssemblies().Select(item => item.Name).ToArray();

        Assert.DoesNotContain("PresentationFramework", references);
        Assert.DoesNotContain("PresentationCore", references);
        Assert.DoesNotContain("WindowsBase", references);
        Assert.DoesNotContain("OpenCode.Workspace.Manager", references);
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

    private static WorkspaceSnapshot CreateSnapshot(string name, bool includeRuntimeState = true, bool updateRequired = false, string? lastOperationResult = null, bool? lastOperationSucceeded = true)
    {
        var root = Path.Combine(Path.GetTempPath(), $"oc-avalonia-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "mounts", "config"));
        Directory.CreateDirectory(Path.Combine(root, "history", "checkpoints"));
        File.WriteAllText(Path.Combine(root, "workspace.yaml"), "workspace: {}\n");
        File.WriteAllText(Path.Combine(root, "compose.yaml"), "services: {}\n");
        File.WriteAllText(Path.Combine(root, "mounts", "config", "provision.sh"), "#!/bin/bash\n");

        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = name,
                RootPath = root,
                RepositoryPath = root,
                LastOpenedUtc = DateTimeOffset.UtcNow,
                CreatedUtc = DateTimeOffset.UtcNow,
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
                AdvancedGit = new WorkspaceAdvancedGitSnapshot { CurrentBranch = $"users/test/{name}", StatusSummary = "clean" },
            },
            Session = new WorkspaceSessionSnapshot { SessionName = name, State = WorkspaceSessionState.Resumable },
            LocalRuntimeState = includeRuntimeState ? new WorkspaceRuntimeStateRecord { ResolvedEngine = "docker", ResolvedPlatform = "linux/amd64", CompatibilityMode = "native" } : null,
            ResolvedRuntimePlan = new ResolvedRuntimePlan { Runtime = "docker", TargetPlatform = "linux/amd64", IsAvailable = true, HostPlatform = new HostPlatformInfo() },
            UpdateRequired = updateRequired,
        };
    }

    private sealed class FakeDesktopShellService : IDesktopShellService
    {
        private readonly IReadOnlyList<WorkspaceSnapshot> _snapshots;
        private readonly IReadOnlyList<WorkspaceShellItem> _extraItems;
        public Func<bool, Action<WorkspaceLoadProgressUpdate>?, CancellationToken, Task<WorkspaceLoadResult>>? LoadWorkspaceItemsAsyncFactory { get; init; }
        public Func<string, IOperationLogSink?, WorkspaceReprovisionResult>? ReprovisionResultFactory { get; init; }
        public Func<string, IOperationLogSink?, CancellationToken, Task<WorkspaceReprovisionResult>>? ReprovisionResultFactoryAsync { get; init; }
        public Exception? ReprovisionException { get; init; }

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

        public Task<WorkspaceReprovisionResult> ReprovisionWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
        {
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

        public WorkspaceTimeline LoadTimeline(string timelinePath)
            => new()
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

        public Task OpenPathAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
        public Task<WorkspaceReprovisionResult> ReprovisionWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceReprovisionResult { Snapshot = CreateSnapshot("tracking"), Succeeded = true, Message = "ok" });
        public Task OpenPathAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingDesktopShellService : IDesktopShellService
    {
        public Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(bool includeRuntimeInspection, Action<WorkspaceLoadProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated workspace discovery failure.");

        public IReadOnlyList<WorkspaceReference> LoadWorkspaceReferences() => [];
        public WorkspaceTimeline LoadTimeline(string timelinePath) => new();
        public WorkspaceCheckpointIndex LoadCheckpointIndex(string checkpointIndexPath) => new();
        public Task<WorkspaceReprovisionResult> ReprovisionWorkspaceAsync(string rootPath, WorkspaceSnapshot? currentSnapshot = null, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated workspace discovery failure.");
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

    private sealed class FakeDiagnosticsShellService : IDiagnosticsShellService
    {
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
