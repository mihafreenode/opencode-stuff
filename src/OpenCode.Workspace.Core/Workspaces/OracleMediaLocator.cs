using System.Security.Cryptography;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public interface IOracleMediaLocator
{
    OracleMediaLocationResult LocateApexMedia(WorkspacePaths paths);
}

public sealed class OracleMediaLocationResult
{
    public string WorkspaceLocalDirectory { get; init; } = string.Empty;
    public string PreferredSharedDirectory { get; init; } = string.Empty;
    public string? ResolvedPath { get; init; }
    public IReadOnlyList<string> SearchedLocations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AcceptedFileNames { get; init; } = Array.Empty<string>();
    public bool IsWorkspaceLocalOverride { get; init; }
}

public sealed class OracleMediaLocator : IOracleMediaLocator
{
    private static readonly string[] AcceptedApexFileNames = ["apex.zip", "apex_*.zip", "apex*.zip"];
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly string _localApplicationDataRoot;
    private readonly string _userProfileRoot;

    public OracleMediaLocator(
        Func<string, string?>? getEnvironmentVariable = null,
        string? localApplicationDataRoot = null,
        string? userProfileRoot = null)
    {
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _localApplicationDataRoot = string.IsNullOrWhiteSpace(localApplicationDataRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localApplicationDataRoot;
        _userProfileRoot = string.IsNullOrWhiteSpace(userProfileRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfileRoot;
    }

    public OracleMediaLocationResult LocateApexMedia(WorkspacePaths paths)
    {
        var workspaceLocalDirectory = GetWorkspaceLocalApexDirectory(paths);
        var preferredSharedDirectory = GetSharedApexCacheDirectory(_localApplicationDataRoot);
        var searchLocations = GetSearchLocations(paths).ToList();

        if (string.Equals(_getEnvironmentVariable("OPENCODE_ORACLE_VERIFICATION_MODE"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return LocatePinnedApexMedia(workspaceLocalDirectory, preferredSharedDirectory, searchLocations);
        }

        foreach (var location in searchLocations)
        {
            var match = FindFirstMatchingFile(location);
            if (match is not null)
            {
                return new OracleMediaLocationResult
                {
                    WorkspaceLocalDirectory = workspaceLocalDirectory,
                    PreferredSharedDirectory = preferredSharedDirectory,
                    ResolvedPath = match,
                    SearchedLocations = searchLocations,
                    AcceptedFileNames = AcceptedApexFileNames,
                    IsWorkspaceLocalOverride = string.Equals(location, workspaceLocalDirectory, StringComparison.OrdinalIgnoreCase),
                };
            }
        }

        return new OracleMediaLocationResult
        {
            WorkspaceLocalDirectory = workspaceLocalDirectory,
            PreferredSharedDirectory = preferredSharedDirectory,
            SearchedLocations = searchLocations,
            AcceptedFileNames = AcceptedApexFileNames,
        };
    }

    private OracleMediaLocationResult LocatePinnedApexMedia(string workspaceLocalDirectory, string preferredSharedDirectory, IReadOnlyList<string> searchLocations)
    {
        var expectedFilename = RequireVerificationValue("OPENCODE_ORACLE_APEX_MEDIA_FILENAME");
        var expectedSha256 = RequireVerificationValue("OPENCODE_ORACLE_APEX_MEDIA_SHA256");
        var resolvedPath = searchLocations
            .Select(location => Path.Combine(location, expectedFilename))
            .FirstOrDefault(File.Exists);
        if (resolvedPath is null)
        {
            throw new InvalidOperationException($"Oracle verification requires APEX media '{expectedFilename}' in a configured Oracle media location.");
        }

        using var stream = File.OpenRead(resolvedPath);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Oracle verification rejected APEX media '{expectedFilename}' because its SHA-256 does not match the pinned provenance.");
        }

        return new OracleMediaLocationResult
        {
            WorkspaceLocalDirectory = workspaceLocalDirectory,
            PreferredSharedDirectory = preferredSharedDirectory,
            ResolvedPath = resolvedPath,
            SearchedLocations = searchLocations,
            AcceptedFileNames = [expectedFilename],
            IsWorkspaceLocalOverride = string.Equals(Path.GetDirectoryName(resolvedPath), workspaceLocalDirectory, StringComparison.OrdinalIgnoreCase),
        };
    }

    private string RequireVerificationValue(string name)
        => _getEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Oracle verification mode requires {name} from the pinned toolchain provenance.");

    public static string GetWorkspaceLocalApexDirectory(WorkspacePaths paths)
        => Path.Combine(paths.RootPath, ".local", "oracle", "downloads", "apex");

    public static string GetSharedApexCacheDirectory(string localApplicationDataRoot)
        => Path.Combine(localApplicationDataRoot, "opencode-stuff", "Downloads", "Oracle", "APEX");

    public static string GetHomeApexCacheDirectory(string userProfileRoot)
        => Path.Combine(userProfileRoot, ".opencode-stuff", "oracle", "downloads", "apex");

    private IEnumerable<string> GetSearchLocations(WorkspacePaths paths)
    {
        yield return GetWorkspaceLocalApexDirectory(paths);

        if (!string.IsNullOrWhiteSpace(_localApplicationDataRoot))
        {
            yield return GetSharedApexCacheDirectory(_localApplicationDataRoot);
        }

        if (!string.IsNullOrWhiteSpace(_userProfileRoot))
        {
            yield return GetHomeApexCacheDirectory(_userProfileRoot);
        }

        var configuredRoot = _getEnvironmentVariable("OPENCODE_STUFF_ORACLE_DOWNLOADS");
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            yield break;
        }

        yield return configuredRoot;

        var fileName = Path.GetFileName(configuredRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.Equals(fileName, "APEX", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fileName, "apex", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(configuredRoot, "APEX");
            yield return Path.Combine(configuredRoot, "apex");
        }
    }

    private static string? FindFirstMatchingFile(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in AcceptedApexFileNames)
        {
            foreach (var file in Directory.GetFiles(directory, pattern).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!seen.Add(file))
                {
                    continue;
                }

                return file;
            }
        }

        return null;
    }
}
