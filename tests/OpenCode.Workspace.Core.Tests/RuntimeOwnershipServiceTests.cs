using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using System.Text.Json;

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
}
