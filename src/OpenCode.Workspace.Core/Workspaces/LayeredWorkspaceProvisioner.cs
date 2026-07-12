using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class LayeredWorkspaceProvisioner : IWorkspaceProvisioner
{
    private readonly IWorkspaceInitializer _initializer;
    private readonly IOracleWorkspaceProvisioner _oracleProvisioner;

    public LayeredWorkspaceProvisioner(IWorkspaceInitializer initializer, IOracleWorkspaceProvisioner oracleProvisioner)
    {
        _initializer = initializer;
        _oracleProvisioner = oracleProvisioner;
    }

    public async Task ProvisionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        await _initializer.InitializeAsync(snapshot, log, cancellationToken);
        await _oracleProvisioner.ProvisionAsync(snapshot, log, cancellationToken);
        await _initializer.ValidateAsync(snapshot, log, cancellationToken);
    }
}
