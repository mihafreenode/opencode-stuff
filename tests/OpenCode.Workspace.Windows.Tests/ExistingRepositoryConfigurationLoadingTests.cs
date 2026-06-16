using System.Diagnostics;
using System.IO;
using OpenCode.Workspace.Manager.Services;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class ExistingRepositoryConfigurationLoadingTests
{
    [Fact]
    public async Task LoadExistingRepositoryConfigurationAsync_PopulatesUiStateFromRepositoryConfiguration()
    {
        if (!CanRunGit())
        {
            return;
        }

        var repositoryRoot = CreateTempPath("ocwm-existing-config-repo");
        var appDataRoot = CreateTempPath("ocwm-existing-config-appdata");

        try
        {
            Directory.CreateDirectory(repositoryRoot);
            await RunGitAsync(repositoryRoot, "init", "-b", "main");
            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "demo\n");
            await RunGitAsync(repositoryRoot, "add", "-A");
            await RunGitAsync(repositoryRoot, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var configPath = Path.Combine(repositoryRoot, ".opencode", "profile.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, new OpenCode.Workspace.Core.Workspaces.WorkspaceYamlService().Write(new OpenCode.Workspace.Core.Models.WorkspaceDefinition
            {
                Workspace = new OpenCode.Workspace.Core.Models.WorkspaceMetadata { Name = "Repository Workspace", Image = "ubuntu:24.04" },
                Provider = new OpenCode.Workspace.Core.Models.WorkspaceProviderDefinition { Type = "git" },
                Runtime = new OpenCode.Workspace.Core.Models.WorkspaceRuntimeDefinition { Default = "default", Node = 24 },
                Features = ["core", "document-processing"],
                Services = ["postgres"],
                Skills = ["demo-skill"],
                Mcp = ["demo-mcp"],
                Agent = new OpenCode.Workspace.Core.Models.AgentPreferences { Profile = "repo-profile" },
                Terminal = new OpenCode.Workspace.Core.Models.TerminalPreferences
                {
                    InstallIfMissing = false,
                    Font = new OpenCode.Workspace.Core.Models.TerminalFontPreferences { Provider = "nerd-fonts", Family = "FiraCode Nerd Font" },
                    Prompt = new OpenCode.Workspace.Core.Models.TerminalPromptPreferences { Provider = "default-bash" },
                    Utilities = new OpenCode.Workspace.Core.Models.TerminalUtilityPreferences { Zoxide = true, Fzf = true },
                },
            }));

            var viewModel = CreateViewModel(appDataRoot);
            await viewModel.InitializeAsync();
            viewModel.PrepareCreateWorkspaceDialog();
            viewModel.SelectedWorkspaceSourceType = OpenCode.Workspace.Core.Models.WorkspaceSourceType.ExistingGitCheckout;

            var plan = await viewModel.LoadExistingRepositoryConfigurationAsync(repositoryRoot);
            var rebuiltDefinition = viewModel.BuildWorkspaceDefinitionFromSelections("Repository Workspace");

            Assert.Equal(OpenCode.Workspace.Core.Workspaces.WorkspaceDiscoveryStatus.Found, plan.DiscoveryResult.Status);
            Assert.True(viewModel.HasLoadedRepositoryConfiguration);
            Assert.True(viewModel.ShowRepositoryConfigurationBanner);
            Assert.Equal(".opencode/profile.yaml", viewModel.LoadedRepositoryConfigurationPath);
            Assert.Equal("Repository Workspace", viewModel.NewWorkspaceName);
            Assert.True(viewModel.AvailableFeatures.Single(item => item.Id == "document-processing").IsSelected);
            Assert.True(viewModel.AvailableServices.Single(item => item.Id == "postgres").IsSelected);
            Assert.Equal("default-bash", viewModel.SelectedPromptProvider);
            Assert.Equal("FiraCode Nerd Font", viewModel.SelectedFontFamily);
            Assert.False(viewModel.InstallTerminalIfMissing);
            Assert.True(viewModel.InstallZoxide);
            Assert.True(viewModel.InstallFzf);
            Assert.Equal(24, rebuiltDefinition.Runtime.Node);
            Assert.Contains("demo-skill", rebuiltDefinition.Skills);
            Assert.Contains("demo-mcp", rebuiltDefinition.Mcp);
            Assert.Equal("repo-profile", rebuiltDefinition.Agent.Profile);
        }
        finally
        {
            DeleteTempPath(repositoryRoot);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public async Task LoadExistingRepositoryConfigurationAsync_SkipsTemplateDefaultsAfterConfigurationLoad()
    {
        if (!CanRunGit())
        {
            return;
        }

        var repositoryRoot = CreateTempPath("ocwm-existing-config-template");
        var appDataRoot = CreateTempPath("ocwm-existing-config-template-appdata");

        try
        {
            Directory.CreateDirectory(repositoryRoot);
            await RunGitAsync(repositoryRoot, "init", "-b", "main");
            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "demo\n");
            await RunGitAsync(repositoryRoot, "add", "-A");
            await RunGitAsync(repositoryRoot, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");
            File.WriteAllText(Path.Combine(repositoryRoot, "workspace.yml"), new OpenCode.Workspace.Core.Workspaces.WorkspaceYamlService().Write(new OpenCode.Workspace.Core.Models.WorkspaceDefinition
            {
                Workspace = new OpenCode.Workspace.Core.Models.WorkspaceMetadata { Name = "Loaded Workspace", Image = "ubuntu:24.04" },
                Provider = new OpenCode.Workspace.Core.Models.WorkspaceProviderDefinition { Type = "git" },
                Runtime = new OpenCode.Workspace.Core.Models.WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
                Features = ["core"],
                Services = ["postgres"],
                Skills = [],
                Mcp = [],
            }));

            var viewModel = CreateViewModel(appDataRoot);
            await viewModel.InitializeAsync();
            viewModel.PrepareCreateWorkspaceDialog();
            viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "documentation-analysis");

            await viewModel.LoadExistingRepositoryConfigurationAsync(repositoryRoot);
            viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "oracle-plsql-demo");

            Assert.True(viewModel.AvailableServices.Single(item => item.Id == "postgres").IsSelected);
            Assert.False(viewModel.AvailableServices.Single(item => item.Id == "oracle-demo").IsSelected);
            Assert.Equal(["core"], viewModel.BuildWorkspaceDefinitionFromSelections("Loaded Workspace").Features);
        }
        finally
        {
            DeleteTempPath(repositoryRoot);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public async Task LoadExistingRepositoryConfigurationAsync_InvalidConfiguration_BlocksContinuationState()
    {
        if (!CanRunGit())
        {
            return;
        }

        var repositoryRoot = CreateTempPath("ocwm-existing-config-invalid");
        var appDataRoot = CreateTempPath("ocwm-existing-config-invalid-appdata");

        try
        {
            Directory.CreateDirectory(repositoryRoot);
            await RunGitAsync(repositoryRoot, "init", "-b", "main");
            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "demo\n");
            Directory.CreateDirectory(Path.Combine(repositoryRoot, ".opencode"));
            File.WriteAllText(Path.Combine(repositoryRoot, ".opencode", "profile.yaml"), "workspace: [\n");
            await RunGitAsync(repositoryRoot, "add", "-A");
            await RunGitAsync(repositoryRoot, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var viewModel = CreateViewModel(appDataRoot);
            await viewModel.InitializeAsync();
            viewModel.PrepareCreateWorkspaceDialog();
            viewModel.SelectedWorkspaceSourceType = OpenCode.Workspace.Core.Models.WorkspaceSourceType.ExistingGitCheckout;

            var plan = await viewModel.LoadExistingRepositoryConfigurationAsync(repositoryRoot);

            Assert.Equal(OpenCode.Workspace.Core.Workspaces.WorkspaceDiscoveryStatus.Invalid, plan.DiscoveryResult.Status);
            Assert.True(viewModel.HasInvalidRepositoryConfiguration);
            Assert.False(viewModel.CanCreateWorkspaceForDialog);
            Assert.Equal(".opencode/profile.yaml", viewModel.LoadedRepositoryConfigurationPath);
        }
        finally
        {
            DeleteTempPath(repositoryRoot);
            DeleteTempPath(appDataRoot);
        }
    }

    private static MainWindowViewModel CreateViewModel(string appDataRoot)
    {
        var bootstrapper = new AppBootstrapper();
        return bootstrapper.CreateMainWindowViewModel(TestPaths.RepositoryRoot, appDataRoot, "en");
    }

    private static async Task<OpenCode.Workspace.Core.Models.ProcessResult> RunGitAsync(string workingDirectory, params string[] arguments)
        => await new OpenCode.Workspace.Core.Runtime.ProcessRunner().RunAsync("git", arguments, workingDirectory);

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
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(entry, FileAttributes.Normal);
                    }
                    catch
                    {
                    }
                }

                Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
