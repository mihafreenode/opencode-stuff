using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.AppSupport;

public sealed class WorkspaceDiscoveryReportService
{
    private readonly WorkspaceOrchestrator _workspaceOrchestrator;
    private readonly WorkspaceRepository _workspaceRepository;

    public WorkspaceDiscoveryReportService(WorkspaceOrchestrator workspaceOrchestrator, WorkspaceRepository workspaceRepository)
    {
        _workspaceOrchestrator = workspaceOrchestrator;
        _workspaceRepository = workspaceRepository;
    }

    public async Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(bool includeRuntimeInspection, CancellationToken cancellationToken = default)
    {
        var items = new List<WorkspaceShellItem>();
        var failures = new List<WorkspaceLoadFailure>();
        var records = _workspaceOrchestrator.LoadWorkspaceRecords()
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var record in records)
        {
            var configurationPath = WorkspacePathBuilder.NormalizeConfigurationRelativePath(record.ConfigurationPath);
            if (!File.Exists(Path.Combine(record.RootPath, configurationPath.Replace('/', Path.DirectorySeparatorChar))))
            {
                var reason = $"Configuration file '{configurationPath}' was not found.";
                items.Add(new WorkspaceShellItem { Record = record, ErrorMessage = reason });
                failures.Add(new WorkspaceLoadFailure(GetDisplayName(record), record.RootPath, reason));
                continue;
            }

            try
            {
                var snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(record.RootPath, cancellationToken, includeRuntimeInspection);
                items.Add(new WorkspaceShellItem { Record = record, Snapshot = snapshot });
            }
            catch (Exception exception)
            {
                items.Add(new WorkspaceShellItem { Record = record, ErrorMessage = exception.Message });
                failures.Add(new WorkspaceLoadFailure(GetDisplayName(record), record.RootPath, exception.Message));
            }
        }

        var result = new WorkspaceLoadResult
        {
            Items = items,
            Report = new WorkspaceLoadReport
            {
                AppDataRoot = Path.GetDirectoryName(_workspaceRepository.IndexFilePath) ?? string.Empty,
                IndexFilePath = _workspaceRepository.IndexFilePath,
                IndexFileExists = File.Exists(_workspaceRepository.IndexFilePath),
                RawRecordCount = records.Count,
                SnapshotAttemptCount = records.Count,
                SnapshotCount = items.Count(item => item.HasSnapshot),
                Failures = failures,
                ItemsReturnedCount = items.Count,
            },
        };

        WriteDiscoveryLog(result.Report);
        return result;
    }

    private static string GetDisplayName(WorkspaceRecord record)
        => string.IsNullOrWhiteSpace(record.Name) ? record.RootPath : record.Name;

    private static void WriteDiscoveryLog(WorkspaceLoadReport report)
    {
#if !DEBUG
        return;
#endif
        var appDataRoot = report.AppDataRoot;
        if (string.IsNullOrWhiteSpace(appDataRoot))
        {
            return;
        }

        Directory.CreateDirectory(appDataRoot);
        var logPath = Path.Combine(appDataRoot, "avalonia-workspace-discovery.log");
        var lines = new List<string>
        {
            $"[{DateTimeOffset.Now:O}] Avalonia workspace discovery",
            $"App data root: {report.AppDataRoot}",
            $"Index path: {report.IndexFilePath}",
            $"Index file exists: {report.IndexFileExists}",
            $"Raw records loaded: {report.RawRecordCount}",
            $"Snapshots attempted: {report.SnapshotAttemptCount}",
            $"Snapshots loaded: {report.SnapshotCount}",
            $"Failures: {report.FailureCount}",
            $"Items returned: {report.ItemsReturnedCount}",
        };

        foreach (var failure in report.Failures)
        {
            lines.Add($"Failure: {failure.DisplayName} | {failure.RootPath} | {failure.Reason}");
        }

        File.WriteAllLines(logPath, lines);
    }
}
