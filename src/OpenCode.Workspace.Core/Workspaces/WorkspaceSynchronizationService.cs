using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceSynchronizationService
{
    private readonly IReadOnlyList<IWorkspaceSynchronizationProvider> _providers;

    public WorkspaceSynchronizationService(IEnumerable<IWorkspaceSynchronizationProvider> providers)
    {
        _providers = providers.ToList();
    }

    public Task<WorkspaceSynchronizationStatusResult> GetStatusAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(provider => provider.GetStatusAsync(request, cancellationToken), request);

    public Task<WorkspaceSynchronizationOperationResult> ValidateAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(provider => provider.ValidateAsync(request, cancellationToken), request);

    public Task<WorkspaceSynchronizationOperationResult> ExportAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(provider => provider.ExportAsync(request, cancellationToken), request);

    public Task<WorkspaceSynchronizationOperationResult> ImportAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(provider => provider.ImportAsync(request, cancellationToken), request);

    public Task<WorkspaceSynchronizationDiffResult> DiffAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(provider => provider.DiffAsync(request, cancellationToken), request);

    public Task<WorkspaceSynchronizationOperationResult> PullAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(provider => provider.PullAsync(request, cancellationToken), request);

    public Task<WorkspaceSynchronizationOperationResult> PushAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(provider => provider.PushAsync(request, cancellationToken), request);

    private Task<T> ExecuteAsync<T>(Func<IWorkspaceSynchronizationProvider, Task<T>> action, WorkspaceSynchronizationRequest request)
    {
        var provider = _providers.FirstOrDefault(item => item.CanHandle(request.Snapshot.Definition));
        if (provider is not null)
        {
            return action(provider);
        }

        return Task.FromResult(CreateUnsupportedResult<T>(request.Snapshot));
    }

    private static T CreateUnsupportedResult<T>(WorkspaceSnapshot snapshot)
    {
        var unsupportedSnapshot = new WorkspaceSynchronizationSnapshot
        {
            IsSupported = false,
            State = WorkspaceSynchronizationState.Unknown,
            Summary = "Workspace synchronization is not configured for this workspace.",
        };

        object result = typeof(T) == typeof(WorkspaceSynchronizationStatusResult)
            ? new WorkspaceSynchronizationStatusResult { Snapshot = unsupportedSnapshot }
            : typeof(T) == typeof(WorkspaceSynchronizationDiffResult)
                ? new WorkspaceSynchronizationDiffResult { Snapshot = unsupportedSnapshot, Summary = unsupportedSnapshot.Summary, DiffText = string.Empty }
                : new WorkspaceSynchronizationOperationResult { Snapshot = unsupportedSnapshot, Message = unsupportedSnapshot.Summary };

        return (T)result;
    }
}
