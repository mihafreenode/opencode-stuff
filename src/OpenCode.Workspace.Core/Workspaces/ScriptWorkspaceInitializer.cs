using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class ScriptWorkspaceInitializer : IWorkspaceInitializer
{
    private readonly IContainerRuntime _containerRuntime;

    public ScriptWorkspaceInitializer(IContainerRuntime containerRuntime)
    {
        _containerRuntime = containerRuntime;
    }

    public Task InitializeAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunScriptAsync(snapshot, "/opt/opencode-workspace/config/workspace-init.sh", "Initializing Workspace", log, cancellationToken);

    public Task ValidateAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunScriptAsync(snapshot, "/opt/opencode-workspace/config/workspace-validate.sh", "Final Validation", log, cancellationToken);

    private async Task RunScriptAsync(WorkspaceSnapshot snapshot, string scriptPath, string phase, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        log?.Invoke(new CommandLogEntry { Source = "app", Message = phase });
        var result = await _containerRuntime.RunSimpleDockerCommandAsync(
        [
            "exec",
            _containerRuntime.GetWorkspaceContainerName(snapshot.Definition),
            "bash",
            scriptPath,
        ], log, cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"{phase} failed.{Environment.NewLine}{result.StandardError}{Environment.NewLine}{result.StandardOutput}".Trim());
        }
    }
}
