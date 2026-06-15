using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public static class WorkspacePathBuilder
{
    public static WorkspacePaths Build(string workspaceRootPath)
    {
        var mountsRoot = Path.Combine(workspaceRootPath, "mounts");
        var configPath = Path.Combine(mountsRoot, "config");
        var historyPath = Path.Combine(workspaceRootPath, "history");
        var checkpointsPath = Path.Combine(historyPath, "checkpoints");
        var artifactsPath = Path.Combine(workspaceRootPath, "artifacts");
        var runtimesPath = Path.Combine(workspaceRootPath, "runtimes");

        return new WorkspacePaths
        {
            RootPath = workspaceRootPath,
            GitIgnorePath = Path.Combine(workspaceRootPath, ".gitignore"),
            WorkspaceYamlPath = Path.Combine(workspaceRootPath, "workspace.yaml"),
            ComposePath = Path.Combine(workspaceRootPath, "compose.yaml"),
            EnvironmentFilePath = Path.Combine(workspaceRootPath, ".env"),
            MountsRootPath = mountsRoot,
            InboxPath = Path.Combine(mountsRoot, "inbox"),
            WorkspacePath = workspaceRootPath,
            UserPath = Path.Combine(mountsRoot, "user"),
            HomePath = Path.Combine(mountsRoot, "home"),
            ConfigPath = configPath,
            ProvisionScriptPath = Path.Combine(configPath, "provision.sh"),
            StarshipConfigPath = Path.Combine(configPath, "starship.toml"),
            ShellInitScriptPath = Path.Combine(configPath, "opencode-shell-init.sh"),
            OpencodeWorkspaceShellPath = Path.Combine(configPath, "opencode-workspace-shell.sh"),
            ScreenConfigPath = Path.Combine(configPath, "screenrc"),
            AttachWrapperScriptPath = Path.Combine(workspaceRootPath, "attach-workspace.ps1"),
            AttachDiagnosticsLogPath = Path.Combine(workspaceRootPath, "attach-diagnostics.log"),
            TerminalDiagnosticsScriptPath = Path.Combine(workspaceRootPath, "terminal-diagnostics.ps1"),
            AppliedStatePath = Path.Combine(configPath, "applied-state.yaml"),
            HistoryPath = historyPath,
            CheckpointsPath = checkpointsPath,
            CheckpointIndexPath = Path.Combine(checkpointsPath, "index.yaml"),
            TimelinePath = Path.Combine(historyPath, "timeline.yaml"),
            RuntimesPath = runtimesPath,
            DefaultRuntimePath = Path.Combine(runtimesPath, "default.yaml"),
            ArtifactsPath = artifactsPath,
            ArtifactRunsPath = Path.Combine(artifactsPath, "runs"),
            ArtifactIndexPath = Path.Combine(artifactsPath, "index.json"),
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
