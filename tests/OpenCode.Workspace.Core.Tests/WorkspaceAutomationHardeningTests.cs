using System.Text.RegularExpressions;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceAutomationHardeningTests
{
    [Theory]
    [InlineData("analytics", null, "core,analytics-reporting", true, false, false)]
    [InlineData("analytics+education", null, "core,analytics-reporting,education-knowledge-pack", true, false, true)]
    [InlineData("analytics+sample", null, "core,analytics-reporting,analytics-sample-data-pack", true, false, false)]
    [InlineData("publishing", null, "core,publishing-tex", false, true, false)]
    [InlineData("publishing+pack", null, "core,publishing-tex,publishing-knowledge-pack", false, true, false)]
    [InlineData("education+sample", null, "core,education-knowledge-pack,analytics-sample-data-pack", false, false, true)]
    [InlineData("analytics+publishing", null, "core,analytics-reporting,publishing-tex", true, true, false)]
    [InlineData("analytics+education+publishing", null, "core,analytics-reporting,education-knowledge-pack,publishing-tex", true, true, true)]
    [InlineData("analytics+education+publishing+sample", null, "core,analytics-reporting,education-knowledge-pack,publishing-tex,analytics-sample-data-pack", true, true, true)]
    [InlineData("oracle-plsql", "oracle-plsql-demo", null, false, false, false)]
    [InlineData("oracle-apex", "oracle-apex-demo", null, false, false, false)]
    [InlineData("oracle-apexlang", "oracle-apexlang-demo", null, false, false, false)]
    [InlineData("oracle+analytics", "oracle-apex-demo", "analytics-reporting", true, false, false)]
    [InlineData("oracle+analytics+publishing", "oracle-apexlang-demo", "analytics-reporting,publishing-tex", true, true, false)]
    public async Task FeatureMatrix_GeneratesDocsAgentsOnboardingComposeAndValidationScripts(
        string scenario,
        string? templateId,
        string? extraFeatures,
        bool expectAnalyticsValidation,
        bool expectPublishingValidation,
        bool expectEducationOnboarding)
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");

        var root = CreateTempPath($"hardening-{scenario.Replace('+', '-')}");

        try
        {
            var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
            var resolver = CreateResolver(provider);
            var orchestrator = CreateOrchestrator(root, resolver);

            WorkspaceDefinition definition;
            if (templateId is not null)
            {
                definition = new TemplateExpander().Expand($"{scenario}-workspace", provider.LoadTemplates().Single(item => item.Id == templateId));
                if (!string.IsNullOrWhiteSpace(extraFeatures))
                {
                    definition = new WorkspaceDefinition
                    {
                        Workspace = definition.Workspace,
                        Provider = definition.Provider,
                        Runtime = definition.Runtime,
                        Features = [.. definition.Features, .. extraFeatures.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                        Services = definition.Services,
                        Skills = definition.Skills,
                        Mcp = definition.Mcp,
                        Terminal = definition.Terminal,
                        Agent = definition.Agent,
                        Oracle = definition.Oracle,
                        Analytics = definition.Analytics,
                    };
                }
            }
            else
            {
                definition = CreateDefinition($"{scenario}-workspace", extraFeatures!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            var snapshot = await orchestrator.CreateWorkspaceAsync(root, definition, includeRuntimeInspection: false);

            Assert.True(File.Exists(snapshot.Paths.ComposePath));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "AGENTS.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "team-onboarding.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "troubleshooting", "workspace-sessions.md")));

            if (expectAnalyticsValidation)
            {
                Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "validate-analytics-tooling.sh")));
            }

            if (expectPublishingValidation)
            {
                Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "validate-publishing-tooling.sh")));
            }

            var agents = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "AGENTS.md"));
            Assert.Contains("docs/capabilities/README.md", agents);
            Assert.DoesNotContain("docs/reference/agent-onboarding/education.md", ExtractGeneratedOnboardingBlock(agents), StringComparison.Ordinal);
            Assert.Equal(1, CountOccurrences(agents, "<!-- BEGIN GENERATED WORKSPACE CAPABILITY GUIDANCE -->"));
            Assert.Equal(1, CountOccurrences(agents, "<!-- END GENERATED WORKSPACE CAPABILITY GUIDANCE -->"));
            Assert.Equal(1, CountOccurrences(agents, "<!-- BEGIN GENERATED ONBOARDING LINKS -->"));
            Assert.Equal(1, CountOccurrences(agents, "<!-- END GENERATED ONBOARDING LINKS -->"));

            if (expectEducationOnboarding)
            {
                Assert.Contains("docs/reference/agent-onboarding/education.md", agents);
            }

            AssertGeneratedMarkdownLinksResolve(snapshot.Paths.RootPath);
            AssertPlainWorkspaceReferencesResolve(snapshot.Paths.RootPath);
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Fact]
    public async Task EducationStemDemo_GenerationIsDeterministicAcrossSeparateRoots()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");

        var firstRoot = CreateTempPath("deterministic-demo-a");
        var secondRoot = CreateTempPath("deterministic-demo-b");

        try
        {
            var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
            var resolver = CreateResolver(provider);
            var template = provider.LoadTemplates().Single(item => item.Id == "education-stem-demo");
            var definition = new TemplateExpander().Expand("education-demo", template);

            var first = await CreateOrchestrator(firstRoot, resolver).CreateWorkspaceAsync(firstRoot, definition, includeRuntimeInspection: false);
            var second = await CreateOrchestrator(secondRoot, resolver).CreateWorkspaceAsync(secondRoot, definition, includeRuntimeInspection: false);

            foreach (var relativePath in new[]
            {
                "README.md",
                Path.Combine("AGENTS.md"),
                Path.Combine("docs", "team-onboarding.md"),
                Path.Combine("docs", "reference", "education-knowledge-map.yaml"),
                Path.Combine("docs", "learning-path.md"),
                Path.Combine("docs", "projects.md"),
                Path.Combine("docs", "example-prompts.md"),
                Path.Combine("examples", "survey-analysis", "analysis.py"),
                Path.Combine("examples", "machine-learning-intro", "model.py"),
                Path.Combine("scripts", "validate-analytics-tooling.sh"),
                Path.Combine("scripts", "validate-publishing-tooling.sh"),
            })
            {
                Assert.Equal(
                    File.ReadAllText(Path.Combine(first.Paths.RootPath, relativePath)),
                    File.ReadAllText(Path.Combine(second.Paths.RootPath, relativePath)));
            }
        }
        finally
        {
            DeleteTempPath(firstRoot);
            DeleteTempPath(secondRoot);
        }
    }

    [Fact]
    public void RepositoryDocumentation_CoversExpectedAnalyticsEducationPublishingAndOracleContent()
    {
        var root = TestPaths.RepositoryRoot;
        var analytics = File.ReadAllText(Path.Combine(root, "docs", "analytics-workspace.md"));
        var education = File.ReadAllText(Path.Combine(root, "docs", "education-stem-workspace.md"));
        var publishing = File.ReadAllText(Path.Combine(root, "docs", "reference", "agent-onboarding", "publishing.md"));
        var oracle = File.ReadAllText(Path.Combine(root, "docs", "capabilities", "oracle.md"));

        Assert.Contains("Marimo", analytics);
        Assert.Contains("Pandas", analytics);
        Assert.Contains("Excel", analytics);
        Assert.Contains("AI-Assisted Learning Guidance", analytics, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("students", education, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("teachers", education, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parents", education, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("self-learners", education, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Markdown", publishing);
        Assert.Contains("Typst", publishing);
        Assert.Contains("LaTeX", publishing);
        Assert.Contains("qpdf --check", publishing);

        Assert.Contains("APEX", oracle);
        Assert.Contains("APEXlang", oracle);
        Assert.Contains("ORDS", oracle);
    }

    [Fact]
    public void SkillPacks_AreReferencedAndNoKnowledgePackSkillRefIsOrphaned()
    {
        var root = TestPaths.RepositoryRoot;
        var provider = new BuiltInCatalogProvider(Path.Combine(root, "catalog"));
        var packs = provider.LoadKnowledgePacks();
        var skillPackDoc = File.ReadAllText(Path.Combine(root, "docs", "skill-packs.md"));
        var onboardingDocs = Directory.GetFiles(Path.Combine(root, "docs", "reference", "agent-onboarding"), "*.md", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToList();
        var existingSkills = Directory.GetFiles(Path.Combine(root, "skills"), "*.md", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(existingSkills);

        foreach (var skillPath in existingSkills)
        {
            var referenced = skillPackDoc.Contains(skillPath, StringComparison.Ordinal)
                || packs.Any(pack => pack.SkillRefs.Contains(skillPath, StringComparer.OrdinalIgnoreCase))
                || onboardingDocs.Any(doc => doc.Contains(skillPath, StringComparison.Ordinal));

            Assert.True(referenced, $"Expected skill to be reachable from docs or knowledge packs: {skillPath}");
        }

        foreach (var skillRef in packs.SelectMany(pack => pack.SkillRefs).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Assert.Contains(skillRef, existingSkills, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TemplateManifests_ExposeConsistentMetadataForAllTemplates()
    {
        var root = TestPaths.RepositoryRoot;
        var templatePaths = Directory.GetFiles(Path.Combine(root, "catalog", "templates"), "*.yaml", SearchOption.TopDirectoryOnly);

        foreach (var templatePath in templatePaths)
        {
            var content = File.ReadAllText(templatePath);
            Assert.Contains("id:", content);
            Assert.Contains("displayName:", content);
            Assert.Contains("description:", content);
            Assert.Contains("category:", content);
            Assert.Contains("lifecycle:", content);
            Assert.Contains("features:", content);
        }
    }

    [Fact]
    public void DocumentationNavigation_RunsFromReadmeThroughIndexToCapabilitiesAndSpecializedWorkspaces()
    {
        var root = TestPaths.RepositoryRoot;
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var index = File.ReadAllText(Path.Combine(root, "docs", "index.md"));

        Assert.Contains("[Documentation index](docs/index.md)", readme, StringComparison.Ordinal);
        Assert.Contains("[Capability Catalog](capabilities/README.md)", index, StringComparison.Ordinal);
        Assert.Contains("[Analytics And Reporting](analytics-workspace.md)", index, StringComparison.Ordinal);
        Assert.Contains("[Education And STEM](education-stem-workspace.md)", index, StringComparison.Ordinal);
        Assert.Contains("[Skill Packs](skill-packs.md)", index, StringComparison.Ordinal);
        Assert.Contains("[Oracle And Oracle APEX Integration](integrations/oracle-apex.md)", index, StringComparison.Ordinal);
    }

    private static string ExtractGeneratedOnboardingBlock(string agents)
    {
        const string beginMarker = "<!-- BEGIN GENERATED ONBOARDING LINKS -->";
        const string endMarker = "<!-- END GENERATED ONBOARDING LINKS -->";
        var begin = agents.IndexOf(beginMarker, StringComparison.Ordinal);
        var end = agents.IndexOf(endMarker, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end >= begin, "Expected generated onboarding block in AGENTS.md.");
        return agents.Substring(begin, end + endMarker.Length - begin);
    }

    private static void AssertGeneratedMarkdownLinksResolve(string workspaceRoot)
    {
        foreach (var markdownPath in Directory.GetFiles(workspaceRoot, "*.md", SearchOption.AllDirectories))
        {
            var relativeMarkdownPath = Path.GetRelativePath(workspaceRoot, markdownPath).Replace(Path.DirectorySeparatorChar, '/');
            if (relativeMarkdownPath.StartsWith("mounts/", StringComparison.OrdinalIgnoreCase)
                || relativeMarkdownPath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = File.ReadAllText(markdownPath);
            foreach (Match match in Regex.Matches(content, @"\[[^\]]+\]\(([^)]+)\)"))
            {
                var link = match.Groups[1].Value.Trim('<', '>');
                if (string.IsNullOrWhiteSpace(link)
                    || link.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    || link.StartsWith('#'))
                {
                    continue;
                }

                var pathOnly = link.Split('#', 2)[0];
                var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(markdownPath)!, pathOnly.Replace('/', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(fullPath), $"Expected generated markdown link target to exist: {relativeMarkdownPath} -> {link}");
            }
        }
    }

    private static int CountOccurrences(string content, string value)
        => content.Split('\n').Count(line => line.Contains(value, StringComparison.Ordinal));

    private static void AssertPlainWorkspaceReferencesResolve(string workspaceRoot)
    {
        var candidates = new List<string>
        {
            Path.Combine(workspaceRoot, "README.md"),
            Path.Combine(workspaceRoot, "AGENTS.md"),
            Path.Combine(workspaceRoot, "docs", "team-onboarding.md"),
        };

        var referenceRoot = Path.Combine(workspaceRoot, "docs", "reference");
        if (Directory.Exists(referenceRoot))
        {
            candidates.AddRange(Directory.GetFiles(referenceRoot, "*.md", SearchOption.AllDirectories));
            candidates.AddRange(Directory.GetFiles(referenceRoot, "*.yaml", SearchOption.AllDirectories));
        }

        var filePaths = candidates
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in filePaths)
        {
            var relativeFilePath = Path.GetRelativePath(workspaceRoot, filePath).Replace(Path.DirectorySeparatorChar, '/');
            var content = File.ReadAllText(filePath);
            foreach (Match match in Regex.Matches(content, @"(?<![A-Za-z0-9_./-])(README\.md|AGENTS\.md|docs/[A-Za-z0-9_./-]+\.(?:md|yaml)|skills/[A-Za-z0-9_./-]+\.md|scripts/[A-Za-z0-9_./-]+\.sh|examples/[A-Za-z0-9_./-]+\.(?:md|py|csv|typ|tex|bib|svg|json|xlsx))(?![A-Za-z0-9_./-])"))
            {
                var referencedPath = match.Groups[1].Value;
                var fullPath = Path.Combine(workspaceRoot, referencedPath.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(fullPath), $"Expected generated plain path reference to exist: {relativeFilePath} -> {referencedPath}");
            }
        }
    }

    private static WorkspaceDefinition CreateDefinition(string workspaceName, IReadOnlyList<string> features)
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = workspaceName, Image = "ubuntu:24.04" },
            Provider = new WorkspaceProviderDefinition { Type = "git" },
            Runtime = new WorkspaceRuntimeDefinition { Default = "default" },
            Features = features.ToList(),
            Services = features.Contains("oracle-apex-demo", StringComparer.OrdinalIgnoreCase) || features.Contains("oracle-apexlang-demo", StringComparer.OrdinalIgnoreCase)
                ? ["oracle-demo", "oracle-ords"]
                : features.Contains("oracle-demo", StringComparer.OrdinalIgnoreCase)
                    ? ["oracle-demo"]
                    : [],
            Skills = [],
            Mcp = [],
        };

    private static WorkspaceResolver CreateResolver(BuiltInCatalogProvider provider)
        => new(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());

    private static WorkspaceOrchestrator CreateOrchestrator(string root, WorkspaceResolver resolver)
    {
        var ignorePolicy = new WorkspaceIgnorePolicyService();
        return new WorkspaceOrchestrator(
            new WorkspaceYamlService(),
            new WorkspaceDiscoveryService(),
            new WorkspaceRepository(GetAppDataRoot(root)),
            resolver,
            new ComposeGenerator(),
            new EnvironmentFileGenerator(),
            new ProvisioningScriptGenerator(),
            new TerminalArtifactsGenerator(),
            new AttachArtifactsGenerator(),
            new WorkspaceContentGenerator(),
            new WorkspaceAppliedStateService(),
            new WorkspaceCheckpointService(),
            new WorkspaceTimelineService(),
            new WorkspaceSafetyService(),
            ignorePolicy,
            new GitWorkspaceProvider(new ProcessRunner(), ignorePolicy),
            new DockerService(new ProcessRunner()),
            new NoOpTerminalLauncher());
    }

    private static string GetAppDataRoot(string root)
        => Path.Combine(Path.GetDirectoryName(root) ?? Path.GetTempPath(), $"{Path.GetFileName(root)}-appdata");

    private static bool CanRunGit()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            process?.WaitForExit(5000);
            return process is not null && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string CreateTempPath(string prefix) => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");

    private static void DeleteTempPath(string path)
    {
        if (Directory.Exists(path))
        {
            TestFileSystem.DeleteDirectoryIfExists(path);
        }

        var appDataRoot = GetAppDataRoot(path);
        if (Directory.Exists(appDataRoot))
        {
            TestFileSystem.DeleteDirectoryIfExists(appDataRoot);
        }
    }

    private sealed class NoOpTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
