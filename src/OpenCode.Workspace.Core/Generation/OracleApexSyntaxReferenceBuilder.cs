using System.Text.RegularExpressions;

namespace OpenCode.Workspace.Core.Generation;

internal static class OracleApexSyntaxReferenceBuilder
{
    public static IReadOnlyDictionary<string, string> BuildFiles(string sourceText, string sourceUrl, string apexVersion)
    {
        var normalized = NormalizeSource(sourceText);
        var blocks = SplitBlocks(normalized);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(".opencode", "skills", "apexlang", "references", "syntax-basics.md")] = BuildReference(
                "APEXlang Syntax Basics",
                sourceUrl,
                apexVersion,
                blocks,
                ["component syntax", "properties", "property groups", "omitted defaults", "arrays", "source-file naming"],
                "Focus on the block shape first, then add only the properties that differ from Builder defaults."),
            [Path.Combine(".opencode", "skills", "apexlang", "references", "identifiers-and-scopes.md")] = BuildReference(
                "APEXlang Identifiers And Scopes",
                sourceUrl,
                apexVersion,
                blocks,
                ["identifiers", "scope", "uniqueness", "component identifier", "page item", "shared component"],
                "Confirm identifier scope before renaming or creating references, because uniqueness depends on the owning component hierarchy."),
            [Path.Combine(".opencode", "skills", "apexlang", "references", "component-references.md")] = BuildReference(
                "APEXlang Component References",
                sourceUrl,
                apexVersion,
                blocks,
                ["local references", "@ reference", "@/ reference", "global page", "universal theme"],
                "Use `@` for local component references and `@/` for Global Page or Universal Theme references; do not rewrite them as plain strings."),
            [Path.Combine(".opencode", "skills", "apexlang", "references", "embedded-languages.md")] = BuildReference(
                "APEXlang Embedded Languages",
                sourceUrl,
                apexVersion,
                blocks,
                ["fenced blocks", "sql", "pl/sql", "javascript", "javascript-browser", "javascript-mle", "html", "css"],
                "Preserve fenced language labels exactly so SQL, PL/SQL, JavaScript, HTML, and CSS stay valid for later validation and review."),
        };
    }

    private static string BuildReference(string title, string sourceUrl, string apexVersion, IReadOnlyList<SyntaxBlock> blocks, IReadOnlyList<string> keywords, string workflowNote)
    {
        var selected = blocks
            .Where(block => keywords.Any(keyword => block.Heading.Contains(keyword, StringComparison.OrdinalIgnoreCase) || block.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var lines = new List<string>
        {
            $"# {title}",
            string.Empty,
            "> Generated guidance derived from Oracle documentation. This is not original OpenCode documentation.",
            $"> Source: Oracle APEX Developer's Companion, Reading APEXlang Syntax",
            $"> URL: {sourceUrl}",
            $"> APEX version: {apexVersion}",
            string.Empty,
            $"- Agent note: {workflowNote}",
            string.Empty,
        };

        foreach (var block in selected)
        {
            lines.Add($"## {block.Heading}");
            lines.Add(string.Empty);
            lines.Add(block.Content.Trim());
            lines.Add(string.Empty);
        }

        return string.Join("\n", lines).TrimEnd() + "\n";
    }

    private static IReadOnlyList<SyntaxBlock> SplitBlocks(string normalized)
    {
        var blocks = new List<SyntaxBlock>();
        string currentHeading = "Overview";
        var currentContent = new List<string>();
        foreach (var line in normalized.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
            {
                if (currentContent.Count > 0)
                {
                    blocks.Add(new SyntaxBlock(currentHeading, string.Join("\n", currentContent).Trim()));
                    currentContent.Clear();
                }

                currentHeading = line[3..].Trim();
                continue;
            }

            currentContent.Add(line);
        }

        if (currentContent.Count > 0)
        {
            blocks.Add(new SyntaxBlock(currentHeading, string.Join("\n", currentContent).Trim()));
        }

        return blocks;
    }

    private static string NormalizeSource(string sourceText)
    {
        var normalized = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, "<pre><code class=\"language-([^\"]+)\">(.*?)</code></pre>", match =>
        {
            var language = match.Groups[1].Value.Trim();
            var content = Decode(match.Groups[2].Value.Trim());
            return $"\n```{language}\n{content}\n```\n";
        }, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, "<h2[^>]*>(.*?)</h2>", match => $"\n## {Decode(match.Groups[1].Value.Trim())}\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, "<h3[^>]*>(.*?)</h3>", match => $"\n### {Decode(match.Groups[1].Value.Trim())}\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, "</p>", "\n\n", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, "<[^>]+>", string.Empty, RegexOptions.Singleline);
        normalized = Decode(normalized);
        return Regex.Replace(normalized, "\n{3,}", "\n\n").Trim() + "\n";
    }

    private static string Decode(string value)
        => value.Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal)
            .Replace("&#64;", "@", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal);

    private sealed record SyntaxBlock(string Heading, string Content);
}
