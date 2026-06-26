using System.Diagnostics;
using System.IO;
using System.Reflection;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.AppSupport;

public sealed class AppBuildInfoService
{
    private readonly string _applicationBasePath;

    public AppBuildInfoService(string applicationBasePath)
    {
        _applicationBasePath = applicationBasePath;
    }

    public AppBuildInfo GetCurrent()
    {
        var executablePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? AppContext.BaseDirectory;
        var entryAssembly = Assembly.GetEntryAssembly() ?? typeof(AppBuildInfoService).Assembly;
        var coreAssembly = typeof(WorkspaceYamlService).Assembly;
        var executableFile = File.Exists(executablePath) ? new FileInfo(executablePath) : null;
        var informationalVersion = entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? entryAssembly.GetName().Version?.ToString()
            ?? "unknown";

        string? commitSha = null;
        try
        {
            var repositoryRoot = ResolveRepositoryRoot(_applicationBasePath);
            commitSha = TryReadGitCommitSha(repositoryRoot);
        }
        catch
        {
        }

        return new AppBuildInfo(
            executablePath,
            GetBuildConfiguration(executablePath),
            entryAssembly.GetName().Version?.ToString() ?? "unknown",
            informationalVersion,
            commitSha ?? "unavailable",
            executableFile?.LastWriteTime.ToString("O") ?? "unavailable",
            coreAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? coreAssembly.GetName().Version?.ToString()
                ?? "unknown",
            WorkspaceYamlService.SchemaVersion);
    }

    private static string ResolveRepositoryRoot(string applicationBasePath)
    {
        var current = new DirectoryInfo(applicationBasePath);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "OpenCode.Workspace.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be resolved from the current application path.");
    }

    private static string GetBuildConfiguration(string executablePath)
    {
        var segments = executablePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var segment in segments)
        {
            if (string.Equals(segment, "Debug", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "Release", StringComparison.OrdinalIgnoreCase))
            {
                return segment;
            }
        }

        return "Unknown";
    }

    private static string? TryReadGitCommitSha(string repositoryRoot)
    {
        var gitPath = Path.Combine(repositoryRoot, ".git");
        if (File.Exists(gitPath))
        {
            var gitDirPointer = File.ReadAllText(gitPath).Trim();
            const string gitDirPrefix = "gitdir: ";
            if (gitDirPointer.StartsWith(gitDirPrefix, StringComparison.OrdinalIgnoreCase))
            {
                gitPath = Path.GetFullPath(Path.Combine(repositoryRoot, gitDirPointer[gitDirPrefix.Length..].Trim()));
            }
        }

        if (!Directory.Exists(gitPath))
        {
            return null;
        }

        var headPath = Path.Combine(gitPath, "HEAD");
        if (!File.Exists(headPath))
        {
            return null;
        }

        var headText = File.ReadAllText(headPath).Trim();
        if (!headText.StartsWith("ref: ", StringComparison.Ordinal))
        {
            return ShortenSha(headText);
        }

        var refPath = Path.Combine(gitPath, headText[5..].Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(refPath))
        {
            return ShortenSha(File.ReadAllText(refPath).Trim());
        }

        var packedRefsPath = Path.Combine(gitPath, "packed-refs");
        if (!File.Exists(packedRefsPath))
        {
            return null;
        }

        var refName = headText[5..].Trim();
        foreach (var line in File.ReadLines(packedRefsPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith('^'))
            {
                continue;
            }

            var parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && string.Equals(parts[1], refName, StringComparison.Ordinal))
            {
                return ShortenSha(parts[0]);
            }
        }

        return null;
    }

    private static string ShortenSha(string sha)
        => sha.Length > 12 ? sha[..12] : sha;
}

public sealed record AppBuildInfo(
    string ExecutablePath,
    string BuildConfiguration,
    string AssemblyVersion,
    string InformationalVersion,
    string GitCommitSha,
    string BuildTimestamp,
    string WorkspaceGeneratorVersion,
    string GeneratedSchemaVersion);
