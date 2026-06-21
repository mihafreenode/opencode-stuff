using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Avalonia.Tests;

public sealed class DesktopShellServiceReprovisionStateTests
{
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

            var failingService = new DesktopShellService(failingOrchestrator, repository, timelineService, checkpointService);
            await Assert.ThrowsAsync<InvalidOperationException>(() => failingService.ReprovisionWorkspaceAsync(created.Paths.RootPath));

            var failedRecord = repository.LoadAll().Single(record => string.Equals(record.RootPath, created.Paths.RootPath, StringComparison.OrdinalIgnoreCase));
            Assert.False(failedRecord.LastOperationSucceeded);
            Assert.Contains("/workspace/.env: line 17", failedRecord.LastOperationResult, StringComparison.Ordinal);

            var successfulRuntime = new StubContainerRuntime();
            var successfulOrchestrator = CreateOrchestrator(tempRoot, repository, timelineService, successfulRuntime);
            var successfulService = new DesktopShellService(successfulOrchestrator, repository, timelineService, checkpointService);

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

    private static WorkspaceOrchestrator CreateOrchestrator(string tempRoot, WorkspaceRepository repository, WorkspaceTimelineService timelineService, IContainerRuntime runtime)
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
            new FixedRuntimeResolver(),
            new NoOpTerminalLauncher());
    }

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

    private sealed class StubContainerRuntime : IContainerRuntime
    {
        private bool _provisioned;

        public string RuntimeId => "docker";

        public Func<ProcessResult>? ProvisionScriptResultFactory { get; init; }

        public string GetWorkspaceContainerName(WorkspaceDefinition definition) => DockerService.GetWorkspaceContainerName(definition);

        public IReadOnlyList<string> CreatePermissionRepairArguments(string workspaceRootPath) => DockerService.CreatePermissionRepairArguments(workspaceRootPath);

        public Task<ProcessResult> StartAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
            => Task.FromResult(Success("docker compose up"));

        public Task<ProcessResult> ValidateAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
            => Task.FromResult(Success("docker compose config"));

        public Task<ProcessResult> StopAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("docker compose down"));

        public Task<ProcessResult> RemoveAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
            => Task.FromResult(Success("docker compose rm"));

        public Task<ProcessResult> ResetAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
            => Task.FromResult(Success("docker compose reset"));

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
                return Task.FromResult(Success("docker ps", "odip-analiza-workspace"));
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
