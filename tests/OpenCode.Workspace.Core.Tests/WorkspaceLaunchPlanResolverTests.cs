using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceLaunchPlanResolverTests
{
    private readonly WorkspaceLaunchPlanResolver _resolver = new();

    [Fact]
    public void Resolve_NewWorkspaceWithoutAppliedState_NeedsProvision()
    {
        var root = CreateTempRoot();

        try
        {
            var snapshot = CreateSnapshot(root, appliedState: null, runtimeState: WorkspaceRuntimeState.Stopped, includeRuntimeStateFile: false, includeRuntimeState: false);

            var plan = _resolver.Resolve(snapshot);

            Assert.True(plan.NeedsProvision);
            Assert.False(plan.NeedsRecover);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Resolve_ProvisionedStoppedWorkspace_NeedsStart()
    {
        var root = CreateTempRoot();

        try
        {
            var snapshot = CreateSnapshot(root, appliedState: new WorkspaceAppliedState(), runtimeState: WorkspaceRuntimeState.Stopped, includeRuntimeStateFile: true, includeRuntimeState: true);

            var plan = _resolver.Resolve(snapshot);

            Assert.True(plan.NeedsStart);
            Assert.False(plan.NeedsRecover);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Resolve_RunningWorkspace_CanAttach()
    {
        var root = CreateTempRoot();

        try
        {
            var snapshot = CreateSnapshot(root, appliedState: new WorkspaceAppliedState(), runtimeState: WorkspaceRuntimeState.Running, includeRuntimeStateFile: true, includeRuntimeState: true);

            var plan = _resolver.Resolve(snapshot);

            Assert.True(plan.CanAttach);
            Assert.Equal("workspace", plan.PrimaryServiceName);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Resolve_MissingRuntimeStateAfterProvisioning_NeedsRecover()
    {
        var root = CreateTempRoot();

        try
        {
            var snapshot = CreateSnapshot(root, appliedState: new WorkspaceAppliedState(), runtimeState: WorkspaceRuntimeState.Stopped, includeRuntimeStateFile: false, includeRuntimeState: false);

            var plan = _resolver.Resolve(snapshot);

            Assert.True(plan.NeedsRecover);
            Assert.Contains("Recover Workspace", plan.Summary, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Resolve_UnknownRuntime_NeedsDiagnostics()
    {
        var root = CreateTempRoot();

        try
        {
            var snapshot = CreateSnapshot(root, appliedState: new WorkspaceAppliedState(), runtimeState: WorkspaceRuntimeState.Unknown, includeRuntimeStateFile: true, includeRuntimeState: true);

            var plan = _resolver.Resolve(snapshot);

            Assert.True(plan.NeedsDiagnostics);
            Assert.Contains("Diagnostics", plan.Summary, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Resolve_OracleApexLangWorkspace_UsesWorkspacePrimaryService()
    {
        var root = CreateTempRoot();

        try
        {
            var definition = CreateDefinition("oracle-apexlang", ["oracle-demo", "oracle-ords"]);
            var paths = CreatePaths(root);
            CreateManagedFiles(paths, includeRuntimeStateFile: true);
            var snapshot = CreateSnapshot(root, appliedState: new WorkspaceAppliedState(), runtimeState: WorkspaceRuntimeState.Running, includeRuntimeStateFile: true, includeRuntimeState: true, definition: definition);

            var plan = _resolver.Resolve(snapshot);

            Assert.True(plan.CanAttach);
            Assert.Equal("workspace", plan.PrimaryServiceName);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static WorkspaceSnapshot CreateSnapshot(string root, WorkspaceAppliedState? appliedState, WorkspaceRuntimeState runtimeState, bool includeRuntimeStateFile, bool includeRuntimeState, WorkspaceDefinition? definition = null)
    {
        definition ??= CreateDefinition("demo", ["oracle-demo", "oracle-ords"]);
        var paths = CreatePaths(root);
        CreateManagedFiles(paths, includeRuntimeStateFile);

        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = definition.Workspace.Name,
                RootPath = root,
                RepositoryPath = root,
                ConfigurationPath = "workspace.yaml",
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            },
            Definition = definition,
            Paths = paths,
            ConfigurationPath = "workspace.yaml",
            RuntimeState = runtimeState,
            Safety = CreateSafeSafetySnapshot(),
            Session = new WorkspaceSessionSnapshot(),
            AppliedState = appliedState,
            LocalRuntimeState = includeRuntimeState ? new WorkspaceRuntimeStateRecord { ResolvedEngine = "docker", ResolvedPlatform = "linux/amd64", CompatibilityMode = "Native" } : null,
            ResolvedRuntimePlan = new ResolvedRuntimePlan { Runtime = "docker", TargetPlatform = "linux/amd64", CompatibilityMode = RuntimeCompatibilityMode.Native, IsAvailable = true },
            UpdateRequired = false,
        };
    }

    private static WorkspaceSafetySnapshot CreateSafeSafetySnapshot()
        => new()
        {
            OverallStatus = WorkspaceSafetyLevel.Protected,
            Headline = "Workspace protected",
            Message = "No safety issues detected.",
            LocalRecovery = new WorkspaceLocalRecoverySnapshot
            {
                IsGitInitialized = true,
                AreUntrackedFilesProtected = true,
            },
            Backup = new WorkspaceBackupSnapshot
            {
                HasRemoteConfigured = true,
                IsCurrentWorkingCopyPublished = true,
            },
            IgnorePolicy = new WorkspaceIgnorePolicyReview(),
            AdvancedGit = new WorkspaceAdvancedGitSnapshot
            {
                CurrentBranch = "users/test/demo",
                StatusSummary = "Working copy protected.",
                IsWorkspaceBranch = true,
            },
        };

    private static WorkspaceDefinition CreateDefinition(string name, IReadOnlyList<string> services)
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = name, Image = "ubuntu:24.04" },
            Provider = new WorkspaceProviderDefinition { Type = "git" },
            Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = WorkspaceRuntimeDefinition.DefaultNodeMajorVersion },
            Features = ["core"],
            Services = services.ToList(),
            Skills = [],
            Mcp = [],
        };

    private static WorkspacePaths CreatePaths(string root)
    {
        Directory.CreateDirectory(root);
        var opencode = Path.Combine(root, ".opencode");
        var local = Path.Combine(opencode, "local");
        var mounts = Path.Combine(root, "mounts");
        var config = Path.Combine(mounts, "config");
        var history = Path.Combine(root, "history");
        var checkpoints = Path.Combine(history, "checkpoints");
        var runtimes = Path.Combine(root, "runtimes");
        var artifacts = Path.Combine(root, "artifacts");
        var artifactRuns = Path.Combine(artifacts, "runs");

        return new WorkspacePaths
        {
            RootPath = root,
            GitIgnorePath = Path.Combine(root, ".gitignore"),
            OpencodePath = opencode,
            OpencodeLocalPath = local,
            WorkspaceYamlRelativePath = "workspace.yaml",
            WorkspaceYamlPath = Path.Combine(root, "workspace.yaml"),
            ComposePath = Path.Combine(root, "compose.yaml"),
            EnvironmentFilePath = Path.Combine(root, ".env"),
            MountsRootPath = mounts,
            InboxPath = Path.Combine(mounts, "inbox"),
            WorkspacePath = root,
            UserPath = Path.Combine(mounts, "user"),
            HomePath = Path.Combine(mounts, "home"),
            ConfigPath = config,
            ProvisionScriptPath = Path.Combine(config, "provision.sh"),
            StarshipConfigPath = Path.Combine(config, "starship.toml"),
            ShellInitScriptPath = Path.Combine(config, "opencode-shell-init.sh"),
            OpencodeWorkspaceShellPath = Path.Combine(config, "opencode-workspace-shell.sh"),
            ScreenConfigPath = Path.Combine(config, "screenrc"),
            AttachWrapperScriptPath = Path.Combine(root, "attach-workspace.ps1"),
            AttachDiagnosticsLogPath = Path.Combine(root, "attach-diagnostics.log"),
            TerminalDiagnosticsScriptPath = Path.Combine(root, "terminal-diagnostics.ps1"),
            RuntimeStatePath = Path.Combine(local, "runtime-state.yaml"),
            AppliedStatePath = Path.Combine(config, "applied-state.yaml"),
            HistoryPath = history,
            CheckpointsPath = checkpoints,
            CheckpointIndexPath = Path.Combine(checkpoints, "index.yaml"),
            TimelinePath = Path.Combine(history, "timeline.yaml"),
            RuntimesPath = runtimes,
            DefaultRuntimePath = Path.Combine(runtimes, "default.yaml"),
            ArtifactsPath = artifacts,
            ArtifactRunsPath = artifactRuns,
            ArtifactIndexPath = Path.Combine(artifacts, "index.json"),
        };
    }

    private static void CreateManagedFiles(WorkspacePaths paths, bool includeRuntimeStateFile)
    {
        Directory.CreateDirectory(paths.ConfigPath);
        Directory.CreateDirectory(paths.OpencodeLocalPath);
        File.WriteAllText(paths.ComposePath, "services:\n  workspace:\n    image: ubuntu:24.04\n");
        File.WriteAllText(paths.AttachWrapperScriptPath, "# attach\n");
        File.WriteAllText(paths.OpencodeWorkspaceShellPath, "#!/usr/bin/env bash\n");
        if (includeRuntimeStateFile)
        {
            File.WriteAllText(paths.RuntimeStatePath, "resolvedEngine: docker\nresolvedPlatform: linux/amd64\n");
        }
    }

    private static string CreateTempRoot() => Path.Combine(Path.GetTempPath(), $"workspace-launch-plan-{Guid.NewGuid():N}");

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}
