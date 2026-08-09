using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace OpenCode.Workspace.Mcp.Tests;

public sealed class UnixPackageArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "unix package archive tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task DownloadedUnixPackage_ExtractsUsingNativeTarAfterSafeEntryValidation()
    {
        if (OperatingSystem.IsWindows()) return;

        var existingArchive = Environment.GetEnvironmentVariable("OPENCODE_EXISTING_UNIX_PACKAGE_ARCHIVE");
        var archivePath = string.IsNullOrWhiteSpace(existingArchive)
            ? CreateArchive("valid.tar.gz", "bin/", "bin/tool")
            : Path.GetFullPath(existingArchive);
        var destination = Path.Combine(_root, "extracted package with spaces");

        Assert.True(UnixPackageArchive.ValidateSafeEntries(archivePath, destination) > 0);
        await UnixPackageArchive.ExtractAsync(archivePath, destination, _root);

        Assert.True(Directory.Exists(Path.Combine(destination, "bin")));
        if (string.IsNullOrWhiteSpace(existingArchive))
        {
            Assert.Equal("content", File.ReadAllText(Path.Combine(destination, "bin", "tool")));
        }
        else
        {
            Assert.True(File.Exists(Path.Combine(destination, "OpenCode.Workspace")));
            Assert.True(File.Exists(Path.Combine(destination, "bin", "local-host", "OpenCode.Workspace.LocalHost")));
            Assert.True(File.Exists(Path.Combine(destination, "bin", "cli", "OpenCode.Workspace.Cli")));
            Assert.True(File.Exists(Path.Combine(destination, "bin", "mcp", "OpenCode.Workspace.Mcp")));
            Assert.True(File.Exists(Path.Combine(destination, "bin", "remote-bridge", "OpenCode.Workspace.RemoteBridge")));
        }
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("/absolute")]
    [InlineData("a/../../evil")]
    public void SafeEntryValidation_RejectsPathEscape(string entryName)
    {
        var archivePath = CreateArchive("malicious.tar.gz", entryName);

        var exception = Assert.Throws<InvalidDataException>(() => UnixPackageArchive.ValidateSafeEntries(archivePath, Path.Combine(_root, "destination")));

        Assert.Contains(entryName, exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateArchive(string fileName, params string[] entryNames)
    {
        Directory.CreateDirectory(_root);
        var archivePath = Path.Combine(_root, fileName);
        using var archive = File.Create(archivePath);
        using var gzip = new GZipStream(archive, CompressionLevel.SmallestSize);
        using var writer = new TarWriter(gzip, leaveOpen: false);
        foreach (var entryName in entryNames)
        {
            var isDirectory = entryName.EndsWith("/", StringComparison.Ordinal);
            var entry = new PaxTarEntry(isDirectory ? TarEntryType.Directory : TarEntryType.RegularFile, entryName);
            if (!isDirectory)
            {
                entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes("content"));
            }
            writer.WriteEntry(entry);
        }
        return archivePath;
    }
}
