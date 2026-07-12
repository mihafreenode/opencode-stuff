using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public interface IWorkspaceImageBuilder
{
    Task EnsureImageAsync(WorkspaceDefinition definition, WorkspacePaths paths, GeneratedWorkspaceArtifacts artifacts, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default);
}
