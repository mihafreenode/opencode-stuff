using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using System.Text.Json;
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
    public async Task Build_XdbInvalidEvidence_DegradesOnlyAffectedOracleLayer()
    {
        var snapshot = CreateSnapshot(
            runtimeState: WorkspaceRuntimeState.Running,
            services: ["oracle-demo", "oracle-ords"],
            localRuntimeState: CreateRuntimeState(),
            appliedState: new WorkspaceAppliedState { AppliedUtc = DateTimeOffset.UtcNow, DesiredStateHash = "desired", WorkspaceDefinitionHash = "definition" },
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

        var health = await WorkspaceHealthEngine.BuildAsync(snapshot, new FakeProbeRunner(
            tcpResults:
            [
                new WorkspaceServiceProbeResult { IsReachable = true },
            ],
            httpResults:
            [
                new WorkspaceServiceProbeResult { IsReachable = true, StatusCode = System.Net.HttpStatusCode.OK, ContentType = "text/html", ResponseSample = "Oracle REST Data Services SQL Developer Web Oracle APEX" },
                new WorkspaceServiceProbeResult { IsReachable = true, StatusCode = System.Net.HttpStatusCode.OK, ContentType = "text/html", ResponseSample = "SQL Developer Web" },
                new WorkspaceServiceProbeResult { IsReachable = true, StatusCode = System.Net.HttpStatusCode.OK, ContentType = "text/html", ResponseSample = "REST APIs" },
                new WorkspaceServiceProbeResult { IsReachable = true, StatusCode = System.Net.HttpStatusCode.OK, ContentType = "text/html", ResponseSample = "Oracle APEX" },
            ]));

        Assert.Equal(WorkspaceHealthStatus.Attention, health.OverallStatus);
        Assert.Equal(WorkspaceHealthStatus.Healthy, health.Providers.Single(item => item.ProviderKey == "oracle").Status);
        Assert.Equal(WorkspaceHealthStatus.Attention, health.Providers.Single(item => item.ProviderKey == "oracle-xdb").Status);
        Assert.Contains("APEX", health.Providers.Single(item => item.ProviderKey == "oracle-xdb").WorkspaceImpact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_OracleContainerUnreachable_ReportsUnavailableOracleLayer()
    {
        var snapshot = CreateSnapshot(
            runtimeState: WorkspaceRuntimeState.Running,
            services: ["oracle-demo", "oracle-ords"],
            localRuntimeState: CreateRuntimeState(),
            appliedState: new WorkspaceAppliedState { AppliedUtc = DateTimeOffset.UtcNow, DesiredStateHash = "desired", WorkspaceDefinitionHash = "definition" });

        var health = await WorkspaceHealthEngine.BuildAsync(snapshot, new FakeProbeRunner(
            tcpResults:
            [
                new WorkspaceServiceProbeResult { IsReachable = false, FailureReason = "Connection refused" },
            ],
            httpResults: []));

        Assert.Equal(WorkspaceHealthStatus.Unavailable, health.Providers.Single(item => item.ProviderKey == "oracle").Status);
    }

    [Fact]
    public async Task Build_ApexUnavailableButOrdsAvailable_StaysAttentionNotUnavailable()
    {
        var snapshot = CreateSnapshot(
            runtimeState: WorkspaceRuntimeState.Running,
            services: ["oracle-demo", "oracle-ords"],
            localRuntimeState: CreateRuntimeState(),
            appliedState: new WorkspaceAppliedState { AppliedUtc = DateTimeOffset.UtcNow, DesiredStateHash = "desired", WorkspaceDefinitionHash = "definition" },
            investigationHistory:
            [
                new WorkspaceInvestigationRecord
                {
                    InvestigationId = "inspect-apex",
                    Title = "Inspect APEX",
                    Summary = "APEX application is unavailable.",
                    Evidence = "APEX returned the App Unavailable page.",
                    Recommendation = "Troubleshoot Workspace.",
                    Outcome = "APEX evidence collected.",
                    Confidence = "HIGH",
                    CompletedUtc = DateTimeOffset.UtcNow,
                    StartedUtc = DateTimeOffset.UtcNow.AddSeconds(-5),
                    Duration = TimeSpan.FromSeconds(5),
                    ProviderName = "Oracle",
                },
            ]);

        var health = await WorkspaceHealthEngine.BuildAsync(snapshot, new FakeProbeRunner(
            tcpResults:
            [
                new WorkspaceServiceProbeResult { IsReachable = true },
            ],
            httpResults:
            [
                new WorkspaceServiceProbeResult { IsReachable = true, StatusCode = System.Net.HttpStatusCode.OK, ContentType = "text/html", ResponseSample = "Oracle REST Data Services SQL Developer Web" },
                new WorkspaceServiceProbeResult { IsReachable = true, StatusCode = System.Net.HttpStatusCode.OK, ContentType = "text/html", ResponseSample = "SQL Developer Web" },
                new WorkspaceServiceProbeResult { IsReachable = true, StatusCode = System.Net.HttpStatusCode.OK, ContentType = "text/html", ResponseSample = "REST APIs" },
                new WorkspaceServiceProbeResult { IsReachable = true, StatusCode = System.Net.HttpStatusCode.ServiceUnavailable, ContentType = "text/html", ResponseSample = "App Unavailable" },
            ]));

        Assert.Equal(WorkspaceHealthStatus.Attention, health.OverallStatus);
        Assert.Equal(WorkspaceHealthStatus.Healthy, health.Providers.Single(item => item.ProviderKey == "ords").Status);
        Assert.Equal(WorkspaceHealthStatus.Attention, health.Providers.Single(item => item.ProviderKey == "apex").Status);
        Assert.DoesNotContain(health.Providers, item => item.ProviderKey == "apex" && item.Status == WorkspaceHealthStatus.Unavailable);
    }

    [Fact]
    public void Build_RuntimeResourceConflict_ReportsHealthFacts()
    {
        var snapshot = CreateSnapshot(localRuntimeState: new WorkspaceRuntimeStateRecord
        {
            ResolvedEngine = "docker",
            ResolvedPlatform = "linux/amd64",
            CompatibilityMode = "Native",
            Resources = new WorkspaceManagedRuntimeResources
            {
                Ports =
                [
                    new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.PostgresResourceId, ServiceId = "postgres", DisplayName = "PostgreSQL", Protocol = "tcp", PreferredPort = 15432, AllocatedPort = 15433, ContainerPort = 5432, AllocationKind = "Alternative", Endpoint = "tcp://localhost:15433", OpenUrl = "tcp://localhost:15433" },
                ],
                Conflicts =
                [
                    new WorkspaceResourceConflictRecord { ResourceId = WorkspaceRuntimeResourceCatalog.PostgresResourceId, DisplayName = "PostgreSQL", PreferredPort = 15432, ConflictKind = "ManagedWorkspace", Owner = "workspace analytics-demo", Resolution = "Allocated alternative port 15433." },
                ],
            },
        }, appliedState: new WorkspaceAppliedState { AppliedUtc = DateTimeOffset.UtcNow, DesiredStateHash = "desired", WorkspaceDefinitionHash = "definition" }, services: ["postgres"]);

        var health = WorkspaceHealthEngine.Build(snapshot);

        var runtime = health.Providers.Single(item => item.ProviderKey == "runtime");
        Assert.Equal(WorkspaceHealthStatus.Attention, runtime.Status);
        Assert.Contains(runtime.Evidence, item => item.Label == "Port 15432" && item.Value.Contains("analytics-demo", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_DevelopmentEnvironmentAttention_DoesNotDowngradeWorkspaceHeadline()
    {
        var snapshot = CreateSnapshot(
            runtimeState: WorkspaceRuntimeState.Running,
            services: Array.Empty<string>(),
            features: Array.Empty<string>(),
            localRuntimeState: CreateRuntimeState(),
            appliedState: new WorkspaceAppliedState { AppliedUtc = DateTimeOffset.UtcNow, DesiredStateHash = "desired", WorkspaceDefinitionHash = "definition" },
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
            });

        var health = WorkspaceHealthEngine.Build(snapshot);

        Assert.Equal(WorkspaceHealthStatus.Healthy, health.OverallStatus);
        Assert.NotNull(health.DevelopmentEnvironment);
        Assert.Equal(WorkspaceHealthStatus.Attention, health.DevelopmentEnvironment!.Status);
        Assert.Contains(health.Providers, item => item.ProviderKey == "development-environment" && item.Status == WorkspaceHealthStatus.Attention);
    }

    private static WorkspaceSnapshot CreateSnapshot(
        WorkspaceRuntimeState runtimeState = WorkspaceRuntimeState.Stopped,
        string[]? services = null,
        string[]? features = null,
        WorkspaceRuntimeStateRecord? localRuntimeState = null,
        WorkspaceAppliedState? appliedState = null,
        IReadOnlyList<WorkspaceInvestigationRecord>? investigationHistory = null,
        WorkspaceDevelopmentEnvironmentHealthSnapshot? developmentEnvironment = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"workspace-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "workspace.yaml"), "workspace: {}\n");
        File.WriteAllText(Path.Combine(root, "compose.yaml"), "services: {}\n");
        Directory.CreateDirectory(Path.Combine(root, "mounts", "config"));
        File.WriteAllText(Path.Combine(root, "mounts", "config", "provision.sh"), "#!/bin/bash\n");
        Directory.CreateDirectory(Path.Combine(root, ".opencode", "local"));
        if (developmentEnvironment is not null)
        {
            File.WriteAllText(Path.Combine(root, ".opencode", "local", "development-environment-health.json"), JsonSerializer.Serialize(developmentEnvironment));
        }

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
                Features = (features ?? ["core", "apex"]).ToList(),
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

    private static WorkspaceRuntimeStateRecord CreateRuntimeState()
        => new()
        {
            ResolvedEngine = "docker",
            ResolvedPlatform = "linux/amd64",
            CompatibilityMode = "Native",
            Resources = new WorkspaceManagedRuntimeResources
            {
                Ports =
                [
                    new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.OracleDatabaseResourceId, ServiceId = "oracle-database", DisplayName = "Oracle Database", Protocol = "tcp", Host = "localhost", ContainerPort = 1521, PreferredPort = 1521, AllocatedPort = 1521, Endpoint = "tcp://localhost:1521", OpenUrl = "tcp://localhost:1521" },
                    new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.OracleOrdsResourceId, ServiceId = "ords", DisplayName = "ORDS", Protocol = "http", Host = "localhost", ContainerPort = 8181, PreferredPort = 8181, AllocatedPort = 8181, Endpoint = "http://localhost:8181/", OpenUrl = "http://localhost:8181/ords/_/landing" },
                ],
                ServiceEndpoints =
                [
                    new WorkspaceServiceEndpointRecord { ServiceId = "ords", DisplayName = "ORDS", Endpoint = "http://localhost:8181/ords/", OpenUrl = "http://localhost:8181/ords/_/landing" },
                    new WorkspaceServiceEndpointRecord { ServiceId = "sql-developer-web", DisplayName = "SQL Developer Web", Endpoint = "http://localhost:8181/ords/", OpenUrl = "http://localhost:8181/ords/_/landing" },
                    new WorkspaceServiceEndpointRecord { ServiceId = "apex", DisplayName = "Oracle APEX", Endpoint = "http://localhost:8181/ords/", OpenUrl = "http://localhost:8181/ords/apex/" },
                ],
            },
        };

    private sealed class FakeProbeRunner(
        IReadOnlyList<WorkspaceServiceProbeResult> tcpResults,
        IReadOnlyList<WorkspaceServiceProbeResult> httpResults) : IWorkspaceServiceProbeRunner
    {
        private readonly Queue<WorkspaceServiceProbeResult> _tcpResults = new(tcpResults);
        private readonly Queue<WorkspaceServiceProbeResult> _httpResults = new(httpResults);

        public Task<WorkspaceServiceProbeResult> ProbeTcpAsync(string host, int port, CancellationToken cancellationToken)
            => Task.FromResult(_tcpResults.Count > 0 ? _tcpResults.Dequeue() : new WorkspaceServiceProbeResult { IsReachable = false, FailureReason = "Connection refused" });

        public Task<WorkspaceServiceProbeResult> ProbeHttpAsync(Uri endpoint, CancellationToken cancellationToken)
            => Task.FromResult(_httpResults.Count > 0 ? _httpResults.Dequeue() : new WorkspaceServiceProbeResult { IsReachable = false, FailureReason = "Connection refused" });
    }
}
