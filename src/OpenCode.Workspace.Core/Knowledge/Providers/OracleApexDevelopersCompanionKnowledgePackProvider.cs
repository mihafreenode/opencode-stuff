using System.Text.Json;
using System.Text.RegularExpressions;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using YamlDotNet.RepresentationModel;

namespace OpenCode.Workspace.Core.Knowledge.Providers;

public sealed class OracleApexDevelopersCompanionKnowledgePackProvider : IKnowledgePackProvider
{
    private const string ExtractedContentFileName = "oracle-apex-developers-companion.extracted.json";
    private const string PdfFileName = "oracle-apex-developers-companion.pdf";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly string[] KnownConcepts =
    [
        "component identifier",
        "staticId",
        "component scope",
        "@ reference",
        "@/ reference",
        "page item",
        "shared component",
        "Universal Theme",
        "fenced code block",
        "SQLcl export",
        "SQLcl validate",
        "SQLcl import",
        "Builder synchronization",
    ];

    private readonly IKnowledgePackRemoteSourceFetcher _remoteSourceFetcher;

    public OracleApexDevelopersCompanionKnowledgePackProvider(IKnowledgePackRemoteSourceFetcher? remoteSourceFetcher = null)
        => _remoteSourceFetcher = remoteSourceFetcher ?? new HttpKnowledgePackRemoteSourceFetcher();

    public string ProviderId => "apex-developers-companion";

    public string Version => "1";

    public bool IsApplicable(WorkspaceDefinition definition, WorkspaceKnowledgePackDefinition configuration)
        => string.Equals(configuration.Provider, ProviderId, StringComparison.OrdinalIgnoreCase);

    public async Task<ProvisionedKnowledgePackContent> GenerateAsync(KnowledgePackContext context, CancellationToken cancellationToken = default)
    {
        var settings = ParseSettings(context.Configuration.Settings);
        var extractedContentSource = await ResolveSourceAsync(context, ExtractedContentFileName, settings.ContentUrl, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Oracle APEX Developer Companion extracted content is required.");
        ResolvedTextSource? pdfSource = null;
        if (!string.IsNullOrWhiteSpace(settings.PdfUrl) || File.Exists(Path.Combine(context.Paths.OpencodePath, "apex-developers-companion", "source", PdfFileName)))
        {
            pdfSource = await ResolveSourceAsync(context, PdfFileName, settings.PdfUrl, cancellationToken, required: false, allowDownload: false).ConfigureAwait(false);
        }

        var document = JsonSerializer.Deserialize<CompanionExtractedDocument>(extractedContentSource.Content, JsonOptions)
            ?? throw new InvalidOperationException("Oracle APEX Developer Companion extracted content could not be deserialized.");
        var title = string.IsNullOrWhiteSpace(document.Title) ? "Oracle APEX Developer's Companion" : document.Title;
        var apexVersion = string.IsNullOrWhiteSpace(document.ApexVersion) ? settings.ApexVersion : document.ApexVersion;
        var sourceUrl = string.IsNullOrWhiteSpace(document.SourceUrl) ? settings.PdfUrl : document.SourceUrl;

        var sectionFiles = BuildSectionFiles(document, apexVersion, title, sourceUrl, extractedContentSource.Hash);
        var indexEntries = BuildIndexEntries(document, apexVersion);
        var generatedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [KnowledgePackPathNormalizer.NormalizeRelativePath("index.json")] = JsonSerializer.Serialize(indexEntries, JsonOptions),
            [KnowledgePackPathNormalizer.NormalizeRelativePath(Path.Combine("prompts", "compact-context.md"))] = BuildCompactContext(indexEntries, apexVersion),
        };

        foreach (var file in sectionFiles)
        {
            generatedFiles[KnowledgePackPathNormalizer.NormalizeRelativePath(file.Key)] = file.Value;
        }

        var sourceHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ExtractedContentFileName] = extractedContentSource.Hash,
        };
        var sourceLocations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ExtractedContentFileName] = extractedContentSource.Location,
        };

        if (pdfSource is not null)
        {
            sourceHashes[PdfFileName] = pdfSource.Hash;
            sourceLocations[PdfFileName] = pdfSource.Location;
        }

        return new ProvisionedKnowledgePackContent
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = title,
                ["apexVersion"] = apexVersion,
                ["sourceUrl"] = sourceUrl,
                ["sourceHash"] = extractedContentSource.Hash,
            },
            SourceHashes = sourceHashes,
            SourceLocations = sourceLocations,
            GeneratedFiles = generatedFiles,
        };
    }

    private static CompanionSettings ParseSettings(YamlNode? settingsNode)
    {
        if (settingsNode is not YamlMappingNode mapping)
        {
            return new CompanionSettings("26.1", "https://docs.oracle.com/en/database/oracle/apex/26.1/apxdc/oracle-apex-developers-companion.pdf", string.Empty);
        }

        return new CompanionSettings(
            ReadSetting(mapping, "apexVersion") ?? "26.1",
            ReadSetting(mapping, "pdfUrl") ?? "https://docs.oracle.com/en/database/oracle/apex/26.1/apxdc/oracle-apex-developers-companion.pdf",
            ReadSetting(mapping, "contentUrl") ?? string.Empty);
    }

    private async Task<ResolvedTextSource?> ResolveSourceAsync(KnowledgePackContext context, string fileName, string? downloadUrl, CancellationToken cancellationToken, bool required = true, bool allowDownload = true)
    {
        var workspaceLocalPath = Path.Combine(context.Paths.OpencodePath, "apex-developers-companion", "source", fileName);
        if (File.Exists(workspaceLocalPath))
        {
            var content = await File.ReadAllTextAsync(workspaceLocalPath, cancellationToken).ConfigureAwait(false);
            return new ResolvedTextSource(workspaceLocalPath, content, WorkspaceAppliedStateService.ComputeHash(content.Replace("\r\n", "\n", StringComparison.Ordinal)));
        }

        var cachePath = Path.Combine(context.SharedCacheRootPath, fileName);
        if (File.Exists(cachePath))
        {
            var content = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
            return new ResolvedTextSource(cachePath, content, WorkspaceAppliedStateService.ComputeHash(content.Replace("\r\n", "\n", StringComparison.Ordinal)));
        }

        if (string.IsNullOrWhiteSpace(downloadUrl) || !allowDownload)
        {
            if (!required)
            {
                return null;
            }

            throw new InvalidOperationException($"Oracle APEX Developer Companion source '{fileName}' was not found locally or in cache, and no download URL is configured.");
        }

        var downloadedContent = await _remoteSourceFetcher.FetchAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(context.SharedCacheRootPath);
        await File.WriteAllTextAsync(cachePath, downloadedContent.Replace("\r\n", "\n", StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
        return new ResolvedTextSource(downloadUrl, downloadedContent, WorkspaceAppliedStateService.ComputeHash(downloadedContent.Replace("\r\n", "\n", StringComparison.Ordinal)));
    }

    private static Dictionary<string, string> BuildSectionFiles(CompanionExtractedDocument document, string apexVersion, string title, string sourceUrl, string sourceHash)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flatSections = document.Chapters.SelectMany(chapter => chapter.Sections.Select(section => (chapter, section))).ToList();
        for (var index = 0; index < flatSections.Count; index++)
        {
            var (chapter, section) = flatSections[index];
            var chapterSlug = Slug(chapter.Title);
            var sectionSlug = Slug(section.Title);
            var relativePath = KnowledgePackPathNormalizer.NormalizeRelativePath(Path.Combine("docs", chapterSlug, sectionSlug + ".md"));
            files[relativePath] = BuildSectionMarkdown(title, apexVersion, sourceUrl, sourceHash, chapter.Title, section, flatSections.ElementAtOrDefault(index - 1).section?.Title, flatSections.ElementAtOrDefault(index + 1).section?.Title);
        }

        return files;
    }

    private static string BuildSectionMarkdown(string title, string apexVersion, string sourceUrl, string sourceHash, string chapterTitle, CompanionSection section, string? previousTitle, string? nextTitle)
    {
        var lines = new List<string>
        {
            $"# {section.Title}",
            string.Empty,
            "> Generated from Oracle documentation. This is a local retrieval copy for navigation and assistant context, not original OpenCode documentation.",
            $"> Source title: {title}",
            $"> Source URL: {ValueOrUnknown(section.OfficialUrl, sourceUrl)}",
            $"> APEX version: {apexVersion}",
            $"> Source page: {section.PageStart}",
            $"> Source hash: {sourceHash}",
            string.Empty,
            $"- Parent chapter: {chapterTitle}",
            $"- Previous section: {ValueOrUnknown(previousTitle)}",
            $"- Next section: {ValueOrUnknown(nextTitle)}",
            string.Empty,
        };

        var concepts = ExtractConcepts(section.Content);
        if (concepts.Count > 0)
        {
            lines.Add($"- Concepts: {string.Join(", ", concepts)}");
            lines.Add(string.Empty);
        }

        lines.Add(NormalizeBody(section.Content));
        lines.Add(string.Empty);
        lines.Add("## Related Local Guidance");
        lines.Add(string.Empty);
        lines.Add("- `.opencode/skills/apexlang/references/syntax-basics.md`");
        lines.Add("- `.opencode/skills/apexlang/references/identifiers-and-scopes.md`");
        lines.Add("- `.opencode/skills/apexlang/references/component-references.md`");
        lines.Add("- `.opencode/skills/apexlang/references/embedded-languages.md`");
        lines.Add("- `.opencode/knowledge/apexlang-language-reference/docs/language-reference-summary.md`");
        lines.Add("- `.opencode/knowledge/apexlang-language-reference/docs/language-reference-diff.md`");
        lines.Add("- `docs/oracle-apex-component-catalog.md`");
        lines.Add("- `docs/oracle-apex-atlas.md`");
        return string.Join("\n", lines).TrimEnd() + "\n";
    }

    private static IReadOnlyList<CompanionIndexEntry> BuildIndexEntries(CompanionExtractedDocument document, string apexVersion)
    {
        var entries = new List<CompanionIndexEntry>();
        foreach (var chapter in document.Chapters)
        {
            var chapterSlug = Slug(chapter.Title);
            foreach (var section in chapter.Sections)
            {
                var concepts = ExtractConcepts(section.Content);
                entries.Add(new CompanionIndexEntry
                {
                    Title = section.Title,
                    Path = KnowledgePackPathNormalizer.NormalizeRelativePath(Path.Combine("docs", chapterSlug, Slug(section.Title) + ".md")),
                    Chapter = chapter.Title,
                    Headings = section.Headings.Count == 0 ? [section.Title] : section.Headings,
                    Keywords = ExtractKeywords(section.Content),
                    Concepts = concepts,
                    SourcePages = [section.PageStart],
                    ApexVersion = apexVersion,
                });
            }
        }

        return entries;
    }

    private static string BuildCompactContext(IReadOnlyList<CompanionIndexEntry> indexEntries, string apexVersion)
    {
        var relevant = indexEntries
            .Where(entry => entry.Title.Contains("APEXlang", StringComparison.OrdinalIgnoreCase) || entry.Concepts.Count > 0)
            .Take(8)
            .ToList();
        var lines = new List<string>
        {
            "# Oracle APEX Developer Companion Context",
            $"- Available local source: .opencode/knowledge/apex-developers-companion/",
            $"- APEX version: {apexVersion}",
            "- Use the language reference catalog for exact component and property facts.",
            "- Use the local APEXlang syntax references for reading and writing source shape safely.",
            "- Use this Developer Companion tree for conceptual explanations, workflows, and examples.",
            "- Use SQLcl output as the final validity authority.",
            string.Empty,
            "Most relevant local sections:",
        };

        foreach (var entry in relevant)
        {
            lines.Add($"- {entry.Title}: {entry.Path}");
        }

        lines.Add(string.Empty);
        lines.Add("Retrieval guidance:");
        lines.Add("- Start from `.opencode/knowledge/apex-developers-companion/index.json`.");
        lines.Add("- Retrieve only the smallest relevant Markdown section from `.opencode/knowledge/apex-developers-companion/docs/`.");
        lines.Add("- Prefer `reading-apexlang-syntax`, `using-coding-agents`, `using-sqlcl`, and `builder-and-external-tools` for workflow questions.");
        return string.Join("\n", lines) + "\n";
    }

    private static IReadOnlyList<string> ExtractKeywords(string content)
        => Regex.Matches(content, "[A-Za-z@/][A-Za-z0-9@/_-]{3,}")
            .Select(match => match.Value)
            .Where(value => !string.Equals(value, "code", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

    private static IReadOnlyList<string> ExtractConcepts(string content)
    {
        var concepts = KnownConcepts.Where(concept => content.Contains(concept, StringComparison.OrdinalIgnoreCase)).ToList();
        if (content.Contains("@/", StringComparison.Ordinal) && !concepts.Contains("@/ reference", StringComparer.OrdinalIgnoreCase))
        {
            concepts.Add("@/ reference");
        }

        if (content.Contains("@", StringComparison.Ordinal) && !concepts.Contains("@ reference", StringComparer.OrdinalIgnoreCase))
        {
            concepts.Add("@ reference");
        }

        return concepts;
    }

    private static string NormalizeBody(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static string Slug(string value)
        => string.Concat(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-')).Replace("--", "-", StringComparison.Ordinal).Trim('-');

    private static string? ReadSetting(YamlMappingNode mapping, string key)
    {
        foreach (var child in mapping.Children)
        {
            if (child.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                return (child.Value as YamlScalarNode)?.Value;
            }
        }

        return null;
    }

    private static string ValueOrUnknown(string? primary, string fallback = "unknown")
        => string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    private sealed record CompanionSettings(string ApexVersion, string PdfUrl, string ContentUrl);

    private sealed record ResolvedTextSource(string Location, string Content, string Hash);

    private sealed class CompanionExtractedDocument
    {
        public string Title { get; init; } = string.Empty;
        public string ApexVersion { get; init; } = string.Empty;
        public string SourceUrl { get; init; } = string.Empty;
        public List<CompanionChapter> Chapters { get; init; } = [];
    }

    private sealed class CompanionChapter
    {
        public string Title { get; init; } = string.Empty;
        public List<CompanionSection> Sections { get; init; } = [];
    }

    private sealed class CompanionSection
    {
        public string Title { get; init; } = string.Empty;
        public string OfficialUrl { get; init; } = string.Empty;
        public int PageStart { get; init; }
        public List<string> Headings { get; init; } = [];
        public string Content { get; init; } = string.Empty;
    }

    private sealed class CompanionIndexEntry
    {
        public string Title { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public string Chapter { get; init; } = string.Empty;
        public IReadOnlyList<string> Headings { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Concepts { get; init; } = Array.Empty<string>();
        public IReadOnlyList<int> SourcePages { get; init; } = Array.Empty<int>();
        public string ApexVersion { get; init; } = string.Empty;
    }
}
