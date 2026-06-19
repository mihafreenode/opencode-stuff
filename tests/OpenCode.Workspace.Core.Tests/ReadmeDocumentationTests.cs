using System.Text.RegularExpressions;

namespace OpenCode.Workspace.Core.Tests;

public sealed class ReadmeDocumentationTests
{
    [Fact]
    public void Readme_ReferencesFeaturedWorkspaces_AndCurrentDocs()
    {
        var repositoryRoot = TestPaths.RepositoryRoot;
        var readmePath = Path.Combine(repositoryRoot, "README.md");
        var readme = File.ReadAllText(readmePath);

        Assert.Contains("## Oracle Family", readme);
        Assert.Contains("## Featured Workspace: Analytics & Reporting", readme);
        Assert.Contains("## Featured Workspace: Education & STEM", readme);
        Assert.Contains("## Acknowledgements", readme);
        Assert.Contains("Education & STEM Demo", readme);
        Assert.Contains("Oracle PL/SQL Demo", readme);
        Assert.Contains("Oracle APEX Demo", readme);
        Assert.Contains("Oracle APEXlang Demo", readme);
        Assert.Contains("Users do not need prior Python experience to begin.", readme);
        Assert.Contains("No prior Python experience is required to begin.", readme);
        Assert.Contains("AI should be treated as a tutor and assistant rather than a replacement for learning.", readme);
        Assert.Contains("https://www.youtube.com/watch?v=ZBI7BDUK1Es", readme);
        Assert.Contains("Repository", readme);
        Assert.Contains("Workspace Discovery", readme);
        Assert.Contains("Provision Environment", readme);
        Assert.Contains("Read Documentation", readme);
        Assert.Contains("Start Working", readme);

        var oracleIndex = readme.IndexOf("## Oracle Family", StringComparison.Ordinal);
        var analyticsIndex = readme.IndexOf("## Featured Workspace: Analytics & Reporting", StringComparison.Ordinal);
        var educationIndex = readme.IndexOf("## Featured Workspace: Education & STEM", StringComparison.Ordinal);
        var acknowledgementsIndex = readme.IndexOf("## Acknowledgements", StringComparison.Ordinal);
        var documentationFeaturesIndex = readme.IndexOf("## Featured Workspace: Documentation Features", StringComparison.Ordinal);

        Assert.True(oracleIndex >= 0, "Expected Oracle Family section to exist.");
        Assert.True(analyticsIndex > oracleIndex, "Expected Analytics section after Oracle Family.");
        Assert.True(educationIndex > analyticsIndex, "Expected Education section after Analytics.");
        Assert.True(acknowledgementsIndex > educationIndex, "Expected Acknowledgements section after Education.");
        Assert.True(documentationFeaturesIndex > acknowledgementsIndex, "Expected Documentation Features section after Acknowledgements.");

        Assert.DoesNotContain("[Oracle PL/SQL Demo Workspace](docs/oracle-demo.md)", readme);

        var expectedLinks = new[]
        {
            "docs/analytics-workspace.md",
            "docs/education-stem-demo.md",
            "docs/education-stem-workspace.md",
            "docs/oracle-plsql-demo.md",
            "docs/oracle-apex-demo.md",
            "docs/oracle-apexlang-demo.md",
            "docs/oracle-lifecycle-workflows.md",
            "docs/capabilities/analytics.md",
            "docs/capabilities/reporting.md",
            "docs/features/education-knowledge-pack.md",
            "docs/reference/agent-onboarding/analytics.md",
            "docs/reference/agent-onboarding/education.md",
            "docs/workspace-yaml.md",
            "docs/agents-guide.md",
            "docs/first-workspace.md",
            "AGENTS.md",
        };

        foreach (var relativePath in expectedLinks)
        {
            Assert.Contains($"({relativePath})", readme);
            Assert.True(File.Exists(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))), $"Expected README link target to exist: {relativePath}");
        }

        AssertAllMarkdownFileLinksExist(repositoryRoot, readmePath);
    }

    [Fact]
    public void WorkspaceDocs_ForAnalyticsAndEducation_Exist_AndSupportOnboardingFlow()
    {
        var repositoryRoot = TestPaths.RepositoryRoot;
        var analyticsPath = Path.Combine(repositoryRoot, "docs", "analytics-workspace.md");
        var educationPath = Path.Combine(repositoryRoot, "docs", "education-stem-workspace.md");
        var educationDemoPath = Path.Combine(repositoryRoot, "docs", "education-stem-demo.md");
        var firstWorkspacePath = Path.Combine(repositoryRoot, "docs", "first-workspace.md");

        Assert.True(File.Exists(analyticsPath), "Expected analytics workspace doc to exist.");
        Assert.True(File.Exists(educationDemoPath), "Expected education demo doc to exist.");
        Assert.True(File.Exists(educationPath), "Expected education workspace doc to exist.");

        var analytics = File.ReadAllText(analyticsPath);
        var educationDemo = File.ReadAllText(educationDemoPath);
        var education = File.ReadAllText(educationPath);
        var firstWorkspace = File.ReadAllText(firstWorkspacePath);

        Assert.Contains("# Analytics & Reporting Workspace", analytics);
        Assert.Contains("Repository", analytics);
        Assert.Contains("Workspace Discovery", analytics);
        Assert.Contains("Provision Environment", analytics);
        Assert.Contains("Read Documentation", analytics);
        Assert.Contains("Start Working", analytics);
        Assert.Contains("Users do not need prior Python experience to begin.", analytics);
        Assert.Contains("Understanding the generated work remains important.", analytics);
        Assert.Contains("Marimo", analytics);
        Assert.Contains("Pandas", analytics);

        Assert.Contains("# Education & STEM Demo", educationDemo);
        Assert.Contains("education-stem-demo", educationDemo);
        Assert.Contains("Repository", educationDemo);
        Assert.Contains("Workspace Discovery", educationDemo);
        Assert.Contains("Provision Environment", educationDemo);
        Assert.Contains("Read Documentation", educationDemo);
        Assert.Contains("Start Working", educationDemo);

        Assert.Contains("# Education & STEM Workspace", education);
        Assert.Contains("Curiosity", education);
        Assert.Contains("Exploration", education);
        Assert.Contains("Project", education);
        Assert.Contains("Understanding", education);
        Assert.Contains("No prior Python experience is required to begin.", education);
        Assert.Contains("Python knowledge remains valuable.", education);
        Assert.Contains("AI should be treated as a tutor and assistant rather than a replacement for learning.", education);

        Assert.Contains("Repository", firstWorkspace);
        Assert.Contains("Workspace Discovery", firstWorkspace);
        Assert.Contains("Provision Environment", firstWorkspace);
        Assert.Contains("Read Documentation", firstWorkspace);
        Assert.Contains("Start Working", firstWorkspace);
        Assert.Contains("analytics-workspace.md", firstWorkspace);
        Assert.Contains("education-stem-workspace.md", firstWorkspace);

        AssertAllMarkdownFileLinksExist(repositoryRoot, analyticsPath);
        AssertAllMarkdownFileLinksExist(repositoryRoot, educationDemoPath);
        AssertAllMarkdownFileLinksExist(repositoryRoot, educationPath);
        AssertAllMarkdownFileLinksExist(repositoryRoot, firstWorkspacePath);
    }

    private static void AssertAllMarkdownFileLinksExist(string repositoryRoot, string markdownPath)
    {
        var content = File.ReadAllText(markdownPath);
        foreach (Match match in Regex.Matches(content, @"\[[^\]]+\]\(([^)]+)\)"))
        {
            var link = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(link))
            {
                continue;
            }

            var normalizedLink = link.Trim('<', '>');
            if (normalizedLink.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                || normalizedLink.StartsWith('#'))
            {
                continue;
            }

            var pathOnly = normalizedLink.Split('#', 2)[0];
            var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(markdownPath)!, pathOnly.Replace('/', Path.DirectorySeparatorChar)));
            Assert.True(File.Exists(fullPath), $"Expected markdown link target to exist: {markdownPath} -> {link}");
            Assert.True(fullPath.StartsWith(repositoryRoot, StringComparison.OrdinalIgnoreCase), $"Expected markdown link target to stay inside repository: {markdownPath} -> {link}");
        }
    }
}
