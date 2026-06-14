using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class PortablePipelineIntegrationTests
{
    [Fact]
    public void EnvironmentFileGenerator_WritesGeneratedHeader()
    {
        var generator = new EnvironmentFileGenerator();
        var content = generator.Generate(new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "portable" },
        });

        Assert.Contains("GENERATED FILE", content);
        Assert.Contains("WORKSPACE_SLUG=portable", content);
    }

    [Fact]
    public void ArchiveImportStateStore_PersistsAndUpdatesImportRecords()
    {
        var store = new ArchiveImportStateStore();
        var filePath = Path.Combine(Path.GetTempPath(), $"archive-state-{Guid.NewGuid():N}.yaml");

        try
        {
            store.MarkImported(filePath, "sample.zip", "abc123", "workspace/sample", DateTimeOffset.Parse("2026-06-12T08:00:00Z"));
            store.MarkImported(filePath, "sample.zip", "def456", "workspace/sample", DateTimeOffset.Parse("2026-06-12T09:00:00Z"));

            var state = store.Load(filePath);
            var entry = Assert.Single(state.Items);
            Assert.Equal("sample.zip", entry.ArchiveName);
            Assert.Equal("def456", entry.Checksum);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void WorkspaceRepository_PersistsAndOrdersMostRecentlyOpenedFirst()
    {
        var root = Path.Combine(Path.GetTempPath(), $"workspace-repo-{Guid.NewGuid():N}");

        try
        {
            var repository = new WorkspaceRepository(root);
            repository.Save(new WorkspaceRecord
            {
                Name = "older",
                RootPath = "c:/older",
                CreatedUtc = DateTimeOffset.Parse("2026-06-12T08:00:00Z"),
                LastOpenedUtc = DateTimeOffset.Parse("2026-06-12T08:05:00Z"),
            });
            repository.Save(new WorkspaceRecord
            {
                Name = "newer",
                RootPath = "c:/newer",
                CreatedUtc = DateTimeOffset.Parse("2026-06-12T08:10:00Z"),
                LastOpenedUtc = DateTimeOffset.Parse("2026-06-12T08:15:00Z"),
            });

            var items = repository.LoadAll();

            Assert.Equal("newer", items[0].Name);
            Assert.Equal("older", items[1].Name);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                TestFileSystem.DeleteDirectoryIfExists(root);
            }
        }
    }

    [Fact]
    public void GeneratedArtifacts_ContainSourceOfTruthHeaders()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = Path.Combine(Path.GetTempPath(), $"portable-headers-{Guid.NewGuid():N}");

        try
        {
            var orchestrator = new WorkspaceOrchestrator(
                new WorkspaceYamlService(),
                new WorkspaceRepository(Path.Combine(tempRoot, ".appdata")),
                new WorkspaceResolver(
                    [new FeatureManifest { Id = "core", AlwaysEnabled = true, Dependencies = new DependencySet() }],
                    []),
                new ComposeGenerator(),
                new EnvironmentFileGenerator(),
                new ProvisioningScriptGenerator(),
                new TerminalArtifactsGenerator(),
                new AttachArtifactsGenerator(),
                new WorkspaceContentGenerator(),
                new WorkspaceAppliedStateService(),
                new WorkspaceCheckpointService(),
                new WorkspaceTimelineService(),
                new WorkspaceSafetyService(),
                new WorkspaceIgnorePolicyService(),
                new GitWorkspaceProvider(new OpenCode.Workspace.Core.Runtime.ProcessRunner(), new WorkspaceIgnorePolicyService()),
                new OpenCode.Workspace.Core.Runtime.DockerService(new OpenCode.Workspace.Core.Runtime.ProcessRunner()),
                new NoOpTerminalLauncher());

            var snapshot = orchestrator.CreateWorkspace(tempRoot, new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "portable" },
                Features = ["core"],
            });

            var compose = File.ReadAllText(snapshot.Paths.ComposePath);
            var environmentFile = File.ReadAllText(snapshot.Paths.EnvironmentFilePath);
            var script = File.ReadAllText(snapshot.Paths.ProvisionScriptPath);

            Assert.Contains("Source inputs: workspace.yaml and catalog manifests", compose);
            Assert.Contains("Source inputs: workspace.yaml and catalog manifests", environmentFile);
            Assert.Contains("Source inputs: workspace.yaml and catalog manifests", script);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                TestFileSystem.DeleteDirectoryIfExists(tempRoot);
            }
        }
    }

    private sealed class NoOpTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static bool CanRunGit()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            process?.WaitForExit(5000);
            return process is not null && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
