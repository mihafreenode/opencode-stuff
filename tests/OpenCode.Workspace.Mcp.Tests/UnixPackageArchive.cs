using System.Formats.Tar;
using System.IO.Compression;

namespace OpenCode.Workspace.Mcp.Tests;

internal static class UnixPackageArchive
{
    public static int ValidateSafeEntries(string archivePath, string destinationRoot)
    {
        var root = Path.GetFullPath(destinationRoot);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;

        using var archive = File.OpenRead(archivePath);
        using var gzip = new GZipStream(archive, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        while (reader.GetNextEntry(copyData: false) is { } entry)
        {
            var normalizedName = ValidateEntryName(entry.Name, root, rootPrefix);
            if (!paths.Add(normalizedName))
            {
                throw new InvalidDataException($"Tar archive contains duplicate path '{entry.Name}'.");
            }

            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory))
            {
                throw new InvalidDataException($"Tar archive entry '{entry.Name}' has unsupported type '{entry.EntryType}'.");
            }

            if (!string.IsNullOrEmpty(entry.LinkName))
            {
                throw new InvalidDataException($"Tar archive entry '{entry.Name}' has unexpected link target '{entry.LinkName}'.");
            }

            count++;
        }

        if (count == 0)
        {
            throw new InvalidDataException("Tar archive contains no entries.");
        }

        return count;
    }

    public static async Task ExtractAsync(string archivePath, string destinationRoot, string workingDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Native tar extraction is only used for Unix packages.");
        }

        ValidateSafeEntries(archivePath, destinationRoot);
        await RunTarAsync("list-unix-package", ["-tzf", archivePath], workingDirectory);
        Directory.CreateDirectory(destinationRoot);
        await RunTarAsync("extract-unix-package", ["-xzf", archivePath, "-C", destinationRoot], workingDirectory);
    }

    private static async Task RunTarAsync(string name, IReadOnlyList<string> arguments, string workingDirectory)
    {
        await using var harness = await PackagedProcessHarness.StartAsync(name, "tar", arguments, workingDirectory);
        await harness.WaitForExitAsync(TimeSpan.FromMinutes(5));
        if (harness.ExitCode != 0)
        {
            throw new InvalidDataException($"Native tar command failed with exit code {harness.ExitCode}.{Environment.NewLine}{harness.StandardError}");
        }
    }

    private static string ValidateEntryName(string name, string root, string rootPrefix)
    {
        if (string.IsNullOrEmpty(name)
            || name.StartsWith("/", StringComparison.Ordinal)
            || name.Contains("\\", StringComparison.Ordinal)
            || (name.Length >= 2 && char.IsAsciiLetter(name[0]) && name[1] == ':'))
        {
            throw new InvalidDataException($"Tar archive entry path is not canonical and relative: '{name}'.");
        }

        var path = name.EndsWith("/", StringComparison.Ordinal) ? name[..^1] : name;
        var segments = path.Split('/', StringSplitOptions.None);
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException($"Tar archive entry path is not canonical and relative: '{name}'.");
        }

        var destination = Path.GetFullPath(Path.Combine([root, .. segments]));
        if (!destination.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Tar archive entry escapes the extraction root: '{name}'.");
        }

        return string.Join('/', segments);
    }
}
