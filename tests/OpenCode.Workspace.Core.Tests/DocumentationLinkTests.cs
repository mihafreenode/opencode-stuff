using System.Text.RegularExpressions;

namespace OpenCode.Workspace.Core.Tests;

public sealed partial class DocumentationLinkTests
{
    [Fact]
    public void RepositoryDocumentation_RelativeLinksResolveInsideRepository()
    {
        var root = Path.GetFullPath(TestPaths.RepositoryRoot);

        foreach (var markdownPath in EnumerateDocumentation(root))
        {
            var content = File.ReadAllText(markdownPath);
            var targets = InlineLinkRegex().Matches(content).Select(match => match.Groups["target"].Value)
                .Concat(ReferenceLinkRegex().Matches(content).Select(match => match.Groups["target"].Value));

            foreach (var target in targets)
            {
                AssertRelativeTargetExists(root, markdownPath, target);
            }
        }
    }

    private static IEnumerable<string> EnumerateDocumentation(string root)
    {
        yield return Path.Combine(root, "README.md");
        yield return Path.Combine(root, "AGENTS.md");

        var paths = Directory.EnumerateFiles(Path.Combine(root, "branding"), "*.md", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "scripts"), "README.md", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "skills"), "*.md", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories));

        foreach (var path in paths)
        {
            var relativePath = Path.GetRelativePath(root, path);
            var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!segments.Any(segment => segment.Equals("fixtures", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("artifacts", StringComparison.OrdinalIgnoreCase)))
            {
                yield return path;
            }
        }
    }

    private static void AssertRelativeTargetExists(string root, string markdownPath, string rawTarget)
    {
        var target = rawTarget.Trim().Trim('<', '>');
        if (string.IsNullOrWhiteSpace(target)
            || target.StartsWith('#')
            || target.StartsWith("//", StringComparison.Ordinal)
            || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || Uri.TryCreate(target, UriKind.Absolute, out _))
        {
            return;
        }

        var pathOnly = target.Split('#', 2)[0].Split('?', 2)[0];
        if (string.IsNullOrWhiteSpace(pathOnly))
        {
            return;
        }

        pathOnly = Uri.UnescapeDataString(pathOnly).Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(markdownPath)!, pathOnly));
        var repositoryPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Assert.True(
            fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(repositoryPrefix, StringComparison.OrdinalIgnoreCase),
            $"Expected documentation link to stay inside repository: {Path.GetRelativePath(root, markdownPath)} -> {rawTarget}");
        Assert.True(
            File.Exists(fullPath) || Directory.Exists(fullPath),
            $"Expected documentation link target to exist: {Path.GetRelativePath(root, markdownPath)} -> {rawTarget}");
    }

    [GeneratedRegex(@"!?\[[^\]]*\]\(\s*(?<target><[^>]+>|[^\s)]+)(?:\s+[^)]*)?\)")]
    private static partial Regex InlineLinkRegex();

    [GeneratedRegex(@"(?m)^\s*\[[^\]]+\]:\s*(?<target><[^>]+>|\S+)")]
    private static partial Regex ReferenceLinkRegex();
}
