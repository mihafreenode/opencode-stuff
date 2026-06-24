using System.IO.Compression;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceBackupExportServiceTests
{
    [Fact]
    public async Task ExportAsync_CreatesArchiveWithExpectedDurableFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"workspace-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "history", "checkpoints"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        Directory.CreateDirectory(Path.Combine(root, "mounts", "config"));
        Directory.CreateDirectory(Path.Combine(root, "runtimes"));
        File.WriteAllText(Path.Combine(root, "workspace.yaml"), "workspace:\n  name: demo\n");
        File.WriteAllText(Path.Combine(root, "history", "timeline.yaml"), "events: []\n");
        File.WriteAllText(Path.Combine(root, "history", "checkpoints", "index.yaml"), "items: []\n");
        File.WriteAllText(Path.Combine(root, "docs", "notes.md"), "# notes\n");
        File.WriteAllText(Path.Combine(root, "mounts", "config", "provision.sh"), "#!/usr/bin/env bash\n");
        File.WriteAllText(Path.Combine(root, "runtimes", "default.yaml"), "runtime: docker\n");
        File.WriteAllText(Path.Combine(root, "README.md"), "workspace readme\n");

        var archivePath = Path.Combine(Path.GetTempPath(), $"workspace-backup-{Guid.NewGuid():N}.zip");
        try
        {
            var service = new WorkspaceBackupExportService();
            var result = await service.ExportAsync(CreateSnapshot(root), archivePath);

            Assert.True(File.Exists(archivePath));
            Assert.Equal(7, result.FileCount);
            using var archive = ZipFile.OpenRead(archivePath);
            var entries = archive.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal).ToList();
            Assert.Contains("workspace.yaml", entries);
            Assert.Contains("history/timeline.yaml", entries);
            Assert.Contains("history/checkpoints/index.yaml", entries);
            Assert.Contains("docs/notes.md", entries);
            Assert.Contains("mounts/config/provision.sh", entries);
            Assert.Contains("runtimes/default.yaml", entries);
            Assert.Contains("README.md", entries);
        }
        finally
        {
            TestFileSystem.DeleteDirectoryIfExists(root);
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
    }

    [Fact]
    public async Task ExportAsync_AppliesExclusionRulesAndWarnings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"workspace-backup-exclusions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, ".opencode", "local"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules", "pkg"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "mounts", "user"));
        File.WriteAllText(Path.Combine(root, "workspace.yaml"), "workspace:\n  name: demo\n");
        File.WriteAllText(Path.Combine(root, ".env"), "SECRET=1\n");
        File.WriteAllText(Path.Combine(root, ".opencode", "local", "runtime-state.yaml"), "state: local\n");
        File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "index.js"), "module.exports = {};\n");
        File.WriteAllText(Path.Combine(root, "bin", "output.txt"), "compiled\n");
        File.WriteAllText(Path.Combine(root, "mounts", "user", "session.log"), "runtime\n");

        var archivePath = Path.Combine(Path.GetTempPath(), $"workspace-backup-exclusions-{Guid.NewGuid():N}.zip");
        try
        {
            var service = new WorkspaceBackupExportService();
            var result = await service.ExportAsync(CreateSnapshot(root), archivePath);

            using var archive = ZipFile.OpenRead(archivePath);
            var entries = archive.Entries.Select(entry => entry.FullName).ToList();
            Assert.Contains("workspace.yaml", entries);
            Assert.DoesNotContain(".env", entries);
            Assert.DoesNotContain(".opencode/local/runtime-state.yaml", entries);
            Assert.DoesNotContain("node_modules/pkg/index.js", entries);
            Assert.DoesNotContain("bin/output.txt", entries);
            Assert.DoesNotContain("mounts/user/session.log", entries);
            Assert.Contains(result.ExcludedEntries, item => item.Path == ".env");
            Assert.Contains(result.Warnings, item => item.Contains(".env", StringComparison.Ordinal));
        }
        finally
        {
            TestFileSystem.DeleteDirectoryIfExists(root);
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
    }

    [Fact]
    public async Task ExportAsync_OmitsLargeFilesByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), $"workspace-backup-large-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "workspace.yaml"), "workspace:\n  name: demo\n");
        await File.WriteAllBytesAsync(Path.Combine(root, "large.bin"), new byte[2048]);

        var archivePath = Path.Combine(Path.GetTempPath(), $"workspace-backup-large-{Guid.NewGuid():N}.zip");
        try
        {
            var service = new WorkspaceBackupExportService(largeFileThresholdBytes: 1024);
            var result = await service.ExportAsync(CreateSnapshot(root), archivePath);

            using var archive = ZipFile.OpenRead(archivePath);
            var entries = archive.Entries.Select(entry => entry.FullName).ToList();
            Assert.Contains("workspace.yaml", entries);
            Assert.DoesNotContain("large.bin", entries);
            Assert.Contains(result.ExcludedEntries, item => item.Path == "large.bin");
            Assert.Contains(result.Warnings, item => item.Contains("large.bin", StringComparison.Ordinal));
        }
        finally
        {
            TestFileSystem.DeleteDirectoryIfExists(root);
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
    }

    private static WorkspaceSnapshot CreateSnapshot(string root)
        => new()
        {
            Record = new WorkspaceRecord
            {
                Name = "demo",
                RootPath = root,
                RepositoryPath = root,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "demo", Image = "ubuntu:24.04" },
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
            Session = new WorkspaceSessionSnapshot { SessionName = "demo", State = WorkspaceSessionState.Unknown },
            UpdateRequired = false,
        };
}
