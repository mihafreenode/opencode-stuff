using System.Net;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceServiceHealthEngineTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, WorkspaceHealthStatus.Healthy)]
    [InlineData(HttpStatusCode.Found, WorkspaceHealthStatus.Healthy)]
    [InlineData(HttpStatusCode.Unauthorized, WorkspaceHealthStatus.Attention)]
    [InlineData(HttpStatusCode.Forbidden, WorkspaceHealthStatus.Attention)]
    [InlineData(HttpStatusCode.NotFound, WorkspaceHealthStatus.Attention)]
    [InlineData(HttpStatusCode.InternalServerError, WorkspaceHealthStatus.Degraded)]
    public async Task HttpServices_ClassifyStatuses(HttpStatusCode statusCode, WorkspaceHealthStatus expected)
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
            },
            TcpResult = new WorkspaceServiceProbeResult { IsReachable = true, Latency = TimeSpan.FromMilliseconds(5) },
        };

        var services = await WorkspaceServiceHealthEngine.BuildAsync(snapshot, runner);

        Assert.Equal(expected, services.Single(item => item.ServiceId == "ords").Status);
    }

    [Fact]
    public async Task ApexValidator_Downgrades503ToAttention()
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
            },
            TcpResult = new WorkspaceServiceProbeResult { IsReachable = true, Latency = TimeSpan.FromMilliseconds(5) },
        };

        var services = await WorkspaceServiceHealthEngine.BuildAsync(snapshot, runner);

        var ords = services.Single(item => item.ServiceId == "ords");
        var apex = services.Single(item => item.ServiceId == "apex");
        Assert.Equal(WorkspaceHealthStatus.Degraded, ords.Status);
        Assert.Equal(WorkspaceHealthStatus.Attention, apex.Status);
        Assert.Equal("Investigate APEX installation.", apex.Recommendation);
        Assert.Equal("http://localhost:8181/ords/", apex.OpenUrl);
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
        Assert.Contains(postgres.Evidence, item => item.Label == "failure" && item.Value == "Connection refused");
    }

    [Fact]
    public async Task RunningRuntime_ExposesClickableHttpUrlsAndRefreshIntervals()
    {
        var snapshot = CreatePostgresSnapshot();
        var runner = new FakeProbeRunner
        {
            TcpResult = new WorkspaceServiceProbeResult { IsReachable = true, Latency = TimeSpan.FromMilliseconds(5) },
            HttpResult = new WorkspaceServiceProbeResult { IsReachable = true, StatusCode = HttpStatusCode.OK, Latency = TimeSpan.FromMilliseconds(20), ContentType = "text/html", ResponseSample = "pgadmin" },
        };

        var services = await WorkspaceServiceHealthEngine.BuildAsync(snapshot, runner);

        var pgadmin = services.Single(item => item.ServiceId == "pgadmin");
        Assert.Equal("http://localhost:18080/", pgadmin.OpenUrl);
        Assert.Equal(TimeSpan.FromSeconds(30), pgadmin.RefreshInterval);
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
