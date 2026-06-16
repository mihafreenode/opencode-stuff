using System.Diagnostics;
using System.IO;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Manager.Services;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class ExistingRepositoryConfigurationViewModelTests
{
    [Fact]
    public async Task ExistingRepositoryConfiguration_LoadsBannerStateAndSelections()
    {
        if (!CanRunGit())
        {
            return;
        }

        var repositoryRoot = CreateTempPath("existing-repo-vm-found-repo");
        var appDataRoot = CreateTempPath("existing-repo-vm-found-appdata");

        try
        {
            Directory.CreateDirectory(repositoryRoot);
            await RunGitAsync(repositoryRoot, "init", "-b", "main");
            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "demo\n");
            await RunGitAsync(repositoryRoot, "add", "-A");
            await RunGitAsync(repositoryRoot, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var configPath = Path.Combine(repositoryRoot, "workspace.yml");
            File.WriteAllText(configPath, new WorkspaceYamlService().Write(new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "Loaded Workspace", Image = "ubuntu:24.04" },
                Provider = new WorkspaceProviderDefinition { Type = "git" },
                Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
                Features = ["core", "document-processing"],
                Services = ["postgres"],
                Skills = [],
                Mcp = [],
            }));

            var viewModel = CreateViewModel(appDataRoot);
            await viewModel.InitializeAsync();
            viewModel.PrepareCreateWorkspaceDialog();
            viewModel.SelectedWorkspaceSourceType = WorkspaceSourceType.ExistingGitCheckout;

            var plan = await viewModel.LoadExistingRepositoryConfigurationAsync(repositoryRoot);

            Assert.Equal(WorkspaceDiscoveryStatus.Found, plan.DiscoveryResult.Status);
            Assert.True(viewModel.ShowRepositoryConfigurationBanner);
            Assert.Equal("workspace.yml", viewModel.LoadedRepositoryConfigurationPath);
            Assert.Equal("Existing workspace configuration found.", viewModel.RepositoryConfigurationBannerTitle);
            Assert.True(viewModel.AvailableFeatures.Single(item => item.Id == "document-processing").IsSelected);
            Assert.True(viewModel.AvailableServices.Single(item => item.Id == "postgres").IsSelected);
        }
        finally
        {
            DeleteTempPath(repositoryRoot);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public async Task InvalidRepositoryConfiguration_SetsBlockingStateWithoutTemplateFallback()
    {
        if (!CanRunGit())
        {
            return;
        }

        var repositoryRoot = CreateTempPath("existing-repo-vm-invalid-repo");
        var appDataRoot = CreateTempPath("existing-repo-vm-invalid-appdata");

        try
        {
            Directory.CreateDirectory(repositoryRoot);
            await RunGitAsync(repositoryRoot, "init", "-b", "main");
            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "demo\n");
            Directory.CreateDirectory(Path.Combine(repositoryRoot, ".opencode"));
            File.WriteAllText(Path.Combine(repositoryRoot, ".opencode", "profile.yml"), "workspace: [\n");
            await RunGitAsync(repositoryRoot, "add", "-A");
            await RunGitAsync(repositoryRoot, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var viewModel = CreateViewModel(appDataRoot);
            await viewModel.InitializeAsync();
            viewModel.PrepareCreateWorkspaceDialog();
            viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "documentation-analysis");
            viewModel.SelectedWorkspaceSourceType = WorkspaceSourceType.ExistingGitCheckout;

            var plan = await viewModel.LoadExistingRepositoryConfigurationAsync(repositoryRoot);

            Assert.Equal(WorkspaceDiscoveryStatus.Invalid, plan.DiscoveryResult.Status);
            Assert.True(viewModel.HasInvalidRepositoryConfiguration);
            Assert.False(viewModel.ShowRepositoryConfigurationBanner);
            Assert.False(viewModel.CanCreateWorkspaceForDialog);
            Assert.Equal(".opencode/profile.yml", viewModel.LoadedRepositoryConfigurationPath);
            Assert.False(viewModel.AvailableServices.Single(item => item.Id == "postgres").IsSelected);
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
