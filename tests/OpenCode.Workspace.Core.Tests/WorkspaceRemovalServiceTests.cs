using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceRemovalServiceTests
{
    [Fact]
    public async Task RemoveAsync_RemovesWorkspaceFromIndexOnly()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"oc-remove-index-{Guid.NewGuid():N}");
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"oc-remove-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDataRoot);
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var repository = new WorkspaceRepository(appDataRoot);
            repository.Save(new WorkspaceRecord
            {
                Name = "demo",
                RootPath = workspaceRoot,
                RepositoryPath = workspaceRoot,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            });

            var service = new WorkspaceRemovalService(repository);
            var result = await service.RemoveAsync(new WorkspaceRemovalRequest
            {
                WorkspaceName = "demo",
                WorkspaceRoot = workspaceRoot,
                DeleteWorkspaceFiles = false,
            });

            Assert.True(result.Succeeded);
            Assert.False(result.FilesDeleted);
            Assert.Empty(repository.LoadAll());
            Assert.True(Directory.Exists(workspaceRoot));
        }
        finally
        {
            if (Directory.Exists(appDataRoot)) Directory.Delete(appDataRoot, true);
            if (Directory.Exists(workspaceRoot)) Directory.Delete(workspaceRoot, true);
        }
    }

    [Fact]
    public async Task RemoveAsync_DoesNotDeleteFiles()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"oc-remove-files-{Guid.NewGuid():N}");
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"oc-remove-files-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDataRoot);
        Directory.CreateDirectory(workspaceRoot);
        File.WriteAllText(Path.Combine(workspaceRoot, "workspace.yaml"), "workspace: {}\n");

        try
        {
            var repository = new WorkspaceRepository(appDataRoot);
            repository.Save(new WorkspaceRecord
            {
                Name = "demo",
                RootPath = workspaceRoot,
                RepositoryPath = workspaceRoot,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            });

            var service = new WorkspaceRemovalService(repository);
            await service.RemoveAsync(new WorkspaceRemovalRequest
            {
                WorkspaceName = "demo",
                WorkspaceRoot = workspaceRoot,
                DeleteWorkspaceFiles = false,
            });

            Assert.True(File.Exists(Path.Combine(workspaceRoot, "workspace.yaml")));
        }
        finally
        {
            if (Directory.Exists(appDataRoot)) Directory.Delete(appDataRoot, true);
            if (Directory.Exists(workspaceRoot)) Directory.Delete(workspaceRoot, true);
        }
    }

    [Fact]
    public async Task RemoveAsync_MissingWorkspaceIsHandledAsWarning()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"oc-remove-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDataRoot);

        try
        {
            var repository = new WorkspaceRepository(appDataRoot);
            var service = new WorkspaceRemovalService(repository);
            var result = await service.RemoveAsync(new WorkspaceRemovalRequest
            {
                WorkspaceName = "missing",
                WorkspaceRoot = "C:\\temp\\missing-workspace",
                DeleteWorkspaceFiles = false,
            });

            Assert.True(result.Succeeded);
            Assert.Contains(result.Warnings, item => item.Contains("not present in the local index", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(appDataRoot)) Directory.Delete(appDataRoot, true);
        }
    }

    [Fact]
    public async Task RemoveAsync_DeleteRequestIsRejected()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"oc-remove-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDataRoot);

        try
        {
            var repository = new WorkspaceRepository(appDataRoot);
            var service = new WorkspaceRemovalService(repository);
            var result = await service.RemoveAsync(new WorkspaceRemovalRequest
            {
                WorkspaceName = "demo",
                WorkspaceRoot = "C:\\temp\\demo",
                DeleteWorkspaceFiles = true,
            });

            Assert.False(result.Succeeded);
            Assert.Contains("not implemented", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(appDataRoot)) Directory.Delete(appDataRoot, true);
        }
    }
}
