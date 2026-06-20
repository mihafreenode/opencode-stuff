using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Manager.Services;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class MainWindowViewModelRecoveryActionTests
{
    [Fact]
    public void ErrorWorkspace_ShowsRecoverAndStartActions()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"ocwm-recover-ui-{Guid.NewGuid():N}");
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"ocwm-recover-ui-appdata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var bootstrapper = new AppBootstrapper();
            var viewModel = bootstrapper.CreateMainWindowViewModel(
                TestPaths.RepositoryRoot,
                appDataRoot,
                "en");

            var localization = new PoLocalizationService(Path.Combine(TestPaths.RepositoryRoot, "Localization"), "en");
            viewModel.SelectedWorkspace = new WorkspaceListItemViewModel(CreateErrorSnapshot(workspaceRoot), localization);

            Assert.True(viewModel.ShowRecoverWorkspaceAction);
            Assert.True(viewModel.ShowViewWorkspaceErrorAction);
            Assert.True(viewModel.ShowStartWorkspaceAction);
            Assert.Equal("Repair Runtime", viewModel.RecoverWorkspaceLabel);
            Assert.Equal("Open Workspace", viewModel.SelectedPrimaryActionLabel);
            Assert.Equal("Start", viewModel.StartWorkspaceLabel);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(appDataRoot))
            {
                Directory.Delete(appDataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HandleCreateWorkspaceWarningAsync_PersistsWorkspaceAndSetsWarningStatus()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"ocwm-create-warning-{Guid.NewGuid():N}");
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"ocwm-create-warning-appdata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var bootstrapper = new AppBootstrapper();
            var viewModel = bootstrapper.CreateMainWindowViewModel(TestPaths.RepositoryRoot, appDataRoot, "en");
            var snapshot = CreateErrorSnapshot(workspaceRoot);

            await viewModel.HandleCreateWorkspaceWarningAsync(snapshot, "terminal profile setup", new InvalidOperationException("Windows Terminal fragments folder is unavailable."), CancellationToken.None);

            var repository = new WorkspaceRepository(appDataRoot);
            var saved = Assert.Single(repository.LoadAll());
            Assert.Equal(workspaceRoot, saved.RootPath);
            Assert.True(saved.LastOperationSucceeded);
            Assert.Contains("Workspace created with warnings", saved.LastOperationResult, StringComparison.Ordinal);
            Assert.Contains("created with warnings", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(appDataRoot))
            {
                Directory.Delete(appDataRoot, recursive: true);
            }
        }
    }

    private static WorkspaceSnapshot CreateErrorSnapshot(string rootPath)
    {
        var paths = WorkspacePathBuilder.Build(rootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.ProvisionScriptPath)!);
        File.WriteAllText(paths.WorkspaceYamlPath, "workspace: {}\n");
        File.WriteAllText(paths.ComposePath, "services: {}\n");
        File.WriteAllText(paths.ProvisionScriptPath, "#!/bin/bash\n");

        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = "Odip Analiza",
                RootPath = rootPath,
                RepositoryPath = rootPath,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
                LastOperationSucceeded = false,
                LastOperationResult = "services.workspace.depends_on must be a array",
            },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Id = "odip-analiza", Name = "Odip Analiza", Image = "ubuntu:24.04" },
                Features = ["core", "document-processing", "ocr-processing", "spellcheck"],
                Services = [],
                Skills = [],
                Mcp = [],
            },
            Paths = paths,
            ConfigurationPath = paths.WorkspaceYamlRelativePath,
            RuntimeState = WorkspaceRuntimeState.Stopped,
            Safety = new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.PartiallyProtected,
                Headline = "Ready",
                Message = "Ready",
                LocalRecovery = new WorkspaceLocalRecoverySnapshot
                {
                    IsGitInitialized = true,
                    HasUncommittedChanges = false,
                    UncommittedChangeCount = 0,
                    UntrackedFileCount = 0,
                    AreUntrackedFilesProtected = true,
                },
                Backup = new WorkspaceBackupSnapshot
                {
                    HasRemoteConfigured = false,
                    HasUnpublishedSavePoints = false,
                    IsCurrentWorkingCopyPublished = false,
                    NeedsReviewBeforePublish = false,
                    IsOnProtectedBranch = false,
                },
                IgnorePolicy = new WorkspaceIgnorePolicyReview(),
                AdvancedGit = new WorkspaceAdvancedGitSnapshot
                {
                    CurrentBranch = "users/test/odip-analiza",
                    StatusSummary = "clean",
                    PatchExportSupported = true,
                },
            },
            Session = new WorkspaceSessionSnapshot { SessionName = "odip-analiza", State = WorkspaceSessionState.Unknown },
            UpdateRequired = false,
        };
    }
}
