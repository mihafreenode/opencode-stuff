using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceRuntimeResourceManagerTests
{
    [Fact]
    public void ResolveState_WhenPreferredPortIsFree_UsesPreferredPort()
    {
        var tempRoot = CreateTempRoot();
        var appDataRoot = CreateTempRoot();

        try
        {
            var repository = new WorkspaceRepository(appDataRoot);
            var service = new WorkspaceRuntimeStateService();
            var manager = new WorkspaceRuntimeResourceManager(repository, service, port => port == 15432 || port == 18080);

            var state = manager.ResolveState(CreatePostgresDefinition(), WorkspacePathBuilder.Build(tempRoot), inspectHostAvailability: true);

            Assert.Equal(15432, WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(CreatePostgresDefinition(), state, WorkspaceRuntimeResourceCatalog.PostgresResourceId));
            Assert.Equal(18080, WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(CreatePostgresDefinition(), state, WorkspaceRuntimeResourceCatalog.PgAdminResourceId));
            Assert.Empty(state.Resources.Conflicts);
        }
        finally
        {
            TestFileSystem.DeleteDirectoryIfExists(tempRoot);
            TestFileSystem.DeleteDirectoryIfExists(appDataRoot);
        }
    }

    [Fact]
    public void ResolveState_WhenPreferredPortIsOwnedByManagedWorkspace_UsesAlternativePortAndRecordsOwner()
    {
        var tempRoot = CreateTempRoot();
        var otherRoot = CreateTempRoot();
        var appDataRoot = CreateTempRoot();

        try
        {
            var repository = new WorkspaceRepository(appDataRoot);
            var service = new WorkspaceRuntimeStateService();
            repository.Save(new WorkspaceRecord { Name = "analytics-demo", RootPath = otherRoot, RepositoryPath = otherRoot, CreatedUtc = DateTimeOffset.UtcNow, LastOpenedUtc = DateTimeOffset.UtcNow });
            service.Write(WorkspacePathBuilder.Build(otherRoot).RuntimeStatePath, new WorkspaceRuntimeStateRecord
            {
                Resources = new WorkspaceManagedRuntimeResources
                {
                    Ports =
                    [
                        new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.PostgresResourceId, ServiceId = "postgres", DisplayName = "PostgreSQL", Protocol = "tcp", PreferredPort = 15432, AllocatedPort = 15432, ContainerPort = 5432, Endpoint = "tcp://localhost:15432", OpenUrl = "tcp://localhost:15432" },
                    ],
                },
            });

            var manager = new WorkspaceRuntimeResourceManager(repository, service, port => port != 15432);
            var state = manager.ResolveState(CreatePostgresDefinition(), WorkspacePathBuilder.Build(tempRoot), inspectHostAvailability: true);

            Assert.Equal(15433, WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(CreatePostgresDefinition(), state, WorkspaceRuntimeResourceCatalog.PostgresResourceId));
            var conflict = Assert.Single(state.Resources.Conflicts);
            Assert.Equal("ManagedWorkspace", conflict.ConflictKind);
            Assert.Contains("analytics-demo", conflict.Owner, StringComparison.Ordinal);
        }
        finally
        {
            TestFileSystem.DeleteDirectoryIfExists(tempRoot);
            TestFileSystem.DeleteDirectoryIfExists(otherRoot);
            TestFileSystem.DeleteDirectoryIfExists(appDataRoot);
        }
    }

    [Fact]
    public void ResolveState_WhenPreferredPortIsOwnedByManagedWorkspaceWithoutHostInspection_StillUsesAlternativePort()
    {
        var tempRoot = CreateTempRoot();
        var otherRoot = CreateTempRoot();
        var appDataRoot = CreateTempRoot();

        try
        {
            var repository = new WorkspaceRepository(appDataRoot);
            var service = new WorkspaceRuntimeStateService();
            repository.Save(new WorkspaceRecord { Name = "analytics-demo", RootPath = otherRoot, RepositoryPath = otherRoot, CreatedUtc = DateTimeOffset.UtcNow, LastOpenedUtc = DateTimeOffset.UtcNow });
            service.Write(WorkspacePathBuilder.Build(otherRoot).RuntimeStatePath, new WorkspaceRuntimeStateRecord
            {
                Resources = new WorkspaceManagedRuntimeResources
                {
                    Ports =
                    [
                        new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.PostgresResourceId, ServiceId = "postgres", DisplayName = "PostgreSQL", Protocol = "tcp", PreferredPort = 15432, AllocatedPort = 15432, ContainerPort = 5432, Endpoint = "tcp://localhost:15432", OpenUrl = "tcp://localhost:15432" },
                    ],
                },
            });

            var manager = new WorkspaceRuntimeResourceManager(repository, service, port => false);
            var state = manager.ResolveState(CreatePostgresDefinition(), WorkspacePathBuilder.Build(tempRoot), inspectHostAvailability: false);

            Assert.Equal(15433, WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(CreatePostgresDefinition(), state, WorkspaceRuntimeResourceCatalog.PostgresResourceId));
            var conflict = Assert.Single(state.Resources.Conflicts);
            Assert.Equal("ManagedWorkspace", conflict.ConflictKind);
            Assert.Contains("analytics-demo", conflict.Owner, StringComparison.Ordinal);
        }
        finally
        {
            TestFileSystem.DeleteDirectoryIfExists(tempRoot);
            TestFileSystem.DeleteDirectoryIfExists(otherRoot);
            TestFileSystem.DeleteDirectoryIfExists(appDataRoot);
        }
    }

    [Fact]
    public void ResolveState_WhenExistingOracleAllocationIsStillValid_PreservesAllocatedPort()
    {
        var tempRoot = CreateTempRoot();
        var appDataRoot = CreateTempRoot();

        try
        {
            var manager = new WorkspaceRuntimeResourceManager(new WorkspaceRepository(appDataRoot), new WorkspaceRuntimeStateService(), port => port == 1522 || port == 8182);
            var existingState = new WorkspaceRuntimeStateRecord
            {
                Resources = new WorkspaceManagedRuntimeResources
                {
                    Ports =
                    [
                        new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.OracleDatabaseResourceId, ServiceId = "oracle-database", DisplayName = "Oracle Database", Protocol = "tcp", PreferredPort = 1521, AllocatedPort = 1522, ContainerPort = 1521, AllocationKind = "Alternative", Automatic = true, Endpoint = "tcp://localhost:1522", OpenUrl = "tcp://localhost:1522" },
                        new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.OracleOrdsResourceId, ServiceId = "ords", DisplayName = "ORDS", Protocol = "http", PreferredPort = 8181, AllocatedPort = 8182, ContainerPort = 8080, AllocationKind = "Alternative", Automatic = true, Endpoint = "http://localhost:8182/", OpenUrl = "http://localhost:8182/ords/_/landing" },
                    ],
                },
            };

            var state = manager.ResolveState(CreateOracleDefinition(), WorkspacePathBuilder.Build(tempRoot), existingState, inspectHostAvailability: true);

            Assert.Equal(1522, WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(CreateOracleDefinition(), state, WorkspaceRuntimeResourceCatalog.OracleDatabaseResourceId));
            Assert.Equal(8182, WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(CreateOracleDefinition(), state, WorkspaceRuntimeResourceCatalog.OracleOrdsResourceId));
        }
        finally
        {
            TestFileSystem.DeleteDirectoryIfExists(tempRoot);
            TestFileSystem.DeleteDirectoryIfExists(appDataRoot);
        }
    }

    [Fact]
    public void ResolveState_WhenOracleRuntimeStateCollidesWithManagedWorkspace_ReallocatesOracleWithoutDriftingOrds()
    {
        var tempRoot = CreateTempRoot();
        var otherRoot = CreateTempRoot();
        var appDataRoot = CreateTempRoot();

        try
        {
            var repository = new WorkspaceRepository(appDataRoot);
            var service = new WorkspaceRuntimeStateService();
            repository.Save(new WorkspaceRecord { Name = "demo-apexlang", RootPath = OperatingSystem.IsWindows() ? otherRoot : "C:\\Users\\miha.pirnat\\source\\opencode-workspaces\\demo-apexlang2", RepositoryPath = otherRoot, CreatedUtc = DateTimeOffset.UtcNow, LastOpenedUtc = DateTimeOffset.UtcNow });
            service.Write(WorkspacePathBuilder.Build(otherRoot).RuntimeStatePath, new WorkspaceRuntimeStateRecord
            {
                Resources = new WorkspaceManagedRuntimeResources
                {
                    Ports =
                    [
                        new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.OracleDatabaseResourceId, ServiceId = "oracle-database", DisplayName = "Oracle Database", Protocol = "tcp", PreferredPort = 1521, AllocatedPort = 1521, ContainerPort = 1521, Endpoint = "tcp://localhost:1521", OpenUrl = "tcp://localhost:1521" },
                        new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.OracleOrdsResourceId, ServiceId = "ords", DisplayName = "ORDS", Protocol = "http", PreferredPort = 8181, AllocatedPort = 8181, ContainerPort = 8080, Endpoint = "http://localhost:8181/", OpenUrl = "http://localhost:8181/ords/_/landing" },
                    ],
                },
            });

            var existingState = new WorkspaceRuntimeStateRecord
            {
                Resources = new WorkspaceManagedRuntimeResources
                {
                    Ports =
                    [
                        new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.OracleDatabaseResourceId, ServiceId = "oracle-database", DisplayName = "Oracle Database", Protocol = "tcp", PreferredPort = 1521, AllocatedPort = 1521, ContainerPort = 1521, AllocationKind = "Preferred", Automatic = false, Endpoint = "tcp://localhost:1521", OpenUrl = "tcp://localhost:1521" },
                        new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.OracleOrdsResourceId, ServiceId = "ords", DisplayName = "ORDS", Protocol = "http", PreferredPort = 8181, AllocatedPort = 8182, ContainerPort = 8080, AllocationKind = "Alternative", Automatic = true, Endpoint = "http://localhost:8182/", OpenUrl = "http://localhost:8182/ords/_/landing" },
                    ],
                },
            };

            var manager = new WorkspaceRuntimeResourceManager(repository, service, port => port == 1522 || port == 8182);
            var state = manager.ResolveState(CreateOracleDefinition(), WorkspacePathBuilder.Build(tempRoot), existingState, inspectHostAvailability: true);

            Assert.Equal(1522, WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(CreateOracleDefinition(), state, WorkspaceRuntimeResourceCatalog.OracleDatabaseResourceId));
            Assert.Equal(8182, WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(CreateOracleDefinition(), state, WorkspaceRuntimeResourceCatalog.OracleOrdsResourceId));
        }
        finally
        {
            TestFileSystem.DeleteDirectoryIfExists(tempRoot);
            TestFileSystem.DeleteDirectoryIfExists(otherRoot);
            TestFileSystem.DeleteDirectoryIfExists(appDataRoot);
        }
    }

    [Fact]
    public void ResolveState_WhenPreferredAndFallbackPortsAreBusy_UsesDynamicPort()
    {
        var tempRoot = CreateTempRoot();
        var appDataRoot = CreateTempRoot();

        try
        {
            var manager = new WorkspaceRuntimeResourceManager(
                new WorkspaceRepository(appDataRoot),
                new WorkspaceRuntimeStateService(),
                port => port >= 15435);

            var state = manager.ResolveState(CreatePostgresDefinition(), WorkspacePathBuilder.Build(tempRoot), inspectHostAvailability: true);

            Assert.Equal(15435, WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(CreatePostgresDefinition(), state, WorkspaceRuntimeResourceCatalog.PostgresResourceId));
            Assert.Contains(state.Resources.Conflicts, item => item.Resolution.Contains("dynamic port 15435", StringComparison.Ordinal));
        }
        finally
        {
            TestFileSystem.DeleteDirectoryIfExists(tempRoot);
            TestFileSystem.DeleteDirectoryIfExists(appDataRoot);
        }
    }

    [Fact]
    public void RuntimeStateService_RoundTripsManagedResources()
    {
        var tempRoot = CreateTempRoot();
        var runtimeStatePath = WorkspacePathBuilder.Build(tempRoot).RuntimeStatePath;
        var service = new WorkspaceRuntimeStateService();
        var state = new WorkspaceRuntimeStateRecord
        {
            ResolvedEngine = "docker",
            ResolvedPlatform = "linux/amd64",
            CompatibilityMode = "Native",
            Resources = new WorkspaceManagedRuntimeResources
            {
                Ports =
                [
                    new WorkspacePortAllocationRecord { ResourceId = WorkspaceRuntimeResourceCatalog.PostgresResourceId, ServiceId = "postgres", DisplayName = "PostgreSQL", Protocol = "tcp", PreferredPort = 15432, AllocatedPort = 15433, ContainerPort = 5432, AllocationKind = "Alternative", Automatic = true, Endpoint = "tcp://localhost:15433", OpenUrl = "tcp://localhost:15433" },
                ],
                Conflicts =
                [
                    new WorkspaceResourceConflictRecord { ResourceId = WorkspaceRuntimeResourceCatalog.PostgresResourceId, DisplayName = "PostgreSQL", PreferredPort = 15432, ConflictKind = "ExternalProcess", Owner = "Unknown external process", Resolution = "Allocated alternative port 15433." },
                ],
            },
        };

        try
        {
            service.Write(runtimeStatePath, state);
            var loaded = service.Read(runtimeStatePath);

            Assert.NotNull(loaded);
            Assert.Equal(15433, loaded.Resources.Ports.Single().AllocatedPort);
            Assert.Equal("ExternalProcess", loaded.Resources.Conflicts.Single().ConflictKind);
        }
        finally
        {
            TestFileSystem.DeleteDirectoryIfExists(tempRoot);
        }
    }

    private static WorkspaceDefinition CreatePostgresDefinition()
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = "postgres-demo", Image = "ubuntu:24.04" },
            Features = ["core"],
            Services = ["postgres", "pgadmin"],
        };

    private static WorkspaceDefinition CreateOracleDefinition()
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-demo", Image = "ubuntu:24.04" },
            Features = ["core", "oracle-demo", "oracle-apex-demo", "oracle-apexlang-demo"],
            Services = ["oracle-demo", "oracle-ords"],
        };

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"runtime-resources-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
