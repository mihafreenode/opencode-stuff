using System.IO.Compression;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.AppSupport;

public sealed class WorkspaceBackupManifestService
{
    private readonly WorkspaceAssetClassificationService _assetClassificationService = new();

    public WorkspaceBackupManifestResult WriteManifestFile(
        WorkspaceSnapshot snapshot,
        WorkspaceBackupExportResult export,
        string archivePath,
        DateTimeOffset createdUtc)
    {
        var manifestPath = Path.ChangeExtension(archivePath, null) + "-backup-manifest.yaml";
        var manifestYaml = BuildManifestYaml(snapshot, export, archivePath, createdUtc);
        File.WriteAllText(manifestPath, manifestYaml);

        return new WorkspaceBackupManifestResult
        {
            ManifestPath = manifestPath,
            ArchiveEntryPath = "backup-manifest.yaml",
            IncludedFileCount = export.FileCount,
            ExcludedFileCount = export.ExcludedEntries.Count,
            WarningCount = export.Warnings.Count,
        };
    }

    public WorkspaceBackupManifestResult WriteAndEmbedManifest(
        WorkspaceSnapshot snapshot,
        WorkspaceBackupExportResult export,
        string archivePath,
        DateTimeOffset createdUtc)
    {
        var manifestResult = WriteManifestFile(snapshot, export, archivePath, createdUtc);
        var manifestYaml = BuildManifestYaml(snapshot, export, archivePath, createdUtc);
        AddManifestToArchive(archivePath, manifestYaml);
        return manifestResult;
    }

    public string BuildManifestYaml(WorkspaceSnapshot snapshot, WorkspaceBackupExportResult export, string archivePath, DateTimeOffset createdUtc)
    {
        var manifest = _assetClassificationService.BuildBackupManifest(
            snapshot,
            createdUtc,
            Path.GetFileName(archivePath),
            export.ArchiveSizeBytes,
            export.FileCount,
            export.ExcludedEntries.Count,
            export.Warnings);
        return _assetClassificationService.SerializeBackupManifest(manifest);
    }

    public static void AddManifestToArchive(string archivePath, string manifestYaml)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        var manifestEntry = archive.GetEntry("backup-manifest.yaml");
        manifestEntry?.Delete();
        manifestEntry = archive.CreateEntry("backup-manifest.yaml", CompressionLevel.Fastest);
        using var writer = new StreamWriter(manifestEntry.Open());
        writer.Write(manifestYaml);
    }
}

public sealed class WorkspaceBackupManifestResult
{
    public required string ManifestPath { get; init; }
    public required string ArchiveEntryPath { get; init; }
    public required int IncludedFileCount { get; init; }
    public required int ExcludedFileCount { get; init; }
    public required int WarningCount { get; init; }
}
