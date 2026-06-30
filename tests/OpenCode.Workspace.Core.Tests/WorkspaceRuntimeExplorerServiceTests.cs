using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceRuntimeExplorerServiceTests
{
    [Fact]
    public async Task BuildAsync_MapsWorkspaceOwnedResources()
    {
        var appDataRoot = CreateTempRoot();
        var workspaceRoot = CreateTempRoot();

        try
        {
            var repository = new WorkspaceRepository(appDataRoot);
            var runtimeStateService = new WorkspaceRuntimeStateService();
            var yamlService = new WorkspaceYamlService();
            var timelineService = new WorkspaceTimelineService();
            var paths = WorkspacePathBuilder.Build(workspaceRoot);
            repository.Save(new WorkspaceRecord { Name = "ERP Demo", RootPath = workspaceRoot, RepositoryPath = workspaceRoot, CreatedUtc = DateTimeOffset.UtcNow, LastOpenedUtc = DateTimeOffset.UtcNow });
            yamlService.WriteToFile(paths.WorkspaceYamlPath, new WorkspaceDefinition { Workspace = new WorkspaceMetadata { Name = "ERP Demo", Image = "ubuntu:24.04" }, Features = ["core"], Services = ["postgres", "pgadmin"] });
            runtimeStateService.Write(paths.RuntimeStatePath, new WorkspaceRuntimeStateRecord
            {
                ResolvedEngine = "docker",
                ResolvedPlatform = "linux/amd64",
                CompatibilityMode = "Native",
                Resources = new WorkspaceManagedRuntimeResources
                {
                    Identity = new WorkspaceRuntimeIdentity { WorkspaceName = "ERP Demo", WorkspaceSlug = "erp-demo", WorkspaceId = "erp-demo" },
                    Ports =
                    [
                        new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.PostgresResourceId, ServiceId = "postgres", DisplayName = "PostgreSQL", Protocol = "tcp", PreferredPort = 15432, AllocatedPort = 15433, ContainerPort = 5432, AllocationKind = "Alternative", Automatic = true, Endpoint = "tcp://localhost:15433", OpenUrl = "tcp://localhost:15433" },
                        new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.PgAdminResourceId, ServiceId = "pgadmin", DisplayName = "pgAdmin", Protocol = "http", PreferredPort = 18080, AllocatedPort = 18081, ContainerPort = 80, AllocationKind = "Alternative", Automatic = true, Endpoint = "http://localhost:18081/", OpenUrl = "http://localhost:18081/" },
                    ],
                    ServiceEndpoints =
                    [
                        new WorkspaceServiceEndpointRecord { ServiceId = "pgadmin", DisplayName = "pgAdmin", Endpoint = "http://localhost:18081/", OpenUrl = "http://localhost:18081/" },
                    ],
                    RuntimeIdentifiers =
                    [
                        new WorkspaceRuntimeIdentifierRecord { ResourceType = "container", ResourceId = "workspace", DisplayName = "Workspace container", Value = "erp-demo-workspace" },
                        new WorkspaceRuntimeIdentifierRecord { ResourceType = "container", ResourceId = "postgres", DisplayName = "postgres container", Value = "erp-demo-postgres-1" },
                        new WorkspaceRuntimeIdentifierRecord { ResourceType = "volume", ResourceId = "postgres-data", DisplayName = "PostgreSQL data volume", Value = "erp-demo_postgres-data" },
                        new WorkspaceRuntimeIdentifierRecord { ResourceType = "network", ResourceId = "default", DisplayName = "Default network", Value = "erp-demo_default" },
                    ],
                    Conflicts = [new WorkspaceResourceConflictRecord { ResourceId = WorkspaceRuntimeResourceCatalog.PostgresResourceId, DisplayName = "PostgreSQL", PreferredPort = 15432, ConflictKind = "ExternalProcess", Owner = "Unknown external process", Recommendation = "Allocate another port.", Resolution = "Allocated alternative port 15433." }],
                },
            });

            var service = new WorkspaceRuntimeExplorerService(repository, runtimeStateService, yamlService, timelineService, new FakeProcessRunner(
                ProcessResultFor("docker ps", "erp-demo-postgres-1\tUp 5 minutes\nerp-demo-workspace\tUp 5 minutes"),
                ProcessResultFor("docker volume ls", "erp-demo_postgres-data"),
                ProcessResultFor("docker network ls", "erp-demo_default")));

            var report = await service.BuildAsync();

            Assert.Contains(report.Workspaces, item => item.WorkspaceName == "ERP Demo" && item.Ports.Contains("15433"));
            Assert.Contains(report.Resources, item => item.ResourceType == "Port" && item.CurrentPort == 15433);
            Assert.Contains(report.Conflicts, item => item.ConflictType == "ExternalProcess");
        }
        finally
        {
            TestFileSystem.DeleteDirectoryIfExists(appDataRoot);
            TestFileSystem.DeleteDirectoryIfExists(workspaceRoot);
        }
    }

    [Fact]
    public async Task BuildAsync_DetectsDuplicatePortAllocationAndOrphanedResources()
    {
        var appDataRoot = CreateTempRoot();
        var firstRoot = CreateTempRoot();
        var secondRoot = CreateTempRoot();

        try
        {
            var repository = new WorkspaceRepository(appDataRoot);
            var runtimeStateService = new WorkspaceRuntimeStateService();
            var yamlService = new WorkspaceYamlService();
            var timelineService = new WorkspaceTimelineService();
            repository.Save(new WorkspaceRecord { Name = "Alpha", RootPath = firstRoot, RepositoryPath = firstRoot, CreatedUtc = DateTimeOffset.UtcNow, LastOpenedUtc = DateTimeOffset.UtcNow });
            repository.Save(new WorkspaceRecord { Name = "Beta", RootPath = secondRoot, RepositoryPath = secondRoot, CreatedUtc = DateTimeOffset.UtcNow, LastOpenedUtc = DateTimeOffset.UtcNow });

            foreach (var root in new[] { firstRoot, secondRoot })
            {
                var paths = WorkspacePathBuilder.Build(root);
                yamlService.WriteToFile(paths.WorkspaceYamlPath, new WorkspaceDefinition { Workspace = new WorkspaceMetadata { Name = Path.GetFileName(root), Image = "ubuntu:24.04" }, Features = ["core"], Services = ["postgres"] });
                runtimeStateService.Write(paths.RuntimeStatePath, new WorkspaceRuntimeStateRecord
                {
                    Resources = new WorkspaceManagedRuntimeResources
                    {
                        Identity = new WorkspaceRuntimeIdentity { WorkspaceName = Path.GetFileName(root), WorkspaceSlug = WorkspacePathBuilder.Slugify(Path.GetFileName(root)), WorkspaceId = WorkspacePathBuilder.Slugify(Path.GetFileName(root)) },
                        Ports = [new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.PostgresResourceId, ServiceId = "postgres", DisplayName = "PostgreSQL", Protocol = "tcp", PreferredPort = 15432, AllocatedPort = 15433, ContainerPort = 5432, Endpoint = "tcp://localhost:15433", OpenUrl = "tcp://localhost:15433" }],
                        RuntimeIdentifiers = [new WorkspaceRuntimeIdentifierRecord { ResourceType = "container", ResourceId = "workspace", DisplayName = "Workspace container", Value = $"{WorkspacePathBuilder.Slugify(Path.GetFileName(root))}-workspace" }],
                    },
                });
            }

            var service = new WorkspaceRuntimeExplorerService(repository, runtimeStateService, yamlService, timelineService, new FakeProcessRunner(
                ProcessResultFor("docker ps", "orphan-postgres-1\tExited (0) 1 hour ago"),
                ProcessResultFor("docker volume ls", "orphan_postgres-data"),
                ProcessResultFor("docker network ls", "orphan_default")));

            var report = await service.BuildAsync();

            Assert.Contains(report.Conflicts, item => item.ConflictType == "DuplicateAllocation");
            Assert.Contains(report.OrphanedResources, item => item.ResourceType == "Volume" && item.RuntimeIdentifier == "orphan_postgres-data");
            Assert.Contains(report.OrphanedResources, item => item.ResourceType == "Network" && item.RuntimeIdentifier == "orphan_default");
        }
        finally
        {
            TestFileSystem.DeleteDirectoryIfExists(appDataRoot);
            TestFileSystem.DeleteDirectoryIfExists(firstRoot);
            TestFileSystem.DeleteDirectoryIfExists(secondRoot);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"runtime-explorer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static ProcessResult ProcessResultFor(string command, string standardOutput)
        => new()
        {
            Command = command,
            ExitCode = 0,
            StandardOutput = standardOutput,
            StandardError = string.Empty,
            StandardOutputLines = standardOutput.Split(Environment.NewLine),
            StandardErrorLines = Array.Empty<string>(),
            Duration = TimeSpan.FromMilliseconds(10),
        };

    private sealed class FakeProcessRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new(results);

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
            => Task.FromResult(_results.Count > 0 ? _results.Dequeue() : ProcessResultFor(string.Join(' ', new[] { fileName }.Concat(arguments)), string.Empty));
    }
}
