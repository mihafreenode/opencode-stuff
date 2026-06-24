using System.IO.Compression;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.AppSupport;

public sealed class WorkspaceBackupExportService
{
    private const long DefaultLargeFileThresholdBytes = 64L * 1024L * 1024L;

    private static readonly string[] RecursivelyExcludedPrefixes =
    [
        ".git/",
        ".opencode/local/",
        ".cache/",
        ".pytest_cache/",
        ".mypy_cache/",
        ".npm/",
        ".pnpm-store/",
        ".artifact-cache/",
        "node_modules/",
        "bin/",
        "obj/",
        "build/",
        "tmp/",
        "temp/",
        "__pycache__/",
        ".venv/",
        "venv/",
        "mounts/home/",
        "mounts/user/",
        "mounts/inbox/",
        "artifacts/runs/",
    ];

    private readonly WorkspaceIgnorePolicyService _ignorePolicyService;
    private readonly long _largeFileThresholdBytes;

    public WorkspaceBackupExportService(WorkspaceIgnorePolicyService? ignorePolicyService = null, long largeFileThresholdBytes = DefaultLargeFileThresholdBytes)
    {
        _ignorePolicyService = ignorePolicyService ?? new WorkspaceIgnorePolicyService();
        _largeFileThresholdBytes = largeFileThresholdBytes <= 0 ? DefaultLargeFileThresholdBytes : largeFileThresholdBytes;
    }

    public async Task<WorkspaceBackupExportResult> ExportAsync(WorkspaceSnapshot snapshot, string archivePath, IOperationLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("Backup destination is required.", nameof(archivePath));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var workspaceRootPath = snapshot.Paths.RootPath;
        if (!Directory.Exists(workspaceRootPath))
        {
            throw new DirectoryNotFoundException($"Workspace root '{workspaceRootPath}' was not found.");
        }

        var destinationDirectory = Path.GetDirectoryName(archivePath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new InvalidOperationException("Backup destination must include a parent directory.");
        }

        Directory.CreateDirectory(destinationDirectory);

        var includedFiles = new List<WorkspaceBackupEntry>();
        var excludedEntries = new List<WorkspaceBackupEntry>();
        var warnings = new List<string>();
        var backupPathInsideWorkspace = TryGetRelativePathInsideWorkspace(workspaceRootPath, archivePath);

        Log(logSink, OperationTranscriptLineKind.Status, "Scanning workspace files...");
        CollectEntries(workspaceRootPath, string.Empty, includedFiles, excludedEntries, warnings, backupPathInsideWorkspace, cancellationToken);

        if (includedFiles.Count == 0)
        {
            throw new InvalidOperationException("The workspace backup did not include any files.");
        }

        Log(logSink, OperationTranscriptLineKind.Status, $"Creating backup archive at '{archivePath}'...");
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        await using (var archiveStream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var includedFile in includedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourcePath = Path.Combine(workspaceRootPath, includedFile.Path.Replace('/', Path.DirectorySeparatorChar));
                var entry = archive.CreateEntry(includedFile.Path, CompressionLevel.Fastest);
                await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                await using var entryStream = entry.Open();
                await sourceStream.CopyToAsync(entryStream, cancellationToken);
                Log(logSink, OperationTranscriptLineKind.Comment, $"Included: {includedFile.Path}");
            }
        }

        foreach (var excluded in excludedEntries)
        {
            Log(logSink, OperationTranscriptLineKind.Comment, $"Excluded: {excluded.Path} ({excluded.Reason})");
        }

        foreach (var warning in warnings.Distinct(StringComparer.Ordinal))
        {
            Log(logSink, OperationTranscriptLineKind.StandardError, warning);
        }

        var archiveInfo = new FileInfo(archivePath);
        Log(logSink, OperationTranscriptLineKind.Result, $"Backup created with {includedFiles.Count} file(s), archive size {FormatSize(archiveInfo.Length)}.");

        return new WorkspaceBackupExportResult
        {
            ArchivePath = archivePath,
            FileCount = includedFiles.Count,
            ArchiveSizeBytes = archiveInfo.Length,
            IncludedEntries = includedFiles,
            ExcludedEntries = excludedEntries,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToList(),
        };
    }

    public static string FormatSize(long sizeBytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = Math.Max(0, sizeBytes);
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{size:0} {units[unitIndex]}" : $"{size:0.0} {units[unitIndex]}";
    }

    private void CollectEntries(
        string workspaceRootPath,
        string relativePath,
        List<WorkspaceBackupEntry> includedFiles,
        List<WorkspaceBackupEntry> excludedEntries,
        List<string> warnings,
        string? backupPathInsideWorkspace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directoryPath = string.IsNullOrWhiteSpace(relativePath)
            ? workspaceRootPath
            : Path.Combine(workspaceRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        foreach (var directory in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            var childRelativePath = string.IsNullOrWhiteSpace(relativePath) ? name : $"{relativePath}/{name}";
            var normalizedPath = Normalize(childRelativePath, isDirectory: true);
            var decision = Classify(normalizedPath, isDirectory: true, sizeBytes: 0, backupPathInsideWorkspace);
            if (!decision.ShouldInclude)
            {
                excludedEntries.Add(new WorkspaceBackupEntry { Path = normalizedPath, Reason = decision.Reason, SizeBytes = 0 });
                AddWarningIfNeeded(warnings, normalizedPath, decision);
                continue;
            }

            AddWarningIfNeeded(warnings, normalizedPath, decision);
            CollectEntries(workspaceRootPath, childRelativePath, includedFiles, excludedEntries, warnings, backupPathInsideWorkspace, cancellationToken);
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            var childRelativePath = string.IsNullOrWhiteSpace(relativePath) ? name : $"{relativePath}/{name}";
            var normalizedPath = Normalize(childRelativePath, isDirectory: false);
            var fileInfo = new FileInfo(file);
            var decision = Classify(normalizedPath, isDirectory: false, fileInfo.Length, backupPathInsideWorkspace);
            if (!decision.ShouldInclude)
            {
                excludedEntries.Add(new WorkspaceBackupEntry { Path = normalizedPath, Reason = decision.Reason, SizeBytes = fileInfo.Length });
                AddWarningIfNeeded(warnings, normalizedPath, decision);
                continue;
            }

            includedFiles.Add(new WorkspaceBackupEntry { Path = normalizedPath, Reason = decision.Reason, SizeBytes = fileInfo.Length });
            AddWarningIfNeeded(warnings, normalizedPath, decision);
        }
    }

    private BackupDecision Classify(string normalizedPath, bool isDirectory, long sizeBytes, string? backupPathInsideWorkspace)
    {
        if (!string.IsNullOrWhiteSpace(backupPathInsideWorkspace)
            && string.Equals(normalizedPath.TrimEnd('/'), backupPathInsideWorkspace, StringComparison.OrdinalIgnoreCase))
        {
            return BackupDecision.Exclude("The backup archive itself is not exported into the archive.");
        }

        if (RecursivelyExcludedPrefixes.Any(prefix => normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return BackupDecision.Exclude("Transient runtime, cache, or build content is excluded from portable backups.");
        }

        if (IsSecretCandidate(normalizedPath))
        {
            return BackupDecision.Exclude("Potential secret content is excluded by default.", isWarning: true);
        }

        if (!isDirectory && sizeBytes > _largeFileThresholdBytes)
        {
            return BackupDecision.Exclude($"Large file exceeds the default export limit of {FormatSize(_largeFileThresholdBytes)}.", isWarning: true);
        }

        if (normalizedPath.StartsWith("history/", StringComparison.OrdinalIgnoreCase))
        {
            return BackupDecision.Include("Workspace history is included for restore and archival flows.");
        }

        if (normalizedPath.StartsWith("mounts/", StringComparison.OrdinalIgnoreCase))
        {
            return BackupDecision.Include("Workspace mount content is included except transient runtime mount folders.");
        }

        if (normalizedPath.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase))
        {
            return BackupDecision.Include("Runtime metadata is included for portable restore context.");
        }

        var classification = _ignorePolicyService.Classify(normalizedPath, isDirectory);
        return classification.Disposition switch
        {
            WorkspaceContentDisposition.Ignored => BackupDecision.Exclude(classification.Reason),
            WorkspaceContentDisposition.NeedsReview when isDirectory && normalizedPath.StartsWith(".", StringComparison.Ordinal) => BackupDecision.Include(classification.Reason, isWarning: true),
            WorkspaceContentDisposition.NeedsReview => BackupDecision.Exclude(classification.Reason, isWarning: true),
            _ => BackupDecision.Include(classification.Reason),
        };
    }

    private static string? TryGetRelativePathInsideWorkspace(string workspaceRootPath, string archivePath)
    {
        try
        {
            var relativePath = Path.GetRelativePath(workspaceRootPath, archivePath);
            if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
            {
                return null;
            }

            return Normalize(relativePath, isDirectory: false);
        }
        catch
        {
            return null;
        }
    }

    private static void AddWarningIfNeeded(List<string> warnings, string normalizedPath, BackupDecision decision)
    {
        if (!decision.IsWarning)
        {
            return;
        }

        warnings.Add($"{normalizedPath}: {decision.Reason}");
    }

    private static void Log(IOperationLogSink? logSink, OperationTranscriptLineKind kind, string text)
        => logSink?.Append(new OperationTranscriptLine { Kind = kind, Text = text });

    private static bool IsSecretCandidate(string normalizedPath)
    {
        var fileName = Path.GetFileName(normalizedPath.TrimEnd('/'));
        return string.Equals(fileName, ".env", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("secrets/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "id_rsa", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "private.key", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".p12", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string relativePath, bool isDirectory)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/').Trim();
        return isDirectory && !normalized.EndsWith("/", StringComparison.Ordinal) ? normalized + "/" : normalized;
    }

    private sealed record BackupDecision(bool ShouldInclude, string Reason, bool IsWarning)
    {
        public static BackupDecision Include(string reason, bool isWarning = false) => new(true, reason, isWarning);
        public static BackupDecision Exclude(string reason, bool isWarning = false) => new(false, reason, isWarning);
    }
}

public sealed class WorkspaceBackupExportResult
{
    public required string ArchivePath { get; init; }
    public required int FileCount { get; init; }
    public required long ArchiveSizeBytes { get; init; }
    public required IReadOnlyList<WorkspaceBackupEntry> IncludedEntries { get; init; }
    public required IReadOnlyList<WorkspaceBackupEntry> ExcludedEntries { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class WorkspaceBackupEntry
{
    public required string Path { get; init; }
    public required string Reason { get; init; }
    public required long SizeBytes { get; init; }
}
