using System.Diagnostics;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceYamlPreservationIntegrationTests
{
    [Theory]
    [InlineData("workspace.yaml")]
    [InlineData("workspace.yml")]
    [InlineData(".opencode/profile.yaml")]
    [InlineData(".opencode/profile.yml")]
    public async Task UnknownTopLevelYaml_SurvivesLoadEditSaveAndRegenerate(string relativePath)
    {
        if (!CanRunGit())
        {
            return;
        }

        var repositoryRoot = CreateTempPath("workspace-yaml-preservation-repo");
        var appDataRoot = CreateTempPath("workspace-yaml-preservation-appdata");

        try
        {
            Directory.CreateDirectory(repositoryRoot);
            await RunGitAsync(repositoryRoot, "init", "-b", "main");
            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "demo\n");
            await RunGitAsync(repositoryRoot, "add", "-A");
            await RunGitAsync(repositoryRoot, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var configPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, """
workspace:
  name: discovery-test

provider:
  type: git

runtime:
  default: default

features:
  - core

services: []
skills: []
mcp: []

agent:
  profile: opencode-default

terminal:
  font:
    provider: nerd-fonts
    family: JetBrainsMono Nerd Font
  prompt:
    provider: starship
  installIfMissing: true
  utilities:
    zoxide: false
    fzf: false

x-company:
  owner: Miha
  onboarding: true
  notes:
    keep: this
""");

            var orchestrator = CreateOrchestrator(appDataRoot);
            var snapshot = await orchestrator.ImportExistingGitCheckoutAsync(new ExistingGitCheckoutImportRequest
            {
                RepositoryPath = repositoryRoot,
                WorkspaceName = "Ignored Name",
                BranchMode = ExistingGitCheckoutBranchMode.UseCurrentBranch,
            });

            var editedDefinition = new WorkspaceDefinition
            {
                Workspace = snapshot.Definition.Workspace,
                Provider = snapshot.Definition.Provider,
                Runtime = snapshot.Definition.Runtime,
                Features = snapshot.Definition.Features,
                Services = ["postgres"],
                Skills = snapshot.Definition.Skills,
                Mcp = snapshot.Definition.Mcp,
                Terminal = snapshot.Definition.Terminal,
                Agent = snapshot.Definition.Agent,
            };

            var updatedSnapshot = new WorkspaceSnapshot
            {
                Record = snapshot.Record,
                Definition = editedDefinition,
                Paths = snapshot.Paths,
                ConfigurationPath = snapshot.ConfigurationPath,
                RuntimeState = snapshot.RuntimeState,
                Safety = snapshot.Safety,
                Session = snapshot.Session,
                AppliedState = snapshot.AppliedState,
                UpdateRequired = snapshot.UpdateRequired,
            };

            await orchestrator.RegenerateAsync(updatedSnapshot);

            var yaml = File.ReadAllText(configPath);

            Assert.Equal(relativePath, updatedSnapshot.ConfigurationPath);
            Assert.Contains("x-company:", yaml);
            Assert.Contains("owner: Miha", yaml);
            Assert.Contains("onboarding: true", yaml);
            Assert.Contains("keep: this", yaml);
            Assert.Contains("- postgres", yaml);
            Assert.False(!string.Equals(relativePath, "workspace.yaml", StringComparison.Ordinal) && File.Exists(Path.Combine(repositoryRoot, "workspace.yaml")));
        }
        finally
        {
            DeleteTempPath(repositoryRoot);
            DeleteTempPath(appDataRoot);
        }
    }

    private static WorkspaceOrchestrator CreateOrchestrator(string appDataRoot)
    {
        var processRunner = new ProcessRunner();
        var catalog = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var resolver = new WorkspaceResolver(catalog.LoadFeatures(), catalog.LoadServices());
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

    private static bool CanRunGit()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
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
