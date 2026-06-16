using System.Diagnostics;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class ExistingGitCheckoutImportTests
{
    [Fact]
    public async Task ImportExistingGitCheckoutAsync_CreatesWorkspaceBranchWithoutChangingMain()
    {
        if (!CanRunGit())
        {
            return;
        }

        var rootPath = CreateTempPath();
        var appDataRoot = CreateTempPath();
        try
        {
            Directory.CreateDirectory(rootPath);
            await RunGitAsync(rootPath, "init", "-b", "main");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");
            await RunGitAsync(rootPath, "add", "-A");
            await RunGitAsync(rootPath, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");
            var mainHead = (await RunGitAsync(rootPath, "rev-parse", "main")).StandardOutput.Trim();

            var orchestrator = CreateOrchestrator(appDataRoot);
            var snapshot = await orchestrator.ImportExistingGitCheckoutAsync(new ExistingGitCheckoutImportRequest
            {
                RepositoryPath = rootPath,
                WorkspaceName = "My Project",
                BranchMode = ExistingGitCheckoutBranchMode.CreateTemporaryWorkspaceBranch,
            });

            var currentBranch = (await RunGitAsync(rootPath, "branch", "--show-current")).StandardOutput.Trim();
            var mainHeadAfter = (await RunGitAsync(rootPath, "rev-parse", "main")).StandardOutput.Trim();

            Assert.Matches(@"^workspace/my-project-\d{8}-\d{4}(-\d+)?$", currentBranch);
            Assert.Equal(mainHead, mainHeadAfter);
            Assert.Equal(WorkspaceSourceType.ExistingGitCheckout, snapshot.Record.SourceType);
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public async Task ImportExistingGitCheckoutAsync_KeepsDirtyChangesWhenCreatingWorkspaceBranch()
    {
        if (!CanRunGit())
        {
            return;
        }

        var rootPath = CreateTempPath();
        var appDataRoot = CreateTempPath();
        try
        {
            Directory.CreateDirectory(rootPath);
            await RunGitAsync(rootPath, "init", "-b", "main");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");
            await RunGitAsync(rootPath, "add", "-A");
            await RunGitAsync(rootPath, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");
            File.AppendAllText(Path.Combine(rootPath, "notes.txt"), "dirty change\n");

            var orchestrator = CreateOrchestrator(appDataRoot);
            await orchestrator.ImportExistingGitCheckoutAsync(new ExistingGitCheckoutImportRequest
            {
                RepositoryPath = rootPath,
                WorkspaceName = "My Project",
                BranchMode = ExistingGitCheckoutBranchMode.CreateTemporaryWorkspaceBranch,
            });

            var contents = File.ReadAllText(Path.Combine(rootPath, "notes.txt"));
            Assert.Contains("dirty change", contents, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public async Task ImportExistingGitCheckoutAsync_CreatesWorkspaceYamlWhenMissing()
    {
        if (!CanRunGit())
        {
            return;
        }

        var rootPath = CreateTempPath();
        var appDataRoot = CreateTempPath();
        try
        {
            Directory.CreateDirectory(rootPath);
            await RunGitAsync(rootPath, "init", "-b", "main");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");
            await RunGitAsync(rootPath, "add", "-A");
            await RunGitAsync(rootPath, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var orchestrator = CreateOrchestrator(appDataRoot);
            var snapshot = await orchestrator.ImportExistingGitCheckoutAsync(new ExistingGitCheckoutImportRequest
            {
                RepositoryPath = rootPath,
                WorkspaceName = "My Project",
                BranchMode = ExistingGitCheckoutBranchMode.UseCurrentBranch,
            });

            Assert.True(File.Exists(Path.Combine(rootPath, "workspace.yaml")));
            Assert.Equal("My Project", snapshot.Definition.Workspace.Name);
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public async Task ImportExistingGitCheckoutAsync_UsesSelectedToolsWhenCreatingWorkspaceYaml()
    {
        if (!CanRunGit())
        {
            return;
        }

        var rootPath = CreateTempPath();
        var appDataRoot = CreateTempPath();
        try
        {
            Directory.CreateDirectory(rootPath);
            await RunGitAsync(rootPath, "init", "-b", "main");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");
            await RunGitAsync(rootPath, "add", "-A");
            await RunGitAsync(rootPath, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var orchestrator = CreateOrchestrator(appDataRoot);
            var snapshot = await orchestrator.ImportExistingGitCheckoutAsync(new ExistingGitCheckoutImportRequest
            {
                RepositoryPath = rootPath,
                WorkspaceName = "My Project",
                BranchMode = ExistingGitCheckoutBranchMode.UseCurrentBranch,
                InitialDefinition = new WorkspaceDefinition
                {
                    Workspace = new WorkspaceMetadata
                    {
                        Name = "My Project",
                        Image = "ubuntu:24.04",
                    },
                    Features = new List<string> { "core" },
                    Services = new List<string> { "postgres" },
                    Skills = new List<string>(),
                    Mcp = new List<string>(),
                },
            });

            Assert.Contains("postgres", snapshot.Definition.Services, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public async Task ImportExistingGitCheckoutAsync_PreservesExistingWorkspaceYaml()
    {
        if (!CanRunGit())
        {
            return;
        }

        var rootPath = CreateTempPath();
        var appDataRoot = CreateTempPath();
        try
        {
            Directory.CreateDirectory(rootPath);
            await RunGitAsync(rootPath, "init", "-b", "main");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");
            await RunGitAsync(rootPath, "add", "-A");
            await RunGitAsync(rootPath, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var yaml = new WorkspaceYamlService().Write(new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata
                {
                    Id = "existing-workspace",
                    Name = "Existing Workspace",
                    Image = "ubuntu:24.04",
                },
                Provider = new WorkspaceProviderDefinition
                {
                    Type = "git",
                },
                Runtime = new WorkspaceRuntimeDefinition
                {
                    Default = "default",
                },
                Features = new List<string> { "core" },
                Services = new List<string> { "postgres" },
                Skills = new List<string>(),
                Mcp = new List<string>(),
            });
            File.WriteAllText(Path.Combine(rootPath, "workspace.yaml"), yaml);

            var orchestrator = CreateOrchestrator(appDataRoot);
            var snapshot = await orchestrator.ImportExistingGitCheckoutAsync(new ExistingGitCheckoutImportRequest
            {
                RepositoryPath = rootPath,
                WorkspaceName = "Ignored Name",
                BranchMode = ExistingGitCheckoutBranchMode.UseCurrentBranch,
            });

            Assert.Equal("Existing Workspace", snapshot.Definition.Workspace.Name);
            Assert.Contains("postgres", snapshot.Definition.Services, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(appDataRoot);
        }
    }

    [Theory]
    [InlineData("workspace.yml")]
    [InlineData(".opencode/profile.yaml")]
    [InlineData(".opencode/profile.yml")]
    public async Task ImportExistingGitCheckoutAsync_PreservesDiscoveredConfigurationPath(string relativePath)
    {
        if (!CanRunGit())
        {
            return;
        }

        var rootPath = CreateTempPath();
        var appDataRoot = CreateTempPath();
        try
        {
            Directory.CreateDirectory(rootPath);
            await RunGitAsync(rootPath, "init", "-b", "main");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");
            await RunGitAsync(rootPath, "add", "-A");
            await RunGitAsync(rootPath, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var configPath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, new WorkspaceYamlService().Write(new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "Existing Workspace", Image = "ubuntu:24.04" },
                Provider = new WorkspaceProviderDefinition { Type = "git" },
                Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
                Features = ["core"],
                Services = ["postgres"],
                Skills = [],
                Mcp = [],
            }));

            var orchestrator = CreateOrchestrator(appDataRoot);
            var snapshot = await orchestrator.ImportExistingGitCheckoutAsync(new ExistingGitCheckoutImportRequest
            {
                RepositoryPath = rootPath,
                WorkspaceName = "Ignored Name",
                BranchMode = ExistingGitCheckoutBranchMode.UseCurrentBranch,
            });

            Assert.Equal(relativePath, snapshot.ConfigurationPath);
            Assert.Equal(relativePath, snapshot.Record.ConfigurationPath);
            Assert.Equal(configPath, snapshot.Paths.WorkspaceYamlPath);
            Assert.False(File.Exists(Path.Combine(rootPath, "workspace.yaml")) && !string.Equals(relativePath, "workspace.yaml", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public async Task InspectExistingGitCheckoutAsync_InvalidDiscoveredConfiguration_ReturnsInvalid()
    {
        if (!CanRunGit())
        {
            return;
        }

        var rootPath = CreateTempPath();
        var appDataRoot = CreateTempPath();
        try
        {
            Directory.CreateDirectory(rootPath);
            await RunGitAsync(rootPath, "init", "-b", "main");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");
            Directory.CreateDirectory(Path.Combine(rootPath, ".opencode"));
            File.WriteAllText(Path.Combine(rootPath, ".opencode/profile.yaml".Replace('/', Path.DirectorySeparatorChar)), "workspace: [\n");
            await RunGitAsync(rootPath, "add", "-A");
            await RunGitAsync(rootPath, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var orchestrator = CreateOrchestrator(appDataRoot);
            var plan = await orchestrator.InspectExistingGitCheckoutAsync(rootPath, "My Project");

            Assert.Equal(WorkspaceDiscoveryStatus.Invalid, plan.DiscoveryResult.Status);
            Assert.Equal(".opencode/profile.yaml", plan.DiscoveryResult.ConfigurationPath);
            Assert.False(string.IsNullOrWhiteSpace(plan.DiscoveryResult.ErrorMessage));
            Assert.Null(plan.LoadedDefinition);
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public async Task ImportExistingGitCheckoutAsync_InvalidDiscoveredConfiguration_ThrowsAndDoesNotCreateWorkspaceYaml()
    {
        if (!CanRunGit())
        {
            return;
        }

        var rootPath = CreateTempPath();
        var appDataRoot = CreateTempPath();
        try
        {
            Directory.CreateDirectory(rootPath);
            await RunGitAsync(rootPath, "init", "-b", "main");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");
            Directory.CreateDirectory(Path.Combine(rootPath, ".opencode"));
            File.WriteAllText(Path.Combine(rootPath, ".opencode", "profile.yaml"), "workspace: [\n");
            await RunGitAsync(rootPath, "add", "-A");
            await RunGitAsync(rootPath, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var orchestrator = CreateOrchestrator(appDataRoot);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.ImportExistingGitCheckoutAsync(new ExistingGitCheckoutImportRequest
            {
                RepositoryPath = rootPath,
                WorkspaceName = "My Project",
                BranchMode = ExistingGitCheckoutBranchMode.UseCurrentBranch,
            }));

            Assert.Contains("Invalid workspace configuration found", exception.Message);
            Assert.False(File.Exists(Path.Combine(rootPath, "workspace.yaml")));
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public async Task ImportExistingGitCheckoutAsync_PreservesUnrelatedYamlValuesWhenRefreshingGeneratedFiles()
    {
        if (!CanRunGit())
        {
            return;
        }

        var rootPath = CreateTempPath();
        var appDataRoot = CreateTempPath();
        try
        {
            Directory.CreateDirectory(rootPath);
            await RunGitAsync(rootPath, "init", "-b", "main");
            File.WriteAllText(Path.Combine(rootPath, "notes.txt"), "draft\n");
            await RunGitAsync(rootPath, "add", "-A");
            await RunGitAsync(rootPath, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var profilePath = Path.Combine(rootPath, ".opencode", "profile.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
            File.WriteAllText(profilePath, """
workspace:
  name: Existing Workspace
provider:
  type: git
runtime:
  default: default
features:
  - core
services:
  - postgres
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
customSection:
  owner: onboarding-team
""");

            var orchestrator = CreateOrchestrator(appDataRoot);
            var snapshot = await orchestrator.ImportExistingGitCheckoutAsync(new ExistingGitCheckoutImportRequest
            {
                RepositoryPath = rootPath,
                WorkspaceName = "Ignored Name",
                BranchMode = ExistingGitCheckoutBranchMode.UseCurrentBranch,
            });

            var refreshedYaml = File.ReadAllText(profilePath);

            Assert.Equal(".opencode/profile.yaml", snapshot.ConfigurationPath);
            Assert.Contains("customSection:", refreshedYaml);
            Assert.Contains("owner: onboarding-team", refreshedYaml);
            Assert.DoesNotContain("workspace.yaml", refreshedYaml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempPath(rootPath);
            DeleteTempPath(appDataRoot);
        }
    }

    private static WorkspaceOrchestrator CreateOrchestrator(string appDataRoot)
    {
        var processRunner = new ProcessRunner();
        var resolver = new WorkspaceResolver(new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog")).LoadFeatures(), new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog")).LoadServices());
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
            new WorkspaceIgnorePolicyService(),
            new GitWorkspaceProvider(processRunner, new WorkspaceIgnorePolicyService()),
            new DockerService(processRunner),
            new TestTerminalLauncher());
    }

    private static async Task<ProcessResult> RunGitAsync(string workingDirectory, params string[] arguments)
        => await new ProcessRunner().RunAsync("git", arguments, workingDirectory);

    private static string CreateTempPath() => Path.Combine(Path.GetTempPath(), $"existing-checkout-{Guid.NewGuid():N}");

    private static void DeleteTempPath(string path)
    {
        if (Directory.Exists(path))
        {
            TestFileSystem.DeleteDirectoryIfExists(path);
        }
    }

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

    private sealed class TestTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
