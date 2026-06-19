using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceAssetClassificationServiceTests
{
    private readonly WorkspaceAssetClassificationService _service = new();

    [Theory]
    [InlineData("workspace.yaml", false, WorkspaceAssetClass.Durable)]
    [InlineData("docs/notes.md", false, WorkspaceAssetClass.Durable)]
    [InlineData("AGENTS.md", false, WorkspaceAssetClass.Durable)]
    [InlineData("compose.yaml", false, WorkspaceAssetClass.Generated)]
    [InlineData(".opencode/local/runtime-state.yaml", false, WorkspaceAssetClass.Ephemeral)]
    [InlineData("mounts/config/provision.sh", false, WorkspaceAssetClass.Generated)]
    [InlineData(".git/HEAD", false, WorkspaceAssetClass.Ephemeral)]
    [InlineData("mounts/user/session.log", false, WorkspaceAssetClass.Ephemeral)]
    [InlineData("attach-diagnostics.log", false, WorkspaceAssetClass.Ephemeral)]
    public void Classify_RepresentativePaths_ReturnsExpectedAssetClass(string path, bool isDirectory, WorkspaceAssetClass expected)
    {
        var classification = _service.Classify(path, isDirectory);

        Assert.Equal(expected, classification.AssetClass);
    }

    [Fact]
    public void BuildBackupManifest_IncludesOwnershipNotesAndRepresentativeItems()
    {
        var root = Path.Combine(Path.GetTempPath(), $"asset-manifest-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            Directory.CreateDirectory(Path.Combine(root, "mounts", "config"));
            Directory.CreateDirectory(Path.Combine(root, "mounts", "user"));
            File.WriteAllText(Path.Combine(root, "workspace.yaml"), "workspace:\n  name: demo\n");
            File.WriteAllText(Path.Combine(root, "compose.yaml"), "services: {}\n");
            File.WriteAllText(Path.Combine(root, "docs", "notes.md"), "# durable\n");
            File.WriteAllText(Path.Combine(root, "mounts", "config", "provision.sh"), "#!/usr/bin/env bash\n");
            File.WriteAllText(Path.Combine(root, "mounts", "user", "shell-history.txt"), "runtime\n");

            var snapshot = new WorkspaceSnapshot
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
                Safety = EmptySafety(),
                Session = new WorkspaceSessionSnapshot { SessionName = "demo", State = WorkspaceSessionState.Unknown },
                UpdateRequired = false,
            };

            var manifest = _service.BuildBackupManifest(snapshot, DateTimeOffset.Parse("2026-06-18T10:00:00Z"));

            Assert.Contains("workspace.yaml", manifest.SourceOfTruthLocations);
            Assert.Contains(manifest.OwnershipNotes, note => note.Contains("User work is the durable asset", StringComparison.Ordinal));
            Assert.Contains("full workspace snapshot", manifest.Warning, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(manifest.Items, item => item.Path == "workspace.yaml" && item.AssetClass == WorkspaceAssetClass.Durable);
            Assert.Contains(manifest.Items, item => item.Path == "compose.yaml" && item.AssetClass == WorkspaceAssetClass.Generated);
            Assert.Contains(manifest.Items, item => item.Path == "mounts/user/shell-history.txt" && item.AssetClass == WorkspaceAssetClass.Ephemeral);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                TestFileSystem.DeleteDirectoryIfExists(root);
            }
        }
    }

    private static WorkspaceSafetySnapshot EmptySafety()
    {
        return new WorkspaceSafetySnapshot
        {
            OverallStatus = WorkspaceSafetyLevel.PartiallyProtected,
            Headline = "Ready",
            Message = "Ready",
            LocalRecovery = new WorkspaceLocalRecoverySnapshot(),
            Backup = new WorkspaceBackupSnapshot(),
            IgnorePolicy = new WorkspaceIgnorePolicyReview(),
            AdvancedGit = new WorkspaceAdvancedGitSnapshot(),
        };
    }
}
