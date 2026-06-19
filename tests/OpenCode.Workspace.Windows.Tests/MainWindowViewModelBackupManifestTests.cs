using System;
using System.IO;
using System.IO.Compression;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Manager.Services;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class MainWindowViewModelBackupManifestTests
{
    [Fact]
    public void WriteBackupManifest_CreatesSiblingManifest_AndArchiveEntry()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"ocwm-backup-manifest-{Guid.NewGuid():N}");
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"ocwm-backup-manifest-appdata-{Guid.NewGuid():N}");
        var exportRoot = Path.Combine(Path.GetTempPath(), $"ocwm-backup-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(exportRoot);

        try
        {
            var snapshot = CreateSnapshot(workspaceRoot);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "docs"));
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "mounts", "config"));
            File.WriteAllText(Path.Combine(workspaceRoot, "workspace.yaml"), "workspace:\n  name: backup-demo\n");
            File.WriteAllText(Path.Combine(workspaceRoot, "docs", "notes.md"), "# notes\n");
            File.WriteAllText(Path.Combine(workspaceRoot, "compose.yaml"), "services: {}\n");
            File.WriteAllText(Path.Combine(workspaceRoot, "mounts", "config", "provision.sh"), "#!/usr/bin/env bash\n");

            var bootstrapper = new AppBootstrapper();
            var viewModel = bootstrapper.CreateMainWindowViewModel(TestPaths.RepositoryRoot, appDataRoot, "en");
            var manifestPath = viewModel.WriteBackupManifest(snapshot, exportRoot, "backup-demo-20260618-100000", DateTimeOffset.Parse("2026-06-18T10:00:00Z"));
            var archivePath = Path.Combine(exportRoot, "backup-demo-20260618-100000.zip");

            ZipFile.CreateFromDirectory(workspaceRoot, archivePath, CompressionLevel.Fastest, includeBaseDirectory: false);
            MainWindowViewModel.AddBackupManifestToArchive(archivePath, File.ReadAllText(manifestPath));

            Assert.True(File.Exists(manifestPath));
            var manifest = File.ReadAllText(manifestPath);
            Assert.Contains("ownershipNotes:", manifest);
            Assert.Contains("durableAssetGroups:", manifest);
            Assert.Contains("generatedAssetGroups:", manifest);
            Assert.Contains("ephemeralAssetGroups:", manifest);
            Assert.Contains("workspace.yaml", manifest);

            using var archive = ZipFile.OpenRead(archivePath);
            var entry = archive.GetEntry("backup-manifest.yaml");
            Assert.NotNull(entry);
            using var reader = new StreamReader(entry!.Open());
            var entryText = reader.ReadToEnd();
            Assert.Contains("workspaceName: backup-demo", entryText);
            Assert.Contains("warning:", entryText);
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

            if (Directory.Exists(exportRoot))
            {
                Directory.Delete(exportRoot, recursive: true);
            }
        }
    }

    private static WorkspaceSnapshot CreateSnapshot(string rootPath)
    {
        var paths = WorkspacePathBuilder.Build(rootPath);
        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = "backup-demo",
                RootPath = rootPath,
                RepositoryPath = rootPath,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "backup-demo", Image = "ubuntu:24.04" },
                Features = ["core"],
            },
            Paths = paths,
            ConfigurationPath = "workspace.yaml",
            RuntimeState = WorkspaceRuntimeState.Stopped,
            Safety = new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.PartiallyProtected,
                Headline = "Ready",
                Message = "Ready",
                LocalRecovery = new WorkspaceLocalRecoverySnapshot(),
                Backup = new WorkspaceBackupSnapshot(),
                IgnorePolicy = new WorkspaceIgnorePolicyReview(),
                AdvancedGit = new WorkspaceAdvancedGitSnapshot(),
            },
            Session = new WorkspaceSessionSnapshot { SessionName = "backup-demo", State = WorkspaceSessionState.Unknown },
            UpdateRequired = false,
        };
    }
}
