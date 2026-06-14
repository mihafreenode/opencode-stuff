using System;
using System.IO;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Manager.Services;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class WorkspaceListItemViewModelTests
{
    [Fact]
    public void UnknownRuntimeWithoutFailure_IsReadyToStartNotError()
    {
        var tempRoot = CreateWorkspaceRoot();
        try
        {
            var viewModel = CreateViewModel(CreateSnapshot(tempRoot, WorkspaceRuntimeState.Unknown, lastOperationSucceeded: true, services: []));

            Assert.False(viewModel.HasError);
            Assert.Equal("Ready to Start", viewModel.StatusLabel);
            Assert.Equal("No add-on services enabled.", viewModel.ServicesStatusSummary);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void OracleService_IsLoadedIntoCardSummaryFromWorkspaceDefinition()
    {
        var tempRoot = CreateWorkspaceRoot();
        try
        {
            var viewModel = CreateViewModel(CreateSnapshot(tempRoot, WorkspaceRuntimeState.Unknown, lastOperationSucceeded: true, services: ["oracle-demo"]));

            Assert.Equal("oracle-demo", viewModel.ServicesSummary);
            Assert.Equal("oracle-demo: unknown", viewModel.ServicesStatusSummary);
            Assert.False(viewModel.HasError);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void FailedLastOperation_IsError()
    {
        var tempRoot = CreateWorkspaceRoot();
        try
        {
            var viewModel = CreateViewModel(CreateSnapshot(tempRoot, WorkspaceRuntimeState.Unknown, lastOperationSucceeded: false, services: ["oracle-demo"]));

            Assert.True(viewModel.HasError);
            Assert.Equal("Error", viewModel.StatusLabel);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static WorkspaceListItemViewModel CreateViewModel(WorkspaceSnapshot snapshot)
        => new(snapshot, new PoLocalizationService(Path.Combine(TestPaths.RepositoryRoot, "Localization"), "en"));

    private static WorkspaceSnapshot CreateSnapshot(string rootPath, WorkspaceRuntimeState runtimeState, bool? lastOperationSucceeded, string[] services)
    {
        var paths = new WorkspacePaths
        {
            RootPath = rootPath,
            GitIgnorePath = Path.Combine(rootPath, ".gitignore"),
            WorkspaceYamlPath = Path.Combine(rootPath, "workspace.yaml"),
            ComposePath = Path.Combine(rootPath, "compose.yaml"),
            EnvironmentFilePath = Path.Combine(rootPath, ".env"),
            MountsRootPath = Path.Combine(rootPath, "mounts"),
            InboxPath = Path.Combine(rootPath, "mounts", "inbox"),
            WorkspacePath = Path.Combine(rootPath, "mounts", "workspace"),
            UserPath = Path.Combine(rootPath, "mounts", "user"),
            HomePath = Path.Combine(rootPath, "mounts", "home"),
            ConfigPath = Path.Combine(rootPath, "mounts", "config"),
            ProvisionScriptPath = Path.Combine(rootPath, "mounts", "config", "provision.sh"),
            StarshipConfigPath = Path.Combine(rootPath, "mounts", "config", "starship.toml"),
            ShellInitScriptPath = Path.Combine(rootPath, "mounts", "config", "opencode-shell-init.sh"),
            OpencodeWorkspaceShellPath = Path.Combine(rootPath, "mounts", "config", "opencode-workspace-shell.sh"),
            ScreenConfigPath = Path.Combine(rootPath, "mounts", "config", "screenrc"),
            AttachWrapperScriptPath = Path.Combine(rootPath, "mounts", "config", "attach.ps1"),
            TerminalDiagnosticsScriptPath = Path.Combine(rootPath, "mounts", "config", "terminal-diagnostics.ps1"),
            AppliedStatePath = Path.Combine(rootPath, "mounts", "config", "applied-state.yaml"),
            HistoryPath = Path.Combine(rootPath, "history"),
            CheckpointsPath = Path.Combine(rootPath, "history", "checkpoints"),
            CheckpointIndexPath = Path.Combine(rootPath, "history", "checkpoints", "index.json"),
            TimelinePath = Path.Combine(rootPath, "history", "timeline.json"),
            RuntimesPath = Path.Combine(rootPath, "runtimes"),
            DefaultRuntimePath = Path.Combine(rootPath, "runtimes", "default.yaml"),
            ArtifactsPath = Path.Combine(rootPath, "artifacts"),
            ArtifactRunsPath = Path.Combine(rootPath, "artifacts", "runs"),
            ArtifactIndexPath = Path.Combine(rootPath, "artifacts", "index.json"),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(paths.ProvisionScriptPath)!);
        File.WriteAllText(paths.WorkspaceYamlPath, "workspace: {}\n");
        File.WriteAllText(paths.ComposePath, "services: {}\n");
        File.WriteAllText(paths.ProvisionScriptPath, "#!/bin/bash\n");

        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = "demo",
                RootPath = rootPath,
                RepositoryPath = rootPath,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
                LastOperationSucceeded = lastOperationSucceeded,
                LastOperationResult = "Workspace created.",
            },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "demo", Image = "ubuntu:24.04" },
                Features = ["core"],
                Services = services.ToList(),
            },
            Paths = paths,
            RuntimeState = runtimeState,
            Safety = new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.PartiallyProtected,
                Headline = "Ready",
                Message = "Ready",
                LocalRecovery = new WorkspaceLocalRecoverySnapshot
                {
                    IsGitInitialized = true,
                    LatestSavePointUtc = null,
                    LatestCheckpointUtc = null,
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
                    LastSuccessfulPublishUtc = null,
                    NeedsReviewBeforePublish = false,
                    IsOnProtectedBranch = false,
                },
                IgnorePolicy = new WorkspaceIgnorePolicyReview(),
                AdvancedGit = new WorkspaceAdvancedGitSnapshot
                {
                    CurrentBranch = "users/test/demo",
                    StatusSummary = "clean",
                    PatchExportSupported = true,
                },
            },
            Session = new WorkspaceSessionSnapshot { SessionName = "demo", State = WorkspaceSessionState.Unknown },
            UpdateRequired = false,
        };
    }

    private static string CreateWorkspaceRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ocwm-card-status-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
