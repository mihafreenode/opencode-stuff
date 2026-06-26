namespace OpenCode.Workspace.AppSupport;

public static class WorkspaceAppDataPaths
{
    // Keep the historical folder name so existing installs keep their local
    // workspace index and state until a dedicated migration is implemented.
    public const string WorkspaceManagerFolderName = "OpenCode.Workspace.Manager";

    public static string GetWorkspaceManagerDataRoot()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), WorkspaceManagerFolderName);

    public static string GetWorkspaceIndexPath()
        => Path.Combine(GetWorkspaceManagerDataRoot(), "workspaces.json");
}
