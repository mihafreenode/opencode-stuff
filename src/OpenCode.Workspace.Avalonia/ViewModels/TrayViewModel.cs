using System.Collections.ObjectModel;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class TrayWorkspaceItemViewModel
{
    public string WorkspaceInstanceId { get; init; } = string.Empty;
    public string WorkspaceName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ActiveOperationId { get; init; } = string.Empty;
    public string ActiveOperationPhase { get; init; } = string.Empty;
    public string ProgressMessage { get; init; } = string.Empty;
}

public sealed class TrayViewModel : ObservableObject
{
    private readonly IWorkspaceLocalHostApplicationService _localHostService;
    private readonly IDesktopLifecycleCoordinator _lifecycleCoordinator;

    public TrayViewModel(IWorkspaceLocalHostApplicationService localHostService, IDesktopLifecycleCoordinator lifecycleCoordinator)
    {
        _localHostService = localHostService;
        _lifecycleCoordinator = lifecycleCoordinator;
        ShowMainWindowCommand = new RelayCommand(() => _lifecycleCoordinator.ShowMainWindow(), () => _lifecycleCoordinator.IsTrayAvailable);
        ExitApplicationCommand = new AsyncRelayCommand(() => _lifecycleCoordinator.RequestExitAsync(), () => _lifecycleCoordinator.IsTrayAvailable && !_lifecycleCoordinator.ExitRequested);
    }

    public ObservableCollection<TrayWorkspaceItemViewModel> Workspaces { get; } = [];
    public RelayCommand ShowMainWindowCommand { get; }
    public AsyncRelayCommand ExitApplicationCommand { get; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var workspaces = await _localHostService.GetWorkspaceInstancesAsync(cancellationToken);
        var operations = await _localHostService.GetOperationsAsync(cancellationToken);
        var mapped = workspaces.Select(item =>
        {
            var active = operations.FirstOrDefault(operation => item.ActiveOperationIds.Contains(operation.OperationId, StringComparer.OrdinalIgnoreCase));
            return new TrayWorkspaceItemViewModel
            {
                WorkspaceInstanceId = item.WorkspaceInstanceId,
                WorkspaceName = item.WorkspaceName,
                Status = item.Status,
                ActiveOperationId = active?.OperationId ?? string.Empty,
                ActiveOperationPhase = active?.CurrentPhase ?? string.Empty,
                ProgressMessage = active?.ProgressMessage ?? string.Empty,
            };
        }).ToArray();

        Workspaces.Clear();
        foreach (var item in mapped)
        {
            Workspaces.Add(item);
        }

        ExitApplicationCommand.RaiseCanExecuteChanged();
    }
}
