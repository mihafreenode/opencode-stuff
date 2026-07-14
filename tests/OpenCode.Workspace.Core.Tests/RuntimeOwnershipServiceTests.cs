using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class RuntimeOwnershipServiceTests
{
    [Fact]
    public async Task BuildInventoryAsync_MapsOwnedResourcesAndProjects()
    {
        var root = CreateWorkspaceRoot();
        var composePath = Path.Combine(root, "compose.yaml");
        File.WriteAllText(composePath, "services: {}\n");
        var createdAt = DateTimeOffset.UtcNow.ToString("O");
        var runtime = new FakeContainerRuntime(root, composePath, createdAt);
        var service = new RuntimeOwnershipService(runtime);

        var inventory = await service.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke" });

        Assert.Equal(3, inventory.Resources.Count);
        Assert.Single(inventory.Projects);
        Assert.Equal(createdAt, inventory.Resources[0].CreatedAt);
        Assert.All(inventory.Resources, item => Assert.Equal("smoke", item.OwnerKind));
    }

    [Fact]
    public async Task BuildInventoryAsync_DetectsMissingComposeAndWorkspaceOrphans()
    {
        var runtime = new FakeContainerRuntime("/missing/workspace", "/missing/compose.yaml", DateTimeOffset.UtcNow.AddDays(-2).ToString("O"));
        var service = new RuntimeOwnershipService(runtime);

        var inventory = await service.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke" });

        Assert.NotEmpty(inventory.Orphans);
        Assert.NotEmpty(inventory.StaleRuntimes);
        Assert.NotEmpty(inventory.MissingComposeFiles);
        Assert.NotEmpty(inventory.MissingWorkspaceDirectories);
    }

    [Fact]
    public async Task CleanupAsync_FiltersByRunId()
    {
        var root = CreateWorkspaceRoot();
        var composePath = Path.Combine(root, "compose.yaml");
        File.WriteAllText(composePath, "services: {}\n");
        var runtime = new FakeContainerRuntime(root, composePath, DateTimeOffset.UtcNow.ToString("O"));
        var service = new RuntimeOwnershipService(runtime);

        var result = await service.CleanupAsync(new RuntimeCleanupOptions { DryRun = true, OwnerKind = "smoke", RunId = "run-1" });

        Assert.True(result.Succeeded);
        Assert.All(result.Resources, item => Assert.Equal("run-1", item.RunId));
    }

    [Fact]
    public async Task BuildInventoryAsync_FiltersByNormalizedWorkspaceRoot()
    {
        var runtime = new FakeContainerRuntime(
            "/private/var/folders/demo/workspace/",
            "/private/var/folders/demo/workspace/compose.yaml",
            DateTimeOffset.UtcNow.ToString("O"));
        var service = new RuntimeOwnershipService(runtime);

        var inventory = await service.BuildInventoryAsync(new RuntimeOwnershipQuery
        {
            OwnerKind = "smoke",
            WorkspaceRoot = "/var/folders/demo/current/../workspace",
        });

        Assert.Equal(3, inventory.Resources.Count);
    }

    [Fact]
    public async Task BuildInventoryAsync_FiltersByNormalizedComposePath()
    {
        var runtime = new FakeContainerRuntime(
            "/private/var/folders/demo/workspace",
            "/private/var/folders/demo/workspace/compose/../compose.yaml",
            DateTimeOffset.UtcNow.ToString("O"));
        var service = new RuntimeOwnershipService(runtime);

        var inventory = await service.BuildInventoryAsync(new RuntimeOwnershipQuery
        {
            OwnerKind = "smoke",
            ComposePath = "/var/folders/demo/workspace/compose.yaml",
        });

        Assert.Equal(3, inventory.Resources.Count);
    }

    [Fact]
    public async Task BuildInventoryAsync_DistinguishesDifferentWorkspaceRoots()
    {
        var runtime = new FakeContainerRuntime(
            "/var/folders/demo/workspace-a",
            "/var/folders/demo/workspace-a/compose.yaml",
            DateTimeOffset.UtcNow.ToString("O"));
        var service = new RuntimeOwnershipService(runtime);

        var inventory = await service.BuildInventoryAsync(new RuntimeOwnershipQuery
        {
            OwnerKind = "smoke",
            WorkspaceRoot = "/var/folders/demo/workspace-b",
        });

        Assert.Empty(inventory.Resources);
    }

    [Fact]
    public async Task CleanupAsync_UsesComposeProfilesForComposeDown()
    {
        var root = CreateWorkspaceRoot();
        var composePath = Path.Combine(root, "compose.yaml");
        File.WriteAllText(composePath, string.Join('\n',
        [
            "services:",
            "  workspace:",
            "    image: ubuntu:24.04",
            "    depends_on:",
            "      oracle-demo:",
            "        condition: service_healthy",
            "      oracle-ords:",
            "        condition: service_started",
            "  oracle-demo:",
            "    image: gvenzl/oracle-free:23-slim-faststart",
            "    profiles:",
            "      - oracle-demo",
            "  oracle-ords:",
            "    image: container-registry.oracle.com/database/ords:latest",
            "    profiles:",
            "      - oracle-ords",
            "    depends_on:",
            "      - oracle-demo",
            "volumes:",
            "  oracle-demo-data:",
            string.Empty,
        ]));

        var runtime = new CleanupScenarioContainerRuntime(root, composePath, composeDownExitCode: 0);
        var service = new RuntimeOwnershipService(runtime);

        var result = await service.CleanupAsync(new RuntimeCleanupOptions { DryRun = false, OwnerKind = "smoke", RunId = "run-1" });

        Assert.True(result.Succeeded);
        Assert.True(result.ComposeDownAttempted);
        Assert.True(result.ComposeDownSucceeded);
        Assert.True(result.VerificationSucceeded);
        Assert.Contains(runtime.Commands, command => command.Contains("compose --project-name runtime-project --file", StringComparison.Ordinal)
            && command.Contains("--profile oracle-demo", StringComparison.Ordinal)
            && command.Contains("--profile oracle-ords", StringComparison.Ordinal)
            && command.Contains(" down -v --remove-orphans", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CleanupAsync_PreservesComposeDownWarningWhileFallbackRemovalSucceeds()
    {
        var root = CreateWorkspaceRoot();
        var composePath = Path.Combine(root, "compose.yaml");
        File.WriteAllText(composePath, string.Join('\n',
        [
            "services:",
            "  workspace:",
            "    image: ubuntu:24.04",
            "    depends_on:",
            "      oracle-demo:",
            "        condition: service_healthy",
            "  oracle-demo:",
            "    image: gvenzl/oracle-free:23-slim-faststart",
            "    profiles:",
            "      - oracle-demo",
            string.Empty,
        ]));

        var runtime = new CleanupScenarioContainerRuntime(root, composePath, composeDownExitCode: 1, composeDownError: "service \"workspace\" depends on undefined service \"oracle-demo\": invalid compose project");
        var service = new RuntimeOwnershipService(runtime);

        var result = await service.CleanupAsync(new RuntimeCleanupOptions { DryRun = false, OwnerKind = "smoke", RunId = "run-1" });

        Assert.True(result.Succeeded);
        Assert.True(result.ComposeDownAttempted);
        Assert.False(result.ComposeDownSucceeded);
        Assert.True(result.FallbackRemovalRequired);
        Assert.True(result.VerificationSucceeded);
        Assert.Contains(result.Warnings, warning => warning.Contains("compose-down:runtime-project:", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, error => error.Contains("compose-down:runtime-project:", StringComparison.Ordinal));
    }

    private static string CreateWorkspaceRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"runtime-owner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FakeContainerRuntime : IContainerRuntime
    {
        private readonly string _workspaceRoot;
        private readonly string _composePath;
        private readonly string _createdAt;

        public FakeContainerRuntime(string workspaceRoot, string composePath, string createdAt)
        {
            _workspaceRoot = workspaceRoot;
            _composePath = composePath;
            _createdAt = createdAt;
        }

        public string RuntimeId => "docker";
        public string GetWorkspaceContainerName(WorkspaceDefinition definition) => "workspace";
        public string GetServiceContainerName(WorkspaceDefinition definition, string serviceName) => serviceName;
        public IReadOnlyList<string> CreatePermissionRepairArguments(string workspaceRootPath) => [];

        public Task<ProcessResult> RunSimpleDockerCommandAsync(IEnumerable<string> arguments, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        {
            var args = arguments.ToArray();
            if (args.SequenceEqual(new[] { "ps", "-a", "--no-trunc", "--format", "{{.ID}}" }))
            {
                return Task.FromResult(Success("container-1\n"));
            }

            if (args.SequenceEqual(new[] { "network", "ls", "--no-trunc", "-q" }))
            {
                return Task.FromResult(Success("network-1\n"));
            }

            if (args.SequenceEqual(new[] { "volume", "ls", "-q" }))
            {
                return Task.FromResult(Success("volume-1\n"));
            }

            if (args.Length >= 2 && args[0] == "inspect")
            {
                return Task.FromResult(Success(BuildContainerInspectJson()));
            }

            if (args.Length >= 3 && args[0] == "network" && args[1] == "inspect")
            {
                return Task.FromResult(Success(BuildNamedResourceInspectJson("network-1", "runtime-network", "created")));
            }

            if (args.Length >= 3 && args[0] == "volume" && args[1] == "inspect")
            {
                return Task.FromResult(Success(BuildNamedResourceInspectJson("volume-1", "runtime-volume", "created")));
            }

            return Task.FromResult(Success(string.Empty));
        }

        private string BuildContainerInspectJson()
            => JsonSerializer.Serialize(new object[]
            {
                new
                {
                    Id = "container-1",
                    Name = "/runtime-container",
                    State = new { Status = "running" },
                    Config = new { Labels = BuildLabels() },
                },
            });

        private string BuildNamedResourceInspectJson(string id, string name, string status)
            => JsonSerializer.Serialize(new object[]
            {
                new
                {
                    Id = id,
                    Name = name,
                    Labels = BuildLabels(),
                },
            });

        private Dictionary<string, string> BuildLabels()
            => new(StringComparer.Ordinal)
            {
                [RuntimeOwnershipLabels.Owner] = "smoke",
                [RuntimeOwnershipLabels.RunId] = "run-1",
                [RuntimeOwnershipLabels.Template] = "oracle-apexlang-demo",
                [RuntimeOwnershipLabels.CreatedBy] = RuntimeOwnershipLabels.CreatedByValue,
                [RuntimeOwnershipLabels.Project] = "runtime-project",
                [RuntimeOwnershipLabels.WorkspaceRoot] = _workspaceRoot,
                [RuntimeOwnershipLabels.ComposePath] = _composePath,
                [RuntimeOwnershipLabels.CreatedAt] = _createdAt,
            };

        private static ProcessResult Success(string output) => new()
        {
            Command = "docker",
            ExitCode = 0,
            StandardOutput = output,
            StandardError = string.Empty,
            StandardOutputLines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            StandardErrorLines = Array.Empty<string>(),
            Duration = TimeSpan.Zero,
        };

        public Task<ProcessResult> StartAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotSupportedException();
        public Task<ProcessResult> ValidateAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotSupportedException();
        public Task<ProcessResult> StopAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RemoveAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotSupportedException();
        public Task<ProcessResult> ResetAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotSupportedException();
        public Task<ProcessResult?> ValidateVolatileEnvironmentAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetPsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetComposePsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetServiceLogsAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RestartServiceAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RunProvisionScriptAsync(WorkspaceDefinition definition, WorkspacePaths paths, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RepairOracleOrdsGatewayAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> ProbeHttpGetFromWorkspaceAsync(WorkspaceDefinition definition, string url, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> InspectContainerImageAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> InspectImageRepoTagsAsync(string imageId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetNodeToolDiagnosticsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetNodeAptPolicyAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetOsReleaseAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> CheckOpencodeUserAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> EnsureOpencodeUserDirectoriesAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> NormalizeWorkspaceFilePermissionsAsync(string workspaceRootPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RunCommandInServiceContainerAsync(WorkspaceDefinition definition, string serviceName, IEnumerable<string> commandArguments, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> ListOpenCodeSessionsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> ExportOpenCodeSessionAsync(WorkspaceDefinition definition, string sessionId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CleanupScenarioContainerRuntime : IContainerRuntime
    {
        private readonly string _workspaceRoot;
        private readonly string _composePath;
        private readonly string _createdAt;
        private readonly int _composeDownExitCode;
        private readonly string _composeDownError;
        private readonly Dictionary<RuntimeResourceType, HashSet<string>> _resources;

        public CleanupScenarioContainerRuntime(string workspaceRoot, string composePath, int composeDownExitCode, string composeDownError = "")
        {
            _workspaceRoot = workspaceRoot;
            _composePath = composePath;
            _composeDownExitCode = composeDownExitCode;
            _composeDownError = composeDownError;
            _createdAt = DateTimeOffset.UtcNow.ToString("O");
            _resources = new Dictionary<RuntimeResourceType, HashSet<string>>
            {
                [RuntimeResourceType.Container] = ["runtime-container"],
                [RuntimeResourceType.Network] = ["runtime-network"],
                [RuntimeResourceType.Volume] = ["runtime-volume"],
            };
        }

        public List<string> Commands { get; } = new();

        public string RuntimeId => "docker";
        public string GetWorkspaceContainerName(WorkspaceDefinition definition) => "workspace";
        public string GetServiceContainerName(WorkspaceDefinition definition, string serviceName) => serviceName;
        public IReadOnlyList<string> CreatePermissionRepairArguments(string workspaceRootPath) => [];

        public Task<ProcessResult> RunSimpleDockerCommandAsync(IEnumerable<string> arguments, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        {
            var args = arguments.ToArray();
            Commands.Add(string.Join(' ', new[] { "docker" }.Concat(args)));

            if (args.SequenceEqual(new[] { "ps", "-a", "--no-trunc", "--format", "{{.ID}}" }))
            {
                return Task.FromResult(Success(string.Join('\n', _resources[RuntimeResourceType.Container].Select((_, index) => $"container-{index + 1}")) + (_resources[RuntimeResourceType.Container].Count > 0 ? "\n" : string.Empty)));
            }

            if (args.SequenceEqual(new[] { "network", "ls", "--no-trunc", "-q" }))
            {
                return Task.FromResult(Success(string.Join('\n', _resources[RuntimeResourceType.Network].Select((_, index) => $"network-{index + 1}")) + (_resources[RuntimeResourceType.Network].Count > 0 ? "\n" : string.Empty)));
            }

            if (args.SequenceEqual(new[] { "volume", "ls", "-q" }))
            {
                return Task.FromResult(Success(string.Join('\n', _resources[RuntimeResourceType.Volume].Select((_, index) => $"volume-{index + 1}")) + (_resources[RuntimeResourceType.Volume].Count > 0 ? "\n" : string.Empty)));
            }

            if (args.Length >= 2 && args[0] == "inspect")
            {
                return Task.FromResult(Success(BuildContainerInspectJson()));
            }

            if (args.Length >= 3 && args[0] == "network" && args[1] == "inspect")
            {
                return Task.FromResult(Success(BuildNamedResourceInspectJson(RuntimeResourceType.Network)));
            }

            if (args.Length >= 3 && args[0] == "volume" && args[1] == "inspect")
            {
                return Task.FromResult(Success(BuildNamedResourceInspectJson(RuntimeResourceType.Volume)));
            }

            if (args.Length >= 6 && args[0] == "compose" && args.Contains("down", StringComparer.Ordinal))
            {
                if (_composeDownExitCode == 0)
                {
                    _resources[RuntimeResourceType.Container].Clear();
                    _resources[RuntimeResourceType.Network].Clear();
                    _resources[RuntimeResourceType.Volume].Clear();
                    return Task.FromResult(Success(string.Empty));
                }

                return Task.FromResult(Failure(_composeDownError));
            }

            if (args.Length >= 3 && args[0] == "rm" && args[1] == "-f")
            {
                _resources[RuntimeResourceType.Container].Remove(args[2]);
                return Task.FromResult(Success(string.Empty));
            }

            if (args.Length >= 3 && args[0] == "network" && args[1] == "rm")
            {
                _resources[RuntimeResourceType.Network].Remove(args[2]);
                return Task.FromResult(Success(string.Empty));
            }

            if (args.Length >= 3 && args[0] == "volume" && args[1] == "rm")
            {
                _resources[RuntimeResourceType.Volume].Remove(args[2]);
                return Task.FromResult(Success(string.Empty));
            }

            return Task.FromResult(Success(string.Empty));
        }

        private string BuildContainerInspectJson()
            => JsonSerializer.Serialize(_resources[RuntimeResourceType.Container].Select((name, index) => new
            {
                Id = $"container-{index + 1}",
                Name = "/" + name,
                State = new { Status = "running" },
                Config = new { Labels = BuildLabels() },
            }).ToArray());

        private string BuildNamedResourceInspectJson(RuntimeResourceType type)
            => JsonSerializer.Serialize(_resources[type].Select((name, index) => new
            {
                Id = $"{type.ToString().ToLowerInvariant()}-{index + 1}",
                Name = name,
                Labels = BuildLabels(),
            }).ToArray());

        private Dictionary<string, string> BuildLabels()
            => new(StringComparer.Ordinal)
            {
                [RuntimeOwnershipLabels.Owner] = "smoke",
                [RuntimeOwnershipLabels.RunId] = "run-1",
                [RuntimeOwnershipLabels.Template] = "oracle-apexlang-demo",
                [RuntimeOwnershipLabels.CreatedBy] = RuntimeOwnershipLabels.CreatedByValue,
                [RuntimeOwnershipLabels.Project] = "runtime-project",
                [RuntimeOwnershipLabels.WorkspaceRoot] = _workspaceRoot,
                [RuntimeOwnershipLabels.ComposePath] = _composePath,
                [RuntimeOwnershipLabels.CreatedAt] = _createdAt,
            };

        private static ProcessResult Success(string output) => new()
        {
            Command = "docker",
            ExitCode = 0,
            StandardOutput = output,
            StandardError = string.Empty,
            StandardOutputLines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            StandardErrorLines = Array.Empty<string>(),
            Duration = TimeSpan.Zero,
        };

        private static ProcessResult Failure(string error) => new()
        {
            Command = "docker",
            ExitCode = 1,
            StandardOutput = string.Empty,
            StandardError = error,
            StandardOutputLines = Array.Empty<string>(),
            StandardErrorLines = error.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            Duration = TimeSpan.Zero,
        };

        public Task<ProcessResult> StartAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotSupportedException();
        public Task<ProcessResult> ValidateAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotSupportedException();
        public Task<ProcessResult> StopAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RemoveAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotSupportedException();
        public Task<ProcessResult> ResetAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotSupportedException();
        public Task<ProcessResult?> ValidateVolatileEnvironmentAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetPsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetComposePsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetServiceLogsAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RestartServiceAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RunProvisionScriptAsync(WorkspaceDefinition definition, WorkspacePaths paths, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RepairOracleOrdsGatewayAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> ProbeHttpGetFromWorkspaceAsync(WorkspaceDefinition definition, string url, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> InspectContainerImageAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> InspectImageRepoTagsAsync(string imageId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetNodeToolDiagnosticsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetNodeAptPolicyAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetOsReleaseAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> CheckOpencodeUserAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> EnsureOpencodeUserDirectoriesAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> NormalizeWorkspaceFilePermissionsAsync(string workspaceRootPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RunCommandInServiceContainerAsync(WorkspaceDefinition definition, string serviceName, IEnumerable<string> commandArguments, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> ListOpenCodeSessionsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> ExportOpenCodeSessionAsync(WorkspaceDefinition definition, string sessionId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
