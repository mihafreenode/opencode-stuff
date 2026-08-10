using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceConfigurationPathRegressionTests
{
    [Theory]
    [InlineData("workspace.yaml", true)]
    [InlineData("workspace.yml", false)]
    [InlineData(".opencode/profile.yaml", false)]
    [InlineData(".opencode/profile.yml", false)]
    public async Task SaveAndRegenerate_KeepUsingDiscoveredConfigurationPath(string relativePath, bool isRootWorkspaceYaml)
    {
        var repositoryRoot = CreateTempPath("workspace-config-path-repo");
        var appDataRoot = CreateTempPath("workspace-config-path-appdata");

        try
        {
            Directory.CreateDirectory(repositoryRoot);
            await RunGitAsync(repositoryRoot, "init", "-b", "main");
            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "demo\n");
            await RunGitAsync(repositoryRoot, "add", "-A");
            await RunGitAsync(repositoryRoot, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            WriteWorkspaceConfiguration(repositoryRoot, relativePath, new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "path-regression", Image = "ubuntu:24.04" },
                Provider = new WorkspaceProviderDefinition { Type = "git" },
                Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
                Features = ["core"],
                Services = ["postgres"],
                Skills = [],
                Mcp = [],
            });

            var orchestrator = CreateOrchestrator(appDataRoot);
            var imported = await orchestrator.ImportExistingGitCheckoutAsync(new ExistingGitCheckoutImportRequest
            {
                RepositoryPath = repositoryRoot,
                WorkspaceName = "Ignored Name",
                BranchMode = ExistingGitCheckoutBranchMode.UseCurrentBranch,
            });

            await orchestrator.RegenerateAsync(imported);
            var reloaded = await orchestrator.LoadSnapshotAsync(repositoryRoot, includeRuntimeInspection: false);

            Assert.Equal(relativePath, imported.ConfigurationPath);
            Assert.Equal(relativePath, reloaded.ConfigurationPath);
            Assert.Equal(relativePath, imported.Record.ConfigurationPath);
            Assert.True(File.Exists(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Equal(isRootWorkspaceYaml, File.Exists(Path.Combine(repositoryRoot, "workspace.yaml")));
        }
        finally
        {
            DeleteTempPath(repositoryRoot);
            DeleteTempPath(appDataRoot);
        }
    }

    private static void WriteWorkspaceConfiguration(string repositoryRoot, string relativePath, WorkspaceDefinition definition)
    {
        var fullPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, new WorkspaceYamlService().Write(definition));
    }

    private static WorkspaceOrchestrator CreateOrchestrator(string appDataRoot)
    {
        var processRunner = new ProcessRunner();
        var catalog = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        var resolver = new WorkspaceResolver(catalog.LoadFeatures(), catalog.LoadServices(), catalog.LoadCapabilities(), catalog.LoadKnowledgePacks());
        var ignorePolicy = new WorkspaceIgnorePolicyService();
        return new WorkspaceOrchestrator(
            new WorkspaceYamlService(),
            new WorkspaceDiscoveryService(),
            new WorkspaceRepository(appDataRoot),
            resolver,
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
            ignorePolicy,
            new GitWorkspaceProvider(processRunner, ignorePolicy),
            new DockerService(processRunner),
            new NoOpTerminalLauncher());
    }

    private static async Task<ProcessResult> RunGitAsync(string workingDirectory, params string[] arguments)
        => await new ProcessRunner().RunAsync("git", arguments, workingDirectory);

    private static string CreateTempPath(string prefix) => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");

    private static void DeleteTempPath(string path)
    {
        if (Directory.Exists(path))
        {
            TestFileSystem.DeleteDirectoryIfExists(path);
        }
    }

    private sealed class NoOpTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
