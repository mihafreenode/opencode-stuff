using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceReadinessEngineTests
{
    [Fact]
    public void Build_ReadyWorkspace_ReturnsReadyWithOpenWorkspace()
    {
        var snapshot = CreateSnapshot(
            runtimeState: WorkspaceRuntimeState.Running,
            localRuntimeState: CreateRuntimeState(),
            appliedState: CreateAppliedState());
        var health = CreateHealth(
            services:
            [
                CreateApplicationService("sql-developer-web", "SQL Developer Web", WorkspaceHealthStatus.Healthy, "SQL Developer Web is available."),
            ]);

        var readiness = WorkspaceReadinessEngine.Build(new WorkspaceReadinessInput { Snapshot = snapshot, Health = health });

        Assert.Equal(WorkspaceReadinessStatus.Ready, readiness.Status);
        Assert.Equal(WorkspacePrimaryAction.OpenWorkspace, readiness.PrimaryAction);
        Assert.Equal(WorkspaceActivity.None, readiness.CurrentActivity);
        Assert.Contains(readiness.Capabilities, item => item.Key == "development-shell" && item.State == WorkspaceCapabilityState.Available);
        Assert.DoesNotContain(readiness.AttentionItems, item => item.Scope == WorkspaceAttentionScope.Runtime && item.Severity == WorkspaceAttentionSeverity.Blocking);
    }

    [Fact]
    public void Build_ActivePreparingOperation_ReturnsPreparingWithViewProgress()
    {
        var snapshot = CreateSnapshot();

        var readiness = WorkspaceReadinessEngine.Build(new WorkspaceReadinessInput
        {
            Snapshot = snapshot,
            Health = CreateHealth(),
            Operation = new WorkspaceOperationState
            {
                IsInProgress = true,
                OperationName = "Open Workspace",
                StatusMessage = "Provisioning runtime...",
            },
        });

        Assert.Equal(WorkspaceReadinessStatus.Preparing, readiness.Status);
        Assert.Equal(WorkspacePrimaryAction.ViewProgress, readiness.PrimaryAction);
        Assert.Equal(WorkspaceActivity.Preparing, readiness.CurrentActivity);
        Assert.Contains("Preparing workspace", readiness.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_UnavailableWorkspaceWithOpenPathPossible_ReturnsUnavailableWithOpenWorkspace()
    {
        var snapshot = CreateSnapshot(localRuntimeState: null, appliedState: null);

        var readiness = WorkspaceReadinessEngine.Build(new WorkspaceReadinessInput { Snapshot = snapshot, Health = CreateHealth() });

        Assert.Equal(WorkspaceReadinessStatus.Unavailable, readiness.Status);
        Assert.Equal(WorkspacePrimaryAction.OpenWorkspace, readiness.PrimaryAction);
        Assert.True(readiness.CanOpenWorkspace);
        Assert.Contains(readiness.AttentionItems, item => item.Key == "runtime-preparation");
    }

    [Fact]
    public void Build_NeedsRebuild_ReturnsNeedsRebuildWithRebuildRuntime()
    {
        var snapshot = CreateSnapshot(
            runtimeState: WorkspaceRuntimeState.Running,
            localRuntimeState: CreateRuntimeState(),
            appliedState: CreateAppliedState(),
            provisioningHealth: new WorkspaceProvisioningHealthRecord
            {
                Repairability = WorkspaceRepairability.CleanupRepair.ToString(),
                RecommendedAction = "Reset Runtime.",
            });

        var readiness = WorkspaceReadinessEngine.Build(new WorkspaceReadinessInput { Snapshot = snapshot, Health = CreateHealth() });

        Assert.Equal(WorkspaceReadinessStatus.NeedsRebuild, readiness.Status);
        Assert.Equal(WorkspacePrimaryAction.RebuildRuntime, readiness.PrimaryAction);
        Assert.True(readiness.CanRebuildRuntime);
        Assert.Contains(readiness.AttentionItems, item => item.Scope == WorkspaceAttentionScope.Runtime && item.Severity == WorkspaceAttentionSeverity.Blocking);
    }

    [Fact]
    public void Build_HostBlocker_ReturnsUnavailableWithRunDiagnostics()
    {
        var snapshot = CreateSnapshot(
            provisioningHealth: new WorkspaceProvisioningHealthRecord
            {
                ProblemScope = "HostProblem",
                RecommendedAction = "Run Diagnostics.",
            });

        var readiness = WorkspaceReadinessEngine.Build(new WorkspaceReadinessInput { Snapshot = snapshot, Health = CreateHealth() });

        Assert.Equal(WorkspaceReadinessStatus.Unavailable, readiness.Status);
        Assert.Equal(WorkspacePrimaryAction.RunDiagnostics, readiness.PrimaryAction);
        Assert.Contains(readiness.AttentionItems, item => item.Scope == WorkspaceAttentionScope.Host);
    }

    [Fact]
    public void Build_DevelopmentEnvironmentAttention_DoesNotBlockReadiness()
    {
        var snapshot = CreateSnapshot(
            runtimeState: WorkspaceRuntimeState.Running,
            localRuntimeState: CreateRuntimeState(),
            appliedState: CreateAppliedState());
        var health = CreateHealth(
            developmentEnvironment: new WorkspaceDevelopmentEnvironmentHealthSnapshot
            {
                Status = WorkspaceHealthStatus.Attention,
                Summary = "Development environment needs attention: OpenCode CLI, screen.",
                Recommendation = "Inspect Development Environment.",
                Checks =
                [
                    new WorkspaceDevelopmentEnvironmentCheck { Name = "OpenCode CLI", Status = "Missing", Summary = "OpenCode CLI is missing." },
                    new WorkspaceDevelopmentEnvironmentCheck { Name = "screen", Status = "Missing", Summary = "screen is missing." },
                ],
            });

        var readiness = WorkspaceReadinessEngine.Build(new WorkspaceReadinessInput { Snapshot = snapshot, Health = health });

        Assert.Equal(WorkspaceReadinessStatus.Ready, readiness.Status);
        Assert.Equal(WorkspacePrimaryAction.OpenWorkspace, readiness.PrimaryAction);
        Assert.Contains(readiness.AttentionItems, item => item.Scope == WorkspaceAttentionScope.DevelopmentEnvironment);
    }

    [Fact]
    public void Build_ReadyWorkspace_IgnoresStaleCleanupRepairRecommendation()
    {
        var original = CreateSnapshot(
            runtimeState: WorkspaceRuntimeState.Running,
            localRuntimeState: CreateRuntimeState(),
            appliedState: CreateAppliedState(),
            provisioningHealth: new WorkspaceProvisioningHealthRecord
            {
                Repairability = WorkspaceRepairability.CleanupRepair.ToString(),
                RecommendedAction = "Rebuild Runtime.",
            });
        var snapshot = new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = original.Record.Name,
                RootPath = original.Record.RootPath,
                RepositoryPath = original.Record.RepositoryPath,
                CreatedUtc = original.Record.CreatedUtc,
                LastOpenedUtc = original.Record.LastOpenedUtc,
                LastProvisioningHealth = original.Record.LastProvisioningHealth,
                LastOperationName = "Open Workspace",
                LastOperationSucceeded = true,
            },
            Definition = original.Definition,
            Paths = original.Paths,
            ConfigurationPath = original.ConfigurationPath,
            RuntimeState = original.RuntimeState,
            Safety = original.Safety,
            Session = original.Session,
            AppliedState = original.AppliedState,
            LocalRuntimeState = original.LocalRuntimeState,
            ResolvedRuntimePlan = original.ResolvedRuntimePlan,
            UpdateRequired = original.UpdateRequired,
            Health = original.Health,
        };
        var health = CreateHealth(
            providers:
            [
                new WorkspaceProviderHealthSnapshot
                {
                    ProviderKey = "runtime",
                    DisplayName = "Runtime",
                    Status = WorkspaceHealthStatus.Healthy,
                    RecommendedAction = "Open Workspace.",
                },
            ]);

        var readiness = WorkspaceReadinessEngine.Build(new WorkspaceReadinessInput { Snapshot = snapshot, Health = health });

        Assert.Equal(WorkspaceReadinessStatus.Ready, readiness.Status);
        Assert.Equal(WorkspacePrimaryAction.OpenWorkspace, readiness.PrimaryAction);
        Assert.False(readiness.CanRebuildRuntime);
    }

    private static WorkspaceHealthSnapshot CreateHealth(
        IReadOnlyList<WorkspaceProviderHealthSnapshot>? providers = null,
        IReadOnlyList<WorkspaceServiceHealthSnapshot>? services = null,
        WorkspaceDevelopmentEnvironmentHealthSnapshot? developmentEnvironment = null)
        => new()
        {
            Providers = providers ?? Array.Empty<WorkspaceProviderHealthSnapshot>(),
            Services = services ?? Array.Empty<WorkspaceServiceHealthSnapshot>(),
            DevelopmentEnvironment = developmentEnvironment,
            Timestamp = DateTimeOffset.UtcNow,
        };

    private static WorkspaceServiceHealthSnapshot CreateApplicationService(string id, string name, WorkspaceHealthStatus status, string summary)
        => new()
        {
            ServiceId = id,
            Name = name,
            Category = "Application",
            Status = status,
            Summary = summary,
            Timestamp = DateTimeOffset.UtcNow,
        };

    private static WorkspaceSnapshot CreateSnapshot(
        WorkspaceRuntimeState runtimeState = WorkspaceRuntimeState.Stopped,
        WorkspaceRuntimeStateRecord? localRuntimeState = null,
        WorkspaceAppliedState? appliedState = null,
        WorkspaceProvisioningHealthRecord? provisioningHealth = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"workspace-readiness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "workspace.yaml"), "workspace: {}\n");

        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = "alpha",
                RootPath = root,
                RepositoryPath = root,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
                LastProvisioningHealth = provisioningHealth,
            },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "alpha", Image = "ubuntu:24.04" },
                Features = ["core"],
                Services = [],
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
                AttachDiagnosticsLogPath = Path.Combine(root, "mounts", "config", "attach.log"),
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
            RuntimeState = runtimeState,
            Safety = new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.Protected,
                Headline = "Protected working copy",
                Message = "Workspace is protected.",
                LocalRecovery = new WorkspaceLocalRecoverySnapshot(),
                Backup = new WorkspaceBackupSnapshot(),
                IgnorePolicy = new WorkspaceIgnorePolicyReview(),
                AdvancedGit = new WorkspaceAdvancedGitSnapshot { CurrentBranch = "users/test/alpha", StatusSummary = "clean" },
            },
            Session = new WorkspaceSessionSnapshot { SessionName = "alpha", State = runtimeState == WorkspaceRuntimeState.Running ? WorkspaceSessionState.Resumable : WorkspaceSessionState.NotRunning },
            AppliedState = appliedState,
            LocalRuntimeState = localRuntimeState,
            ResolvedRuntimePlan = new ResolvedRuntimePlan { Runtime = "docker", TargetPlatform = "linux/amd64", IsAvailable = true, HostPlatform = new HostPlatformInfo() },
            UpdateRequired = false,
            Health = new WorkspaceHealthSnapshot(),
        };
    }

    private static WorkspaceRuntimeStateRecord CreateRuntimeState()
        => new()
        {
            ResolvedEngine = "docker",
            ResolvedPlatform = "linux/amd64",
            CompatibilityMode = "Native",
        };

    private static WorkspaceAppliedState CreateAppliedState()
        => new()
        {
            AppliedUtc = DateTimeOffset.UtcNow,
            DesiredStateHash = "desired",
            WorkspaceDefinitionHash = "definition",
        };
}
