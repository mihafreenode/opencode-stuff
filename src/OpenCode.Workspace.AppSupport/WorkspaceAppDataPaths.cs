namespace OpenCode.Workspace.AppSupport;

public static class WorkspaceAppDataPaths
{
    public const string WorkspaceManagerFolderName = "OpenCode.Workspace.Manager";

    public static string GetWorkspaceManagerDataRoot()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), WorkspaceManagerFolderName);

    public static string GetWorkspaceIndexPath()
        => Path.Combine(GetWorkspaceManagerDataRoot(), "workspaces.json");
}
