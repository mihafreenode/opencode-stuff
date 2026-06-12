using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public static class WorkspacePathBuilder
{
    public static WorkspacePaths Build(string workspaceRootPath)
    {
        var mountsRoot = Path.Combine(workspaceRootPath, "mounts");
        var configPath = Path.Combine(mountsRoot, "config");

        return new WorkspacePaths
        {
            RootPath = workspaceRootPath,
            WorkspaceYamlPath = Path.Combine(workspaceRootPath, "workspace.yaml"),
            ComposePath = Path.Combine(workspaceRootPath, "compose.yaml"),
            EnvironmentFilePath = Path.Combine(workspaceRootPath, ".env"),
            MountsRootPath = mountsRoot,
            InboxPath = Path.Combine(mountsRoot, "inbox"),
            WorkspacePath = Path.Combine(mountsRoot, "workspace"),
            UserPath = Path.Combine(mountsRoot, "user"),
            HomePath = Path.Combine(mountsRoot, "home"),
            ConfigPath = configPath,
            ProvisionScriptPath = Path.Combine(configPath, "provision.sh"),
            StarshipConfigPath = Path.Combine(configPath, "starship.toml"),
            ShellInitScriptPath = Path.Combine(configPath, "opencode-shell-init.sh"),
            OpencodeWorkspaceShellPath = Path.Combine(configPath, "opencode-workspace-shell.sh"),
            ScreenConfigPath = Path.Combine(configPath, "screenrc"),
            AttachWrapperScriptPath = Path.Combine(workspaceRootPath, "attach-workspace.ps1"),
            TerminalDiagnosticsScriptPath = Path.Combine(workspaceRootPath, "terminal-diagnostics.ps1"),
            AppliedStatePath = Path.Combine(configPath, "applied-state.yaml"),
        };
    }

    public static string ToDockerVolumePath(string windowsPath) => windowsPath.Replace("\\", "/", StringComparison.Ordinal);

    public static string Slugify(string value)
    {
        var cleaned = new string(value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }

        return cleaned.Trim('-');
    }
}
