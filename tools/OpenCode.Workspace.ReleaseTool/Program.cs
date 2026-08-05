using System.IO.Compression;

if (args.Length == 0 || string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(args[0], "-h", StringComparison.OrdinalIgnoreCase))
{
    PrintHelp();
    return 0;
}

if (!string.Equals(args[0], "assemble", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
    PrintHelp();
    return 2;
}

var options = ParseOptions(args[1..]);
var packageRootName = $"opencode-workspace-{options.Version}-{options.RuntimeIdentifier}";
var distributionRoot = Path.Combine(Path.GetFullPath(options.OutputRoot), packageRootName);
if (Directory.Exists(distributionRoot))
{
    Directory.Delete(distributionRoot, recursive: true);
}

Directory.CreateDirectory(distributionRoot);
CopyHost(options.DesktopPublishDir, distributionRoot);
CopyHost(options.CliPublishDir, Path.Combine(distributionRoot, "bin", "cli"));
CopyHost(options.ApiPublishDir, Path.Combine(distributionRoot, "bin", "local-host"));
CopyHost(options.McpPublishDir, Path.Combine(distributionRoot, "bin", "mcp"));

CopyDirectory(Path.Combine(options.SourceRoot, "catalog"), Path.Combine(distributionRoot, "catalog"));
CopyDirectory(Path.Combine(options.SourceRoot, "docs"), Path.Combine(distributionRoot, "docs"));
CopyDirectory(Path.Combine(options.SourceRoot, "Localization"), Path.Combine(distributionRoot, "Localization"));
CopyFile(Path.Combine(options.SourceRoot, "README.md"), Path.Combine(distributionRoot, "README.md"));
CopyFile(Path.Combine(options.SourceRoot, "LICENSE"), Path.Combine(distributionRoot, "LICENSE"));
CopyFile(Path.Combine(options.SourceRoot, "THIRD-PARTY-NOTICES.md"), Path.Combine(distributionRoot, "THIRD-PARTY-NOTICES.md"));
CopyFile(Path.Combine(options.SourceRoot, "src", "OpenCode.Workspace.Api", "appsettings.json"), Path.Combine(distributionRoot, "config", "api", "appsettings.json"));
CopyFile(Path.Combine(options.SourceRoot, "src", "OpenCode.Workspace.Mcp", "appsettings.json"), Path.Combine(distributionRoot, "config", "mcp", "appsettings.json"));

if (options.CreateZip)
{
    var zipPath = distributionRoot + ".zip";
    if (File.Exists(zipPath))
    {
        File.Delete(zipPath);
    }

    ZipFile.CreateFromDirectory(distributionRoot, zipPath, CompressionLevel.SmallestSize, includeBaseDirectory: false);
}

return 0;

static void CopyHost(string sourceDirectory, string destinationDirectory)
{
    CopyDirectory(sourceDirectory, destinationDirectory);
    DeleteIfExists(Path.Combine(destinationDirectory, "catalog"));
    DeleteIfExists(Path.Combine(destinationDirectory, "docs"));
    DeleteIfExists(Path.Combine(destinationDirectory, "Localization"));
    DeleteIfExists(Path.Combine(destinationDirectory, "README.md"));
    DeleteIfExists(Path.Combine(destinationDirectory, "LICENSE"));
    DeleteIfExists(Path.Combine(destinationDirectory, "THIRD-PARTY-NOTICES.md"));
    DeleteIfExists(Path.Combine(destinationDirectory, "appsettings.json"));
}

static void DeleteIfExists(string path)
{
    if (Directory.Exists(path))
    {
        Directory.Delete(path, recursive: true);
    }

    if (File.Exists(path))
    {
        File.Delete(path);
    }
}

static void CopyDirectory(string sourceDirectory, string destinationDirectory)
{
    var source = new DirectoryInfo(sourceDirectory);
    if (!source.Exists)
    {
        throw new DirectoryNotFoundException($"Missing source directory '{sourceDirectory}'.");
    }

    Directory.CreateDirectory(destinationDirectory);
    foreach (var directory in source.GetDirectories("*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(source.FullName, directory.FullName)));
    }

    foreach (var file in source.GetFiles("*", SearchOption.AllDirectories))
    {
        var destinationPath = Path.Combine(destinationDirectory, Path.GetRelativePath(source.FullName, file.FullName));
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        file.CopyTo(destinationPath, overwrite: true);
        CopyUnixMode(file.FullName, destinationPath);
    }
}

static void CopyFile(string sourcePath, string destinationPath)
{
    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
    File.Copy(sourcePath, destinationPath, overwrite: true);
    CopyUnixMode(sourcePath, destinationPath);
}

static void CopyUnixMode(string sourcePath, string destinationPath)
{
    if (OperatingSystem.IsWindows())
    {
        return;
    }

    var mode = File.GetUnixFileMode(sourcePath);
    File.SetUnixFileMode(destinationPath, mode);
}

static ReleaseAssemblyOptions ParseOptions(string[] args)
{
    string ReadRequired(string name)
        => ParseOption(args, name) ?? throw new InvalidOperationException($"Missing required option --{name}.");

    return new ReleaseAssemblyOptions(
        SourceRoot: Path.GetFullPath(ReadRequired("source-root")),
        OutputRoot: Path.GetFullPath(ReadRequired("output-root")),
        RuntimeIdentifier: ReadRequired("runtime"),
        Version: ReadRequired("version"),
        DesktopPublishDir: Path.GetFullPath(ReadRequired("desktop-publish-dir")),
        CliPublishDir: Path.GetFullPath(ReadRequired("cli-publish-dir")),
        ApiPublishDir: Path.GetFullPath(ReadRequired("api-publish-dir")),
        McpPublishDir: Path.GetFullPath(ReadRequired("mcp-publish-dir")),
        CreateZip: string.Equals(ParseOption(args, "create-zip"), "true", StringComparison.OrdinalIgnoreCase));
}

static string? ParseOption(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], $"--{name}", StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}

static void PrintHelp()
{
    Console.WriteLine("OpenCode.Workspace.ReleaseTool assemble --source-root <path> --output-root <path> --runtime <rid> --version <version> --desktop-publish-dir <path> --cli-publish-dir <path> --api-publish-dir <path> --mcp-publish-dir <path> [--create-zip true]");
}

internal sealed record ReleaseAssemblyOptions(
    string SourceRoot,
    string OutputRoot,
    string RuntimeIdentifier,
    string Version,
    string DesktopPublishDir,
    string CliPublishDir,
    string ApiPublishDir,
    string McpPublishDir,
    bool CreateZip);
