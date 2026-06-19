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
            "docs/philosophy.md",
            "docs/design-principles.md",
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
    public void Philosophy_AndDesignPrinciples_Docs_Expose_OpenSorcery_Principles()
    {
        var repositoryRoot = TestPaths.RepositoryRoot;
        var philosophyPath = Path.Combine(repositoryRoot, "docs", "philosophy.md");
        var designPrinciplesPath = Path.Combine(repositoryRoot, "docs", "design-principles.md");
        var agentsGuidePath = Path.Combine(repositoryRoot, "docs", "agents-guide.md");

        Assert.True(File.Exists(philosophyPath), "Expected philosophy doc to exist.");
        Assert.True(File.Exists(designPrinciplesPath), "Expected design principles doc to exist.");
        Assert.True(File.Exists(agentsGuidePath), "Expected agents guide doc to exist.");

        var philosophy = File.ReadAllText(philosophyPath);
        var designPrinciples = File.ReadAllText(designPrinciplesPath);
        var agentsGuide = File.ReadAllText(agentsGuidePath);

        Assert.Contains("## Open Sorcery", philosophy);
        Assert.Contains("## There Is No Magic", philosophy);
        Assert.Contains("## Open Sorcery, Not Wizardry", philosophy);
        Assert.Contains("## Spells, Spellbooks, and Evidence", philosophy);
        Assert.Contains("## Maps Over Mazes", philosophy);
        Assert.Contains("## Knowledge Gravity", philosophy);
        Assert.Contains("## Portable Understanding", philosophy);
        Assert.Contains("## From Apprentice To Teacher", philosophy);
        Assert.Contains("## Preserve The Ladder", philosophy);
        Assert.Contains("## Visible Systems", philosophy);
        Assert.Contains("## Relationship To Existing Philosophy", philosophy);
        Assert.Contains("## Educational Perspective", philosophy);
        Assert.Contains("## Business Perspective", philosophy);
        Assert.Contains("Software may feel magical. Its operation should never be mysterious.", philosophy);
        Assert.Contains("The goal is not to eliminate magic. The goal is to make the magic inspectable.", philosophy);
        Assert.Contains("Knowledge should live in repositories, specifications, documentation, and tests—not in individuals.", philosophy);
        Assert.Contains("A spell is reusable knowledge.", philosophy);
        Assert.Contains("A repository is a spellbook.", philosophy);
        Assert.Contains("Every important spell should leave evidence.", philosophy);
        Assert.Contains("The best spells can be taught.", philosophy);
        Assert.Contains("Knowledge becomes durable when it can survive the loss of its creator.", philosophy);
        Assert.Contains("If a result matters, there should be evidence explaining how it was produced.", philosophy);
        Assert.Contains("Do not remove complexity. Make it navigable.", philosophy);
        Assert.Contains("A good diagram is a map.", philosophy);
        Assert.Contains("A good specification is a map of intent.", philosophy);
        Assert.Contains("A good test is a map of expectations.", philosophy);
        Assert.Contains("Useful knowledge should attract more knowledge.", philosophy);
        Assert.Contains("A solved problem should become easier to solve again.", philosophy);
        Assert.Contains("Repositories should become easier to understand as they grow.", philosophy);
        Assert.Contains("Knowledge is portable when understanding is portable.", philosophy);
        Assert.Contains("The best onboarding is already in the repository.", philosophy);
        Assert.Contains("A repository should explain itself.", philosophy);
        Assert.Contains("The best proof of understanding is explanation.", philosophy);
        Assert.Contains("The highest form of learning is teaching.", philosophy);
        Assert.Contains("Knowledge becomes durable when it can be taught.", philosophy);
        Assert.Contains("OpenCode should make systems easier to use without removing the path to understanding them.", philosophy);
        Assert.Contains("Not every user needs to become an expert, but the system should preserve the ability to move from use to inspect to modify to contribute to teach.", philosophy);
        Assert.Contains("Invisible knowledge is fragile knowledge.", philosophy);
        Assert.Contains("What cannot be seen cannot easily be taught.", philosophy);
        Assert.Contains("Visibility is a prerequisite for understanding.", philosophy);
        Assert.Contains("If knowledge matters, make it visible.", philosophy);
        Assert.Contains("Good tooling feels magical. Good engineering makes the magic auditable.", philosophy);
        Assert.Contains("Users should never lose their work because a tool, service, agent, or model failed.", philosophy);
        Assert.Contains("[Philosophy](philosophy.md)", philosophy);
        Assert.Contains("[Design Principles](design-principles.md)", philosophy);
        Assert.Contains("[AGENTS.md Guide](agents-guide.md)", philosophy);
        Assert.Contains("[Team Onboarding](team-onboarding.md)", philosophy);
        Assert.Contains("[Agent Transparency](agents-guide.md#agent-transparency)", philosophy);
        Assert.Contains("[Repository as source of truth](workspace-yaml.md)", philosophy);
        Assert.Contains("[Durable workspaces](concepts/workspace.md)", philosophy);
        Assert.Contains("[Save Points](concepts/save-point.md)", philosophy);
        Assert.Contains("[Recovery workflows](architecture/recovery-model.md)", philosophy);
        Assert.Contains("[Documentation-first onboarding](first-workspace.md)", philosophy);
        Assert.Contains("[Ownership and trust](capabilities/repository.md)", philosophy);
        Assert.Contains("[Repository Workflows](capabilities/repository.md)", philosophy);

        Assert.Contains("# Design Principles", designPrinciples);
        Assert.Contains("## Open Sorcery, With Receipts", designPrinciples);
        Assert.Contains("## Repository Before Runtime", designPrinciples);
        Assert.Contains("## Generated Artifacts Must Stay Inspectable", designPrinciples);
        Assert.Contains("## AI Assists, But Does Not Own", designPrinciples);
        Assert.Contains("## Recovery Is A Core Design Constraint", designPrinciples);
        Assert.Contains("## Documentation Is Product Surface", designPrinciples);
        Assert.Contains("## Maps, Visibility, and Portable Understanding", designPrinciples);
        Assert.Contains("## Preserve The Path To Understanding", designPrinciples);
        Assert.Contains("## Knowledge Transfer As A Design Requirement", designPrinciples);
        Assert.Contains("Important capabilities should leave a visible path to where behavior is defined, changed, validated, and recovered.", designPrinciples);
        Assert.Contains("[Philosophy](philosophy.md)", designPrinciples);
        Assert.Contains("[AGENTS.md Guide](agents-guide.md)", designPrinciples);
        Assert.Contains("[Team Onboarding](team-onboarding.md)", designPrinciples);

        Assert.Contains("## Agent Transparency", agentsGuide);
        Assert.Contains("AI is an accelerator, not a source of truth.", agentsGuide);
        Assert.Contains("[Portable Understanding](philosophy.md#portable-understanding)", agentsGuide);
        Assert.Contains("[Recovery Model](architecture/recovery-model.md)", agentsGuide);
        Assert.Contains("[Philosophy](philosophy.md)", agentsGuide);
        Assert.Contains("[Design Principles](design-principles.md)", agentsGuide);

        AssertAllMarkdownFileLinksExist(repositoryRoot, philosophyPath);
        AssertAllMarkdownFileLinksExist(repositoryRoot, designPrinciplesPath);
        AssertAllMarkdownFileLinksExist(repositoryRoot, agentsGuidePath);
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
