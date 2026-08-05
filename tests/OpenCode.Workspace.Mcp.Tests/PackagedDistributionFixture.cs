using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Mcp.Tests;

public sealed class PackagedDistributionFixture : IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly string CacheRoot = Path.Combine(Path.GetTempPath(), "opencode-package-cache", GetRuntimeIdentifier());
    private static readonly string ManifestPath = Path.Combine(CacheRoot, "package-manifest.json");
    private static readonly string BuildLogPathValue = Path.Combine(CacheRoot, "build.log");
    private static int _initializationCount;
    private static int _buildCount;

    public string PackageRoot { get; private set; } = string.Empty;
    public bool CacheHit { get; private set; }
    public string Fingerprint { get; private set; } = string.Empty;
    public string BuildLogPath => BuildLogPathValue;
    public int InitializationCount => _initializationCount;
    public int BuildCount => _buildCount;

    public async Task InitializeAsync()
    {
        await Gate.WaitAsync();
        try
        {
            Interlocked.Increment(ref _initializationCount);
            Directory.CreateDirectory(CacheRoot);
            Fingerprint = BuildFingerprint();
            await File.AppendAllTextAsync(BuildLogPathValue, $"[{DateTimeOffset.UtcNow:O}] initialize fingerprint={Fingerprint}{Environment.NewLine}");

            var manifest = ReadManifest();
            if (manifest is not null
                && manifest.Completed
                && string.Equals(manifest.Fingerprint, Fingerprint, StringComparison.Ordinal)
                && Directory.Exists(manifest.PackageRoot))
            {
                CacheHit = true;
                PackageRoot = manifest.PackageRoot;
                await File.AppendAllTextAsync(BuildLogPathValue, $"[{DateTimeOffset.UtcNow:O}] cache-hit root={PackageRoot}{Environment.NewLine}");
                return;
            }

            CacheHit = false;
            Interlocked.Increment(ref _buildCount);
            var runtime = GetRuntimeIdentifier();
            var buildRoot = Path.Combine(CacheRoot, $"building-{Guid.NewGuid():N}");
            var publishRoot = Path.Combine(buildRoot, "publish");
            var outputRoot = Path.Combine(buildRoot, "dist");
            Directory.CreateDirectory(publishRoot);
            await File.AppendAllTextAsync(BuildLogPathValue, $"[{DateTimeOffset.UtcNow:O}] cache-miss buildRoot={buildRoot}{Environment.NewLine}");

            await RunSetupCommandAsync("dotnet", ["publish", "src/OpenCode.Workspace.Avalonia/OpenCode.Workspace.Avalonia.csproj", "-c", "Release", "-r", runtime, "--self-contained", "true", "-o", Path.Combine(publishRoot, "desktop")], TestPaths.RepositoryRoot);
            await RunSetupCommandAsync("dotnet", ["publish", "src/OpenCode.Workspace.Cli/OpenCode.Workspace.Cli.csproj", "-c", "Release", "-r", runtime, "--self-contained", "true", "-o", Path.Combine(publishRoot, "cli")], TestPaths.RepositoryRoot);
            await RunSetupCommandAsync("dotnet", ["publish", "src/OpenCode.Workspace.Api/OpenCode.Workspace.Api.csproj", "-c", "Release", "-r", runtime, "--self-contained", "true", "-o", Path.Combine(publishRoot, "api")], TestPaths.RepositoryRoot);
            await RunSetupCommandAsync("dotnet", ["publish", "src/OpenCode.Workspace.Mcp/OpenCode.Workspace.Mcp.csproj", "-c", "Release", "-r", runtime, "--self-contained", "true", "-o", Path.Combine(publishRoot, "mcp")], TestPaths.RepositoryRoot);
            await RunSetupCommandAsync("dotnet", ["run", "--project", "tools/OpenCode.Workspace.ReleaseTool/OpenCode.Workspace.ReleaseTool.csproj", "--", "assemble", "--source-root", TestPaths.RepositoryRoot, "--output-root", outputRoot, "--runtime", runtime, "--version", "0.0.0-test", "--desktop-publish-dir", Path.Combine(publishRoot, "desktop"), "--cli-publish-dir", Path.Combine(publishRoot, "cli"), "--api-publish-dir", Path.Combine(publishRoot, "api"), "--mcp-publish-dir", Path.Combine(publishRoot, "mcp"), "--create-zip", OperatingSystem.IsWindows() ? "true" : "false"], TestPaths.RepositoryRoot);

            var finalPackageRoot = OperatingSystem.IsWindows()
                ? ExtractWindowsPackage(outputRoot, runtime, buildRoot)
                : Path.Combine(outputRoot, $"opencode-workspace-0.0.0-test-{runtime}");

            var cachePackageRoot = Path.Combine(CacheRoot, $"package-{Fingerprint}");
            if (Directory.Exists(cachePackageRoot))
            {
                Directory.Delete(cachePackageRoot, recursive: true);
            }

            CopyDirectory(finalPackageRoot, cachePackageRoot);
            WriteManifest(new PackageManifest { Fingerprint = Fingerprint, PackageRoot = cachePackageRoot, Completed = true, RuntimeIdentifier = runtime, CreatedUtc = DateTimeOffset.UtcNow });
            PackageRoot = cachePackageRoot;
            await File.AppendAllTextAsync(BuildLogPathValue, $"[{DateTimeOffset.UtcNow:O}] build-complete packageRoot={PackageRoot}{Environment.NewLine}");
            Directory.Delete(buildRoot, recursive: true);
        }
        finally
        {
            Gate.Release();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public string CopyPackageTo(string destinationRoot)
    {
        var copiedRoot = Path.Combine(destinationRoot, "copied", Path.GetFileName(PackageRoot));
        CopyDirectory(PackageRoot, copiedRoot);
        return copiedRoot;
    }

    private static string BuildFingerprint()
    {
        var inputs = new[]
        {
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "Directory.Packages.props")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tools", "OpenCode.Workspace.ReleaseTool", "Program.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tools", "build-release.ps1")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Avalonia", "OpenCode.Workspace.Avalonia.csproj")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Cli", "OpenCode.Workspace.Cli.csproj")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Cli", "InteractiveSessionAttachHelper.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Api", "OpenCode.Workspace.Api.csproj")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Mcp", "OpenCode.Workspace.Mcp.csproj")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Api", "Program.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Api", "LocalHostServices.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.LocalClient", "LocalHostClient.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.LocalClient", "LocalHostContracts.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Mcp", "LocalHostMcpProxyServices.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Mcp", "McpCompatibilityMapper.cs")),
            GetRuntimeIdentifier(),
            "Release",
            "self-contained",
            "package-layout-v4",
        };

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n---\n", inputs)))).ToLowerInvariant();
    }

    private static async Task RunSetupCommandAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        await File.AppendAllTextAsync(BuildLogPathValue, $"[{DateTimeOffset.UtcNow:O}] start {fileName} {string.Join(' ', arguments)} cwd={workingDirectory}{Environment.NewLine}");
        try
        {
            await using var harness = await PackagedProcessHarness.StartAsync($"setup-{Path.GetFileNameWithoutExtension(fileName)}", fileName, arguments, workingDirectory);
            await harness.WaitForExitAsync(TimeSpan.FromMinutes(20));
            await File.AppendAllTextAsync(BuildLogPathValue, $"[{DateTimeOffset.UtcNow:O}] exit {fileName} code={harness.ExitCode}{Environment.NewLine}");
            Assert.True(harness.ExitCode == 0, $"Setup command failed: {fileName} {string.Join(' ', arguments)}{Environment.NewLine}{harness.StandardOutput}{Environment.NewLine}{harness.StandardError}");
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"Setup command timed out: {fileName} {string.Join(' ', arguments)}", exception);
        }
    }

    private static PackageManifest? ReadManifest()
        => File.Exists(ManifestPath) ? JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(ManifestPath)) : null;

    private static void WriteManifest(PackageManifest manifest)
    {
        var tempPath = ManifestPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(manifest));
        File.Move(tempPath, ManifestPath, overwrite: true);
    }

    private static string ExtractWindowsPackage(string outputRoot, string runtime, string buildRoot)
    {
        var zipPath = Path.Combine(outputRoot, $"opencode-workspace-0.0.0-test-{runtime}.zip");
        var extractRoot = Path.Combine(buildRoot, "extracted");
        ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);
        return extractRoot;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(destinationPath, File.GetUnixFileMode(file));
            }
        }
    }

    private static string GetRuntimeIdentifier()
        => OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsMacOS() ? "osx-arm64" : "linux-x64";

    private sealed class PackageManifest
    {
        public string Fingerprint { get; init; } = string.Empty;
        public string PackageRoot { get; init; } = string.Empty;
        public string RuntimeIdentifier { get; init; } = string.Empty;
        public bool Completed { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
    }
}
