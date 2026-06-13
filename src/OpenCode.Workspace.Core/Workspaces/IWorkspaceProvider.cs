using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public interface IWorkspaceProvider
{
    // The UI talks in workspace terms while the provider translates those actions
    // into the persistence engine's concrete behavior. Git is the V1 provider, but
    // the desktop layer should not depend on Git-specific services directly.
    string Type { get; }

    Task InitializeWorkspaceAsync(WorkspacePaths paths, WorkspaceDefinition definition, bool createInitialSavePoint, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default);

    Task<WorkspaceGitState> GetGitStateAsync(WorkspacePaths paths, WorkspaceDefinition definition, CancellationToken cancellationToken = default);

    Task<bool> CreateSavePointAsync(WorkspacePaths paths, WorkspaceDefinition definition, string message, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default);

    Task<WorkspacePublishReview> PublishAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default);

    Task<WorkspacePublishReview> UpdateWorkingCopyAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default);

    Task<WorkspacePublishReview> PublishToReviewWorkingCopyAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default);

    Task<string> ExportPatchAsync(WorkspacePaths paths, WorkspaceDefinition definition, string outputPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default);
}
