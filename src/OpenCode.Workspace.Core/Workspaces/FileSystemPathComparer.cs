namespace OpenCode.Workspace.Core.Workspaces;

public static class FileSystemPathComparer
{
    public static bool AreEquivalent(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);
        }

        var leftIsWindows = LooksLikeWindowsPath(left);
        var rightIsWindows = LooksLikeWindowsPath(right);
        if (leftIsWindows || rightIsWindows)
        {
            return leftIsWindows
                && rightIsWindows
                && string.Equals(NormalizeWindowsPath(left), NormalizeWindowsPath(right), StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(NormalizeUnixPath(left), NormalizeUnixPath(right), StringComparison.Ordinal);
    }

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return LooksLikeWindowsPath(path)
            ? NormalizeWindowsPath(path)
            : NormalizeUnixPath(path);
    }

    private static bool LooksLikeWindowsPath(string path)
        => path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';

    private static string NormalizeWindowsPath(string path)
    {
        var normalized = path.Trim().Replace('/', '\\');
        var root = normalized.Length >= 3 && normalized[2] == '\\'
            ? char.ToLowerInvariant(normalized[0]) + @":\"
            : char.ToLowerInvariant(normalized[0]) + @":";
        var remainder = normalized.Length > root.Length ? normalized[root.Length..] : string.Empty;
        var segments = remainder.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var collapsed = CollapseSegments(segments);
        return collapsed.Count == 0
            ? root.TrimEnd('\\') + (root.EndsWith("\\", StringComparison.Ordinal) ? "\\" : string.Empty)
            : root + string.Join("\\", collapsed);
    }

    private static string NormalizeUnixPath(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        if (normalized.StartsWith("/private/var/", StringComparison.Ordinal))
        {
            normalized = "/var/" + normalized[13..];
        }
        else if (string.Equals(normalized, "/private/var", StringComparison.Ordinal))
        {
            normalized = "/var";
        }

        var rooted = normalized.StartsWith("/", StringComparison.Ordinal);
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var collapsed = CollapseSegments(segments);
        if (collapsed.Count == 0)
        {
            return rooted ? "/" : ".";
        }

        return rooted
            ? "/" + string.Join("/", collapsed)
            : string.Join("/", collapsed);
    }

    private static List<string> CollapseSegments(IEnumerable<string> segments)
    {
        var collapsed = new List<string>();
        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment) || string.Equals(segment, ".", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                if (collapsed.Count > 0 && !string.Equals(collapsed[^1], "..", StringComparison.Ordinal))
                {
                    collapsed.RemoveAt(collapsed.Count - 1);
                }

                continue;
            }

            collapsed.Add(segment);
        }

        return collapsed;
    }
}
