namespace OpenCode.Workspace.AppSupport;

public sealed record WorkspaceLoadFailure(string DisplayName, string RootPath, string Reason);
