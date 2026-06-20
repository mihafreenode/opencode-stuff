using System.Diagnostics;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class DesktopShellService : IDesktopShellService
{
    private readonly WorkspaceOrchestrator _workspaceOrchestrator;
    private readonly WorkspaceTimelineService _timelineService;
    private readonly WorkspaceCheckpointService _checkpointService;

    public DesktopShellService(
        WorkspaceOrchestrator workspaceOrchestrator,
        WorkspaceTimelineService timelineService,
        WorkspaceCheckpointService checkpointService)
    {
        _workspaceOrchestrator = workspaceOrchestrator;
        _timelineService = timelineService;
        _checkpointService = checkpointService;
    }

    public async Task<IReadOnlyList<WorkspaceSnapshot>> LoadWorkspaceSnapshotsAsync(bool includeRuntimeInspection, CancellationToken cancellationToken = default)
    {
        var snapshots = new List<WorkspaceSnapshot>();
        foreach (var record in _workspaceOrchestrator.LoadWorkspaceRecords())
        {
            var configurationPath = WorkspacePathBuilder.NormalizeConfigurationRelativePath(record.ConfigurationPath);
            if (!File.Exists(Path.Combine(record.RootPath, configurationPath.Replace('/', Path.DirectorySeparatorChar))))
            {
                continue;
            }

            try
            {
                snapshots.Add(await _workspaceOrchestrator.LoadSnapshotAsync(record.RootPath, cancellationToken, includeRuntimeInspection));
            }
            catch
            {
            }
        }

        return snapshots;
    }

    public IReadOnlyList<WorkspaceReference> LoadWorkspaceReferences()
        => _workspaceOrchestrator.LoadWorkspaceRecords()
            .Select(record => new WorkspaceReference(record.Name, record.RootPath))
            .ToList();

    public WorkspaceTimeline LoadTimeline(string timelinePath) => _timelineService.Load(timelinePath);

    public WorkspaceCheckpointIndex LoadCheckpointIndex(string checkpointIndexPath) => _checkpointService.LoadIndex(checkpointIndexPath);

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
}
