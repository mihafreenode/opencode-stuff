namespace OpenCode.Workspace.Core.Knowledge;

internal static class KnowledgePackPathNormalizer
{
    public static string NormalizeRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = path.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException($"Knowledge Pack paths must be relative. Received '{path}'.");
        }

        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        normalized = normalized.TrimStart('/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized;
    }
}
