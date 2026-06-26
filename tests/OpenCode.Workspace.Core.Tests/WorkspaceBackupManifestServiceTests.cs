using System.IO.Compression;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceBackupManifestServiceTests
{
    [Fact]
    public void WriteAndEmbedManifest_CreatesSiblingManifestAndArchiveEntry()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"backup-manifest-workspace-{Guid.NewGuid():N}");
        var exportRoot = Path.Combine(Path.GetTempPath(), $"backup-manifest-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "docs"));
        Directory.CreateDirectory(exportRoot);
        File.WriteAllText(Path.Combine(workspaceRoot, "workspace.yaml"), "workspace:\n  name: backup-demo\n");
        File.WriteAllText(Path.Combine(workspaceRoot, "docs", "notes.md"), "# notes\n");

        var archivePath = Path.Combine(exportRoot, "backup-demo.zip");
        try
        {
            ZipFile.CreateFromDirectory(workspaceRoot, archivePath, CompressionLevel.Fastest, includeBaseDirectory: false);
            var snapshot = CreateSnapshot(workspaceRoot);
            var service = new WorkspaceBackupManifestService();

            var result = service.WriteAndEmbedManifest(
                snapshot,
                new WorkspaceBackupExportResult
                {
                    ArchivePath = archivePath,
                    FileCount = 2,
                    ArchiveSizeBytes = new FileInfo(archivePath).Length,
                    IncludedEntries = [new WorkspaceBackupEntry { Path = "workspace.yaml", Reason = "included", SizeBytes = 20 }],
                    ExcludedEntries = [],
                    Warnings = ["warning: full snapshot"],
                },
                archivePath,
                DateTimeOffset.Parse("2026-06-18T10:00:00Z"));

            Assert.True(File.Exists(result.ManifestPath));
            var manifest = File.ReadAllText(result.ManifestPath);
            Assert.Contains("archiveFileName: backup-demo.zip", manifest);
            Assert.Contains("includedFileCount: 2", manifest);
            Assert.Contains("warnings:", manifest);

            using var archive = ZipFile.OpenRead(archivePath);
            var entry = archive.GetEntry("backup-manifest.yaml");
            Assert.NotNull(entry);
        }
        finally
        {
            TestFileSystem.DeleteDirectoryIfExists(workspaceRoot);
            TestFileSystem.DeleteDirectoryIfExists(exportRoot);
        }
    }

    private static WorkspaceSnapshot CreateSnapshot(string root)
        => new()
        {
            Record = new WorkspaceRecord
            {
                Name = "backup-demo",
                RootPath = root,
                RepositoryPath = root,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "backup-demo", Image = "ubuntu:24.04" },
                Features = ["core"],
            },
            Paths = WorkspacePathBuilder.Build(root),
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
