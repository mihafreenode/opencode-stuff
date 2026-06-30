using System.Net;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceServiceHealthEngineTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, WorkspaceHealthStatus.Healthy, "Available")]
    [InlineData(HttpStatusCode.Found, WorkspaceHealthStatus.Healthy, "Available")]
    [InlineData(HttpStatusCode.Unauthorized, WorkspaceHealthStatus.Attention, "Authentication required")]
    [InlineData(HttpStatusCode.Forbidden, WorkspaceHealthStatus.Attention, "Access denied")]
    [InlineData(HttpStatusCode.NotFound, WorkspaceHealthStatus.Attention, "Not configured")]
    [InlineData(HttpStatusCode.InternalServerError, WorkspaceHealthStatus.Degraded, "Application error")]
    public async Task HttpServices_ClassifyStatuses(HttpStatusCode statusCode, WorkspaceHealthStatus expected, string expectedLabel)
    {
        var snapshot = CreateOracleSnapshot();
        var runner = new FakeProbeRunner
        {
            HttpResult = new WorkspaceServiceProbeResult
            {
                IsReachable = true,
                StatusCode = statusCode,
                Latency = TimeSpan.FromMilliseconds(41),
                ResponseSample = "Oracle Application Express",
                ContentType = "text/html",
                RedirectLocation = statusCode == HttpStatusCode.Found ? "http://localhost:8181/ords/_/landing" : string.Empty,
                ResponseHeaders = "Location: http://localhost:8181/ords/_/landing",
            },
            TcpResult = new WorkspaceServiceProbeResult { IsReachable = true, Latency = TimeSpan.FromMilliseconds(5) },
        };

        var services = await WorkspaceServiceHealthEngine.BuildAsync(snapshot, runner);

        var ords = services.Single(item => item.ServiceId == "ords");
        Assert.Equal(expected, ords.Status);
        Assert.Equal(expectedLabel, ords.StatusLabel);
    }

    [Fact]
    public async Task ApexValidator_ReportsUnavailableApplicationWithoutOpenUrl()
    {
        var snapshot = CreateOracleSnapshot();
        var runner = new FakeProbeRunner
        {
            HttpResult = new WorkspaceServiceProbeResult
            {
                IsReachable = true,
                StatusCode = HttpStatusCode.ServiceUnavailable,
                Latency = TimeSpan.FromMilliseconds(55),
                ResponseSample = "App Unavailable",
                ContentType = "text/html",
                ResponseHeaders = "content-type: text/html",
            },
            TcpResult = new WorkspaceServiceProbeResult { IsReachable = true, Latency = TimeSpan.FromMilliseconds(5) },
        };

        var services = await WorkspaceServiceHealthEngine.BuildAsync(snapshot, runner);

        var ords = services.Single(item => item.ServiceId == "ords");
        var sqlDeveloperWeb = services.Single(item => item.ServiceId == "sql-developer-web");
        var restApis = services.Single(item => item.ServiceId == "rest-apis");
        var apex = services.Single(item => item.ServiceId == "apex");
        Assert.Equal(WorkspaceHealthStatus.Degraded, ords.Status);
        Assert.Equal(WorkspaceHealthStatus.Degraded, sqlDeveloperWeb.Status);
        Assert.Equal(WorkspaceHealthStatus.Degraded, restApis.Status);
        Assert.Contains("⚠ SQL Developer Web", ords.Applications);
        Assert.Contains("⚠ REST APIs", ords.Applications);
        Assert.Contains("⚠ Oracle APEX", ords.Applications);
        Assert.Equal(WorkspaceHealthStatus.Attention, apex.Status);
        Assert.Equal("Unavailable", apex.StatusLabel);
        Assert.Equal("ORDS is available but APEX application is not currently configured.", apex.Summary);
        Assert.Equal("Complete APEX installation.", apex.Recommendation);
        Assert.Equal("Open Oracle APEX", apex.ActionLabel);
        Assert.Equal(string.Empty, apex.OpenUrl);
        Assert.Contains(apex.Evidence, item => item.Label == "Content validation");
    }

    [Fact]
    public async Task TcpUnavailable_ReportsServiceUnavailable()
    {
        var snapshot = CreatePostgresSnapshot();
        var runner = new FakeProbeRunner
        {
            TcpResult = new WorkspaceServiceProbeResult { FailureReason = "Connection refused" },
            HttpResult = new WorkspaceServiceProbeResult { FailureReason = "Connection refused" },
        };

        var services = await WorkspaceServiceHealthEngine.BuildAsync(snapshot, runner);

        var postgres = services.Single(item => item.ServiceId == "postgres");
        Assert.Equal(WorkspaceHealthStatus.Unavailable, postgres.Status);
        Assert.Equal("Unavailable", postgres.StatusLabel);
        Assert.Equal("localhost:15432", postgres.PrimaryUrl);
    }

    [Fact]
    public async Task RunningRuntime_ExposesApplicationOpenUrlPrimaryUrlAndDetails()
    {
        var snapshot = CreatePostgresSnapshot();
        var runner = new FakeProbeRunner
        {
            TcpResult = new WorkspaceServiceProbeResult { IsReachable = true, Latency = TimeSpan.FromMilliseconds(5) },
            HttpResult = new WorkspaceServiceProbeResult
            {
                IsReachable = true,
                StatusCode = HttpStatusCode.OK,
                Latency = TimeSpan.FromMilliseconds(20),
                ContentType = "text/html",
                ResponseSample = "pgadmin",
                ResponseHeaders = "content-type: text/html",
            },
        };

        var services = await WorkspaceServiceHealthEngine.BuildAsync(snapshot, runner);

        var pgadmin = services.Single(item => item.ServiceId == "pgadmin");
        Assert.Equal("http://localhost:18080/", pgadmin.OpenUrl);
        Assert.Equal("http://localhost:18080/", pgadmin.PrimaryUrl);
        Assert.Equal(TimeSpan.FromSeconds(30), pgadmin.RefreshInterval);
        Assert.Contains(pgadmin.Highlights, item => item.Label == "Latency" && item.Value == "20 ms");
        Assert.Contains(pgadmin.Evidence, item => item.Label == "Headers");
        Assert.Contains(pgadmin.Evidence, item => item.Label == "Last checked");
        Assert.Equal("Open pgAdmin", pgadmin.ActionLabel);
    }

    [Fact]
    public async Task RedirectResponses_UseRedirectTargetForOpenUrlAndPrimaryUrl()
    {
        var snapshot = CreateOracleSnapshot();
        var runner = new FakeProbeRunner
        {
            HttpResult = new WorkspaceServiceProbeResult
            {
                IsReachable = true,
                StatusCode = HttpStatusCode.Found,
                Latency = TimeSpan.FromMilliseconds(20),
                RedirectLocation = "http://localhost:8181/ords/_/landing",
                ResponseHeaders = "Location: http://localhost:8181/ords/_/landing",
            },
            TcpResult = new WorkspaceServiceProbeResult { IsReachable = true, Latency = TimeSpan.FromMilliseconds(5) },
        };

        var services = await WorkspaceServiceHealthEngine.BuildAsync(snapshot, runner);

        var ords = services.Single(item => item.ServiceId == "ords");
        Assert.Equal("http://localhost:8181/ords/_/landing", ords.OpenUrl);
        Assert.Equal("http://localhost:8181/ords/_/landing", ords.PrimaryUrl);
        Assert.Contains(ords.Evidence, item => item.Label == "Redirect" && item.Value == "http://localhost:8181/ords/_/landing");
    }

    [Fact]
    public async Task OrdsDiscovery_ExposesWorkspaceApplicationsWhenApexIsAvailable()
    {
        var snapshot = CreateOracleSnapshot();
        var runner = new FakeProbeRunner
        {
            HttpResult = new WorkspaceServiceProbeResult
            {
                IsReachable = true,
                StatusCode = HttpStatusCode.OK,
                Latency = TimeSpan.FromMilliseconds(24),
                ResponseSample = "Oracle Application Express SQL Developer Web",
                ContentType = "text/html",
                ResponseHeaders = "content-type: text/html",
            },
            TcpResult = new WorkspaceServiceProbeResult { IsReachable = true, Latency = TimeSpan.FromMilliseconds(5) },
        };

        var services = await WorkspaceServiceHealthEngine.BuildAsync(snapshot, runner);

        var ords = services.Single(item => item.ServiceId == "ords");
        var sqlDeveloperWeb = services.Single(item => item.ServiceId == "sql-developer-web");
        var restApis = services.Single(item => item.ServiceId == "rest-apis");
        var apex = services.Single(item => item.ServiceId == "apex");
        Assert.Equal("Open Oracle REST Data Services", ords.ActionLabel);
        Assert.Equal("Open SQL Developer Web", sqlDeveloperWeb.ActionLabel);
        Assert.Equal("Open REST APIs", restApis.ActionLabel);
        Assert.Equal(WorkspaceHealthStatus.Healthy, apex.Status);
        Assert.Contains("✓ SQL Developer Web", ords.Applications);
        Assert.Contains("✓ REST APIs", ords.Applications);
        Assert.Contains("✓ Oracle APEX", ords.Applications);
    }

    private static WorkspaceSnapshot CreateOracleSnapshot()
        => CreateSnapshot(WorkspaceRuntimeState.Running, ["oracle-demo", "oracle-ords"], ["core", "apex"]);

    private static WorkspaceSnapshot CreatePostgresSnapshot()
        => CreateSnapshot(WorkspaceRuntimeState.Running, ["postgres", "pgadmin"], ["core"]);

    private static WorkspaceSnapshot CreateSnapshot(WorkspaceRuntimeState runtimeState, string[] services, string[] features)
    {
        var root = Path.Combine(Path.GetTempPath(), $"workspace-service-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, ".opencode", "local"));
        Directory.CreateDirectory(Path.Combine(root, "mounts", "config"));
        File.WriteAllText(Path.Combine(root, "workspace.yaml"), "workspace: {}\n");
        File.WriteAllText(Path.Combine(root, "compose.yaml"), "services: {}\n");
        File.WriteAllText(Path.Combine(root, "mounts", "config", "provision.sh"), "#!/bin/bash\n");
        File.WriteAllText(Path.Combine(root, ".opencode", "local", "runtime-state.yaml"), "runtime: docker\n");
        File.WriteAllText(Path.Combine(root, "mounts", "config", "applied-state.yaml"), "applied: true\n");
        File.WriteAllText(Path.Combine(root, "mounts", "config", "attach.ps1"), "Write-Output attach\n");

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
                Services = services.ToList(),
                Features = features.ToList(),
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
            Session = new WorkspaceSessionSnapshot { SessionName = "alpha", State = WorkspaceSessionState.Resumable },
            AppliedState = new WorkspaceAppliedState { AppliedUtc = DateTimeOffset.UtcNow, DesiredStateHash = "desired", WorkspaceDefinitionHash = "definition" },
            LocalRuntimeState = new WorkspaceRuntimeStateRecord { ResolvedEngine = "docker", ResolvedPlatform = "linux/amd64", CompatibilityMode = "native" },
            ResolvedRuntimePlan = new ResolvedRuntimePlan { Runtime = "docker", TargetPlatform = "linux/amd64", IsAvailable = true, HostPlatform = new HostPlatformInfo() },
            UpdateRequired = false,
            Health = new WorkspaceHealthSnapshot(),
        };
    }

    private sealed class FakeProbeRunner : IWorkspaceServiceProbeRunner
    {
        public WorkspaceServiceProbeResult TcpResult { get; init; } = new();
        public WorkspaceServiceProbeResult HttpResult { get; init; } = new();

        public Task<WorkspaceServiceProbeResult> ProbeTcpAsync(string host, int port, CancellationToken cancellationToken)
            => Task.FromResult(TcpResult);

        public Task<WorkspaceServiceProbeResult> ProbeHttpAsync(Uri endpoint, CancellationToken cancellationToken)
            => Task.FromResult(HttpResult);
    }
}
