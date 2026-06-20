using System.Reflection;
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
    public void Navigation_SelectsCorrectPage()
    {
        var shell = CreateShell();

        var diagnostics = shell.NavigationItems.Single(item => item.Title == "Diagnostics");
        diagnostics.SelectCommand.Execute(null);

        Assert.Equal("Diagnostics", shell.CurrentPage.Title);
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
    public void StatusBar_UpdatesWhenWorkspaceSelectionChanges()
    {
        var shell = CreateShell([CreateSnapshot("alpha"), CreateSnapshot("beta")]);

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
        return ShellViewModel.Create(
            desktop,
            new FakeDiagnosticsShellService(),
            new FakeTemplateCatalogShellService(),
            new FakeDocumentationShellService(),
            new ThemeCoordinator(ThemeMode.System),
            CreateAppBuildInfo(),
            "en");
    }

    private static AppBuildInfo CreateAppBuildInfo()
        => new("/tmp/app", "Debug", "1.0.0", "1.0.0-preview", "abcdef123456", DateTimeOffset.UtcNow.ToString("O"), "1.0.0", "workspace-yaml-v1");

    private static WorkspaceSnapshot CreateSnapshot(string name, bool includeRuntimeState = true)
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
                LastOperationResult = "Loaded workspace.",
                LastOperationSucceeded = true,
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
            UpdateRequired = false,
        };
    }

    private sealed class FakeDesktopShellService : IDesktopShellService
    {
        private readonly IReadOnlyList<WorkspaceSnapshot> _snapshots;
        private readonly IReadOnlyList<WorkspaceShellItem> _extraItems;

        public FakeDesktopShellService(IReadOnlyList<WorkspaceSnapshot> snapshots, IReadOnlyList<WorkspaceShellItem>? extraItems = null)
        {
            _snapshots = snapshots;
            _extraItems = extraItems ?? [];
        }

        public Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(bool includeRuntimeInspection, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceLoadResult
            {
                Items = _snapshots.Select(item => new WorkspaceShellItem { Record = item.Record, Snapshot = item }).Concat(_extraItems).ToList(),
                Report = new WorkspaceLoadReport
                {
                    IndexFilePath = WorkspaceAppDataPaths.GetWorkspaceIndexPath(),
                    AppDataRoot = WorkspaceAppDataPaths.GetWorkspaceManagerDataRoot(),
                    RawRecordCount = _snapshots.Count + _extraItems.Count,
                    SnapshotAttemptCount = _snapshots.Count + _extraItems.Count,
                    SnapshotCount = _snapshots.Count,
                    Failures = _extraItems.Select(item => new WorkspaceLoadFailure(string.IsNullOrWhiteSpace(item.Record.Name) ? item.Record.RootPath : item.Record.Name, item.Record.RootPath, item.ErrorMessage)).ToList(),
                    ItemsReturnedCount = _snapshots.Count + _extraItems.Count,
                },
            });

        public IReadOnlyList<WorkspaceReference> LoadWorkspaceReferences()
            => _snapshots.Select(item => new WorkspaceReference(item.Definition.Workspace.Name, item.Paths.RootPath))
                .Concat(_extraItems.Select(item => new WorkspaceReference(item.Record.Name, item.Record.RootPath)))
                .ToList();

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
