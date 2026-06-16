using System.Text.RegularExpressions;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceCapabilityCatalogTests
{
    [Fact]
    public async Task NewWorkspaceGeneration_CreatesCapabilityCatalogAndAgentsGuidance()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        using var fixture = new WorkspaceCapabilityCatalogFixture();
        var snapshot = await fixture.CreateWorkspaceAsync(
            "documentation-capabilities",
            ["core", "document-processing", "ocr-processing", "spellcheck"]);

        var catalogPath = Path.Combine(snapshot.Paths.RootPath, "docs", "capabilities", "README.md");
        var agentsPath = Path.Combine(snapshot.Paths.RootPath, "AGENTS.md");

        Assert.True(File.Exists(catalogPath));
        Assert.True(File.Exists(agentsPath));

        var catalog = File.ReadAllText(catalogPath);
        var agents = File.ReadAllText(agentsPath);

        Assert.Contains("Repository Workflows", catalog);
        Assert.Contains("Documentation", catalog);
        Assert.Contains("Document Processing", catalog);
        Assert.Contains("OCR", catalog);
        Assert.Contains("Spell Checking", catalog);
        Assert.Contains("Analytics", catalog);
        Assert.Contains("Reporting", catalog);
        Assert.Contains("Testing", catalog);
        Assert.Contains("Localization", catalog);
        Assert.Contains("Read more: [Document Processing](document-processing.md)", catalog);

        Assert.Contains("docs/capabilities/README.md", agents);
        Assert.Contains("BEGIN GENERATED WORKSPACE CAPABILITY GUIDANCE", agents);
        Assert.Contains("END GENERATED WORKSPACE CAPABILITY GUIDANCE", agents);
        Assert.Contains("BEGIN GENERATED ONBOARDING LINKS", agents);
        Assert.Contains("END GENERATED ONBOARDING LINKS", agents);
        Assert.Contains("## Enabled Onboarding Materials", agents);
        Assert.Contains("Do not scan the repository first.", agents);
        Assert.Contains("docs/documentation-features.md", agents);
        Assert.Contains("docs/capabilities/document-processing.md", agents);
        Assert.Contains("docs/capabilities/ocr.md", agents);
        Assert.Contains("docs/capabilities/spell-checking.md", agents);

        AssertCapabilityLinksResolve(snapshot.Paths.RootPath);
    }

    [Fact]
    public async Task Regenerate_UpdatesCapabilityDocsAndPreservesUserAuthoredFiles()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        using var fixture = new WorkspaceCapabilityCatalogFixture();
        var snapshot = await fixture.CreateWorkspaceAsync(
            "existing-documentation-workspace",
            ["core", "document-processing", "ocr-processing", "spellcheck"]);

        var catalogPath = Path.Combine(snapshot.Paths.RootPath, "docs", "capabilities", "README.md");
        var testingPath = Path.Combine(snapshot.Paths.RootPath, "docs", "capabilities", "testing.md");
        var userDocPath = Path.Combine(snapshot.Paths.RootPath, "docs", "user-notes.md");
        var mountedMarkerPath = Path.Combine(snapshot.Paths.RootPath, "mounts", "user", "oracle-volume-marker.txt");

        Directory.CreateDirectory(Path.GetDirectoryName(userDocPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(mountedMarkerPath)!);
        File.WriteAllText(catalogPath, "STALE CATALOG");
        File.WriteAllText(testingPath, "STALE TESTING PAGE");
        File.WriteAllText(userDocPath, "user-owned docs survive regenerate");
        File.WriteAllText(mountedMarkerPath, "persistent volume marker");

        await fixture.Orchestrator.RegenerateAsync(snapshot);
        await fixture.Orchestrator.RegenerateAsync(snapshot);

        var catalog = File.ReadAllText(catalogPath);
        var testing = File.ReadAllText(testingPath);

        Assert.DoesNotContain("STALE CATALOG", catalog);
        Assert.DoesNotContain("STALE TESTING PAGE", testing);
        Assert.Contains("Document Processing", catalog);
        Assert.Contains("Playwright", testing);
        Assert.Equal("user-owned docs survive regenerate", File.ReadAllText(userDocPath));
        Assert.Equal("persistent volume marker", File.ReadAllText(mountedMarkerPath));
        AssertCapabilityLinksResolve(snapshot.Paths.RootPath);
    }

    [Fact]
    public async Task Regenerate_PatchesOnlyGeneratedAgentsBlock_AndIsIdempotent()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        using var fixture = new WorkspaceCapabilityCatalogFixture();
        var snapshot = await fixture.CreateWorkspaceAsync(
            "agents-preservation-workspace",
            ["core", "document-processing", "ocr-processing", "spellcheck"]);

        var agentsPath = Path.Combine(snapshot.Paths.RootPath, "AGENTS.md");
        var customAgents = string.Join("\n",
        [
            "# Local Notes",
            string.Empty,
            "User content before block.",
            string.Empty,
            "<!-- BEGIN GENERATED WORKSPACE CAPABILITY GUIDANCE -->",
            "STALE GENERATED CONTENT",
            "<!-- END GENERATED WORKSPACE CAPABILITY GUIDANCE -->",
            string.Empty,
            "User content after block.",
            string.Empty,
        ]);
        File.WriteAllText(agentsPath, customAgents);

        await fixture.Orchestrator.RegenerateAsync(snapshot);
        var agentsAfterFirstRegenerate = File.ReadAllText(agentsPath);
        await fixture.Orchestrator.RegenerateAsync(snapshot);

        var agents = File.ReadAllText(agentsPath);
        var onboardingBlock = ExtractBlock(agents, "<!-- BEGIN GENERATED ONBOARDING LINKS -->", "<!-- END GENERATED ONBOARDING LINKS -->");
        Assert.Contains("User content before block.", agents);
        Assert.Contains("User content after block.", agents);
        Assert.DoesNotContain("STALE GENERATED CONTENT", agents);
        Assert.Single(Regex.Matches(agents, Regex.Escape("<!-- BEGIN GENERATED WORKSPACE CAPABILITY GUIDANCE -->")).Cast<Match>());
        Assert.Single(Regex.Matches(agents, Regex.Escape("<!-- END GENERATED WORKSPACE CAPABILITY GUIDANCE -->")).Cast<Match>());
        Assert.Single(Regex.Matches(agents, Regex.Escape("<!-- BEGIN GENERATED ONBOARDING LINKS -->")).Cast<Match>());
        Assert.Single(Regex.Matches(agents, Regex.Escape("<!-- END GENERATED ONBOARDING LINKS -->")).Cast<Match>());
        Assert.Contains("docs/capabilities/README.md", agents);
        Assert.Contains("docs/capabilities/document-processing.md", agents);
        Assert.Contains("docs/documentation-features.md", agents);
        Assert.Equal(agentsAfterFirstRegenerate, agents);
        Assert.Single(Regex.Matches(onboardingBlock, Regex.Escape("docs/documentation-features.md")).Cast<Match>());
    }

    [Fact]
    public async Task OracleWorkspace_RegenerateMaintainsOracleCapabilityDiscovery()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        using var fixture = new WorkspaceCapabilityCatalogFixture();
        var snapshot = await fixture.CreateWorkspaceFromTemplateAsync("oracle-apexlang-demo", "oracle-capability-workspace");

        var catalogPath = Path.Combine(snapshot.Paths.RootPath, "docs", "capabilities", "README.md");
        var oraclePath = Path.Combine(snapshot.Paths.RootPath, "docs", "capabilities", "oracle.md");
        var agentsPath = Path.Combine(snapshot.Paths.RootPath, "AGENTS.md");
        var volumeMarkerPath = Path.Combine(snapshot.Paths.RootPath, "mounts", "user", "oracle-volume-marker.txt");

        Directory.CreateDirectory(Path.GetDirectoryName(volumeMarkerPath)!);
        File.WriteAllText(volumeMarkerPath, "oracle-data-preserved");
        File.WriteAllText(catalogPath, "STALE ORACLE CATALOG");

        await fixture.Orchestrator.RegenerateAsync(snapshot);

        var catalog = File.ReadAllText(catalogPath);
        var oracle = File.ReadAllText(oraclePath);
        var agents = File.ReadAllText(agentsPath);

        Assert.DoesNotContain("STALE ORACLE CATALOG", catalog);
        Assert.Contains("Oracle", catalog);
        Assert.Contains("SQLcl", oracle);
        Assert.Contains("ORDS", oracle);
        Assert.Contains("APEXlang", oracle);
        Assert.Contains("../oracle-tools/README.md", oracle);
        Assert.Contains("../oracle-plsql-demo.md", oracle);
        Assert.Contains("../oracle-apex-demo.md", oracle);
        Assert.Contains("../oracle-apexlang-demo.md", oracle);
        Assert.Contains("## Enabled Onboarding Materials", agents);
        Assert.Contains("docs/oracle-plsql-demo.md", agents);
        Assert.Contains("docs/oracle-apex-demo.md", agents);
        Assert.Contains("docs/oracle-apexlang-demo.md", agents);
        Assert.Contains("docs/oracle-tools/README.md", agents);
        Assert.Contains("docs/oracle-samples.md", agents);
        Assert.Contains("PL/SQL", agents);
        Assert.Contains("APEX", agents);
        Assert.Contains("APEXlang", agents);
        Assert.Equal("oracle-data-preserved", File.ReadAllText(volumeMarkerPath));
        AssertCapabilityLinksResolve(snapshot.Paths.RootPath);
    }

    [Fact]
    public void AgentsMerge_AppendsGeneratedBlockWhenMissing()
    {
        var original = "# Notes\n\nUser-authored content.\n";
        var merged = WorkspaceContentGenerator.MergeGeneratedCapabilityGuidance(original, "## Workspace Capability Discovery\n\n- docs/capabilities/README.md");

        Assert.Contains("User-authored content.", merged);
        Assert.Contains("BEGIN GENERATED WORKSPACE CAPABILITY GUIDANCE", merged);
        Assert.Contains("docs/capabilities/README.md", merged);
    }

    [Fact]
    public void OnboardingMerge_AppendsGeneratedBlockWhenMissing()
    {
        var original = "# Notes\n\nUser-authored content.\n";
        var merged = WorkspaceContentGenerator.MergeGeneratedOnboardingLinks(original, "## Enabled Onboarding Materials\n\nAnalytics:\n\n- docs/capabilities/analytics.md");

        Assert.Contains("User-authored content.", merged);
        Assert.Contains("BEGIN GENERATED ONBOARDING LINKS", merged);
        Assert.Contains("docs/capabilities/analytics.md", merged);
    }

    private static void AssertCapabilityLinksResolve(string workspaceRoot)
    {
        var capabilityRoot = Path.Combine(workspaceRoot, "docs", "capabilities");
        foreach (var filePath in Directory.GetFiles(capabilityRoot, "*.md", SearchOption.TopDirectoryOnly))
        {
            var content = File.ReadAllText(filePath);
            foreach (Match match in Regex.Matches(content, @"\[[^\]]+\]\(([^)]+)\)"))
            {
                var link = match.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(link)
                    || link.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    || link.StartsWith('#'))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, link.Replace('/', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(fullPath), $"Expected capability link target to exist: {filePath} -> {link}");
            }
        }
    }

    private static string ExtractBlock(string content, string beginMarker, string endMarker)
    {
        var beginIndex = content.IndexOf(beginMarker, StringComparison.Ordinal);
        var endIndex = content.IndexOf(endMarker, StringComparison.Ordinal);
        Assert.True(beginIndex >= 0 && endIndex >= beginIndex, $"Expected block markers '{beginMarker}' and '{endMarker}'.");
        return content.Substring(beginIndex, endIndex + endMarker.Length - beginIndex);
    }

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

    private sealed class WorkspaceCapabilityCatalogFixture : IDisposable
    {
        private readonly BuiltInCatalogProvider _catalogProvider;

        public WorkspaceCapabilityCatalogFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"capability-catalog-{Guid.NewGuid():N}");
            AppDataRoot = Path.Combine(Root, ".appdata");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(AppDataRoot);
            _catalogProvider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
            Orchestrator = CreateOrchestrator();
        }

        public string Root { get; }

        public string AppDataRoot { get; }

        public WorkspaceOrchestrator Orchestrator { get; }

        public Task<WorkspaceSnapshot> CreateWorkspaceAsync(string workspaceName, IReadOnlyList<string> features)
        {
            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = workspaceName, Image = "ubuntu:24.04" },
                Features = features.ToList(),
                Services = [],
                Skills = [],
                Mcp = [],
            };

            return Orchestrator.CreateWorkspaceAsync(Path.Combine(Root, workspaceName), definition, includeRuntimeInspection: false);
        }

        public Task<WorkspaceSnapshot> CreateWorkspaceFromTemplateAsync(string templateId, string workspaceName)
        {
            var template = _catalogProvider.LoadTemplates().Single(item => item.Id == templateId);
            var definition = new TemplateExpander().Expand(workspaceName, template);
            return Orchestrator.CreateWorkspaceAsync(Path.Combine(Root, workspaceName), definition, includeRuntimeInspection: false);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                TestFileSystem.DeleteDirectoryIfExists(Root);
            }
        }

        private WorkspaceOrchestrator CreateOrchestrator()
        {
            var ignorePolicyService = new WorkspaceIgnorePolicyService();
            return new WorkspaceOrchestrator(
                new WorkspaceYamlService(),
                new WorkspaceDiscoveryService(),
                new WorkspaceRepository(AppDataRoot),
                new WorkspaceResolver(_catalogProvider.LoadFeatures(), _catalogProvider.LoadServices(), _catalogProvider.LoadCapabilities()),
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
                ignorePolicyService,
                new GitWorkspaceProvider(new ProcessRunner(), ignorePolicyService),
                new DockerService(new ProcessRunner()),
                new NoOpTerminalLauncher());
        }

        private sealed class NoOpTerminalLauncher : ITerminalLauncher
        {
            public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }
    }
}
