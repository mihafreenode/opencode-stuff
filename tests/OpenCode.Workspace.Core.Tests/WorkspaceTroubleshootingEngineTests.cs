using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceTroubleshootingEngineTests
{
    [Fact]
    public void RecordRepairAttempt_ResolvedIssue_IsStoredAsSucceeded()
    {
        var before = CreateSnapshot();
        var after = CreateSnapshot(runtimeState: WorkspaceRuntimeState.Running);
        var previous = CreateHealth("Oracle XML Database (XDB) is invalid.", "XDB status = INVALID", "Reset Runtime.", WorkspaceRepairability.CleanupRepair.ToString());
        var diagnosis = CreateHealth("Oracle workspace provisioning completed successfully.", "Runtime state = Running", "Open Workspace.", WorkspaceRepairability.CleanupRepair.ToString(), succeeded: true);

        var updated = WorkspaceTroubleshootingEngine.RecordRepairAttempt(
            previous,
            "Reset Runtime",
            DateTimeOffset.UtcNow.AddMinutes(-4),
            DateTimeOffset.UtcNow,
            before,
            after,
            diagnosis);

        var attempt = Assert.Single(updated.RepairHistory);
        Assert.Equal(WorkspaceRepairOutcome.RepairSucceeded, attempt.Result);
        Assert.Equal("Reset Runtime.", attempt.PreviousRecommendation);
        Assert.Equal("Open Workspace.", attempt.UpdatedRecommendation);
    }

    [Fact]
    public void ApplyDiagnosis_UnchangedRootCauseAfterResetRuntime_MarksNoEffectAndChangesRecommendation()
    {
        var snapshot = CreateSnapshot();
        var previous = CreateHealth(
            "Oracle XML Database (XDB) is invalid.",
            "XDB status = INVALID",
            "Open Workspace.",
            WorkspaceRepairability.CleanupRepair.ToString(),
            repairHistory:
            [
                new WorkspaceRepairAttemptRecord
                {
                    RepairType = "Reset Runtime",
                    StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                    CompletedUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                    Duration = TimeSpan.FromMinutes(5),
                    Result = WorkspaceRepairOutcome.RepairSucceeded,
                    EvidenceBefore = "XDB status = INVALID",
                    RootCauseBefore = "Oracle XML Database (XDB) is invalid.",
                    WorkspaceStateBefore = "runtime=Running",
                    WorkspaceStateAfter = "runtime=Running",
                    PreviousRecommendation = "Reset Runtime.",
                    UpdatedRecommendation = "Open Workspace.",
                },
            ]);

        var current = CreateHealth(
            "Oracle XML Database (XDB) is invalid.",
            "XDB status = INVALID",
            "Reset Runtime.",
            WorkspaceRepairability.CleanupRepair.ToString());

        var updated = WorkspaceTroubleshootingEngine.ApplyDiagnosis(snapshot, current, previous);

        Assert.Equal("Troubleshoot Workspace.", updated.RecommendedAction);
        Assert.Equal("Reset Runtime.", updated.PreviousRecommendedAction);
        Assert.Equal(WorkspaceRepairability.ManualRepair.ToString(), updated.Repairability);
        var attempt = Assert.Single(updated.RepairHistory);
        Assert.Equal(WorkspaceRepairOutcome.RepairNoEffect, attempt.Result);
        Assert.Equal("XDB status = INVALID", attempt.EvidenceAfter);
    }

    [Fact]
    public void ApplyDiagnosis_ChangedRootCauseAfterRepair_MarksImprovedAndUsesNextRecommendation()
    {
        var snapshot = CreateSnapshot();
        var previous = CreateHealth(
            "Oracle XML Database (XDB) is invalid.",
            "XDB status = INVALID",
            "Open Workspace.",
            WorkspaceRepairability.CleanupRepair.ToString(),
            repairHistory:
            [
                new WorkspaceRepairAttemptRecord
                {
                    RepairType = "Reset Runtime",
                    StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                    CompletedUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                    Duration = TimeSpan.FromMinutes(5),
                    Result = WorkspaceRepairOutcome.RepairSucceeded,
                    EvidenceBefore = "XDB status = INVALID",
                    RootCauseBefore = "Oracle XML Database (XDB) is invalid.",
                    WorkspaceStateBefore = "runtime=Running",
                    WorkspaceStateAfter = "runtime=Running",
                    PreviousRecommendation = "Reset Runtime.",
                    UpdatedRecommendation = "Open Workspace.",
                },
            ]);

        var current = CreateHealth(
            "5432 port is already in use.",
            "Port 5432 is already in use.",
            "Troubleshoot Workspace.",
            WorkspaceRepairability.AutomaticRepair.ToString());

        var updated = WorkspaceTroubleshootingEngine.ApplyDiagnosis(snapshot, current, previous);

        Assert.Equal("Troubleshoot Workspace.", updated.RecommendedAction);
        var attempt = Assert.Single(updated.RepairHistory);
        Assert.Equal(WorkspaceRepairOutcome.RepairImproved, attempt.Result);
        Assert.Equal("5432 port is already in use.", attempt.RootCauseAfter);
    }

    [Fact]
    public void ApplyDiagnosis_DoesNotLoopSameRepairAfterNoEffect()
    {
        var snapshot = CreateSnapshot();
        var previous = CreateHealth(
            "Oracle XML Database (XDB) is invalid.",
            "XDB status = INVALID",
            "Troubleshoot Workspace.",
            WorkspaceRepairability.ManualRepair.ToString(),
            repairHistory:
            [
                new WorkspaceRepairAttemptRecord
                {
                    RepairType = "Reset Runtime",
                    StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                    CompletedUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
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
            ]);

        var current = CreateHealth(
            "Oracle XML Database (XDB) is invalid.",
            "XDB status = INVALID",
            "Troubleshoot Workspace.",
            WorkspaceRepairability.ManualRepair.ToString());

        var updated = WorkspaceTroubleshootingEngine.ApplyDiagnosis(snapshot, current, previous);

        Assert.Equal("Troubleshoot Workspace.", updated.RecommendedAction);
        Assert.DoesNotContain("Reset Runtime", updated.RecommendedAction, StringComparison.Ordinal);
    }

    [Fact]
    public void GetAvailableInvestigations_OracleWorkspace_ContributesOracleInvestigations()
    {
        var snapshot = CreateSnapshot(services: ["oracle-demo", "oracle-ords"]);
        var context = CreateContext(snapshot, CreateHealth("Oracle XML Database (XDB) is invalid.", "XDB status = INVALID", "Troubleshoot Workspace.", WorkspaceRepairability.ManualRepair.ToString()));

        var investigations = WorkspaceTroubleshootingEngine.GetAvailableInvestigations(context);

        Assert.Contains(investigations, item => item.Id == "inspect-oracle-runtime");
        Assert.Contains(investigations, item => item.Id == "inspect-apex");
        Assert.Contains(investigations, item => item.Id == "inspect-ords");
        Assert.Contains(investigations, item => item.Id == "inspect-workspace-runtime-files");
    }

    [Fact]
    public void GetAvailableInvestigations_PostgreSqlWorkspace_ContributesPostgreSqlInvestigation()
    {
        var snapshot = CreateSnapshot(services: ["postgres"]);
        var context = CreateContext(snapshot, CreateHealth("Migration failed.", "Extension missing", "Troubleshoot Workspace.", WorkspaceRepairability.Unknown.ToString()));

        var investigations = WorkspaceTroubleshootingEngine.GetAvailableInvestigations(context);

        Assert.Contains(investigations, item => item.Id == "inspect-postgres-runtime");
    }

    [Fact]
    public void ExecuteInvestigation_OracleRuntime_UpdatesRecommendationAndPersistsHistory()
    {
        var snapshot = CreateSnapshot();
        var health = CreateHealth(
            "Oracle XML Database (XDB) is invalid.",
            "XDB status = INVALID",
            "Troubleshoot Workspace.",
            WorkspaceRepairability.ManualRepair.ToString(),
            repairHistory:
            [
                new WorkspaceRepairAttemptRecord
                {
                    RepairType = "Reset Runtime",
                    StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                    CompletedUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
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
            ]);
        var context = CreateContext(snapshot, health, transcriptExcerpt: "[oracle-apex] Stage: Installing APEX\nXDB status = INVALID");

        var result = WorkspaceTroubleshootingEngine.ExecuteInvestigation(context, "inspect-oracle-runtime");

        Assert.Equal("Manual intervention required.", result.UpdatedHealth.RecommendedAction);
        var investigation = Assert.Single(result.UpdatedHealth.InvestigationHistory);
        Assert.Equal("Inspect Oracle runtime", investigation.Title);
        Assert.Equal("XDB status = INVALID", investigation.Evidence);
    }

    [Fact]
    public void ExecuteInvestigation_InProgressOracleApex_RecommendsKeepWaiting()
    {
        var snapshot = CreateSnapshot();
        var context = CreateContext(snapshot, CreateHealth("Oracle provisioning running.", "Installing APEX", "Troubleshoot Workspace.", WorkspaceRepairability.Unknown.ToString()), isProvisioningInProgress: true, currentStatusMessage: "Installing APEX...", transcriptExcerpt: "Installing APEX");

        var result = WorkspaceTroubleshootingEngine.ExecuteInvestigation(context, "inspect-apex");

        Assert.Equal("Keep Waiting.", result.UpdatedHealth.RecommendedAction);
        Assert.Equal("APEX installation is still running.", result.Investigation.Summary);
    }

    private static WorkspaceTroubleshootingContext CreateContext(WorkspaceSnapshot snapshot, WorkspaceProvisioningHealthRecord health, bool isProvisioningInProgress = false, string currentStatusMessage = "", string transcriptExcerpt = "")
        => new()
        {
            Snapshot = snapshot,
            Health = health,
            IsProvisioningInProgress = isProvisioningInProgress,
            CurrentOperationName = isProvisioningInProgress ? "Open Workspace" : string.Empty,
            CurrentStatusMessage = currentStatusMessage,
            TranscriptFilePath = Path.Combine(snapshot.Paths.ConfigPath, "transcript.log"),
            TranscriptExcerpt = transcriptExcerpt,
        };

    private static WorkspaceSnapshot CreateSnapshot(string[]? services = null, WorkspaceRuntimeState runtimeState = WorkspaceRuntimeState.Running)
    {
        var root = Path.Combine(Path.GetTempPath(), $"workspace-troubleshooting-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = "alpha",
                RootPath = root,
                RepositoryPath = root,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "alpha", Image = "ubuntu:24.04" },
                Features = ["core"],
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
            Session = new WorkspaceSessionSnapshot(),
            AppliedState = new WorkspaceAppliedState
            {
                AppliedUtc = DateTimeOffset.UtcNow,
                DesiredStateHash = "desired",
                WorkspaceDefinitionHash = "definition",
            },
            LocalRuntimeState = new WorkspaceRuntimeStateRecord { ResolvedEngine = "docker", ResolvedPlatform = "linux/amd64", CompatibilityMode = "native" },
            ResolvedRuntimePlan = new ResolvedRuntimePlan { Runtime = "docker", TargetPlatform = "linux/amd64", IsAvailable = true, HostPlatform = new HostPlatformInfo() },
            UpdateRequired = false,
        };
    }

    private static WorkspaceProvisioningHealthRecord CreateHealth(
        string reason,
        string evidence,
        string recommendedAction,
        string repairability,
        bool succeeded = false,
        IReadOnlyList<WorkspaceRepairAttemptRecord>? repairHistory = null)
        => new()
        {
            Succeeded = succeeded,
            Stage = "Validate runtime",
            Summary = succeeded ? "Provisioning completed." : "Workspace provisioning stopped.",
            Reason = reason,
            Evidence = evidence,
            RecommendedAction = recommendedAction,
            Confidence = "HIGH",
            Timestamp = DateTimeOffset.UtcNow,
            Duration = TimeSpan.FromMinutes(1),
            RawLogReference = "provision.sh",
            Repairability = repairability,
            EstimatedEffort = "Medium",
            EstimatedDuration = "4-6 minutes",
            LastDiagnosticsTimestamp = DateTimeOffset.UtcNow,
            RepairHistory = repairHistory ?? Array.Empty<WorkspaceRepairAttemptRecord>(),
        };
}
