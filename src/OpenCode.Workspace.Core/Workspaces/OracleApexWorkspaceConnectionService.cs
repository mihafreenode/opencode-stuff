using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexWorkspaceConnectionService
{
    private readonly IReadOnlyList<IOracleApexWorkspaceConnectionProvider> _providers;

    public OracleApexWorkspaceConnectionService(IEnumerable<IOracleApexWorkspaceConnectionProvider> providers)
    {
        _providers = providers.ToList();
    }

    public Task<OracleApexApplicationDiscoveryResult> DiscoverApplicationsAsync(OracleApexApplicationDiscoveryRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(provider => provider.DiscoverApplicationsAsync(request, cancellationToken), request.Snapshot);

    public Task<OracleApexConnectExistingApplicationResult> ConnectExistingApplicationAsync(OracleApexConnectExistingApplicationRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(provider => provider.ConnectExistingApplicationAsync(request, cancellationToken), request.Snapshot);

    private Task<T> ExecuteAsync<T>(Func<IOracleApexWorkspaceConnectionProvider, Task<T>> action, WorkspaceSnapshot snapshot)
    {
        var provider = _providers.FirstOrDefault(item => item.CanHandle(snapshot.Definition));
        if (provider is null)
        {
            throw new InvalidOperationException("Oracle APEX application connection is not available for this workspace.");
        }

        return action(provider);
    }
}
