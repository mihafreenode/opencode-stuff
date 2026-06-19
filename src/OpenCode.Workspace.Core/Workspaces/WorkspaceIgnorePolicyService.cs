using OpenCode.Workspace.Core.Models;
using System.Text.RegularExpressions;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceIgnorePolicyService
{
    private const string GeneratedFileHeader = "# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES";

    // The ignore policy is intentionally conservative: oversized repositories are
    // preferable to losing durable work. Unknown hidden content is reviewed instead
    // of being silently ignored, while obvious caches and secrets are handled
    // explicitly so Save Points stay useful and recoverable.
    private static readonly string[] TrackedPrefixes =
    [
        "sources/",
        "knowledge/",
        "work/",
        "artifacts/",
        "docs/",
        "runtimes/",
        ".local/",
        ".local/oracle/",
        ".opencode/",
        ".github/",
        ".devcontainer/",
        ".vscode/",
    ];

    private static readonly string[] TrackedExact =
    [
        "workspace.yaml",
        "workspace.yml",
        ".opencode/profile.yaml",
        ".opencode/profile.yml",
        "history/timeline.yaml",
        ".editorconfig",
        ".gitattributes",
        ".gitignore",
    ];

    private static readonly string[] IgnoredPrefixes =
    [
        ".cache/",
        ".pytest_cache/",
        ".mypy_cache/",
        ".npm/",
        ".pnpm-store/",
        "node_modules/",
        "bin/",
        "obj/",
        ".venv/",
        "venv/",
        "__pycache__/",
        "tmp/",
        "temp/",
        ".artifact-cache/",
    ];

    private static readonly string[] RecursiveSkipPrefixes =
    [
        ".git/",
        "node_modules/",
        "bin/",
        "obj/",
        ".venv/",
        "venv/",
        ".artifact-cache/",
        ".cache/",
        ".pytest_cache/",
        ".mypy_cache/",
        ".npm/",
        ".pnpm-store/",
        "tmp/",
        "temp/",
        "__pycache__/",
    ];

    public WorkspaceContentClassification Classify(string relativePath, bool isDirectory)
    {
        var normalizedPath = Normalize(relativePath, isDirectory);

        if (IsSecretCandidate(normalizedPath))
        {
            return new WorkspaceContentClassification
            {
                RelativePath = normalizedPath,
                IsDirectory = isDirectory,
                Disposition = WorkspaceContentDisposition.NeedsReview,
                Reason = "Potential secret content requires review before creating a Save Point.",
            };
        }

        if (TrackedExact.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase)
            || TrackedPrefixes.Any(prefix => normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return new WorkspaceContentClassification
            {
                RelativePath = normalizedPath,
                IsDirectory = isDirectory,
                Disposition = WorkspaceContentDisposition.Tracked,
                Reason = "Durable workspace content is preserved by default.",
            };
        }

        if (IgnoredPrefixes.Any(prefix => normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return new WorkspaceContentClassification
            {
                RelativePath = normalizedPath,
                IsDirectory = isDirectory,
                Disposition = WorkspaceContentDisposition.Ignored,
                Reason = "Rebuildable or machine-local content is ignored by default.",
            };
        }

        if (normalizedPath.StartsWith("build/", StringComparison.OrdinalIgnoreCase))
        {
            return new WorkspaceContentClassification
            {
                RelativePath = normalizedPath,
                IsDirectory = isDirectory,
                Disposition = WorkspaceContentDisposition.Ignored,
                Reason = "Build output is ignored unless the workspace explicitly opts in.",
            };
        }

        if (isDirectory && IsUnknownHiddenFolder(normalizedPath))
        {
            return new WorkspaceContentClassification
            {
                RelativePath = normalizedPath,
                IsDirectory = true,
                Disposition = WorkspaceContentDisposition.NeedsReview,
                Reason = "Unknown hidden folders are neither auto-tracked nor auto-ignored.",
            };
        }

        return new WorkspaceContentClassification
        {
            RelativePath = normalizedPath,
            IsDirectory = isDirectory,
            Disposition = WorkspaceContentDisposition.Tracked,
            Reason = "Workspace content is preserved when no ignore rule clearly applies.",
        };
    }

    public WorkspaceIgnorePolicyReview Review(IEnumerable<WorkspaceContentClassification> classifications, IEnumerable<string>? gitIgnoreLines = null)
    {
        var items = classifications.ToList();
        var findings = new List<WorkspaceContentFinding>();

        foreach (var item in items)
        {
            if (item.Disposition != WorkspaceContentDisposition.NeedsReview)
            {
                continue;
            }

            if (IsSecretCandidate(item.RelativePath))
            {
                findings.Add(new WorkspaceContentFinding
                {
                    Kind = WorkspaceContentFindingKind.SecretCandidate,
                    RelativePath = item.RelativePath,
                    Message = "Potential secret detected. Review before creating a Save Point.",
                });
                continue;
            }

            if (item.IsDirectory && IsUnknownHiddenFolder(item.RelativePath))
            {
                // Unknown hidden folders require explicit review because a blanket dot-folder
                // ignore rule would hide important project assets, while auto-tracking could
                // silently pull in machine-local tool state.
                findings.Add(new WorkspaceContentFinding
                {
                    Kind = WorkspaceContentFindingKind.UnknownHiddenFolder,
                    RelativePath = item.RelativePath,
                    Message = "Unknown hidden folder detected. Review before creating a Save Point.",
                });
            }
        }

        if (gitIgnoreLines is not null)
        {
            var ignoreEntries = gitIgnoreLines
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
                .ToList();

            if (ignoreEntries.Any(line => string.Equals(line, ".*", StringComparison.OrdinalIgnoreCase) || string.Equals(line, "*/.*", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new WorkspaceContentFinding
                {
                    Kind = WorkspaceContentFindingKind.DurablePathIgnored,
                    RelativePath = ".*",
                    Message = "Blanket hidden-file ignore rules are not allowed for durable workspaces.",
                });
            }

            foreach (var durablePath in TrackedExact.Concat(TrackedPrefixes).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (ignoreEntries.Any(line => MatchesIgnoreRule(line, durablePath)))
                {
                    findings.Add(new WorkspaceContentFinding
                    {
                        Kind = WorkspaceContentFindingKind.DurablePathIgnored,
                        RelativePath = durablePath,
                        Message = "Durable workspace content appears to be ignored and needs review.",
                    });
                }
            }
        }

        return new WorkspaceIgnorePolicyReview
        {
            Classifications = items,
            Findings = findings,
        };
    }

    public WorkspaceIgnorePolicyReview ReviewWorkspace(string workspaceRootPath)
    {
        if (!Directory.Exists(workspaceRootPath))
        {
            return new WorkspaceIgnorePolicyReview();
        }

        return ReviewPaths(workspaceRootPath, DiscoverWorkspacePathsRecursively(workspaceRootPath));
    }

    public WorkspaceIgnorePolicyReview ReviewWorkspaceForProtection(string workspaceRootPath)
    {
        if (!Directory.Exists(workspaceRootPath))
        {
            return new WorkspaceIgnorePolicyReview();
        }

        return ReviewPathsInternal(workspaceRootPath, DiscoverWorkspacePathsRecursively(workspaceRootPath), enforceSecretPrecedenceForGeneratedFiles: true);
    }

    public WorkspaceIgnorePolicyReview ReviewPaths(string workspaceRootPath, IEnumerable<string> relativePaths)
        => ReviewPathsInternal(workspaceRootPath, relativePaths, enforceSecretPrecedenceForGeneratedFiles: false);

    public WorkspaceIgnorePolicyReview ReviewPathsForProtection(string workspaceRootPath, IEnumerable<string> relativePaths)
        => ReviewPathsInternal(workspaceRootPath, relativePaths, enforceSecretPrecedenceForGeneratedFiles: true);

    public WorkspaceIgnorePolicyReview ReviewChangedPaths(string workspaceRootPath, IEnumerable<string> changedPaths)
        => ReviewPaths(workspaceRootPath, ExpandPathsForReview(workspaceRootPath, changedPaths));

    public WorkspaceIgnorePolicyReview ReviewChangedPathsForProtection(string workspaceRootPath, IEnumerable<string> changedPaths)
        => ReviewPathsForProtection(workspaceRootPath, ExpandPathsForReview(workspaceRootPath, changedPaths));

    private WorkspaceIgnorePolicyReview ReviewPathsInternal(string workspaceRootPath, IEnumerable<string> relativePaths, bool enforceSecretPrecedenceForGeneratedFiles)
    {
        var classifications = BuildClassifications(workspaceRootPath, relativePaths, enforceSecretPrecedenceForGeneratedFiles).ToList();
        var gitIgnorePath = Path.Combine(workspaceRootPath, ".gitignore");
        var gitIgnoreLines = File.Exists(gitIgnorePath) ? File.ReadAllLines(gitIgnorePath) : Array.Empty<string>();
        return Review(classifications, gitIgnoreLines);
    }

    public IReadOnlyList<string> DiscoverWorkspacePathsRecursively(string workspaceRootPath)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        VisitDirectory(workspaceRootPath, string.Empty, paths);
        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> ExpandPathsForReview(string workspaceRootPath, IEnumerable<string> changedPaths)
    {
        var expandedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var changedPath in changedPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var normalizedPath = Normalize(changedPath, isDirectory: false).TrimEnd('/');
            AddPathAndParents(workspaceRootPath, normalizedPath, expandedPaths);
        }

        return expandedPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IEnumerable<WorkspaceContentClassification> BuildClassifications(string workspaceRootPath, IEnumerable<string> relativePaths, bool enforceSecretPrecedenceForGeneratedFiles)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in relativePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var normalized = Normalize(relativePath, Directory.Exists(Path.Combine(workspaceRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar).TrimEnd('/'))));
            if (!yielded.Add(normalized))
            {
                continue;
            }

            var fullPath = Path.Combine(workspaceRootPath, normalized.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar));
            var isDirectory = Directory.Exists(fullPath) || normalized.EndsWith("/", StringComparison.Ordinal);
            if (!enforceSecretPrecedenceForGeneratedFiles && IsGeneratedEnvironmentFile(normalized, isDirectory, fullPath))
            {
                yield return new WorkspaceContentClassification
                {
                    RelativePath = normalized,
                    IsDirectory = false,
                    Disposition = WorkspaceContentDisposition.Tracked,
                    Reason = "Managed generated runtime content is tracked without workspace-level secret review when its generated header is intact.",
                };
                continue;
            }

            yield return Classify(normalized, isDirectory);
        }
    }

    private static bool IsGeneratedEnvironmentFile(string normalizedPath, bool isDirectory, string fullPath)
    {
        if (isDirectory || !string.Equals(normalizedPath, ".env", StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            return false;
        }

        using var reader = new StreamReader(fullPath);
        var firstLine = reader.ReadLine();
        return string.Equals(firstLine, GeneratedFileHeader, StringComparison.Ordinal);
    }

    private static void AddPathAndParents(string workspaceRootPath, string normalizedPath, HashSet<string> expandedPaths)
    {
        var fullPath = Path.Combine(workspaceRootPath, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
        {
            expandedPaths.Add(normalizedPath);
        }
        else if (Directory.Exists(fullPath))
        {
            expandedPaths.Add(Normalize(normalizedPath, isDirectory: true));
        }

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            var parentPath = string.Join("/", segments.Take(index + 1));
            var parentFullPath = Path.Combine(workspaceRootPath, parentPath.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(parentFullPath))
            {
                expandedPaths.Add(Normalize(parentPath, isDirectory: true));
            }
        }
    }

    private void VisitDirectory(string workspaceRootPath, string relativePath, HashSet<string> paths)
    {
        var directoryPath = string.IsNullOrWhiteSpace(relativePath)
            ? workspaceRootPath
            : Path.Combine(workspaceRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        foreach (var directory in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var childRelativePath = string.IsNullOrWhiteSpace(relativePath) ? name : $"{relativePath}/{name}";
            var normalized = Normalize(childRelativePath, isDirectory: true);
            paths.Add(normalized);
            if (RecursiveSkipPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            VisitDirectory(workspaceRootPath, childRelativePath, paths);
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            var childRelativePath = string.IsNullOrWhiteSpace(relativePath) ? name : $"{relativePath}/{name}";
            paths.Add(Normalize(childRelativePath, isDirectory: false));
        }
    }

    private static bool IsSecretCandidate(string normalizedPath)
    {
        var fileName = Path.GetFileName(normalizedPath.TrimEnd('/'));
        return string.Equals(fileName, ".env", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("secrets/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "id_rsa", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "private.key", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".p12", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnknownHiddenFolder(string normalizedPath)
    {
        if (!normalizedPath.EndsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        if (TrackedPrefixes.Any(prefix => string.Equals(prefix, normalizedPath, StringComparison.OrdinalIgnoreCase))
            || IgnoredPrefixes.Any(prefix => string.Equals(prefix, normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var segments = normalizedPath[..^1].Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment.StartsWith(".", StringComparison.Ordinal));
    }

    private static bool MatchesIgnoreRule(string ignoreRule, string durablePath)
    {
        if (string.IsNullOrWhiteSpace(ignoreRule))
        {
            return false;
        }

        var normalizedRule = ignoreRule.Trim();
        if (normalizedRule.StartsWith("!"))
        {
            return false;
        }

        normalizedRule = normalizedRule.TrimStart('/');
        var durableCandidates = new[] { durablePath, durablePath.TrimEnd('/') };
        return durableCandidates.Any(candidate => Regex.IsMatch(candidate, GlobToRegex(normalizedRule), RegexOptions.IgnoreCase));
    }

    private static string GlobToRegex(string pattern)
    {
        var normalizedPattern = pattern.Replace('\\', '/');
        var regex = Regex.Escape(normalizedPattern)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", ".");

        return $"^{regex}/?$";
    }

    private static string Normalize(string relativePath, bool isDirectory)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/').Trim();
        return isDirectory && !normalized.EndsWith("/", StringComparison.Ordinal) ? normalized + "/" : normalized;
    }
}
