using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    public bool IsExternalPackage { get; private set; }

    public async Task InitializeAsync()
    {
        await Gate.WaitAsync();
        try
        {
            Interlocked.Increment(ref _initializationCount);
            Directory.CreateDirectory(CacheRoot);
            var externalArchive = Environment.GetEnvironmentVariable("OPENCODE_EXISTING_PACKAGE_ARCHIVE");
            if (!string.IsNullOrWhiteSpace(externalArchive))
            {
                PackageRoot = await ExtractExternalPackageAsync(Path.GetFullPath(externalArchive));
                IsExternalPackage = true;
                return;
            }

            var externalRoot = Environment.GetEnvironmentVariable("OPENCODE_EXISTING_PACKAGE_ROOT");
            if (!string.IsNullOrWhiteSpace(externalRoot))
            {
                PackageRoot = Path.GetFullPath(externalRoot);
                Assert.True(Directory.Exists(PackageRoot), $"Existing package root was not found: '{PackageRoot}'.");
                IsExternalPackage = true;
                return;
            }

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

            await RunSetupCommandAsync("dotnet", ["publish", "src/OpenCode.Workspace.Avalonia/OpenCode.Workspace.Avalonia.csproj", "-c", "Release", "-r", runtime, "--self-contained", "true", "-p:Version=0.0.0-test", "-p:DebugSymbols=false", "-p:DebugType=None", "-m:1", "-o", Path.Combine(publishRoot, "desktop")], TestPaths.RepositoryRoot);
            await RunSetupCommandAsync("dotnet", ["publish", "src/OpenCode.Workspace.Cli/OpenCode.Workspace.Cli.csproj", "-c", "Release", "-r", runtime, "--self-contained", "true", "-p:Version=0.0.0-test", "-p:DebugSymbols=false", "-p:DebugType=None", "-m:1", "-o", Path.Combine(publishRoot, "cli")], TestPaths.RepositoryRoot);
            await RunSetupCommandAsync("dotnet", ["publish", "src/OpenCode.Workspace.Api/OpenCode.Workspace.Api.csproj", "-c", "Release", "-r", runtime, "--self-contained", "true", "-p:Version=0.0.0-test", "-p:DebugSymbols=false", "-p:DebugType=None", "-m:1", "-o", Path.Combine(publishRoot, "api")], TestPaths.RepositoryRoot);
            await RunSetupCommandAsync("dotnet", ["publish", "src/OpenCode.Workspace.Mcp/OpenCode.Workspace.Mcp.csproj", "-c", "Release", "-r", runtime, "--self-contained", "true", "-p:Version=0.0.0-test", "-p:DebugSymbols=false", "-p:DebugType=None", "-m:1", "-o", Path.Combine(publishRoot, "mcp")], TestPaths.RepositoryRoot);
            await RunSetupCommandAsync("dotnet", ["publish", "src/OpenCode.Workspace.RemoteBridge/OpenCode.Workspace.RemoteBridge.csproj", "-c", "Release", "-r", runtime, "--self-contained", "true", "-p:Version=0.0.0-test", "-p:DebugSymbols=false", "-p:DebugType=None", "-m:1", "-o", Path.Combine(publishRoot, "remote-bridge")], TestPaths.RepositoryRoot);
            if (OperatingSystem.IsWindows())
            {
                await RunSetupCommandAsync("dotnet", ["publish", "tests/OpenCode.Workspace.ConPtyTestChild/OpenCode.Workspace.ConPtyTestChild.csproj", "-c", "Release", "-r", runtime, "--self-contained", "true", "-p:DebugSymbols=false", "-p:DebugType=None", "-m:1", "-o", Path.Combine(publishRoot, "conpty-test-child")], TestPaths.RepositoryRoot);
            }
            var archiveKind = OperatingSystem.IsWindows() ? "zip" : "tar.gz";
            await RunSetupCommandAsync("dotnet", ["run", "--project", "tools/OpenCode.Workspace.ReleaseTool/OpenCode.Workspace.ReleaseTool.csproj", "--", "assemble", "--source-root", TestPaths.RepositoryRoot, "--output-root", outputRoot, "--runtime", runtime, "--version", "0.0.0-test", "--desktop-publish-dir", Path.Combine(publishRoot, "desktop"), "--cli-publish-dir", Path.Combine(publishRoot, "cli"), "--api-publish-dir", Path.Combine(publishRoot, "api"), "--mcp-publish-dir", Path.Combine(publishRoot, "mcp"), "--remote-bridge-publish-dir", Path.Combine(publishRoot, "remote-bridge"), "--archive-kind", archiveKind, "--git-commit", "0000000000000000000000000000000000000000", "--build-timestamp", "2000-01-01T00:00:00Z", "--self-contained", "true"], TestPaths.RepositoryRoot);

            var finalPackageRoot = await ExtractPackageAsync(outputRoot, runtime, buildRoot, archiveKind);
            if (OperatingSystem.IsWindows())
            {
                CopyDirectory(Path.Combine(publishRoot, "conpty-test-child"), Path.Combine(finalPackageRoot, "bin", "local-host", "test-assets", "conpty-child"));
            }

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
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.RemoteBridge", "OpenCode.Workspace.RemoteBridge.csproj")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.RemoteBridge", "RemoteBridgeApplication.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.RemoteBridge", "RemoteBridgeOptions.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.RemoteBridge", "CloudflareAccessJwtValidator.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.RemoteBridge", "RemoteBridgeBackend.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.RemoteBridge", "BridgeGrantStore.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.RemoteBridge", "RemoteTerminalProxy.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.RemoteBridge", "RemoteTerminalAssets.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.RemoteBridge", "Program.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.RemoteBridge", "appsettings.json")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Api", "Program.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Api", "LocalHostServices.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Api", "InteractiveTerminalRuntimeService.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.ConPtyTestChild", "Program.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.LocalClient", "LocalHostClient.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.LocalClient", "LocalHostContracts.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Mcp", "LocalHostMcpProxyServices.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Mcp", "McpCompatibilityMapper.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Mcp", "Program.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Cli", "McpCliCommands.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Core", "Smoke", "WorkspaceSmokeRunner.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Core", "Runtime", "ProcessRunner.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Core", "Workspaces", "GitWorkspaceProvider.cs")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Core", "Workspaces", "GitRepositoryService.cs")),
            GetRuntimeIdentifier(),
            "Release",
            "self-contained",
            "package-layout-v10-no-symbols",
        };

        var packageContentFiles = new[] { "catalog", "docs", "Localization" }
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(TestPaths.RepositoryRoot, directory), "*", SearchOption.AllDirectories))
            .Concat(new[]
            {
                Path.Combine(TestPaths.RepositoryRoot, "README.md"),
                Path.Combine(TestPaths.RepositoryRoot, "LICENSE"),
                Path.Combine(TestPaths.RepositoryRoot, "THIRD-PARTY-NOTICES.md"),
                Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Mcp.Tests", "PackagedDistributionFixture.cs"),
                Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Mcp.Tests", "PackagedDistributionTests.cs"),
                Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Mcp.Tests", "PackagedOracleApexEndToEndAcceptanceTests.cs"),
                Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Mcp.Tests", "PhasedAcceptanceRunner.cs"),
            })
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => $"{Path.GetRelativePath(TestPaths.RepositoryRoot, path)}\n{File.ReadAllText(path)}");

        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n---\n", inputs.Concat(packageContentFiles))))).ToLowerInvariant();
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

    private static Task<string> ExtractPackageAsync(string outputRoot, string runtime, string buildRoot, string archiveKind)
    {
        var archivePath = Path.Combine(outputRoot, $"opencode-workspace-0.0.0-test-{runtime}.{archiveKind}");
        return ExtractArchiveAsync(archivePath, runtime, buildRoot);
    }

    private static async Task<string> ExtractExternalPackageAsync(string archivePath)
    {
        Assert.True(File.Exists(archivePath), $"Existing package archive was not found: '{archivePath}'.");
        var fileName = Path.GetFileName(archivePath);
        var match = Regex.Match(fileName, "^opencode-workspace-(?<version>[0-9A-Za-z][0-9A-Za-z._+-]*)-(?<rid>win-x64|linux-x64|osx-arm64)\\.(?<kind>zip|tar\\.gz)$", RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Package archive name is not canonical: '{fileName}'.");
        var runtime = match.Groups["rid"].Value;
        Assert.Equal(GetRuntimeIdentifier(), runtime);
        Assert.Equal(OperatingSystem.IsWindows() ? "zip" : "tar.gz", match.Groups["kind"].Value);

        using var archive = File.OpenRead(archivePath);
        var fingerprint = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        var extractRoot = Path.Combine(CacheRoot, $"downloaded-{fingerprint}", "extracted package with spaces");
        if (Directory.Exists(extractRoot))
        {
            Directory.Delete(extractRoot, recursive: true);
        }

        return await ExtractArchiveAsync(archivePath, runtime, Path.GetDirectoryName(extractRoot)!, extractRoot);
    }

    private static async Task<string> ExtractArchiveAsync(string archivePath, string runtime, string buildRoot, string? extractRoot = null)
    {
        var checksumPath = archivePath + ".sha256";
        Assert.True(File.Exists(checksumPath), $"Package checksum was not found: '{checksumPath}'.");
        var checksumParts = File.ReadAllText(checksumPath).Trim().Split("  ", 2, StringSplitOptions.None);
        Assert.Equal(2, checksumParts.Length);
        Assert.Equal(Path.GetFileName(archivePath), checksumParts[1]);
        using (var archive = File.OpenRead(archivePath))
        {
            Assert.Equal(checksumParts[0], Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant());
        }

        extractRoot ??= Path.Combine(buildRoot, "extracted");
        Directory.CreateDirectory(extractRoot);
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, extractRoot, overwriteFiles: true);
        }
        else
        {
            await UnixPackageArchive.ExtractAsync(archivePath, extractRoot, buildRoot);
        }

        Assert.True(File.Exists(Path.Combine(extractRoot, OperatingSystem.IsWindows() ? "OpenCode.Workspace.exe" : "OpenCode.Workspace")), "Archive must extract package contents directly at the selected root.");
        Assert.DoesNotContain(Directory.EnumerateDirectories(extractRoot), path => Path.GetFileName(path).StartsWith("opencode-workspace-", StringComparison.OrdinalIgnoreCase));
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
