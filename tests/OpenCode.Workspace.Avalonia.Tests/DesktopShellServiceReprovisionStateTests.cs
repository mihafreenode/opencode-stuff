using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Platform.Windows;
using OperationTranscriptLineKind = OpenCode.Workspace.AppSupport.OperationTranscriptLineKind;

namespace OpenCode.Workspace.Avalonia.Tests;

public sealed class DesktopShellServiceReprovisionStateTests
{
    [Fact]
    public async Task OpenWorkspace_OnNewWorkspace_ProvisionsThenOpens()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var fixture = await CreateValidNewWorkspaceFixtureAsync(tempRoot, "Odip Analiza");

            var result = await fixture.Service.OpenWorkspaceAsync(fixture.CreatedSnapshot.Paths.RootPath, fixture.CreatedSnapshot);

            Assert.True(File.Exists(fixture.CreatedSnapshot.Paths.RuntimeStatePath));
            Assert.True(File.Exists(fixture.CreatedSnapshot.Paths.AppliedStatePath));
            Assert.True(result.Snapshot.Record.LastOperationSucceeded);
            Assert.Contains("is open", result.Message, StringComparison.Ordinal);
            var transcript = result.Transcript.Lines.Select(line => line.Text).ToArray();
            Assert.Contains("Checking workspace...", transcript);
            Assert.Contains("Opening terminal...", transcript);
            Assert.Contains("Ready.", transcript);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task OpenWorkspace_OnStoppedProvisionedWorkspace_StartsThenOpens()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var fixture = await CreateValidProvisionedStoppedWorkspaceFixtureAsync(tempRoot, "Odip Analiza");
            var provisioned = fixture.OpenSnapshot;
            var stoppedSnapshot = new WorkspaceSnapshot
            {
                Record = provisioned.Record,
                Definition = provisioned.Definition,
                Paths = provisioned.Paths,
                ConfigurationPath = provisioned.ConfigurationPath,
                RuntimeState = WorkspaceRuntimeState.Stopped,
                Safety = provisioned.Safety,
                Session = provisioned.Session,
                AppliedState = provisioned.AppliedState,
                LocalRuntimeState = provisioned.LocalRuntimeState,
                ResolvedRuntimePlan = provisioned.ResolvedRuntimePlan,
                UpdateRequired = false,
                Health = provisioned.Health,
                Readiness = provisioned.Readiness,
            };

            var result = await fixture.Service.OpenWorkspaceAsync(fixture.CreatedSnapshot.Paths.RootPath, stoppedSnapshot);

            Assert.True(result.Snapshot.Record.LastOperationSucceeded);
            Assert.Contains("Ready.", string.Join(Environment.NewLine, result.Transcript.Lines.Select(line => line.Text)), StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task OpenWorkspace_WithMissingRuntimeState_AutoRepairsAndOpens()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var fixture = await CreateMissingRuntimeStateWorkspaceFixtureAsync(tempRoot, "Odip Analiza");

            var result = await fixture.Service.OpenWorkspaceAsync(fixture.CreatedSnapshot.Paths.RootPath, fixture.OpenSnapshot);

            Assert.True(result.Snapshot.Record.LastOperationSucceeded);
            var transcript = string.Join(Environment.NewLine, result.Transcript.Lines.Select(line => line.Text));
            Assert.Contains("Repairing runtime...", transcript, StringComparison.Ordinal);
            Assert.Contains("Opening terminal...", transcript, StringComparison.Ordinal);
            Assert.Contains("Ready.", transcript, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task Recover_RegeneratesComposeAndRuntimeState_WithoutChangingUserFile()
    {
        var tempRoot = CreateTempRoot();
        var workspaceRoot = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var repository = new WorkspaceRepository(GetAppDataRoot(tempRoot));
            var timelineService = new WorkspaceTimelineService();
            var checkpointService = new WorkspaceCheckpointService();
            var runtime = new StubContainerRuntime();
            var orchestrator = CreateOrchestrator(tempRoot, repository, timelineService, runtime);
            var created = await orchestrator.CreateWorkspaceAsync(workspaceRoot, CreateDefinition("Recover Demo"), includeRuntimeInspection: false);
            var service = new DesktopShellService(orchestrator, repository, timelineService, checkpointService, new WorkspaceSavePointMessageService(new ProcessRunner()), new WorkspaceBackupExportService(), new WorkspaceBackupManifestService(), new WorkspacePublishAssessmentService(new ProcessRunner()), new WorkspaceRemovalService(repository), new OracleSoftwareNoticeService(repository), new WindowsTerminalProfileSetupService(new WindowsTerminalProfileManager(), new WindowsHostCapabilities(new ProcessRunner())));

            var userFile = Path.Combine(workspaceRoot, "docs", "preserve-me.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(userFile)!);
            await File.WriteAllTextAsync(userFile, "preserve this file");
            var userFileHashBefore = ComputeFileHash(userFile);

            File.Delete(created.Paths.ComposePath);
            File.Delete(created.Paths.RuntimeStatePath);

            var result = await service.RecoverWorkspaceAsync(created.Paths.RootPath, created);

            Assert.True(File.Exists(created.Paths.ComposePath));
            Assert.True(File.Exists(created.Paths.RuntimeStatePath));
            Assert.Equal(userFileHashBefore, ComputeFileHash(userFile));
            Assert.Equal($"Workspace '{created.Definition.Workspace.Name}' runtime was repaired.", result.Message);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task RefreshVolatileWorkspaceStateAsync_ClearsStalePortConflictWhenPortIsNowFree()
    {
        var tempRoot = CreateTempRoot();
        var workspaceRoot = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var repository = new WorkspaceRepository(GetAppDataRoot(tempRoot));
            var timelineService = new WorkspaceTimelineService();
            var checkpointService = new WorkspaceCheckpointService();
            var runtime = new StubContainerRuntime();
            var orchestrator = CreateOrchestrator(tempRoot, repository, timelineService, runtime);
            var created = await orchestrator.CreateWorkspaceAsync(workspaceRoot, CreateDefinition("Port Recheck"), includeRuntimeInspection: false);
            var staleRecord = new WorkspaceRecord
            {
                Name = created.Record.Name,
                RootPath = created.Record.RootPath,
                RepositoryPath = created.Record.RepositoryPath,
                ConfigurationPath = created.Record.ConfigurationPath,
                SourceType = created.Record.SourceType,
                ImportedFromExistingCheckout = created.Record.ImportedFromExistingCheckout,
                OriginalDefaultBranch = created.Record.OriginalDefaultBranch,
                SelectedWorkspaceBranch = created.Record.SelectedWorkspaceBranch,
                RemoteOriginUrl = created.Record.RemoteOriginUrl,
                CreatedUtc = created.Record.CreatedUtc,
                LastOpenedUtc = created.Record.LastOpenedUtc,
                LastPreparedUtc = created.Record.LastPreparedUtc,
                OracleSoftwareNoticeShown = created.Record.OracleSoftwareNoticeShown,
                LastOperationName = "Prepare",
                LastOperationResult = "Oracle port 1521 is already in use.",
                LastOperationSucceeded = false,
                LastOperationUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastProvisioningHealth = new WorkspaceProvisioningHealthRecord
                {
                    Succeeded = false,
                    Stage = "Volatile environment revalidation",
                    Summary = "Workspace runtime is currently blocked by a volatile host conflict.",
                    Reason = "Oracle port 1521 is already in use.",
                    Evidence = "Port 1521 currently owned by: other-oracle",
                    ProblemScope = "WorkspaceProblem",
                    RecommendedAction = "Troubleshoot Workspace.",
                    Confidence = "HIGH",
                    Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
                    Duration = TimeSpan.Zero,
                    RawLogReference = created.Paths.ComposePath,
                    Repairability = WorkspaceRepairability.AutomaticRepair.ToString(),
                    EstimatedEffort = "Low",
                    EstimatedDuration = "1-2 minutes",
                    LastDiagnosticsTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
                },
            };
            await repository.SaveAsync(staleRecord, CancellationToken.None);
            var staleSnapshot = await orchestrator.LoadSnapshotAsync(workspaceRoot, includeRuntimeInspection: true, includeSessionInspection: false);
            var service = CreateDesktopShellService(orchestrator, repository, timelineService, checkpointService);

            var refreshed = await service.RefreshVolatileWorkspaceStateAsync(workspaceRoot, staleSnapshot);

            Assert.True(refreshed.Record.LastOperationSucceeded);
            Assert.Equal("Health Recheck", refreshed.Record.LastOperationName);
            Assert.Contains("Previous port conflict is no longer present", refreshed.Record.LastOperationResult, StringComparison.Ordinal);
            Assert.Null(refreshed.Record.LastProvisioningHealth);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task OpenWorkspace_FailsSafelyWhenProvisioningFailsAfterContainerStart()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var workspaceRoot = Path.Combine(tempRoot, "workspace");
            Directory.CreateDirectory(workspaceRoot);
            var repository = new WorkspaceRepository(GetAppDataRoot(tempRoot));
            var timelineService = new WorkspaceTimelineService();
            var checkpointService = new WorkspaceCheckpointService();
            var runtime = new StubContainerRuntime
            {
                ProvisionScriptResultFactory = () => Failure("docker exec provision", "provision failed"),
            };
            var orchestrator = CreateOrchestrator(tempRoot, repository, timelineService, runtime);
            var created = await orchestrator.CreateWorkspaceAsync(workspaceRoot, CreateDefinition("Open Failure"), includeRuntimeInspection: false);
            var service = CreateDesktopShellService(orchestrator, repository, timelineService, checkpointService);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenWorkspaceAsync(created.Paths.RootPath, created));

            Assert.Contains("Workspace provisioning failed.", exception.Message, StringComparison.Ordinal);
            var runtimeState = new WorkspaceRuntimeStateService().Read(created.Paths.RuntimeStatePath);
            Assert.NotNull(runtimeState);
            Assert.Null(runtimeState.LastSuccessfulProvision);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task Recover_FailsWhenRuntimeStateCannotBeRegenerated()
    {
        var tempRoot = CreateTempRoot();
        var workspaceRoot = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var repository = new WorkspaceRepository(GetAppDataRoot(tempRoot));
            var timelineService = new WorkspaceTimelineService();
            var checkpointService = new WorkspaceCheckpointService();
            var runtime = new StubContainerRuntime();
            var orchestrator = CreateOrchestrator(tempRoot, repository, timelineService, runtime);
            var created = await orchestrator.CreateWorkspaceAsync(workspaceRoot, CreateDefinition("Recover Failure"), includeRuntimeInspection: false);
            var service = new DesktopShellService(orchestrator, repository, timelineService, checkpointService, new WorkspaceSavePointMessageService(new ProcessRunner()), new WorkspaceBackupExportService(), new WorkspaceBackupManifestService(), new WorkspacePublishAssessmentService(new ProcessRunner()), new WorkspaceRemovalService(repository), new OracleSoftwareNoticeService(repository), new WindowsTerminalProfileSetupService(new WindowsTerminalProfileManager(), new WindowsHostCapabilities(new ProcessRunner())));

            File.Delete(created.Paths.ComposePath);
            File.Delete(created.Paths.RuntimeStatePath);
            Directory.Delete(created.Paths.OpencodeLocalPath, recursive: true);
            File.WriteAllText(created.Paths.OpencodeLocalPath, "block runtime-state directory creation");

            var exception = await Assert.ThrowsAnyAsync<Exception>(() => service.RecoverWorkspaceAsync(created.Paths.RootPath, created));

            Assert.True(
                exception.Message.Contains("runtime-state.yaml", StringComparison.Ordinal)
                || exception.Message.Contains("required managed runtime files", StringComparison.Ordinal)
                || exception.Message.Contains("already exists", StringComparison.Ordinal),
                $"Unexpected exception message: {exception.Message}");
            var savedRecord = repository.LoadAll().Single(record => string.Equals(record.RootPath, created.Paths.RootPath, StringComparison.OrdinalIgnoreCase));
            Assert.False(savedRecord.LastOperationSucceeded);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task TroubleshootWorkspace_IncludesTerminalReadinessChecks()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var fixture = await CreateValidProvisionedStoppedWorkspaceFixtureAsync(tempRoot, "Terminal Readiness");
            var runningSnapshot = new WorkspaceSnapshot
            {
                Record = new WorkspaceRecord
                {
                    Name = fixture.OpenSnapshot.Record.Name,
                    RootPath = fixture.OpenSnapshot.Record.RootPath,
                    RepositoryPath = fixture.OpenSnapshot.Record.RepositoryPath,
                    ConfigurationPath = fixture.OpenSnapshot.Record.ConfigurationPath,
                    SourceType = fixture.OpenSnapshot.Record.SourceType,
                    ImportedFromExistingCheckout = fixture.OpenSnapshot.Record.ImportedFromExistingCheckout,
                    OriginalDefaultBranch = fixture.OpenSnapshot.Record.OriginalDefaultBranch,
                    SelectedWorkspaceBranch = fixture.OpenSnapshot.Record.SelectedWorkspaceBranch,
                    RemoteOriginUrl = fixture.OpenSnapshot.Record.RemoteOriginUrl,
                    CreatedUtc = fixture.OpenSnapshot.Record.CreatedUtc,
                    LastOpenedUtc = fixture.OpenSnapshot.Record.LastOpenedUtc,
                    LastPreparedUtc = fixture.OpenSnapshot.Record.LastPreparedUtc,
                    OracleSoftwareNoticeShown = fixture.OpenSnapshot.Record.OracleSoftwareNoticeShown,
                    LastOperationName = "Open Workspace",
                    LastOperationResult = "Open Workspace could not finish preparing the terminal. Troubleshoot Workspace can inspect the runtime files and launch readiness.",
                    LastOperationSucceeded = false,
                    LastOperationUtc = DateTimeOffset.UtcNow,
                },
                Definition = fixture.OpenSnapshot.Definition,
                Paths = fixture.OpenSnapshot.Paths,
                ConfigurationPath = fixture.OpenSnapshot.ConfigurationPath,
                RuntimeState = WorkspaceRuntimeState.Running,
                Safety = fixture.OpenSnapshot.Safety,
                Session = fixture.OpenSnapshot.Session,
                AppliedState = fixture.OpenSnapshot.AppliedState,
                LocalRuntimeState = fixture.OpenSnapshot.LocalRuntimeState,
                ResolvedRuntimePlan = fixture.OpenSnapshot.ResolvedRuntimePlan,
                UpdateRequired = false,
                Health = new WorkspaceHealthSnapshot
                {
                    OverallStatus = WorkspaceHealthStatus.Attention,
                    Summary = "Workspace services are available, but OpenCode terminal could not be prepared.",
                    Recommendation = "Troubleshoot Workspace.",
                    Services = [new WorkspaceServiceHealthSnapshot { ServiceId = "pgadmin", Name = "pgAdmin", Category = "Application", Status = WorkspaceHealthStatus.Healthy, StatusLabel = "Available", Summary = "pgAdmin is available.", Recommendation = "Open Workspace.", Timestamp = DateTimeOffset.UtcNow }],
                },
                Readiness = fixture.OpenSnapshot.Readiness,
            };

            var report = await fixture.Service.GetWorkspaceTroubleshootingReportAsync(new WorkspaceTroubleshootingRequest { RootPath = runningSnapshot.Paths.RootPath, Snapshot = runningSnapshot, WorkspaceName = runningSnapshot.Definition.Workspace.Name });

            Assert.Contains(report.Facts, item => item.Label == "Launch state");
            Assert.Contains(report.Facts, item => item.Label == "Selected service");
            Assert.Contains(report.Facts, item => item.Label == "Attach blocked reason");
            Assert.Contains(report.Facts, item => item.Label == "Workspace shell script");
            Assert.Contains(report.Facts, item => item.Label == "Attach diagnostics log");
            Assert.Contains(report.Facts, item => item.Label == "Docker exec");
            Assert.Contains(report.Facts, item => item.Label == "Workspace shell script in container");
            Assert.Contains(report.Facts, item => item.Label == "Windows Terminal launch readiness");
            Assert.Contains(report.Facts, item => item.Label == "Last attach failure");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task OpenWorkspace_AfterAutomaticRepairFailure_RecordsNoEffectAndRecommendsTroubleshootWorkspace()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var workspaceRoot = Path.Combine(tempRoot, "workspace");
            Directory.CreateDirectory(workspaceRoot);
            var repository = new WorkspaceRepository(GetAppDataRoot(tempRoot));
            var timelineService = new WorkspaceTimelineService();
            var checkpointService = new WorkspaceCheckpointService();
            var runtime = new StubContainerRuntime();
            var orchestrator = CreateOrchestrator(tempRoot, repository, timelineService, runtime, terminalLauncher: new FailingTerminalLauncher());
            var created = await orchestrator.CreateWorkspaceAsync(workspaceRoot, CreateDefinition("Open Loop"), includeRuntimeInspection: false);
            await orchestrator.ProvisionAsync(created);
            File.Delete(created.Paths.RuntimeStatePath);
            var missingRuntimeStateSnapshot = await orchestrator.LoadSnapshotAsync(created.Paths.RootPath, includeRuntimeInspection: true, includeSessionInspection: false);
            var service = CreateDesktopShellService(orchestrator, repository, timelineService, checkpointService);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenWorkspaceAsync(created.Paths.RootPath, missingRuntimeStateSnapshot));
            var saved = repository.LoadAll().Single(record => string.Equals(record.RootPath, created.Paths.RootPath, StringComparison.OrdinalIgnoreCase));

            Assert.True(
                exception.Message.Contains("repair the runtime automatically", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("preparing the terminal", StringComparison.OrdinalIgnoreCase),
                $"Unexpected exception message: {exception.Message}");
            Assert.NotNull(saved.LastProvisioningHealth);
            Assert.Equal("Rebuild Runtime.", saved.LastProvisioningHealth!.RecommendedAction);
            Assert.Contains(saved.LastProvisioningHealth.RepairHistory, item => item.RepairType == "Automatic Safe Repair" && item.Result == WorkspaceRepairOutcome.RepairNoEffect);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task Reprovision_FailureThenSuccess_ClearsCurrentFailureAndRetainsHistory()
    {
        var tempRoot = CreateTempRoot();
        var workspaceRoot = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var repository = new WorkspaceRepository(GetAppDataRoot(tempRoot));
            var timelineService = new WorkspaceTimelineService();
            var checkpointService = new WorkspaceCheckpointService();
            var failingRuntime = new StubContainerRuntime
            {
                ProvisionScriptResultFactory = () => Failure("docker exec provision", "/workspace/.env: line 17: $'Analiza\\r': command not found"),
            };

            var failingOrchestrator = CreateOrchestrator(tempRoot, repository, timelineService, failingRuntime);
            var created = await failingOrchestrator.CreateWorkspaceAsync(workspaceRoot, CreateDefinition("Odip Analiza"), includeRuntimeInspection: false);

            var failingService = new DesktopShellService(failingOrchestrator, repository, timelineService, checkpointService, new WorkspaceSavePointMessageService(new ProcessRunner()), new WorkspaceBackupExportService(), new WorkspaceBackupManifestService(), new WorkspacePublishAssessmentService(new ProcessRunner()), new WorkspaceRemovalService(repository), new OracleSoftwareNoticeService(repository), new WindowsTerminalProfileSetupService(new WindowsTerminalProfileManager(), new WindowsHostCapabilities(new ProcessRunner())));
            await Assert.ThrowsAsync<InvalidOperationException>(() => failingService.ReprovisionWorkspaceAsync(created.Paths.RootPath));

            var failedRecord = repository.LoadAll().Single(record => string.Equals(record.RootPath, created.Paths.RootPath, StringComparison.OrdinalIgnoreCase));
            Assert.False(failedRecord.LastOperationSucceeded);
            Assert.Contains("/workspace/.env: line 17", failedRecord.LastOperationResult, StringComparison.Ordinal);

            var successfulRuntime = new StubContainerRuntime();
            var successfulOrchestrator = CreateOrchestrator(tempRoot, repository, timelineService, successfulRuntime);
            var successfulService = new DesktopShellService(successfulOrchestrator, repository, timelineService, checkpointService, new WorkspaceSavePointMessageService(new ProcessRunner()), new WorkspaceBackupExportService(), new WorkspaceBackupManifestService(), new WorkspacePublishAssessmentService(new ProcessRunner()), new WorkspaceRemovalService(repository), new OracleSoftwareNoticeService(repository), new WindowsTerminalProfileSetupService(new WindowsTerminalProfileManager(), new WindowsHostCapabilities(new ProcessRunner())));

            var result = await successfulService.ReprovisionWorkspaceAsync(created.Paths.RootPath);

            Assert.True(result.Succeeded);
            Assert.True(result.Snapshot.Record.LastOperationSucceeded);
            Assert.Equal("Workspace reprovisioned successfully.", result.Snapshot.Record.LastOperationResult);

            var savedRecord = repository.LoadAll().Single(record => string.Equals(record.RootPath, created.Paths.RootPath, StringComparison.OrdinalIgnoreCase));
            Assert.True(savedRecord.LastOperationSucceeded);
            Assert.Equal("Workspace reprovisioned successfully.", savedRecord.LastOperationResult);

            var timeline = timelineService.Load(result.Snapshot.Paths.TimelinePath);
            Assert.Contains(timeline.Events, item => item.Type == "reprovision-failed" && item.Details.Contains("/workspace/.env: line 17", StringComparison.Ordinal));
            Assert.Contains(timeline.Events, item => item.Type == "reprovision-succeeded");
            Assert.Contains(result.Transcript.Lines, line => line.Kind == OperationTranscriptLineKind.Result && line.Text == "Completed");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task Remove_DeleteFilesUnsupported_IsRejectedBeforeSideEffects_AndLeavesWorkspaceRegistered()
    {
        var tempRoot = CreateTempRoot();
        var workspaceRoot = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var repository = new WorkspaceRepository(GetAppDataRoot(tempRoot));
            var timelineService = new WorkspaceTimelineService();
            var checkpointService = new WorkspaceCheckpointService();
            var runtime = new StubContainerRuntime();
            var orchestrator = CreateOrchestrator(tempRoot, repository, timelineService, runtime);
            var created = await orchestrator.CreateWorkspaceAsync(workspaceRoot, CreateDefinition("Remove Failure"), includeRuntimeInspection: false);
            var service = new DesktopShellService(orchestrator, repository, timelineService, checkpointService, new WorkspaceSavePointMessageService(new ProcessRunner()), new WorkspaceBackupExportService(), new WorkspaceBackupManifestService(), new WorkspacePublishAssessmentService(new ProcessRunner()), new WorkspaceRemovalService(repository), new OracleSoftwareNoticeService(repository), new WindowsTerminalProfileSetupService(new WindowsTerminalProfileManager(), new WindowsHostCapabilities(new ProcessRunner())));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveWorkspaceAsync(created.Paths.RootPath, WorkspaceRemovalChoice.DeleteFiles, created));

            Assert.Contains("Delete workspace files is not available in this version", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, runtime.RemoveCallCount);
            Assert.Contains(repository.LoadAll(), record => string.Equals(record.RootPath, created.Paths.RootPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task Remove_DockerCleanupFailure_LeavesWorkspaceRegistered()
    {
        var tempRoot = CreateTempRoot();
        var workspaceRoot = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var repository = new WorkspaceRepository(GetAppDataRoot(tempRoot));
            var timelineService = new WorkspaceTimelineService();
            var checkpointService = new WorkspaceCheckpointService();
            var runtime = new StubContainerRuntime
            {
                RemoveResultFactory = () => Failure("docker compose rm", "Docker engine is unavailable."),
            };
            var orchestrator = CreateOrchestrator(tempRoot, repository, timelineService, runtime);
            var created = await orchestrator.CreateWorkspaceAsync(workspaceRoot, CreateDefinition("Remove Docker Failure"), includeRuntimeInspection: false);
            var service = new DesktopShellService(orchestrator, repository, timelineService, checkpointService, new WorkspaceSavePointMessageService(new ProcessRunner()), new WorkspaceBackupExportService(), new WorkspaceBackupManifestService(), new WorkspacePublishAssessmentService(new ProcessRunner()), new WorkspaceRemovalService(repository), new OracleSoftwareNoticeService(repository), new WindowsTerminalProfileSetupService(new WindowsTerminalProfileManager(), new WindowsHostCapabilities(new ProcessRunner())));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveWorkspaceAsync(created.Paths.RootPath, WorkspaceRemovalChoice.DockerResources, created));

            Assert.Contains("Docker engine is unavailable.", exception.Message, StringComparison.Ordinal);
            Assert.Equal(1, runtime.RemoveCallCount);
            Assert.Contains(repository.LoadAll(), record => string.Equals(record.RootPath, created.Paths.RootPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task Remove_DockerResourcesWithNestedConfigurationPath_ProbesWorkspaceYamlInsideSelectedWorkspaceRoot()
    {
        var tempRoot = CreateTempRoot();
        var baseRoot = Path.Combine(tempRoot, "workspaces");
        var workspaceRoot = Path.Combine(baseRoot, "rc-first-workspace");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var repository = new WorkspaceRepository(GetAppDataRoot(tempRoot));
            repository.Save(new WorkspaceRecord
            {
                Name = "rc-first-workspace",
                RootPath = baseRoot,
                RepositoryPath = baseRoot,
                ConfigurationPath = "rc-first-workspace/workspace.yaml",
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            });

            var timelineService = new WorkspaceTimelineService();
            var checkpointService = new WorkspaceCheckpointService();
            var runtime = new StubContainerRuntime();
            var orchestrator = CreateOrchestrator(tempRoot, repository, timelineService, runtime);
            var service = new DesktopShellService(orchestrator, repository, timelineService, checkpointService, new WorkspaceSavePointMessageService(new ProcessRunner()), new WorkspaceBackupExportService(), new WorkspaceBackupManifestService(), new WorkspacePublishAssessmentService(new ProcessRunner()), new WorkspaceRemovalService(repository), new OracleSoftwareNoticeService(repository), new WindowsTerminalProfileSetupService(new WindowsTerminalProfileManager(), new WindowsHostCapabilities(new ProcessRunner())));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveWorkspaceAsync(workspaceRoot, WorkspaceRemovalChoice.DockerResources));

            var expectedPath = Path.Combine(workspaceRoot, "workspace.yaml");
            var unexpectedPath = Path.Combine(baseRoot, "workspace.yaml");
            Assert.Contains(expectedPath, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(unexpectedPath, exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, runtime.RemoveCallCount);
            Assert.Contains(repository.LoadAll(), record => string.Equals(record.Name, "rc-first-workspace", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    private static WorkspaceOrchestrator CreateOrchestrator(string tempRoot, WorkspaceRepository repository, WorkspaceTimelineService timelineService, IContainerRuntime runtime, IRuntimeResolver? runtimeResolver = null, ITerminalLauncher? terminalLauncher = null)
    {
        return new WorkspaceOrchestrator(
            new WorkspaceYamlService(),
            new WorkspaceDiscoveryService(),
            repository,
            CreateResolver(),
            new ComposeGenerator(),
            new EnvironmentFileGenerator(),
            new ProvisioningScriptGenerator(),
            new TerminalArtifactsGenerator(),
            new AttachArtifactsGenerator(),
            new WorkspaceContentGenerator(),
            new WorkspaceAppliedStateService(),
            new WorkspaceCheckpointService(),
            timelineService,
            new WorkspaceSafetyService(),
            new WorkspaceIgnorePolicyService(),
            new WorkspaceRuntimeStateService(),
            new FakeWorkspaceProvider(),
            runtime,
            new FixedPlatformDetector(),
            runtimeResolver ?? new FixedRuntimeResolver(),
            terminalLauncher ?? new NoOpTerminalLauncher());
    }

    private static async Task<WorkspaceOpenFixture> CreateValidNewWorkspaceFixtureAsync(string tempRoot, string workspaceName)
    {
        var workspaceRoot = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(workspaceRoot);
        var repository = new WorkspaceRepository(GetAppDataRoot(tempRoot));
        var timelineService = new WorkspaceTimelineService();
        var checkpointService = new WorkspaceCheckpointService();
        var runtime = new StubContainerRuntime();
        var orchestrator = CreateOrchestrator(tempRoot, repository, timelineService, runtime);
        var created = await orchestrator.CreateWorkspaceAsync(workspaceRoot, CreateDefinition(workspaceName), includeRuntimeInspection: false);
        var service = CreateDesktopShellService(orchestrator, repository, timelineService, checkpointService);
        return new WorkspaceOpenFixture(created, created, service, orchestrator);
    }

    private static async Task<WorkspaceOpenFixture> CreateValidProvisionedStoppedWorkspaceFixtureAsync(string tempRoot, string workspaceName)
    {
        var fixture = await CreateValidNewWorkspaceFixtureAsync(tempRoot, workspaceName);
        await fixture.Orchestrator.ProvisionAsync(fixture.CreatedSnapshot);
        var provisioned = await fixture.Orchestrator.LoadSnapshotAsync(fixture.CreatedSnapshot.Paths.RootPath, includeRuntimeInspection: true, includeSessionInspection: false);
        return fixture with { OpenSnapshot = provisioned };
    }

    private static async Task<WorkspaceOpenFixture> CreateMissingRuntimeStateWorkspaceFixtureAsync(string tempRoot, string workspaceName)
    {
        var fixture = await CreateValidProvisionedStoppedWorkspaceFixtureAsync(tempRoot, workspaceName);
        File.Delete(fixture.CreatedSnapshot.Paths.RuntimeStatePath);
        var snapshot = await fixture.Orchestrator.LoadSnapshotAsync(fixture.CreatedSnapshot.Paths.RootPath, includeRuntimeInspection: true, includeSessionInspection: false);
        return fixture with { OpenSnapshot = snapshot };
    }

    private static DesktopShellService CreateDesktopShellService(WorkspaceOrchestrator orchestrator, WorkspaceRepository repository, WorkspaceTimelineService timelineService, WorkspaceCheckpointService checkpointService)
        => new(orchestrator, repository, timelineService, checkpointService, new WorkspaceSavePointMessageService(new ProcessRunner()), new WorkspaceBackupExportService(), new WorkspaceBackupManifestService(), new WorkspacePublishAssessmentService(new ProcessRunner()), new WorkspaceRemovalService(repository), new OracleSoftwareNoticeService(repository), new WindowsTerminalProfileSetupService(new WindowsTerminalProfileManager(), new WindowsHostCapabilities(new ProcessRunner())));

    private static WorkspaceResolver CreateResolver()
    {
        return new WorkspaceResolver(
            [new FeatureManifest { Id = "core", AlwaysEnabled = true, Dependencies = new DependencySet { Apt = ["git", "curl"] } }],
            Array.Empty<ServiceManifest>(),
            Array.Empty<CapabilityManifest>(),
            Array.Empty<KnowledgePackManifest>());
    }

    private static WorkspaceDefinition CreateDefinition(string name)
    {
        return new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = name, Image = "ubuntu:24.04" },
            Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
            Features = ["core"],
            Terminal = new TerminalPreferences
            {
                Prompt = new TerminalPromptPreferences { Provider = "starship" },
                Utilities = new TerminalUtilityPreferences(),
            },
        };
    }

    private static string CreateTempRoot() => Path.Combine(Path.GetTempPath(), $"avalonia-reprovision-state-{Guid.NewGuid():N}");

    private static string GetAppDataRoot(string tempRoot)
        => Path.Combine(Path.GetDirectoryName(tempRoot) ?? Path.GetTempPath(), $"{Path.GetFileName(tempRoot)}-appdata");

    private static void DeleteTempRoot(string tempRoot)
    {
        var appDataRoot = GetAppDataRoot(tempRoot);
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }

        if (Directory.Exists(appDataRoot))
        {
            Directory.Delete(appDataRoot, true);
        }
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    private sealed record WorkspaceOpenFixture(WorkspaceSnapshot CreatedSnapshot, WorkspaceSnapshot OpenSnapshot, DesktopShellService Service, WorkspaceOrchestrator Orchestrator);

    private sealed class FakeWorkspaceProvider : IWorkspaceProvider
    {
        public string Type => "git";

        public Task InitializeWorkspaceAsync(WorkspacePaths paths, WorkspaceDefinition definition, bool createInitialSavePoint, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<WorkspaceGitState> GetGitStateAsync(WorkspacePaths paths, WorkspaceDefinition definition, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceGitState
            {
                IsRepository = true,
                WorkingCopyName = "users/test/demo-20260620-1200",
                CurrentBranch = "users/test/demo-20260620-1200",
                LatestCommitSha = "abc123",
                LatestCommitUtc = DateTimeOffset.UtcNow,
                IsSafeWorkingCopy = true,
                StatusSummary = "clean",
            });

        public Task<bool> CreateSavePointAsync(WorkspacePaths paths, WorkspaceDefinition definition, string message, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<WorkspacePublishReview> PublishAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspacePublishReview { Message = "Published." });

        public Task<WorkspacePublishReview> UpdateWorkingCopyAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspacePublishReview { Message = "Updated." });

        public Task<WorkspacePublishReview> PublishToReviewWorkingCopyAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspacePublishReview { Message = "Published review Working Copy." });

        public Task<string> ExportPatchAsync(WorkspacePaths paths, WorkspaceDefinition definition, string outputPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(outputPath);
    }

    private sealed class FixedPlatformDetector : IPlatformDetector
    {
        public Task<HostPlatformInfo> DetectAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new HostPlatformInfo
            {
                OperatingSystem = HostOperatingSystem.Windows,
                Architecture = HostArchitecture.X64,
                HostDescription = "Windows X64",
                NativeContainerPlatform = "linux/amd64",
                Docker = new ContainerRuntimeAvailability
                {
                    EngineId = "docker",
                    CliAvailable = true,
                    EngineReachable = true,
                    BuildxAvailable = true,
                    SupportedPlatforms = ["linux/amd64", "linux/arm64"],
                },
            });
        }
    }

    private sealed class FixedRuntimeResolver : IRuntimeResolver
    {
        public Task<ResolvedRuntimePlan> ResolveAsync(WorkspaceDefinition definition, HostPlatformInfo hostPlatform, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = "linux/amd64",
                CompatibilityMode = RuntimeCompatibilityMode.Native,
                SupportLevel = SupportLevel.NativeTested,
                IsAvailable = true,
                DiagnosticExplanation = "Test runtime plan.",
                HostPlatform = hostPlatform,
            });
        }
    }

    private sealed class NoOpTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FailingTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromException(new InvalidOperationException("Open Workspace could not finish preparing the terminal. Troubleshoot Workspace can inspect the runtime files and launch readiness."));
    }

    private sealed class UnavailableRuntimeResolver : IRuntimeResolver
    {
        public Task<ResolvedRuntimePlan> ResolveAsync(WorkspaceDefinition definition, HostPlatformInfo hostPlatform, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = "linux/amd64",
                CompatibilityMode = RuntimeCompatibilityMode.Unavailable,
                SupportLevel = SupportLevel.Unavailable,
                IsAvailable = false,
                DiagnosticExplanation = "Runtime unavailable for test.",
                HostPlatform = hostPlatform,
            });
        }
    }

    private sealed class StubContainerRuntime : IContainerRuntime
    {
        private bool _provisioned;

        public string RuntimeId => "docker";

        public int RemoveCallCount { get; private set; }

        public Func<ProcessResult>? ProvisionScriptResultFactory { get; init; }
        public Func<ProcessResult>? RemoveResultFactory { get; init; }
        public Func<ProcessResult?>? ValidateVolatileEnvironmentResultFactory { get; init; }

        public string GetWorkspaceContainerName(WorkspaceDefinition definition) => DockerService.GetWorkspaceContainerName(definition);

        public IReadOnlyList<string> CreatePermissionRepairArguments(string workspaceRootPath) => DockerService.CreatePermissionRepairArguments(workspaceRootPath);

        public Task<ProcessResult> StartAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
            => Task.FromResult(Success("docker compose up"));

        public Task<ProcessResult> ValidateAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
            => Task.FromResult(Success("docker compose config"));

        public Task<ProcessResult> StopAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("docker compose down"));

        public Task<ProcessResult> RemoveAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
        {
            RemoveCallCount++;
            return Task.FromResult(RemoveResultFactory?.Invoke() ?? Success("docker compose rm"));
        }

        public Task<ProcessResult> ResetAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
            => Task.FromResult(Success("docker compose reset"));

        public Task<ProcessResult?> ValidateVolatileEnvironmentAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(ValidateVolatileEnvironmentResultFactory?.Invoke());

        public Task<ProcessResult> GetPsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("docker compose ps", "workspace"));

        public Task<ProcessResult> GetComposePsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("docker compose ps", "workspace"));

        public Task<ProcessResult> GetServiceLogsAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("docker compose logs"));

        public Task<ProcessResult> RunProvisionScriptAsync(WorkspaceDefinition definition, WorkspacePaths paths, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        {
            var result = ProvisionScriptResultFactory?.Invoke() ?? Success("docker exec provision");
            if (result.IsSuccess)
            {
                _provisioned = true;
            }

            return Task.FromResult(result);
        }

        public Task<ProcessResult> InspectContainerImageAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("docker inspect image", "sha256:test-image"));

        public Task<ProcessResult> InspectImageRepoTagsAsync(string imageId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("docker inspect tags", "[\"ubuntu:24.04\"]"));

        public Task<ProcessResult> GetNodeToolDiagnosticsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("docker exec node", "/usr/bin/node\nv22.15.0\n/usr/bin/npm\n10.9.2"));

        public Task<ProcessResult> GetNodeAptPolicyAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("docker exec apt-cache", "nodejs:\n  Installed: 22.15.0-1nodesource1"));

        public Task<ProcessResult> GetOsReleaseAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("docker exec os-release", "PRETTY_NAME=Ubuntu 24.04 LTS"));

        public Task<ProcessResult> CheckOpencodeUserAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult((_provisioned ? Success("docker exec id", "uid=1001(opencode)") : Failure("docker exec id", "id: 'opencode': no such user")));

        public Task<ProcessResult> EnsureOpencodeUserDirectoriesAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("docker exec ensure-directories"));

        public Task<ProcessResult> NormalizeWorkspaceFilePermissionsAsync(string workspaceRootPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("docker run chmod-helper"));

        public Task<ProcessResult> RunSimpleDockerCommandAsync(IEnumerable<string> arguments, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        {
            var argumentList = arguments.ToList();
            if (argumentList.Count > 0 && argumentList[0] == "ps")
            {
                var filter = argumentList.FirstOrDefault(item => item.StartsWith("name=", StringComparison.Ordinal));
                var containerName = filter is null ? "workspace" : filter[5..];
                return Task.FromResult(Success("docker ps", containerName));
            }

            if (argumentList.Count >= 5 && argumentList[0] == "exec" && argumentList[3] == "-lc")
            {
                var shellCommand = argumentList[4];
                if (shellCommand.Contains("command -v opencode && command -v screen && command -v node && command -v npm && getent passwd opencode", StringComparison.Ordinal))
                {
                    return Task.FromResult(Success("docker exec tool-check", "/usr/local/bin/opencode\n/usr/bin/screen\n/usr/bin/node\n/usr/bin/npm\nopencode:x:1001:1001::/home/opencode:/bin/bash"));
                }

                if (shellCommand.Contains("command -v starship", StringComparison.Ordinal))
                {
                    return Task.FromResult(Success("docker exec starship", "starship 1.0.0"));
                }
            }

            return Task.FromResult(Success("docker command"));
        }

        public Task<ProcessResult> ListOpenCodeSessionsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("docker exec opencode session list"));

        public Task<ProcessResult> ExportOpenCodeSessionAsync(WorkspaceDefinition definition, string sessionId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("docker exec opencode session export"));
    }

    private static ProcessResult Success(string command, string standardOutput = "")
        => new()
        {
            Command = command,
            ExitCode = 0,
            StandardOutput = standardOutput,
            StandardError = string.Empty,
            StandardOutputLines = string.IsNullOrWhiteSpace(standardOutput) ? Array.Empty<string>() : standardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StandardErrorLines = Array.Empty<string>(),
            Duration = TimeSpan.FromMilliseconds(10),
        };

    private static ProcessResult Failure(string command, string standardError)
        => new()
        {
            Command = command,
            ExitCode = 1,
            StandardOutput = string.Empty,
            StandardError = standardError,
            StandardOutputLines = Array.Empty<string>(),
            StandardErrorLines = [standardError],
            Duration = TimeSpan.FromMilliseconds(10),
        };
}
