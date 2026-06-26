using System.Diagnostics;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceDiscoveryIntegrationTests
{
    [Fact]
    public async Task EmptyExistingGitRepository_DiscoveryReturnsNotFound_AndDoesNotCreateWorkspaceYaml()
    {
        if (!CanRunGit())
        {
            return;
        }

        var repositoryRoot = CreateTempPath("workspace-discovery-empty-repo");
        var appDataRoot = CreateTempPath("workspace-discovery-empty-appdata");

        try
        {
            Directory.CreateDirectory(repositoryRoot);
            await RunGitAsync(repositoryRoot, "init", "-b", "main");
            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "demo\n");
            await RunGitAsync(repositoryRoot, "add", "-A");
            await RunGitAsync(repositoryRoot, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var discovery = new WorkspaceDiscoveryService().Discover(repositoryRoot);
            var orchestrator = CreateOrchestrator(appDataRoot);
            var plan = await orchestrator.InspectExistingGitCheckoutAsync(repositoryRoot, "Demo Workspace");

            Assert.Equal(WorkspaceDiscoveryStatus.NotFound, discovery.Status);
            Assert.Equal(WorkspaceDiscoveryStatus.NotFound, plan.DiscoveryResult.Status);
            Assert.False(plan.HasWorkspaceConfiguration);
            Assert.Null(plan.LoadedDefinition);
            Assert.False(File.Exists(Path.Combine(repositoryRoot, "workspace.yaml")));
            Assert.False(File.Exists(Path.Combine(repositoryRoot, ".opencode", "profile.yaml")));
        }
        finally
        {
            DeleteTempPath(repositoryRoot);
            DeleteTempPath(appDataRoot);
        }
    }

    [Theory]
    [InlineData("workspace.yaml")]
    [InlineData("workspace.yml")]
    [InlineData(".opencode/profile.yaml")]
    [InlineData(".opencode/profile.yml")]
    public async Task RepositoryWithSupportedConfiguration_DiscoveryReturnsFound_AndLoadsWorkspaceDefinition(string relativePath)
    {
        if (!CanRunGit())
        {
            return;
        }

        var repositoryRoot = CreateTempPath("workspace-discovery-found-repo");
        var appDataRoot = CreateTempPath("workspace-discovery-found-appdata");

        try
        {
            Directory.CreateDirectory(repositoryRoot);
            await RunGitAsync(repositoryRoot, "init", "-b", "main");
            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "demo\n");
            await RunGitAsync(repositoryRoot, "add", "-A");
            await RunGitAsync(repositoryRoot, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            WriteWorkspaceConfiguration(repositoryRoot, relativePath, new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "discovery-test", Image = "ubuntu:24.04" },
                Provider = new WorkspaceProviderDefinition { Type = "git" },
                Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
                Features = ["core"],
                Services = ["postgres"],
                Skills = [],
                Mcp = [],
            });

            var orchestrator = CreateOrchestrator(appDataRoot);
            var plan = await orchestrator.InspectExistingGitCheckoutAsync(repositoryRoot, "Ignored Name");

            Assert.Equal(WorkspaceDiscoveryStatus.Found, plan.DiscoveryResult.Status);
            Assert.Equal(relativePath, plan.DiscoveryResult.ConfigurationPath);
            Assert.NotNull(plan.LoadedDefinition);
            Assert.Equal("discovery-test", plan.LoadedDefinition!.Workspace.Name);
            Assert.Contains("postgres", plan.LoadedDefinition.Services, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempPath(repositoryRoot);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public async Task InvalidDiscoveredConfiguration_BlocksRepositoryManagedImport_WithoutFallbackOrReplacementYaml()
    {
        if (!CanRunGit())
        {
            return;
        }

        var repositoryRoot = CreateTempPath("workspace-discovery-invalid-repo");
        var appDataRoot = CreateTempPath("workspace-discovery-invalid-appdata");

        try
        {
            Directory.CreateDirectory(repositoryRoot);
            await RunGitAsync(repositoryRoot, "init", "-b", "main");
            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "demo\n");
            Directory.CreateDirectory(Path.Combine(repositoryRoot, ".opencode"));
            File.WriteAllText(Path.Combine(repositoryRoot, ".opencode", "profile.yaml"), "workspace: [\n");
            await RunGitAsync(repositoryRoot, "add", "-A");
            await RunGitAsync(repositoryRoot, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var orchestrator = CreateOrchestrator(appDataRoot);
            var plan = await orchestrator.InspectExistingGitCheckoutAsync(repositoryRoot, "Ignored Name");
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.ImportExistingGitCheckoutAsync(new ExistingGitCheckoutImportRequest
            {
                RepositoryPath = repositoryRoot,
                WorkspaceName = "Ignored Name",
                BranchMode = ExistingGitCheckoutBranchMode.UseCurrentBranch,
            }));

            Assert.Equal(WorkspaceDiscoveryStatus.Invalid, plan.DiscoveryResult.Status);
            Assert.Equal(".opencode/profile.yaml", plan.DiscoveryResult.ConfigurationPath);
            Assert.False(string.IsNullOrWhiteSpace(plan.DiscoveryResult.ErrorMessage));
            Assert.Contains("Invalid workspace configuration found", exception.Message);
            Assert.DoesNotContain("template", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(repositoryRoot, "workspace.yaml")));
        }
        finally
        {
            DeleteTempPath(repositoryRoot);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public async Task MultipleSupportedConfigurations_UseDocumentedDiscoveryPriority()
    {
        if (!CanRunGit())
        {
            return;
        }

        var repositoryRoot = CreateTempPath("workspace-discovery-priority-repo");
        var appDataRoot = CreateTempPath("workspace-discovery-priority-appdata");

        try
        {
            Directory.CreateDirectory(repositoryRoot);
            await RunGitAsync(repositoryRoot, "init", "-b", "main");
            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "demo\n");
            await RunGitAsync(repositoryRoot, "add", "-A");
            await RunGitAsync(repositoryRoot, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            WriteWorkspaceConfiguration(repositoryRoot, ".opencode/profile.yml", CreateDefinition("profile-yml"));
            WriteWorkspaceConfiguration(repositoryRoot, ".opencode/profile.yaml", CreateDefinition("profile-yaml"));
            WriteWorkspaceConfiguration(repositoryRoot, "workspace.yml", CreateDefinition("workspace-yml"));
            WriteWorkspaceConfiguration(repositoryRoot, "workspace.yaml", CreateDefinition("workspace-yaml"));

            var orchestrator = CreateOrchestrator(appDataRoot);
            var plan = await orchestrator.InspectExistingGitCheckoutAsync(repositoryRoot, "Ignored Name");

            Assert.Equal(WorkspaceDiscoveryStatus.Found, plan.DiscoveryResult.Status);
            Assert.Equal("workspace.yaml", plan.DiscoveryResult.ConfigurationPath);
            Assert.Equal("workspace-yaml", plan.LoadedDefinition!.Workspace.Name);
        }
        finally
        {
            DeleteTempPath(repositoryRoot);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public async Task InspectExistingGitCheckoutAsync_UsesSelectedRootInsteadOfParentDirectory()
    {
        if (!CanRunGit())
        {
            return;
        }

        var parentRoot = CreateTempPath("workspace-discovery-parent-root");
        var repositoryRoot = Path.Combine(parentRoot, "child-repo");
        var appDataRoot = CreateTempPath("workspace-discovery-selected-root-appdata");

        try
        {
            Directory.CreateDirectory(repositoryRoot);
            await RunGitAsync(repositoryRoot, "init", "-b", "main");
            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "demo\n");
            await RunGitAsync(repositoryRoot, "add", "-A");
            await RunGitAsync(repositoryRoot, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var orchestrator = CreateOrchestrator(appDataRoot);
            var plan = await orchestrator.InspectExistingGitCheckoutAsync(repositoryRoot, "Child Repo");

            Assert.True(plan.Repository.IsRepository);
            Assert.Equal(repositoryRoot, plan.RepositoryPath);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.InspectExistingGitCheckoutAsync(parentRoot, "Parent"));
            Assert.Contains(parentRoot, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempPath(parentRoot);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public async Task InspectExistingGitCheckoutAsync_FailureReportsPathAndProbeCommand()
    {
        if (!CanRunGit())
        {
            return;
        }

        var repositoryRoot = CreateTempPath("workspace-discovery-invalid-selected-root");
        var appDataRoot = CreateTempPath("workspace-discovery-invalid-selected-appdata");

        try
        {
            Directory.CreateDirectory(repositoryRoot);
            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "not a repo\n");

            var orchestrator = CreateOrchestrator(appDataRoot);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.InspectExistingGitCheckoutAsync(repositoryRoot, "Not Repo"));

            Assert.Contains("The selected folder is not a Git checkout.", exception.Message, StringComparison.Ordinal);
            Assert.Contains(repositoryRoot, exception.Message, StringComparison.Ordinal);
            Assert.Contains("git rev-parse --is-inside-work-tree", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Working-directory exit code:", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempPath(repositoryRoot);
            DeleteTempPath(appDataRoot);
        }
    }

    private static WorkspaceDefinition CreateDefinition(string workspaceName)
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = workspaceName, Image = "ubuntu:24.04" },
            Provider = new WorkspaceProviderDefinition { Type = "git" },
            Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
            Features = ["core"],
            Services = ["postgres"],
            Skills = [],
            Mcp = [],
        };

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
