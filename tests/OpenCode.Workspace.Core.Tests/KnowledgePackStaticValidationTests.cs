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
}
