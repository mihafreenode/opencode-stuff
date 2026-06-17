namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceEnvironmentConflictException : InvalidOperationException
{
    public WorkspaceEnvironmentConflictException(string message)
        : base(message)
    {
    }
}
