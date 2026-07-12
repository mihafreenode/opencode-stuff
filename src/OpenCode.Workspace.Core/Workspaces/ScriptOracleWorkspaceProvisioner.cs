using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class ScriptOracleWorkspaceProvisioner : IOracleWorkspaceProvisioner
{
    private readonly IContainerRuntime _containerRuntime;

    public ScriptOracleWorkspaceProvisioner(IContainerRuntime containerRuntime)
    {
        _containerRuntime = containerRuntime;
    }

    public async Task ProvisionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        if (!OracleWorkspaceFamily.IsOracleWorkspace(snapshot.Definition))
        {
            return;
        }

        log?.Invoke(new CommandLogEntry { Source = "app", Message = "Provisioning Oracle" });
        var result = await _containerRuntime.RunSimpleDockerCommandAsync(
        [
            "exec",
            _containerRuntime.GetWorkspaceContainerName(snapshot.Definition),
            "bash",
            "/opt/opencode-workspace/config/oracle-provision.sh",
        ], log, cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Oracle workspace provisioning failed.{Environment.NewLine}{result.StandardError}{Environment.NewLine}{result.StandardOutput}".Trim());
        }
    }
}
