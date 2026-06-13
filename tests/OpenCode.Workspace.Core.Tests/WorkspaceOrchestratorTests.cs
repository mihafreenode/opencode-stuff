using System.Diagnostics;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceOrchestratorTests
{
    [Fact]
    public void CreateWorkspace_WritesCanonicalAndGeneratedFiles()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core", "document-processing"));

            Assert.True(File.Exists(snapshot.Paths.WorkspaceYamlPath));
            Assert.True(File.Exists(snapshot.Paths.ComposePath));
            Assert.True(File.Exists(snapshot.Paths.EnvironmentFilePath));
            Assert.True(File.Exists(snapshot.Paths.ProvisionScriptPath));
            Assert.True(File.Exists(snapshot.Paths.StarshipConfigPath));
            Assert.True(File.Exists(snapshot.Paths.ShellInitScriptPath));
            Assert.True(File.Exists(snapshot.Paths.OpencodeWorkspaceShellPath));
            Assert.True(File.Exists(snapshot.Paths.ScreenConfigPath));
            Assert.True(File.Exists(snapshot.Paths.AttachWrapperScriptPath));
            Assert.True(File.Exists(snapshot.Paths.TerminalDiagnosticsScriptPath));
            Assert.True(Directory.Exists(Path.Combine(tempRoot, ".git")));
            Assert.True(File.Exists(snapshot.Paths.TimelinePath));
            Assert.True(File.Exists(snapshot.Paths.CheckpointIndexPath));
            Assert.Contains("save-point", File.ReadAllText(snapshot.Paths.TimelinePath));
            Assert.Contains("GENERATED FILE", File.ReadAllText(snapshot.Paths.ComposePath));
            Assert.Contains("npm install -g opencode-ai", File.ReadAllText(snapshot.Paths.ProvisionScriptPath));
            Assert.Contains("/home/opencode/.local/share/opencode/log", File.ReadAllText(snapshot.Paths.ProvisionScriptPath));
            Assert.Contains("Initializing OpenCode user directories", File.ReadAllText(snapshot.Paths.OpencodeWorkspaceShellPath));
            Assert.Equal(WorkspaceSafetyLevel.PartiallyProtected, snapshot.Safety.OverallStatus);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void WriteAppliedState_WritesAppliedStateFile()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));

            orchestrator.WriteAppliedState(snapshot);

            Assert.True(File.Exists(snapshot.Paths.AppliedStatePath));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_AfterAppliedState_DoesNotRequireUpdate()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            orchestrator.WriteAppliedState(snapshot);

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.False(reloaded.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_AfterRuntimeOnlyReload_DoesNotRequireUpdate()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            orchestrator.WriteAppliedState(snapshot);

            var reloaded = orchestrator.LoadSnapshot(tempRoot);
            var runtimeOnlySnapshot = new WorkspaceSnapshot
            {
                Record = reloaded.Record,
                Definition = reloaded.Definition,
                Paths = reloaded.Paths,
                RuntimeState = WorkspaceRuntimeState.Running,
                Safety = reloaded.Safety,
                Session = reloaded.Session,
                AppliedState = reloaded.AppliedState,
                UpdateRequired = reloaded.UpdateRequired,
            };

            Assert.False(runtimeOnlySnapshot.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenWorkspaceYamlChanges_RequiresUpdate()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            orchestrator.WriteAppliedState(snapshot);

            File.WriteAllText(snapshot.Paths.WorkspaceYamlPath, File.ReadAllText(snapshot.Paths.WorkspaceYamlPath).Replace("ubuntu:24.04", "ubuntu:22.04", StringComparison.Ordinal));

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.True(reloaded.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenSelectedFeaturesChange_RequiresUpdate()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            orchestrator.WriteAppliedState(snapshot);

            var updatedDefinition = CreateDefinition("core", "document-processing");
            File.WriteAllText(snapshot.Paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(updatedDefinition));

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.True(reloaded.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenRelevantCatalogPlanChanges_RequiresUpdate()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var originalResolver = CreateResolver();
            var originalOrchestrator = CreateOrchestrator(tempRoot, originalResolver);
            var snapshot = originalOrchestrator.CreateWorkspace(tempRoot, CreateDefinition("core", "document-processing"));
            originalOrchestrator.WriteAppliedState(snapshot);

            var changedResolver = CreateResolver(additionalDocumentProcessingAptPackage: "tesseract-ocr");
            var changedOrchestrator = CreateOrchestrator(tempRoot, changedResolver);

            var reloaded = changedOrchestrator.LoadSnapshot(tempRoot);

            Assert.True(reloaded.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void CreatePermissionRepairArguments_UsesHelperContainerAndTargetMount()
    {
        var arguments = DockerService.CreatePermissionRepairArguments("C:\\Workspaces\\Demo");

        Assert.Equal("run", arguments[0]);
        Assert.Contains("ubuntu:24.04", arguments);
        Assert.Contains("C:\\Workspaces\\Demo:/target", arguments);
        Assert.Contains("chmod -R u+rwX,go+rwX /target || true", arguments[^1]);
    }

    [Fact]
    public void OpenFolderAsWorkspace_InitializesGitAndCreatesInitialSavePoint()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, "notes.txt"), "draft notes");
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());

            var snapshot = orchestrator.OpenFolderAsWorkspace(tempRoot);

            Assert.True(File.Exists(snapshot.Paths.WorkspaceYamlPath));
            Assert.True(Directory.Exists(Path.Combine(tempRoot, ".git")));
            Assert.NotNull(snapshot.Safety.LocalRecovery.LatestSavePointUtc);
            Assert.Equal(WorkspaceSafetyLevel.PartiallyProtected, snapshot.Safety.OverallStatus);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task PublishAsync_RecordsBlockedPublishInTimeline()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var timelineService = new WorkspaceTimelineService();
            var orchestrator = CreateOrchestratorWithProvider(tempRoot, CreateResolver(), new FakeWorkspaceProvider(), timelineService);
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));

            var review = await orchestrator.PublishAsync(snapshot);
            var timeline = timelineService.Load(snapshot.Paths.TimelinePath);

            Assert.True(review.IsBlocked);
            Assert.Contains(timeline.Events, item => item.Type == "publish-attempted");
            Assert.Contains(timeline.Events, item => item.Type == "publish-blocked");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenUnknownHiddenFolderExists_ReturnsNeedsReview()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            Directory.CreateDirectory(Path.Combine(tempRoot, ".foo"));
            File.WriteAllText(Path.Combine(tempRoot, ".foo", "state.json"), "{}");

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.Equal(WorkspaceSafetyLevel.NeedsReview, reloaded.Safety.OverallStatus);
            Assert.True(reloaded.Safety.IgnorePolicy.HasUnknownHiddenFolders);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenSecretCandidateExists_ReturnsAtRisk()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            File.WriteAllText(Path.Combine(tempRoot, ".env"), "API_KEY=secret");

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.Equal(WorkspaceSafetyLevel.AtRisk, reloaded.Safety.OverallStatus);
            Assert.True(reloaded.Safety.IgnorePolicy.HasSecretCandidates);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenTimelineIsIgnored_ReturnsNeedsReview()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            File.AppendAllText(Path.Combine(tempRoot, ".gitignore"), "history/*.yaml\n");

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.Equal(WorkspaceSafetyLevel.NeedsReview, reloaded.Safety.OverallStatus);
            Assert.True(reloaded.Safety.IgnorePolicy.HasDurableIgnoreConflicts);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    private static WorkspaceDefinition CreateDefinition(params string[] features)
    {
        return new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Name = "smoke-workspace",
                Image = "ubuntu:24.04",
            },
            Features = features.ToList(),
            Services = new List<string> { "postgres", "pgadmin" },
            Skills = new List<string>(),
            Mcp = new List<string>(),
        };
    }

    private static WorkspaceResolver CreateResolver(string? additionalDocumentProcessingAptPackage = null)
    {
        var documentPackages = new List<string> { "pandoc" };
        if (!string.IsNullOrWhiteSpace(additionalDocumentProcessingAptPackage))
        {
            documentPackages.Add(additionalDocumentProcessingAptPackage);
        }

        return new WorkspaceResolver(
            new[]
            {
                new FeatureManifest
                {
                    Id = "core",
                    AlwaysEnabled = true,
                    Dependencies = new DependencySet { Apt = new List<string> { "git", "curl" } },
                },
                new FeatureManifest
                {
                    Id = "document-processing",
                    Dependencies = new DependencySet { Apt = documentPackages },
                },
            },
            new[]
            {
                new ServiceManifest
                {
                    Id = "postgres",
                    Image = "postgres:17",
                    HostPorts = new List<string> { "15432:5432" },
                    Volumes = new List<string> { "postgres-data:/var/lib/postgresql/data" },
                },
                new ServiceManifest
                {
                    Id = "pgadmin",
                    Image = "dpage/pgadmin4:9",
                    HostPorts = new List<string> { "18080:80" },
                    DependsOn = new List<string> { "postgres" },
                },
            });
    }

    private static WorkspaceOrchestrator CreateOrchestrator(string tempRoot, WorkspaceResolver resolver)
    {
        var ignorePolicyService = new WorkspaceIgnorePolicyService();
        return CreateOrchestratorWithProvider(tempRoot, resolver, new GitWorkspaceProvider(new ProcessRunner(), ignorePolicyService), new WorkspaceTimelineService(), ignorePolicyService);
    }

    private static WorkspaceOrchestrator CreateOrchestratorWithProvider(string tempRoot, WorkspaceResolver resolver, IWorkspaceProvider provider, WorkspaceTimelineService timelineService, WorkspaceIgnorePolicyService? ignorePolicyService = null)
    {
        return new WorkspaceOrchestrator(
            new WorkspaceYamlService(),
            new WorkspaceRepository(GetAppDataRoot(tempRoot)),
            resolver,
            new ComposeGenerator(),
            new EnvironmentFileGenerator(),
            new ProvisioningScriptGenerator(),
            new TerminalArtifactsGenerator(),
            new AttachArtifactsGenerator(),
            new WorkspaceAppliedStateService(),
            new WorkspaceCheckpointService(),
            timelineService,
            new WorkspaceSafetyService(),
            ignorePolicyService ?? new WorkspaceIgnorePolicyService(),
            provider,
            new DockerService(new ProcessRunner()),
            new NoOpTerminalLauncher());
    }

    private static string CreateTempRoot() => Path.Combine(Path.GetTempPath(), $"opencode-workspace-manager-{Guid.NewGuid():N}");

    private static void DeleteTempRoot(string tempRoot)
    {
        var appDataRoot = GetAppDataRoot(tempRoot);
        if (Directory.Exists(tempRoot))
        {
            TestFileSystem.DeleteDirectoryIfExists(tempRoot);
        }

        if (Directory.Exists(appDataRoot))
        {
            TestFileSystem.DeleteDirectoryIfExists(appDataRoot);
        }
    }

    private static string GetAppDataRoot(string tempRoot)
        => Path.Combine(Path.GetDirectoryName(tempRoot) ?? Path.GetTempPath(), $"{Path.GetFileName(tempRoot)}-appdata");

    private static bool CanRunGit()
    {
        try
        {
            using var process = Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            process?.WaitForExit(5000);
            return process is not null && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private sealed class NoOpTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeWorkspaceProvider : IWorkspaceProvider
    {
        public string Type => "git";

        public Task InitializeWorkspaceAsync(WorkspacePaths paths, WorkspaceDefinition definition, bool createInitialSavePoint, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<WorkspaceGitState> GetGitStateAsync(WorkspacePaths paths, WorkspaceDefinition definition, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkspaceGitState
            {
                IsRepository = true,
                WorkingCopyName = "users/test/demo-20260613-1542",
                CurrentBranch = "users/test/demo-20260613-1542",
                LatestCommitSha = "abc123",
                LatestCommitUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                IsSafeWorkingCopy = true,
                StatusSummary = "clean",
            });
        }

        public Task<bool> CreateSavePointAsync(WorkspacePaths paths, WorkspaceDefinition definition, string message, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<WorkspacePublishReview> PublishAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkspacePublishReview
            {
                IsBlocked = true,
                Message = "Your local work is safe. The remote workspace changed and needs review before publishing.",
                WorkingCopyName = "users/test/demo-20260613-1542",
                RemoteName = "origin",
                RemoteBranch = "origin/users/test/demo-20260613-1542",
                AheadCount = 1,
                BehindCount = 1,
                LatestCommitSha = "abc123",
                LatestSavePointUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            });
        }

        public Task<WorkspacePublishReview> UpdateWorkingCopyAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspacePublishReview { Message = "Updated." });

        public Task<WorkspacePublishReview> PublishToReviewWorkingCopyAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspacePublishReview { Message = "Published review Working Copy." });

        public Task<string> ExportPatchAsync(WorkspacePaths paths, WorkspaceDefinition definition, string outputPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(outputPath);
    }
}
