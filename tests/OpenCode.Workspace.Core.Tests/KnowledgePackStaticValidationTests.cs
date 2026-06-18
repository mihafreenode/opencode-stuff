using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Tests;

public sealed class KnowledgePackStaticValidationTests
{
    [Fact]
    public void Repository_IncludesOnboardingRegistrySkillPacksAndKnowledgeMaps()
    {
        var repoRoot = TestPaths.RepositoryRoot;

        var expectedPaths = new[]
        {
            Path.Combine(repoRoot, "docs", "reference", "agent-onboarding", "oracle.md"),
            Path.Combine(repoRoot, "docs", "reference", "agent-onboarding", "analytics.md"),
            Path.Combine(repoRoot, "docs", "reference", "agent-onboarding", "education.md"),
            Path.Combine(repoRoot, "docs", "reference", "agent-onboarding", "publishing.md"),
            Path.Combine(repoRoot, "docs", "reference", "education-knowledge-map.yaml"),
            Path.Combine(repoRoot, "docs", "reference", "publishing-knowledge-map.yaml"),
            Path.Combine(repoRoot, "docs", "skill-packs.md"),
            Path.Combine(repoRoot, "skills", "analytics", "excel-analysis.md"),
            Path.Combine(repoRoot, "skills", "education", "lesson-plan.md"),
            Path.Combine(repoRoot, "skills", "publishing", "typst-report.md"),
        };

        Assert.All(expectedPaths, path => Assert.True(File.Exists(path), $"Expected static knowledge-pack asset to exist: {path}"));
    }

    [Fact]
    public void RepositoryDocs_DescribeDurableAssetsAndTypstRecommendation()
    {
        var repoRoot = TestPaths.RepositoryRoot;
        var analyticsOnboarding = File.ReadAllText(Path.Combine(repoRoot, "docs", "reference", "agent-onboarding", "analytics.md"));
        var publishingOnboarding = File.ReadAllText(Path.Combine(repoRoot, "docs", "reference", "agent-onboarding", "publishing.md"));
        var skillPacks = File.ReadAllText(Path.Combine(repoRoot, "docs", "skill-packs.md"));

        Assert.Contains(".xlsx", analyticsOnboarding);
        Assert.Contains("reproducible build artifacts", analyticsOnboarding, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1. Markdown", publishingOnboarding);
        Assert.Contains("2. Typst", publishingOnboarding);
        Assert.Contains("3. LaTeX", publishingOnboarding);
        Assert.Contains("source -> build -> validate -> inspect", publishingOnboarding);
        Assert.Contains("skills/analytics/", skillPacks);
        Assert.Contains("skills/publishing/", skillPacks);
    }

    [Fact]
    public void KnowledgePackCatalog_LoadsUniqueIdsAndValidSources()
    {
        var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var packs = provider.LoadKnowledgePacks();

        Assert.NotEmpty(packs);
        Assert.Equal(packs.Count, packs.Select(pack => pack.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var pack in packs)
        {
            Assert.False(string.IsNullOrWhiteSpace(pack.Id));
            Assert.False(string.IsNullOrWhiteSpace(pack.Title));
            Assert.False(string.IsNullOrWhiteSpace(pack.Category));
            Assert.NotEmpty(pack.Sources);

            foreach (var source in pack.Sources)
            {
                Assert.False(string.IsNullOrWhiteSpace(source.Name));
                Assert.False(string.IsNullOrWhiteSpace(source.Url));
                Assert.False(string.IsNullOrWhiteSpace(source.Category));
            }
        }
    }

    [Fact]
    public void GeneratedKnowledgeMaps_AreDeterministic_AndOracleAggregateUsesExpectedIdentity()
    {
        var first = GenerateWorkspaceFiles("oracle-demo", "oracle-apex-demo", "oracle-apexlang-demo", "education-knowledge-pack", "publishing-knowledge-pack");
        var second = GenerateWorkspaceFiles("oracle-demo", "oracle-apex-demo", "oracle-apexlang-demo", "education-knowledge-pack", "publishing-knowledge-pack");

        Assert.Equal(first["docs/reference/oracle-knowledge-map.yaml"], second["docs/reference/oracle-knowledge-map.yaml"]);
        Assert.Equal(first["docs/reference/education-knowledge-map.yaml"], second["docs/reference/education-knowledge-map.yaml"]);
        Assert.Equal(first["docs/reference/publishing-knowledge-map.yaml"], second["docs/reference/publishing-knowledge-map.yaml"]);

        var oracleMap = first["docs/reference/oracle-knowledge-map.yaml"];
        Assert.Contains("id: oracle-knowledge-pack", oracleMap);
        Assert.Contains("title: Oracle Knowledge Pack", oracleMap);
    }

    [Fact]
    public void CheckedInAndGeneratedKnowledgeMaps_StayAlignedForIdsTitlesAndSourceCounts()
    {
        var generated = GenerateWorkspaceFiles("education-knowledge-pack", "publishing-knowledge-pack", "oracle-demo", "oracle-apex-demo", "oracle-apexlang-demo");

        AssertMapParity(
            Path.Combine(TestPaths.RepositoryRoot, "docs", "reference", "education-knowledge-map.yaml"),
            generated["docs/reference/education-knowledge-map.yaml"],
            compareSourceCounts: true);

        AssertMapParity(
            Path.Combine(TestPaths.RepositoryRoot, "docs", "reference", "publishing-knowledge-map.yaml"),
            generated["docs/reference/publishing-knowledge-map.yaml"],
            compareSourceCounts: true);

        AssertMapParity(
            Path.Combine(TestPaths.RepositoryRoot, "docs", "reference", "oracle-knowledge-map.yaml"),
            generated["docs/reference/oracle-knowledge-map.yaml"],
            compareSourceCounts: false);
    }

    private static IReadOnlyDictionary<string, string> GenerateWorkspaceFiles(params string[] features)
    {
        var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "knowledge-pack-test", Image = "ubuntu:24.04" },
            Features = features.ToList(),
            Services = [],
            Skills = [],
            Mcp = [],
        };

        return new WorkspaceContentGenerator().Generate(resolver.Resolve(definition));
    }

    private static void AssertMapParity(string checkedInPath, string generatedContent, bool compareSourceCounts)
    {
        var checkedIn = File.ReadAllText(checkedInPath);
        Assert.Equal(ExtractScalar(checkedIn, "id"), ExtractScalar(generatedContent, "id"));
        Assert.Equal(ExtractScalar(checkedIn, "title"), ExtractScalar(generatedContent, "title"));

        if (compareSourceCounts)
        {
            Assert.Equal(CountOccurrences(checkedIn, "  - name:"), CountOccurrences(generatedContent, "  - name:"));
        }
    }

    private static string ExtractScalar(string yaml, string key)
    {
        var prefix = key + ": ";
        var line = yaml.Split('\n').Select(item => item.TrimEnd('\r')).First(item => item.StartsWith(prefix, StringComparison.Ordinal));
        return line[prefix.Length..];
    }

    private static int CountOccurrences(string content, string value)
        => content.Split('\n').Count(line => line.Contains(value, StringComparison.Ordinal));
}
