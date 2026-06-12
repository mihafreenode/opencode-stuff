using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceOrchestratorTests
{
    [Fact]
    public void CreateWorkspace_WritesCanonicalAndGeneratedFiles()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core", "document-processing"));

            Assert.True(File.Exists(snapshot.Paths.WorkspaceYamlPath));
            Assert.True(File.Exists(snapshot.Paths.ComposePath));
            Assert.True(File.Exists(snapshot.Paths.EnvironmentFilePath));
            Assert.True(File.Exists(snapshot.Paths.ProvisionScriptPath));
            Assert.True(File.Exists(snapshot.Paths.StarshipConfigPath));
            Assert.True(File.Exists(snapshot.Paths.ShellInitScriptPath));
            Assert.True(File.Exists(snapshot.Paths.OpencodeWorkspaceShellPath));
            Assert.True(File.Exists(snapshot.Paths.ScreenConfigPath));
            Assert.True(File.Exists(snapshot.Paths.AttachWrapperScriptPath));
            Assert.True(File.Exists(snapshot.Paths.TerminalDiagnosticsScriptPath));
            Assert.Contains("GENERATED FILE", File.ReadAllText(snapshot.Paths.ComposePath));
            Assert.Contains("npm install -g opencode-ai", File.ReadAllText(snapshot.Paths.ProvisionScriptPath));
            Assert.Contains("/home/opencode/.local/share/opencode/log", File.ReadAllText(snapshot.Paths.ProvisionScriptPath));
            Assert.Contains("Initializing OpenCode user directories", File.ReadAllText(snapshot.Paths.OpencodeWorkspaceShellPath));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void WriteAppliedState_WritesAppliedStateFile()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));

            orchestrator.WriteAppliedState(snapshot);

            Assert.True(File.Exists(snapshot.Paths.AppliedStatePath));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_AfterAppliedState_DoesNotRequireUpdate()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            orchestrator.WriteAppliedState(snapshot);

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.False(reloaded.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_AfterRuntimeOnlyReload_DoesNotRequireUpdate()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            orchestrator.WriteAppliedState(snapshot);

            var reloaded = orchestrator.LoadSnapshot(tempRoot);
            var runtimeOnlySnapshot = new WorkspaceSnapshot
            {
                Record = reloaded.Record,
                Definition = reloaded.Definition,
                Paths = reloaded.Paths,
                RuntimeState = WorkspaceRuntimeState.Running,
                AppliedState = reloaded.AppliedState,
                UpdateRequired = reloaded.UpdateRequired,
            };

            Assert.False(runtimeOnlySnapshot.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenWorkspaceYamlChanges_RequiresUpdate()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            orchestrator.WriteAppliedState(snapshot);

            File.WriteAllText(snapshot.Paths.WorkspaceYamlPath, File.ReadAllText(snapshot.Paths.WorkspaceYamlPath).Replace("ubuntu:24.04", "ubuntu:22.04", StringComparison.Ordinal));

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.True(reloaded.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenSelectedFeaturesChange_RequiresUpdate()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            orchestrator.WriteAppliedState(snapshot);

            var updatedDefinition = CreateDefinition("core", "document-processing");
            File.WriteAllText(snapshot.Paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(updatedDefinition));

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.True(reloaded.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenRelevantCatalogPlanChanges_RequiresUpdate()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var originalResolver = CreateResolver();
            var originalOrchestrator = CreateOrchestrator(tempRoot, originalResolver);
            var snapshot = originalOrchestrator.CreateWorkspace(tempRoot, CreateDefinition("core", "document-processing"));
            originalOrchestrator.WriteAppliedState(snapshot);

            var changedResolver = CreateResolver(additionalDocumentProcessingAptPackage: "tesseract-ocr");
            var changedOrchestrator = CreateOrchestrator(tempRoot, changedResolver);

            var reloaded = changedOrchestrator.LoadSnapshot(tempRoot);

            Assert.True(reloaded.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void CreatePermissionRepairArguments_UsesHelperContainerAndTargetMount()
    {
        var arguments = DockerService.CreatePermissionRepairArguments("C:\\Workspaces\\Demo");

        Assert.Equal("run", arguments[0]);
        Assert.Contains("ubuntu:24.04", arguments);
        Assert.Contains("C:\\Workspaces\\Demo:/target", arguments);
        Assert.Contains("chmod -R u+rwX,go+rwX /target || true", arguments[^1]);
    }

    private static WorkspaceDefinition CreateDefinition(params string[] features)
    {
        return new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Name = "smoke-workspace",
                Image = "ubuntu:24.04",
            },
            Features = features.ToList(),
            Services = new List<string> { "postgres", "pgadmin" },
            Skills = new List<string>(),
            Mcp = new List<string>(),
        };
    }

    private static WorkspaceResolver CreateResolver(string? additionalDocumentProcessingAptPackage = null)
    {
        var documentPackages = new List<string> { "pandoc" };
        if (!string.IsNullOrWhiteSpace(additionalDocumentProcessingAptPackage))
        {
            documentPackages.Add(additionalDocumentProcessingAptPackage);
        }

        return new WorkspaceResolver(
            new[]
            {
                new FeatureManifest
                {
                    Id = "core",
                    AlwaysEnabled = true,
                    Dependencies = new DependencySet { Apt = new List<string> { "git", "curl" } },
                },
                new FeatureManifest
                {
                    Id = "document-processing",
                    Dependencies = new DependencySet { Apt = documentPackages },
                },
            },
            new[]
            {
                new ServiceManifest
                {
                    Id = "postgres",
                    Image = "postgres:17",
                    HostPorts = new List<string> { "15432:5432" },
                    Volumes = new List<string> { "postgres-data:/var/lib/postgresql/data" },
                },
                new ServiceManifest
                {
                    Id = "pgadmin",
                    Image = "dpage/pgadmin4:9",
                    HostPorts = new List<string> { "18080:80" },
                    DependsOn = new List<string> { "postgres" },
                },
            });
    }

    private static WorkspaceOrchestrator CreateOrchestrator(string tempRoot, WorkspaceResolver resolver)
    {
        return new WorkspaceOrchestrator(
            new WorkspaceYamlService(),
            new WorkspaceRepository(Path.Combine(tempRoot, ".appdata")),
            resolver,
            new ComposeGenerator(),
            new EnvironmentFileGenerator(),
            new ProvisioningScriptGenerator(),
            new TerminalArtifactsGenerator(),
            new AttachArtifactsGenerator(),
            new WorkspaceAppliedStateService(),
            new DockerService(new ProcessRunner()),
            new NoOpTerminalLauncher());
    }

    private static string CreateTempRoot() => Path.Combine(Path.GetTempPath(), $"opencode-workspace-manager-{Guid.NewGuid():N}");

    private static void DeleteTempRoot(string tempRoot)
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class NoOpTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
