using System.IO.Compression;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

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
if (!new[] { "win-x64", "linux-x64", "osx-arm64" }.Contains(options.RuntimeIdentifier, StringComparer.Ordinal))
{
    throw new InvalidOperationException($"Runtime identifier '{options.RuntimeIdentifier}' is not part of the release contract.");
}
if (!options.SelfContained)
{
    throw new InvalidOperationException("Release artifacts must be self-contained.");
}
if (!Regex.IsMatch(options.Version, "^[0-9A-Za-z][0-9A-Za-z._+-]*$"))
{
    throw new InvalidOperationException($"Version '{options.Version}' is not valid for a canonical artifact filename.");
}
if (!Regex.IsMatch(options.GitCommit, "^[0-9a-fA-F]{7,64}$"))
{
    throw new InvalidOperationException("Git commit provenance must be a hexadecimal SHA.");
}
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
CopyHost(options.RemoteBridgePublishDir, Path.Combine(distributionRoot, "bin", "remote-bridge"));

CopyDirectory(Path.Combine(options.SourceRoot, "catalog"), Path.Combine(distributionRoot, "catalog"));
CopyPackageDocumentation(options.SourceRoot, distributionRoot);
CopyDirectory(Path.Combine(options.SourceRoot, "Localization"), Path.Combine(distributionRoot, "Localization"));
CopyFile(Path.Combine(options.SourceRoot, "README.md"), Path.Combine(distributionRoot, "README.md"));
CopyFile(Path.Combine(options.SourceRoot, "LICENSE"), Path.Combine(distributionRoot, "LICENSE"));
CopyFile(Path.Combine(options.SourceRoot, "THIRD-PARTY-NOTICES.md"), Path.Combine(distributionRoot, "THIRD-PARTY-NOTICES.md"));
CopyFile(Path.Combine(options.SourceRoot, "src", "OpenCode.Workspace.Api", "appsettings.json"), Path.Combine(distributionRoot, "config", "api", "appsettings.json"));
CopyFile(Path.Combine(options.SourceRoot, "src", "OpenCode.Workspace.Mcp", "appsettings.json"), Path.Combine(distributionRoot, "config", "mcp", "appsettings.json"));
CopyFile(Path.Combine(options.SourceRoot, "src", "OpenCode.Workspace.RemoteBridge", "appsettings.json"), Path.Combine(distributionRoot, "config", "remote-bridge", "appsettings.json"));

var manifest = new ReleaseManifest(
    options.Version,
    options.GitCommit,
    options.BuildTimestamp,
    options.RuntimeIdentifier,
    options.SelfContained);
File.WriteAllText(
    Path.Combine(distributionRoot, "release-manifest.json"),
    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }) + Environment.NewLine);

ValidatePackageLayout(distributionRoot);

if (!string.Equals(options.ArchiveKind, "none", StringComparison.OrdinalIgnoreCase))
{
    var archiveRoot = options.ArchiveOutputRoot ?? Path.GetDirectoryName(distributionRoot)!;
    Directory.CreateDirectory(archiveRoot);
    var archivePath = options.ArchiveKind switch
    {
        "zip" => Path.Combine(archiveRoot, packageRootName + ".zip"),
        "tar.gz" => Path.Combine(archiveRoot, packageRootName + ".tar.gz"),
        _ => throw new InvalidOperationException($"Unsupported archive kind '{options.ArchiveKind}'."),
    };
    DeleteIfExists(archivePath);
    DeleteIfExists(archivePath + ".sha256");

    if (string.Equals(options.ArchiveKind, "zip", StringComparison.OrdinalIgnoreCase))
    {
        ZipFile.CreateFromDirectory(distributionRoot, archivePath, CompressionLevel.SmallestSize, includeBaseDirectory: false);
    }
    else
    {
        var tarPath = archivePath + ".tmp";
        DeleteIfExists(tarPath);
        try
        {
            TarFile.CreateFromDirectory(distributionRoot, tarPath, includeBaseDirectory: false);
            using var source = File.OpenRead(tarPath);
            using var destination = File.Create(archivePath);
            using var gzip = new GZipStream(destination, CompressionLevel.SmallestSize);
            source.CopyTo(gzip);
        }
        finally
        {
            DeleteIfExists(tarPath);
        }
    }

    var hash = ComputeSha256(archivePath);
    var checksum = $"{hash}  {Path.GetFileName(archivePath)}";
    File.WriteAllText(archivePath + ".sha256", checksum);
    var verifiedHash = ComputeSha256(archivePath);
    if (!string.Equals(hash, verifiedHash, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Checksum verification failed for '{archivePath}'.");
    }
}

static string ComputeSha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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

static void CopyPackageDocumentation(string sourceRoot, string distributionRoot)
{
    var docsRoot = Path.Combine(sourceRoot, "docs");
    var destinationRoot = Path.Combine(distributionRoot, "docs");
    var files = new[]
    {
        "index.md",
        "getting-started.md",
        "analytics-workspace.md",
        "documentation-features.md",
        "education-stem-demo.md",
        "education-stem-workspace.md",
        "oracle-apex-demo.md",
        "oracle-apexlang-demo.md",
        "oracle-demo.md",
        "oracle-documentation-strategy.md",
        "oracle-lifecycle-workflows.md",
        "oracle-plsql-demo.md",
        "oracle-samples.md",
        "skill-packs.md",
        Path.Combine("testing", "oracle-apex-development-loop.md"),
        Path.Combine("testing", "oracle-apex-runtime-smoke.md"),
        Path.Combine("testing", "smoke-cli-contract.md"),
        Path.Combine("troubleshooting", "wsl-windows-interop.md"),
    };
    var directories = new[]
    {
        "articles",
        "capabilities",
        "features",
        "integrations",
        "oracle",
        "oracle-tools",
        "user",
    };

    foreach (var file in files)
    {
        CopyFile(Path.Combine(docsRoot, file), Path.Combine(destinationRoot, file));
    }

    foreach (var directory in directories)
    {
        CopyDirectory(Path.Combine(docsRoot, directory), Path.Combine(destinationRoot, directory));
    }

    CopyDirectory(
        Path.Combine(docsRoot, "reference"),
        Path.Combine(destinationRoot, "reference"),
        relativePath => !relativePath.StartsWith($"agent-onboarding{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
}

static void ValidatePackageLayout(string distributionRoot)
{
    foreach (var deprecatedPath in new[] { Path.Combine("bin", "api"), Path.Combine("bin", "desktop") })
    {
        if (Directory.Exists(Path.Combine(distributionRoot, deprecatedPath)))
        {
            throw new InvalidOperationException($"Deprecated package path '{deprecatedPath}' is not allowed.");
        }
    }

    AssertSingleHostPayload(distributionRoot, "OpenCode.Workspace.Mcp", Path.Combine("bin", "mcp"), "mcp.appsettings.json");
    AssertSingleHostPayload(distributionRoot, "OpenCode.Workspace.RemoteBridge", Path.Combine("bin", "remote-bridge"));
}

static void AssertSingleHostPayload(string distributionRoot, string hostName, string canonicalDirectory, params string[] additionalPayloadNames)
{
    var forbiddenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        hostName,
        hostName + ".exe",
        hostName + ".deps.json",
        hostName + ".runtimeconfig.json",
    };
    forbiddenNames.UnionWith(additionalPayloadNames);

    foreach (var file in Directory.EnumerateFiles(distributionRoot, "*", SearchOption.AllDirectories))
    {
        if (!forbiddenNames.Contains(Path.GetFileName(file)))
        {
            continue;
        }

        var relativePath = Path.GetRelativePath(distributionRoot, file);
        if (!relativePath.StartsWith(canonicalDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Duplicate {hostName} payload is not allowed at '{relativePath}'.");
        }
    }
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

static void CopyDirectory(string sourceDirectory, string destinationDirectory, Func<string, bool>? include = null)
{
    var source = new DirectoryInfo(sourceDirectory);
    if (!source.Exists)
    {
        throw new DirectoryNotFoundException($"Missing source directory '{sourceDirectory}'.");
    }

    Directory.CreateDirectory(destinationDirectory);
    foreach (var directory in source.GetDirectories("*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(source.FullName, directory.FullName);
        if (include?.Invoke(relativePath + Path.DirectorySeparatorChar) ?? true)
        {
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }
    }

    foreach (var file in source.GetFiles("*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(source.FullName, file.FullName);
        if (!(include?.Invoke(relativePath) ?? true))
        {
            continue;
        }

        var destinationPath = Path.Combine(destinationDirectory, relativePath);
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
        RemoteBridgePublishDir: Path.GetFullPath(ReadRequired("remote-bridge-publish-dir")),
        ArchiveKind: ParseOption(args, "archive-kind")
            ?? "none",
        ArchiveOutputRoot: ParseOption(args, "archive-output-root") is { } archiveOutputRoot ? Path.GetFullPath(archiveOutputRoot) : null,
        GitCommit: ReadRequired("git-commit"),
        BuildTimestamp: DateTimeOffset.Parse(ReadRequired("build-timestamp"), System.Globalization.CultureInfo.InvariantCulture),
        SelfContained: bool.Parse(ReadRequired("self-contained")));
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
    Console.WriteLine("OpenCode.Workspace.ReleaseTool assemble --source-root <path> --output-root <path> --runtime <rid> --version <version> --desktop-publish-dir <path> --cli-publish-dir <path> --api-publish-dir <path> --mcp-publish-dir <path> --remote-bridge-publish-dir <path> --archive-kind <zip|tar.gz|none> [--archive-output-root <path>] --git-commit <sha> --build-timestamp <ISO-8601> --self-contained <true|false>");
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
    string RemoteBridgePublishDir,
    string ArchiveKind,
    string? ArchiveOutputRoot,
    string GitCommit,
    DateTimeOffset BuildTimestamp,
    bool SelfContained);

internal sealed record ReleaseManifest(
    string Version,
    string GitCommit,
    DateTimeOffset BuildTimestamp,
    string RuntimeIdentifier,
    bool SelfContained);
