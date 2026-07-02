using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class WorkspaceDiagnosticsBundleExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task ExportAsync(WorkspaceDiagnosticsSession session, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Diagnostics bundle destination is required.", nameof(destinationPath));
        }

        destinationPath = EnsureZipExtension(destinationPath);

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new InvalidOperationException("Diagnostics bundle destination must include a parent directory.");
        }

        Directory.CreateDirectory(destinationDirectory);
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        await using var archiveStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: false);

        await AddEntryAsync(archive, "diagnostics-summary.txt", WorkspaceDiagnosticsTextFormatter.BuildSummaryText(session), cancellationToken);
        await AddEntryAsync(archive, "diagnostics-full-log.txt", WorkspaceDiagnosticsTextFormatter.BuildFullLogText(session), cancellationToken);

        if (session.Readiness is not null)
        {
            await AddEntryAsync(archive, "readiness.json", JsonSerializer.Serialize(session.Readiness, JsonOptions), cancellationToken);
        }

        if (session.ProvisioningHealth is not null)
        {
            await AddEntryAsync(archive, "provisioning-health.json", JsonSerializer.Serialize(session.ProvisioningHealth, JsonOptions), cancellationToken);
        }
    }

    private static string EnsureZipExtension(string destinationPath)
    {
        if (destinationPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return destinationPath;
        }

        return Path.ChangeExtension(destinationPath, ".zip");
    }

    private static async Task AddEntryAsync(ZipArchive archive, string path, string content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        await using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }
}
