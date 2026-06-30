using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceHealthEngineTests
{
    [Fact]
    public void Build_MissingRuntimeFiles_ReportsDegradedRuntimeHealth()
    {
        var snapshot = CreateSnapshot(localRuntimeState: null, appliedState: null);

        var health = WorkspaceHealthEngine.Build(snapshot);

        Assert.Equal(WorkspaceHealthStatus.Degraded, health.OverallStatus);
        var runtime = Assert.Single(health.Providers.Where(item => item.ProviderKey == "runtime"));
        Assert.Equal(WorkspaceHealthStatus.Degraded, runtime.Status);
        Assert.Equal("Open Workspace.", runtime.RecommendedAction);
    }

    [Fact]
    public void Build_RunningOracleWorkspace_ExposesLayeredOracleProviders()
    {
        var snapshot = CreateSnapshot(
            runtimeState: WorkspaceRuntimeState.Running,
            services: ["oracle-demo", "oracle-ords"],
            investigationHistory:
            [
                new WorkspaceInvestigationRecord
                {
                    InvestigationId = "inspect-ords",
                    Title = "Inspect ORDS",
                    Summary = "ORDS inspection completed.",
                    Evidence = "ORDS endpoint reachable",
                    Recommendation = "Open Workspace.",
                    Outcome = "ORDS evidence collected.",
                    Confidence = "HIGH",
                    CompletedUtc = DateTimeOffset.UtcNow,
                    StartedUtc = DateTimeOffset.UtcNow.AddSeconds(-5),
                    Duration = TimeSpan.FromSeconds(5),
                    ProviderName = "Oracle",
                },
            ]);

        var health = WorkspaceHealthEngine.Build(snapshot);

        Assert.Contains(health.Providers, item => item.ProviderKey == "oracle");
        Assert.Contains(health.Providers, item => item.ProviderKey == "ords");
        Assert.Contains(health.Providers, item => item.ProviderKey == "apex");
    }

    [Fact]
    public void Build_XdbInvalidEvidence_DegradesOnlyAffectedOracleLayer()
    {
        var snapshot = CreateSnapshot(
            runtimeState: WorkspaceRuntimeState.Running,
            services: ["oracle-demo", "oracle-ords"],
            investigationHistory:
            [
                new WorkspaceInvestigationRecord
                {
                    InvestigationId = "inspect-oracle-runtime",
                    Title = "Inspect Oracle runtime",
                    Summary = "Oracle prerequisite validation failed.",
                    Evidence = "XDB status = INVALID",
                    Recommendation = "Reset Runtime.",
                    Outcome = "Oracle runtime issue confirmed.",
                    Confidence = "HIGH",
                    CompletedUtc = DateTimeOffset.UtcNow,
                    StartedUtc = DateTimeOffset.UtcNow.AddSeconds(-5),
                    Duration = TimeSpan.FromSeconds(5),
                    ProviderName = "Oracle",
                },
            ]);

        var health = WorkspaceHealthEngine.Build(snapshot);

        Assert.Equal(WorkspaceHealthStatus.Degraded, health.OverallStatus);
        Assert.Equal(WorkspaceHealthStatus.Healthy, health.Providers.Single(item => item.ProviderKey == "oracle").Status);
        Assert.Equal(WorkspaceHealthStatus.Degraded, health.Providers.Single(item => item.ProviderKey == "oracle-xdb").Status);
        Assert.Contains("APEX", health.Providers.Single(item => item.ProviderKey == "oracle-xdb").WorkspaceImpact, StringComparison.Ordinal);
    }

    private static WorkspaceSnapshot CreateSnapshot(
        WorkspaceRuntimeState runtimeState = WorkspaceRuntimeState.Stopped,
        string[]? services = null,
        WorkspaceRuntimeStateRecord? localRuntimeState = null,
        WorkspaceAppliedState? appliedState = null,
        IReadOnlyList<WorkspaceInvestigationRecord>? investigationHistory = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"workspace-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "workspace.yaml"), "workspace: {}\n");
        File.WriteAllText(Path.Combine(root, "compose.yaml"), "services: {}\n");
        Directory.CreateDirectory(Path.Combine(root, "mounts", "config"));
        File.WriteAllText(Path.Combine(root, "mounts", "config", "provision.sh"), "#!/bin/bash\n");

        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = "alpha",
                RootPath = root,
                RepositoryPath = root,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
                LastProvisioningHealth = new WorkspaceProvisioningHealthRecord
                {
                    InvestigationHistory = investigationHistory ?? Array.Empty<WorkspaceInvestigationRecord>(),
                },
            },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "alpha", Image = "ubuntu:24.04" },
                Features = ["core", "apex"],
                Services = (services ?? ["oracle-demo"]).ToList(),
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
                OverallStatus = WorkspaceSafetyLevel.PartiallyProtected,
                Headline = "Protected working copy",
                Message = "Workspace is on a safe working copy.",
                LocalRecovery = new WorkspaceLocalRecoverySnapshot { IsGitInitialized = true, AreUntrackedFilesProtected = true },
                Backup = new WorkspaceBackupSnapshot { HasRemoteConfigured = true },
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
}
