using System.Diagnostics;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class DesktopShellService : IDesktopShellService
{
    private readonly WorkspaceOrchestrator _workspaceOrchestrator;
    private readonly WorkspaceDiscoveryReportService _workspaceDiscoveryReportService;
    private readonly WorkspaceRepository _workspaceRepository;
    private readonly WorkspaceTimelineService _timelineService;
    private readonly WorkspaceCheckpointService _checkpointService;

    public DesktopShellService(
        WorkspaceOrchestrator workspaceOrchestrator,
        WorkspaceRepository workspaceRepository,
        WorkspaceTimelineService timelineService,
        WorkspaceCheckpointService checkpointService)
    {
        _workspaceOrchestrator = workspaceOrchestrator;
        _workspaceDiscoveryReportService = new WorkspaceDiscoveryReportService(workspaceOrchestrator, workspaceRepository);
        _workspaceRepository = workspaceRepository;
        _timelineService = timelineService;
        _checkpointService = checkpointService;
    }

    public async Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(bool includeRuntimeInspection, CancellationToken cancellationToken = default)
        => await _workspaceDiscoveryReportService.LoadWorkspaceItemsAsync(includeRuntimeInspection, cancellationToken);

    public IReadOnlyList<WorkspaceReference> LoadWorkspaceReferences()
        => _workspaceOrchestrator.LoadWorkspaceRecords()
            .Select(record => new WorkspaceReference(record.Name, record.RootPath))
            .ToList();

    public WorkspaceTimeline LoadTimeline(string timelinePath) => _timelineService.Load(timelinePath);

    public WorkspaceCheckpointIndex LoadCheckpointIndex(string checkpointIndexPath) => _checkpointService.LoadIndex(checkpointIndexPath);

    public async Task<WorkspaceReprovisionResult> ReprovisionWorkspaceAsync(string rootPath, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true);
        var wasRunning = snapshot.RuntimeState == WorkspaceRuntimeState.Running;
        Action<CommandLogEntry>? log = entry => progress?.Invoke(entry.Message);

        try
        {
            progress?.Invoke("Preparing workspace");
            if (wasRunning)
            {
                await _workspaceOrchestrator.StopAsync(snapshot, log, cancellationToken);
                snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false);
            }

            progress?.Invoke("Generating files");
            progress?.Invoke("Provisioning runtime");
            await _workspaceOrchestrator.ProvisionAsync(snapshot, log, cancellationToken);

            progress?.Invoke("Validating compose");
            var validationSnapshot = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: false);
            await _workspaceOrchestrator.RecoverAsync(validationSnapshot, log, cancellationToken);

            var refreshed = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true);
            if (!wasRunning)
            {
                await _workspaceOrchestrator.StopAsync(refreshed, log, cancellationToken);
                refreshed = await _workspaceOrchestrator.LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection: true);
            }

            await PersistWorkspaceRecordAsync(refreshed, "Reprovision", "Workspace reprovisioned successfully.", true, cancellationToken, DateTimeOffset.UtcNow);
            _timelineService.Append(refreshed.Paths.TimelinePath, "reprovision-succeeded", "Reprovisioned workspace", "Regenerated runtime files and refreshed workspace state.");
            progress?.Invoke("Completed");

            return new WorkspaceReprovisionResult
            {
                Snapshot = refreshed,
                Succeeded = true,
                Message = "Workspace reprovisioned successfully.",
            };
        }
        catch (Exception exception)
        {
            await PersistWorkspaceRecordFailureAsync(snapshot.Record, exception.Message, cancellationToken);
            _timelineService.Append(snapshot.Paths.TimelinePath, "reprovision-failed", "Reprovision failed", exception.Message);
            progress?.Invoke("Failed");
            throw;
        }
    }

    public Task OpenPathAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            : OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open", $"\"{path}\"")
                : new ProcessStartInfo("xdg-open", $"\"{path}\"");

        startInfo.UseShellExecute = false;
        Process.Start(startInfo);
        return Task.CompletedTask;
    }

    private Task PersistWorkspaceRecordAsync(WorkspaceSnapshot snapshot, string operationName, string operationResult, bool succeeded, CancellationToken cancellationToken, DateTimeOffset? lastPreparedUtc = null)
    {
        var record = new WorkspaceRecord
        {
            Name = snapshot.Record.Name,
            RootPath = snapshot.Record.RootPath,
            RepositoryPath = snapshot.Record.RepositoryPath,
            ConfigurationPath = snapshot.Record.ConfigurationPath,
            SourceType = snapshot.Record.SourceType,
            ImportedFromExistingCheckout = snapshot.Record.ImportedFromExistingCheckout,
            OriginalDefaultBranch = snapshot.Record.OriginalDefaultBranch,
            SelectedWorkspaceBranch = snapshot.Record.SelectedWorkspaceBranch,
            RemoteOriginUrl = snapshot.Record.RemoteOriginUrl,
            CreatedUtc = snapshot.Record.CreatedUtc,
            LastOpenedUtc = snapshot.Record.LastOpenedUtc,
            LastPreparedUtc = lastPreparedUtc ?? snapshot.Record.LastPreparedUtc,
            OracleSoftwareNoticeShown = snapshot.Record.OracleSoftwareNoticeShown,
            LastOperationName = operationName,
            LastOperationResult = operationResult,
            LastOperationSucceeded = succeeded,
            LastOperationUtc = DateTimeOffset.UtcNow,
        };

        return _workspaceRepository.SaveAsync(record, cancellationToken);
    }

    private Task PersistWorkspaceRecordFailureAsync(WorkspaceRecord record, string errorMessage, CancellationToken cancellationToken)
    {
        var failureRecord = new WorkspaceRecord
        {
            Name = record.Name,
            RootPath = record.RootPath,
            RepositoryPath = record.RepositoryPath,
            ConfigurationPath = record.ConfigurationPath,
            SourceType = record.SourceType,
            ImportedFromExistingCheckout = record.ImportedFromExistingCheckout,
            OriginalDefaultBranch = record.OriginalDefaultBranch,
            SelectedWorkspaceBranch = record.SelectedWorkspaceBranch,
            RemoteOriginUrl = record.RemoteOriginUrl,
            CreatedUtc = record.CreatedUtc,
            LastOpenedUtc = record.LastOpenedUtc,
            LastPreparedUtc = record.LastPreparedUtc,
            OracleSoftwareNoticeShown = record.OracleSoftwareNoticeShown,
            LastOperationName = "Reprovision",
            LastOperationResult = errorMessage,
            LastOperationSucceeded = false,
            LastOperationUtc = DateTimeOffset.UtcNow,
        };

        return _workspaceRepository.SaveAsync(failureRecord, cancellationToken);
    }

}
