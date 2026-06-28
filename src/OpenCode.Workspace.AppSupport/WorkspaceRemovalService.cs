using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.AppSupport;

public sealed class WorkspaceRemovalService
{
    private readonly WorkspaceRepository _workspaceRepository;

    public WorkspaceRemovalService(WorkspaceRepository workspaceRepository)
    {
        _workspaceRepository = workspaceRepository;
    }

    public Task<WorkspaceRemovalResult> RemoveAsync(WorkspaceRemovalRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.WorkspaceRoot))
        {
            return Task.FromResult(new WorkspaceRemovalResult
            {
                WorkspaceName = request.WorkspaceName,
                WorkspaceRoot = request.WorkspaceRoot,
                FilesDeleted = false,
                Warnings = [],
                Succeeded = false,
                FailureReason = "Workspace root path is required before removal can run.",
            });
        }

        if (request.DeleteWorkspaceFiles)
        {
            return Task.FromResult(new WorkspaceRemovalResult
            {
                WorkspaceName = request.WorkspaceName,
                WorkspaceRoot = request.WorkspaceRoot,
                FilesDeleted = false,
                Warnings = [],
                Succeeded = false,
                FailureReason = "Local file deletion is not implemented in the Avalonia removal workflow yet.",
            });
        }

        var warnings = new List<string>();
        var existing = _workspaceRepository.LoadAll()
            .FirstOrDefault(record => string.Equals(record.RootPath, request.WorkspaceRoot, StringComparison.OrdinalIgnoreCase)
                || string.Equals(WorkspaceRecordPathResolver.GetWorkspaceRoot(record), request.WorkspaceRoot, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            warnings.Add("Workspace was not present in the local index. The remove request is treated as already applied.");
        }
        else
        {
            _workspaceRepository.Delete(existing.RootPath);
        }

        return Task.FromResult(new WorkspaceRemovalResult
        {
            WorkspaceName = string.IsNullOrWhiteSpace(request.WorkspaceName) ? existing?.Name ?? Path.GetFileName(request.WorkspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : request.WorkspaceName,
            WorkspaceRoot = request.WorkspaceRoot,
            FilesDeleted = false,
            Warnings = warnings,
            Succeeded = true,
            FailureReason = string.Empty,
        });
    }
}

public sealed class WorkspaceRemovalRequest
{
    public required string WorkspaceName { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required bool DeleteWorkspaceFiles { get; init; }
}

public sealed class WorkspaceRemovalResult
{
    public required string WorkspaceName { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required bool FilesDeleted { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool Succeeded { get; init; }
    public required string FailureReason { get; init; }
}
