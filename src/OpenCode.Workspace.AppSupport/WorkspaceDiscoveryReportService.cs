using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using System.Diagnostics;

namespace OpenCode.Workspace.AppSupport;

public sealed class WorkspaceDiscoveryReportService
{
    private const int MaxConcurrentWorkspaceLoads = 3;
    private readonly WorkspaceOrchestrator _workspaceOrchestrator;
    private readonly WorkspaceRepository _workspaceRepository;

    public WorkspaceDiscoveryReportService(WorkspaceOrchestrator workspaceOrchestrator, WorkspaceRepository workspaceRepository)
    {
        _workspaceOrchestrator = workspaceOrchestrator;
        _workspaceRepository = workspaceRepository;
    }

    public async Task<WorkspaceLoadResult> LoadWorkspaceItemsAsync(bool includeRuntimeInspection, Action<WorkspaceLoadProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
    {
        var items = new WorkspaceShellItem?[0];
        var failures = new List<WorkspaceLoadFailure>();
        var timings = new List<WorkspaceLoadTiming>();
        var sync = new object();
        var startedUtc = DateTimeOffset.UtcNow;

        progress?.Invoke(new WorkspaceLoadProgressUpdate
        {
            Title = "Loading workspace index...",
            Message = "Reading the shared workspace index.",
        });

        var indexStartedUtc = DateTimeOffset.UtcNow;
        var indexStopwatch = Stopwatch.StartNew();
        var records = _workspaceOrchestrator.LoadWorkspaceRecords()
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        indexStopwatch.Stop();
        timings.Add(new WorkspaceLoadTiming
        {
            StageKey = "workspace-index",
            StageLabel = "Workspace index",
            Details = $"Loaded {records.Count} workspace records.",
            StartedUtc = indexStartedUtc,
            CompletedUtc = indexStartedUtc + indexStopwatch.Elapsed,
            Duration = indexStopwatch.Elapsed,
            Succeeded = true,
        });

        progress?.Invoke(new WorkspaceLoadProgressUpdate
        {
            Title = "Loading workspace index...",
            Message = $"Found {records.Count} workspace{(records.Count == 1 ? string.Empty : "s")}. Workspace index loaded in {FormatDuration(indexStopwatch.Elapsed)}.",
            ProgressLabel = records.Count == 0 ? "No workspaces found" : $"Workspace 0 of {records.Count}",
            TotalWorkspaces = records.Count,
        });

        items = new WorkspaceShellItem?[records.Count];

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var displayName = GetDisplayName(record);
            var placeholder = CreateLoadingItem(record, "Loading details...");
            items[index] = placeholder;
            progress?.Invoke(new WorkspaceLoadProgressUpdate
            {
                Title = $"Loading {displayName}...",
                Message = "Loading details in background.",
                ProgressLabel = $"Workspace {index + 1} of {records.Count}",
                CurrentWorkspaceName = displayName,
                CurrentWorkspaceIndex = index + 1,
                TotalWorkspaces = records.Count,
                LoadedItem = placeholder,
            });
        }

        using var detailSemaphore = new SemaphoreSlim(Math.Min(MaxConcurrentWorkspaceLoads, Math.Max(1, records.Count)));
        await Task.WhenAll(records.Select((record, index) => LoadWorkspaceDetailsAsync(
            record,
            index,
            records.Count,
            includeRuntimeInspection,
            items,
            failures,
            timings,
            sync,
            detailSemaphore,
            progress,
            cancellationToken)));

        var completedUtc = DateTimeOffset.UtcNow;
        var totalDuration = completedUtc - startedUtc;
        var finalItems = items.Where(item => item is not null).Select(item => item!).ToList();

        var result = new WorkspaceLoadResult
        {
            Items = finalItems,
            Report = new WorkspaceLoadReport
            {
                AppDataRoot = Path.GetDirectoryName(_workspaceRepository.IndexFilePath) ?? string.Empty,
                IndexFilePath = _workspaceRepository.IndexFilePath,
                IndexFileExists = File.Exists(_workspaceRepository.IndexFilePath),
                StartedUtc = startedUtc,
                CompletedUtc = completedUtc,
                TotalDuration = totalDuration,
                RawRecordCount = records.Count,
                SnapshotAttemptCount = records.Count,
                SnapshotCount = finalItems.Count(item => item.HasSnapshot),
                Failures = failures,
                ItemsReturnedCount = finalItems.Count,
                Timings = timings,
            },
        };

        progress?.Invoke(new WorkspaceLoadProgressUpdate
        {
            Title = "Workspace loading complete.",
            Message = $"Loaded {result.Report.SnapshotCount} of {records.Count} workspaces in {FormatDuration(totalDuration)}.",
            ProgressLabel = records.Count == 0 ? "No workspaces found" : $"Workspace {records.Count} of {records.Count}",
            TotalWorkspaces = records.Count,
            CurrentWorkspaceIndex = records.Count,
            IsCompleted = true,
        });

        WriteDiscoveryLog(result.Report);
        return result;
    }

    private async Task LoadWorkspaceDetailsAsync(
        WorkspaceRecord record,
        int index,
        int totalCount,
        bool includeRuntimeInspection,
        WorkspaceShellItem?[] items,
        List<WorkspaceLoadFailure> failures,
        List<WorkspaceLoadTiming> timings,
        object sync,
        SemaphoreSlim detailSemaphore,
        Action<WorkspaceLoadProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        await detailSemaphore.WaitAsync(cancellationToken);
        try
        {
            var displayName = GetDisplayName(record);
            var progressLabel = $"Workspace {index + 1} of {totalCount}";
            var configurationPath = WorkspacePathBuilder.NormalizeConfigurationRelativePath(record.ConfigurationPath);

            PublishProgress(progress, displayName, index, totalCount, "Checking workspace configuration...", CreateLoadingItem(record, "Checking workspace configuration..."));

            var configurationCheckStartedUtc = DateTimeOffset.UtcNow;
            var configurationStopwatch = Stopwatch.StartNew();
            var configurationExists = File.Exists(Path.Combine(record.RootPath, configurationPath.Replace('/', Path.DirectorySeparatorChar)));
            configurationStopwatch.Stop();

            AddTiming(sync, timings, new WorkspaceLoadTiming
            {
                StageKey = "configuration-check",
                StageLabel = "Configuration check",
                WorkspaceName = displayName,
                RootPath = record.RootPath,
                Details = $"Checked '{configurationPath}'.",
                StartedUtc = configurationCheckStartedUtc,
                CompletedUtc = configurationCheckStartedUtc + configurationStopwatch.Elapsed,
                Duration = configurationStopwatch.Elapsed,
                Succeeded = configurationExists,
                FailureMessage = configurationExists ? string.Empty : $"Configuration file '{configurationPath}' was not found.",
            });

            if (!configurationExists)
            {
                var reason = $"Configuration file '{configurationPath}' was not found.";
                var failedItem = new WorkspaceShellItem { Record = record, ErrorMessage = reason };
                lock (sync)
                {
                    items[index] = failedItem;
                    failures.Add(new WorkspaceLoadFailure(displayName, record.RootPath, reason));
                }

                progress?.Invoke(new WorkspaceLoadProgressUpdate
                {
                    Title = $"Loading {displayName}...",
                    Message = reason,
                    ProgressLabel = progressLabel,
                    CurrentWorkspaceName = displayName,
                    CurrentWorkspaceIndex = index + 1,
                    TotalWorkspaces = totalCount,
                    LoadedItem = failedItem,
                });
                return;
            }

            var snapshotStartedUtc = DateTimeOffset.UtcNow;
            var snapshotStopwatch = Stopwatch.StartNew();
            try
            {
                var snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(
                    record.RootPath,
                    cancellationToken,
                    includeRuntimeInspection,
                    timing => AddTiming(sync, timings, timing),
                    includeSessionInspection: false,
                    stageProgress: stage => PublishProgress(progress, displayName, index, totalCount, BuildStageMessage(stage.StageKey, stage.StageLabel), CreateLoadingItem(record, BuildStageMessage(stage.StageKey, stage.StageLabel))));
                snapshotStopwatch.Stop();

                AddTiming(sync, timings, new WorkspaceLoadTiming
                {
                    StageKey = "workspace-snapshot",
                    StageLabel = "Workspace snapshot",
                    WorkspaceName = displayName,
                    RootPath = record.RootPath,
                    Details = includeRuntimeInspection ? "Loaded workspace snapshot with runtime inspection." : "Loaded workspace snapshot.",
                    StartedUtc = snapshotStartedUtc,
                    CompletedUtc = snapshotStartedUtc + snapshotStopwatch.Elapsed,
                    Duration = snapshotStopwatch.Elapsed,
                    Succeeded = true,
                });

                var loadedItem = new WorkspaceShellItem { Record = snapshot.Record, Snapshot = snapshot };
                lock (sync)
                {
                    items[index] = loadedItem;
                }

                progress?.Invoke(new WorkspaceLoadProgressUpdate
                {
                    Title = $"Loading {displayName}...",
                    Message = $"Snapshot loaded in {FormatDuration(snapshotStopwatch.Elapsed)}.",
                    ProgressLabel = progressLabel,
                    CurrentWorkspaceName = displayName,
                    CurrentWorkspaceIndex = index + 1,
                    TotalWorkspaces = totalCount,
                    LoadedItem = loadedItem,
                });
            }
            catch (Exception exception)
            {
                snapshotStopwatch.Stop();
                AddTiming(sync, timings, new WorkspaceLoadTiming
                {
                    StageKey = "workspace-snapshot",
                    StageLabel = "Workspace snapshot",
                    WorkspaceName = displayName,
                    RootPath = record.RootPath,
                    Details = "Workspace snapshot failed.",
                    StartedUtc = snapshotStartedUtc,
                    CompletedUtc = snapshotStartedUtc + snapshotStopwatch.Elapsed,
                    Duration = snapshotStopwatch.Elapsed,
                    Succeeded = false,
                    FailureMessage = exception.Message,
                });

                var failedItem = new WorkspaceShellItem { Record = record, ErrorMessage = exception.Message };
                lock (sync)
                {
                    items[index] = failedItem;
                    failures.Add(new WorkspaceLoadFailure(displayName, record.RootPath, exception.Message));
                }

                progress?.Invoke(new WorkspaceLoadProgressUpdate
                {
                    Title = $"Loading {displayName}...",
                    Message = $"Failed after {FormatDuration(snapshotStopwatch.Elapsed)}. {exception.Message}",
                    ProgressLabel = progressLabel,
                    CurrentWorkspaceName = displayName,
                    CurrentWorkspaceIndex = index + 1,
                    TotalWorkspaces = totalCount,
                    LoadedItem = failedItem,
                });
            }
        }
        finally
        {
            detailSemaphore.Release();
        }
    }

    private static void AddTiming(object sync, List<WorkspaceLoadTiming> timings, WorkspaceLoadTiming timing)
    {
        lock (sync)
        {
            timings.Add(timing);
        }
    }

    private static WorkspaceShellItem CreateLoadingItem(WorkspaceRecord record, string message)
        => new()
        {
            Record = record,
            IsLoading = true,
            LoadingStatusMessage = message,
        };

    private static void PublishProgress(Action<WorkspaceLoadProgressUpdate>? progress, string displayName, int index, int totalCount, string message, WorkspaceShellItem placeholder)
    {
        progress?.Invoke(new WorkspaceLoadProgressUpdate
        {
            Title = $"Loading {displayName}...",
            Message = message,
            ProgressLabel = $"Workspace {index + 1} of {totalCount}",
            CurrentWorkspaceName = displayName,
            CurrentWorkspaceIndex = index + 1,
            TotalWorkspaces = totalCount,
            LoadedItem = placeholder,
        });
    }

    private static string BuildStageMessage(string stageKey, string stageLabel)
        => stageKey switch
        {
            "workspace-definition" or "configuration-path" or "workspace-paths" => "Checking workspace configuration...",
            "applied-state" or "local-runtime-state" or "update-required" or "checkpoint-index" or "timeline-history" => "Reading workspace state...",
            "git-status" => "Checking Git status...",
            "ignore-policy" => "Reviewing workspace content...",
            "runtime-plan" => "Resolving runtime plan...",
            "runtime-inspection" => "Checking runtime state...",
            "session-inspection" => "Checking OpenCode sessions...",
            _ => $"Checking {stageLabel.ToLowerInvariant()}...",
        };

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
            $"Total duration: {FormatDuration(report.TotalDuration)}",
        };

        if (report.SlowestTiming is not null)
        {
            lines.Add($"Slowest stage: {report.SlowestTiming.StageLabel} | {report.SlowestTiming.WorkspaceName} | {FormatDuration(report.SlowestTiming.Duration)}");
        }

        foreach (var timing in report.Timings.OrderBy(item => item.StartedUtc))
        {
            var scope = string.IsNullOrWhiteSpace(timing.WorkspaceName) ? timing.StageLabel : $"{timing.WorkspaceName} | {timing.StageLabel}";
            var outcome = timing.Succeeded ? string.Empty : $" | FAILED: {timing.FailureMessage}";
            lines.Add($"Stage: {scope} | {FormatDuration(timing.Duration)}{outcome}");
        }

        foreach (var failure in report.Failures)
        {
            lines.Add($"Failure: {failure.DisplayName} | {failure.RootPath} | {failure.Reason}");
        }

        File.WriteAllLines(logPath, lines);
    }

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalMilliseconds >= 1000
            ? $"{duration.TotalSeconds:F1} s"
            : $"{Math.Max(1, duration.TotalMilliseconds):F0} ms";
}
