using System.Diagnostics;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceLifecycleRegressionTests
{
    [Fact]
    public async Task Regenerate_AnalyticsWorkspace_PreservesEditedDurableSourcesAndUserData()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var root = CreateTempPath("analytics-regenerate");

        try
        {
            var orchestrator = CreateOrchestrator(root, CreateCatalogResolver());
            var snapshot = await orchestrator.CreateWorkspaceAsync(root, CreateDefinition("analytics-regression", ["core", "analytics-reporting", "education-knowledge-pack", "analytics-sample-data-pack"]), includeRuntimeInspection: false);

            var analysisPath = Path.Combine(snapshot.Paths.RootPath, "examples", "analytics", "analysis.py");
            var reportPath = Path.Combine(snapshot.Paths.RootPath, "examples", "analytics", "report.md");
            var userDataPath = Path.Combine(snapshot.Paths.RootPath, "examples", "analytics", "sample-data", "user-measurements.csv");

            File.WriteAllText(analysisPath, "# user analytics script\nprint('keep analytics changes')\n");
            File.WriteAllText(reportPath, "# user report\n");
            File.WriteAllText(userDataPath, "value\n42\n");

            await orchestrator.RegenerateAsync(snapshot);

            Assert.Equal("# user analytics script\nprint('keep analytics changes')\n", File.ReadAllText(analysisPath));
            Assert.Equal("# user report\n", File.ReadAllText(reportPath));
            Assert.Equal("value\n42\n", File.ReadAllText(userDataPath));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "smoke-marimo.sh")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "agent-onboarding", "analytics.md")));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Fact]
    public async Task Regenerate_PublishingWorkspace_PreservesEditedDurableSources()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var root = CreateTempPath("publishing-regenerate");

        try
        {
            var orchestrator = CreateOrchestrator(root, CreateCatalogResolver());
            var snapshot = await orchestrator.CreateWorkspaceAsync(root, CreateDefinition("publishing-regression", ["core", "publishing-tex", "publishing-knowledge-pack"]), includeRuntimeInspection: false);

            var typstPath = Path.Combine(snapshot.Paths.RootPath, "examples", "publishing", "report.typ");
            var paperPath = Path.Combine(snapshot.Paths.RootPath, "examples", "publishing", "paper.tex");
            var bibPath = Path.Combine(snapshot.Paths.RootPath, "examples", "publishing", "bibliography.bib");

            File.WriteAllText(typstPath, "= user typst\n");
            File.WriteAllText(paperPath, "\\documentclass{article}\n\\begin{document}user\\end{document}\n");
            File.WriteAllText(bibPath, "@book{user,title={Kept}}\n");

            await orchestrator.RegenerateAsync(snapshot);

            Assert.Equal("= user typst\n", File.ReadAllText(typstPath));
            Assert.Equal("\\documentclass{article}\n\\begin{document}user\\end{document}\n", File.ReadAllText(paperPath));
            Assert.Equal("@book{user,title={Kept}}\n", File.ReadAllText(bibPath));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "demo-publishing-workflows.sh")));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Fact]
    public async Task Regenerate_EducationKnowledgePack_DoesNotDuplicateOnboardingOrReferenceLinks()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var root = CreateTempPath("education-regenerate");

        try
        {
            var orchestrator = CreateOrchestrator(root, CreateCatalogResolver());
            var snapshot = await orchestrator.CreateWorkspaceAsync(root, CreateDefinition("education-repeat", ["core", "analytics-reporting", "education-knowledge-pack"]), includeRuntimeInspection: false);

            await orchestrator.RegenerateAsync(snapshot);
            await orchestrator.RegenerateAsync(snapshot);

            var agents = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "AGENTS.md"));
            var knowledgeMap = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "education-knowledge-map.yaml"));

            Assert.Equal(1, agents.Split('\n').Count(line => line.Contains("docs/reference/agent-onboarding/education.md", StringComparison.Ordinal)));
            Assert.Equal(1, agents.Split('\n').Count(line => line.Contains("docs/reference/agent-onboarding/analytics.md", StringComparison.Ordinal)));
            Assert.Equal(1, knowledgeMap.Split('\n').Count(line => line.Trim() == "- docs/reference/agent-onboarding/education.md"));
            Assert.Equal(1, knowledgeMap.Split('\n').Count(line => line.Trim() == "- docs/reference/agent-onboarding/analytics.md"));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Fact]
    public async Task Regenerate_EducationStemDemo_PreservesEditedDurableSourcesAndGuides()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var root = CreateTempPath("education-demo-regenerate");

        try
        {
            var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
            var resolver = CreateCatalogResolver(provider);
            var orchestrator = CreateOrchestrator(root, resolver);
            var template = provider.LoadTemplates().Single(item => item.Id == "education-stem-demo");
            var definition = new TemplateExpander().Expand("education-demo", template);
            var snapshot = await orchestrator.CreateWorkspaceAsync(root, definition, includeRuntimeInspection: false);

            var readmePath = Path.Combine(snapshot.Paths.RootPath, "README.md");
            var learningPath = Path.Combine(snapshot.Paths.RootPath, "docs", "learning-path.md");
            var surveyScript = Path.Combine(snapshot.Paths.RootPath, "examples", "survey-analysis", "analysis.py");
            var datasetPath = Path.Combine(snapshot.Paths.RootPath, "examples", "machine-learning-intro", "dataset.csv");

            File.WriteAllText(readmePath, "# user education demo\n");
            File.WriteAllText(learningPath, "# learner path\n");
            File.WriteAllText(surveyScript, "print('keep learner changes')\n");
            File.WriteAllText(datasetPath, "feature,target\n1,2\n");

            await orchestrator.RegenerateAsync(snapshot);

            Assert.Equal("# user education demo\n", File.ReadAllText(readmePath));
            Assert.Equal("# learner path\n", File.ReadAllText(learningPath));
            Assert.Equal("print('keep learner changes')\n", File.ReadAllText(surveyScript));
            Assert.Equal("feature,target\n1,2\n", File.ReadAllText(datasetPath));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Fact]
    public async Task RecoverWorkspace_PreservesEditedDurableSourcesAcrossFeatureSets()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var root = CreateTempPath("recover-durable");

        try
        {
            var orchestrator = CreateOrchestratorWithProviderAndDocker(root, CreateCatalogResolver(), new FakeWorkspaceProvider(), new DockerService(new ComposeOnlySuccessRunner()));
            var snapshot = await orchestrator.CreateWorkspaceAsync(root, CreateDefinition("mixed-recovery", ["core", "analytics-reporting", "publishing-tex", "education-knowledge-pack", "publishing-knowledge-pack"]), includeRuntimeInspection: false);

            var analysisPath = Path.Combine(snapshot.Paths.RootPath, "examples", "analytics", "analysis.py");
            var typstPath = Path.Combine(snapshot.Paths.RootPath, "examples", "publishing", "report.typ");
            var oracleNotesPath = Path.Combine(snapshot.Paths.RootPath, "docs", "user-onboarding-notes.md");
            File.WriteAllText(analysisPath, "# keep analytics source\n");
            File.WriteAllText(typstPath, "= keep publishing source\n");
            File.WriteAllText(oracleNotesPath, "keep docs\n");

            await orchestrator.RecoverAsync(snapshot);

            Assert.Equal("# keep analytics source\n", File.ReadAllText(analysisPath));
            Assert.Equal("= keep publishing source\n", File.ReadAllText(typstPath));
            Assert.Equal("keep docs\n", File.ReadAllText(oracleNotesPath));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Fact]
    public async Task Regenerate_ProtectsAllFeaturedStarterAssetsAcrossAnalyticsPublishingEducationAndOracle()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var root = CreateTempPath("starter-assets-regenerate");

        try
        {
            var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
            var resolver = CreateCatalogResolver(provider);
            var orchestrator = CreateOrchestrator(root, resolver);
            var template = provider.LoadTemplates().Single(item => item.Id == "education-stem-demo");
            var definition = new TemplateExpander().Expand("education-demo", template);
            definition = new WorkspaceDefinition
            {
                Workspace = definition.Workspace,
                Provider = definition.Provider,
                Runtime = definition.Runtime,
                Features = [.. definition.Features, "publishing-knowledge-pack"],
                Services = definition.Services,
                Skills = definition.Skills,
                Mcp = definition.Mcp,
                Terminal = definition.Terminal,
                Agent = definition.Agent,
                Oracle = definition.Oracle,
                Analytics = definition.Analytics,
            };

            var snapshot = await orchestrator.CreateWorkspaceAsync(root, definition, includeRuntimeInspection: false);

            var editableFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Path.Combine("examples", "analytics", "analysis.py")] = "# keep analytics script\n",
                [Path.Combine("examples", "analytics", "report.md")] = "# keep analytics report\n",
                [Path.Combine("examples", "analytics", "sample-data", "survey.csv")] = "value\n1\n",
                [Path.Combine("examples", "publishing", "report.typ")] = "= keep typst\n",
                [Path.Combine("examples", "publishing", "paper.tex")] = "\\documentclass{article}\n\\begin{document}keep\\end{document}\n",
                [Path.Combine("examples", "publishing", "bibliography.bib")] = "@book{keep,title={Keep}}\n",
                [Path.Combine("examples", "publishing", "diagram.svg")] = "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>\n",
                ["README.md"] = "# keep root readme\n",
                [Path.Combine("docs", "learning-path.md")] = "# keep learning path\n",
                [Path.Combine("docs", "educator-guide.md")] = "# keep educator guide\n",
                [Path.Combine("examples", "survey-analysis", "analysis.py")] = "print('keep survey')\n",
                [Path.Combine("examples", "science-report", "report.typ")] = "= keep science typst\n",
            };

            foreach (var pair in editableFiles)
            {
                File.WriteAllText(Path.Combine(snapshot.Paths.RootPath, pair.Key), pair.Value);
            }

            await orchestrator.RegenerateAsync(snapshot);

            foreach (var pair in editableFiles)
            {
                Assert.Equal(pair.Value, File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, pair.Key)));
            }
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Fact]
    public async Task RecoverWorkspace_RestoresMissingGeneratedDocsAndScripts_WithoutOverwritingDurableStarterAssets()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var root = CreateTempPath("recover-generated-assets");

        try
        {
            var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
            var resolver = CreateCatalogResolver(provider);
            var orchestrator = CreateOrchestratorWithProviderAndDocker(root, resolver, new FakeWorkspaceProvider(), new DockerService(new ComposeOnlySuccessRunner()));
            var template = provider.LoadTemplates().Single(item => item.Id == "education-stem-demo");
            var definition = new TemplateExpander().Expand("education-demo", template);
            var snapshot = await orchestrator.CreateWorkspaceAsync(root, definition, includeRuntimeInspection: false);

            var durablePath = Path.Combine(snapshot.Paths.RootPath, "examples", "survey-analysis", "analysis.py");
            var missingDocPath = Path.Combine(snapshot.Paths.RootPath, "docs", "projects.md");
            var missingScriptPath = Path.Combine(snapshot.Paths.RootPath, "scripts", "validate-analytics-tooling.sh");
            var missingGuidePath = Path.Combine(snapshot.Paths.RootPath, "docs", "team-onboarding.md");

            File.WriteAllText(durablePath, "print('keep durable learner file')\n");
            File.Delete(missingDocPath);
            File.Delete(missingScriptPath);
            File.Delete(missingGuidePath);

            await orchestrator.RecoverAsync(snapshot);

            Assert.Equal("print('keep durable learner file')\n", File.ReadAllText(durablePath));
            Assert.True(File.Exists(missingDocPath));
            Assert.True(File.Exists(missingScriptPath));
            Assert.True(File.Exists(missingGuidePath));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Fact]
    public async Task CreateCheckpointAsync_WhenSecretBearingUntrackedFileExists_BlocksWithoutChangingDurableFiles()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var root = CreateTempPath("checkpoint-secret");

        try
        {
            var orchestrator = CreateGitOrchestrator(root, CreateCatalogResolver());
            var snapshot = await orchestrator.CreateWorkspaceAsync(root, CreateDefinition("checkpoint-secret", ["core", "analytics-reporting"]), includeRuntimeInspection: false);
            var durableDocPath = Path.Combine(snapshot.Paths.RootPath, "docs", "user-notes.md");
            var secretPath = Path.Combine(snapshot.Paths.RootPath, ".env");
            File.WriteAllText(durableDocPath, "keep me\n");
            File.WriteAllText(secretPath, "API_KEY=secret\n");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.CreateCheckpointAsync(snapshot));

            Assert.Contains("Workspace Review required before creating a checkpoint", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".env", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("keep me\n", File.ReadAllText(durableDocPath));
            Assert.Empty(Directory.Exists(snapshot.Paths.CheckpointsPath) ? Directory.GetDirectories(snapshot.Paths.CheckpointsPath) : Array.Empty<string>());
        }
        finally
        {
            DeleteTempPath(root);
            DeleteTempPath(GetAppDataRoot(root));
        }
    }

    [Fact]
    public async Task CreateCheckpointAsync_WhenUntrackedDurableFileExists_CapturesCheckpoint()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var root = CreateTempPath("checkpoint-durable");

        try
        {
            var orchestrator = CreateGitOrchestrator(root, CreateCatalogResolver());
            var snapshot = await orchestrator.CreateWorkspaceAsync(root, CreateDefinition("checkpoint-durable", ["core", "analytics-reporting"]), includeRuntimeInspection: false);
            await RunGitAsync(root, "add", "-A");
            await RunGitAsync(root, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Track generated workspace files");
            var reportPath = Path.Combine(snapshot.Paths.RootPath, "docs", "draft-report.md");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, "# draft\n");

            var checkpoint = await orchestrator.CreateCheckpointAsync(snapshot);
            var checkpointPath = Path.Combine(snapshot.Paths.CheckpointsPath, checkpoint.Id, "untracked", "docs", "draft-report.md");

            Assert.True(File.Exists(checkpointPath));
            Assert.Equal("# draft\n", File.ReadAllText(checkpointPath));
        }
        finally
        {
            DeleteTempPath(root);
            DeleteTempPath(GetAppDataRoot(root));
        }
    }

    [Theory]
    [InlineData("analytics", true, false, false)]
    [InlineData("analytics+education", true, false, false)]
    [InlineData("publishing", false, true, false)]
    [InlineData("publishing+pack", false, true, false)]
    [InlineData("analytics+publishing", true, true, false)]
    [InlineData("analytics+education+publishing", true, true, false)]
    [InlineData("oracle-apex+analytics", true, false, true)]
    [InlineData("oracle-apexlang+analytics+publishing", true, true, true)]
    public async Task FeatureCombinationMatrix_GeneratesDeterministicArtifactsWithoutDuplicatePortsOrLinks(string scenario, bool expectAnalytics, bool expectPublishing, bool useOracleTemplate)
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var root = CreateTempPath($"matrix-{scenario.Replace('+', '-')}");

        try
        {
            var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
            var resolver = CreateCatalogResolver(provider);
            var orchestrator = CreateOrchestrator(root, resolver);

            WorkspaceDefinition definition;
            if (string.Equals(scenario, "oracle-apex+analytics", StringComparison.Ordinal))
            {
                definition = new TemplateExpander().Expand("oracle-apex-analytics", provider.LoadTemplates().Single(item => item.Id == "oracle-apex-demo"));
                definition = new WorkspaceDefinition
                {
                    Workspace = definition.Workspace,
                    Provider = definition.Provider,
                    Runtime = definition.Runtime,
                    Features = [.. definition.Features, "analytics-reporting"],
                    Services = definition.Services,
                    Skills = definition.Skills,
                    Mcp = definition.Mcp,
                    Terminal = definition.Terminal,
                    Agent = definition.Agent,
                    Oracle = definition.Oracle,
                    Analytics = definition.Analytics,
                };
            }
            else if (string.Equals(scenario, "oracle-apexlang+analytics+publishing", StringComparison.Ordinal))
            {
                definition = new TemplateExpander().Expand("oracle-apexlang-analytics-publishing", provider.LoadTemplates().Single(item => item.Id == "oracle-apexlang-demo"));
                definition = new WorkspaceDefinition
                {
                    Workspace = definition.Workspace,
                    Provider = definition.Provider,
                    Runtime = definition.Runtime,
                    Features = [.. definition.Features, "analytics-reporting", "publishing-tex"],
                    Services = definition.Services,
                    Skills = definition.Skills,
                    Mcp = definition.Mcp,
                    Terminal = definition.Terminal,
                    Agent = definition.Agent,
                    Oracle = definition.Oracle,
                    Analytics = definition.Analytics,
                };
            }
            else
            {
                definition = scenario switch
                {
                    "analytics" => CreateDefinition("analytics", ["core", "analytics-reporting"]),
                    "analytics+education" => CreateDefinition("analytics-education", ["core", "analytics-reporting", "education-knowledge-pack"]),
                    "publishing" => CreateDefinition("publishing", ["core", "publishing-tex"]),
                    "publishing+pack" => CreateDefinition("publishing-pack", ["core", "publishing-tex", "publishing-knowledge-pack"]),
                    "analytics+publishing" => CreateDefinition("analytics-publishing", ["core", "analytics-reporting", "publishing-tex"]),
                    _ => CreateDefinition("analytics-education-publishing", ["core", "analytics-reporting", "education-knowledge-pack", "publishing-tex"]),
                };
            }

            var snapshot = await orchestrator.CreateWorkspaceAsync(root, definition, includeRuntimeInspection: false);
            var compose = File.ReadAllText(snapshot.Paths.ComposePath);
            var env = File.ReadAllText(snapshot.Paths.EnvironmentFilePath);
            var agents = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "AGENTS.md"));
            var provision = File.ReadAllText(snapshot.Paths.ProvisionScriptPath);

            Assert.True(File.Exists(snapshot.Paths.ComposePath));
            Assert.True(File.Exists(snapshot.Paths.EnvironmentFilePath));
            Assert.True(File.Exists(snapshot.Paths.ProvisionScriptPath));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "AGENTS.md")));

            Assert.Equal(expectAnalytics, env.Contains("MARIMO_PORT=", StringComparison.Ordinal));
            Assert.Equal(expectAnalytics ? 1 : 0, compose.Split('\n').Count(line => line.Contains("127.0.0.1:${MARIMO_PORT}:2718", StringComparison.Ordinal)));
            Assert.Equal(agents.Split('\n').Count(line => line.Contains("docs/reference/agent-onboarding/analytics.md", StringComparison.Ordinal)), agents.Split('\n').Distinct(StringComparer.Ordinal).Count(line => line.Contains("docs/reference/agent-onboarding/analytics.md", StringComparison.Ordinal)));
            Assert.Equal(agents.Split('\n').Count(line => line.Contains("docs/reference/agent-onboarding/publishing.md", StringComparison.Ordinal)), agents.Split('\n').Distinct(StringComparer.Ordinal).Count(line => line.Contains("docs/reference/agent-onboarding/publishing.md", StringComparison.Ordinal)));

            var aptPlan = provision.Split('\n').FirstOrDefault(line => line.StartsWith("apt-get install -y ", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(aptPlan))
            {
                var packages = aptPlan["apt-get install -y ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                Assert.Equal(packages.Length, packages.Distinct(StringComparer.Ordinal).Count());
            }

            if (useOracleTemplate)
            {
                Assert.Contains("docs/reference/agent-onboarding/oracle.md", agents);
            }

            Assert.Equal(expectPublishing, agents.Contains("docs/reference/agent-onboarding/publishing.md", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Fact]
    public async Task AnalyticsPortCollision_DefaultPort_ReportsClearDiagnostic_AndPreservesWorkspace()
    {
        var root = CreateTempPath("analytics-port-conflict");
        Directory.CreateDirectory(root);

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            File.WriteAllText(paths.ComposePath, "services:\n  workspace:\n    image: ubuntu:24.04\n");
            var runner = new SequenceProcessRunner(
                Match(" compose ", Result("docker compose config", 0)),
                Match("ps --format", Result("docker ps", 0, standardOutput: "other-analytics\t127.0.0.1:2718->2718/tcp")),
                Match(" compose ", Result("docker compose ps", 0, standardOutput: "NAME STATUS PORTS")),
                Match(HostPortProbeCommandFragment(), Result(HostPortProbeCommandFragment(), 0, standardOutput: HostPortDiagnosticOutput(2718))));

            var docker = new DockerService(runner);
            var definition = CreateDefinition("analytics-conflict", ["core", "analytics-reporting"]);

            var result = await docker.StartAsync(paths, definition);

            Assert.False(result.IsSuccess);
            Assert.Equal(WorkspaceFailureClassification.EnvironmentPortConflict, result.FailureClassification);
            Assert.Contains("Marimo port 2718 is already in use.", result.StandardError);
            Assert.Contains("Set analytics.marimoPort to a different value", result.StandardError);
            Assert.True(File.Exists(paths.ComposePath));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Fact]
    public async Task AnalyticsPortCollision_CustomPort_ReportsClearDiagnostic_AndDoesNotCorruptWorkspace()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var root = CreateTempPath("analytics-custom-port-conflict");

        try
        {
            var resolver = CreateCatalogResolver();
            var runner = new SequenceProcessRunner(
                Match(" compose ", Result("docker compose config", 0)),
                Match("ps --format", Result("docker ps", 0, standardOutput: "other-analytics\t127.0.0.1:3818->2718/tcp")),
                Match(" compose ", Result("docker compose ps", 0, standardOutput: "NAME STATUS PORTS")),
                Match(HostPortProbeCommandFragment(), Result(HostPortProbeCommandFragment(), 0, standardOutput: HostPortDiagnosticOutput(3818))));

            var orchestrator = CreateOrchestratorWithProviderAndDocker(root, resolver, new FakeWorkspaceProvider(), new DockerService(runner));
            var snapshot = await orchestrator.CreateWorkspaceAsync(root, CreateDefinition("analytics-custom", ["core", "analytics-reporting"], analyticsPort: 3818), includeRuntimeInspection: false);
            var userPath = Path.Combine(snapshot.Paths.RootPath, "examples", "analytics", "report.md");
            File.WriteAllText(userPath, "# keep me\n");

            var exception = await Assert.ThrowsAsync<WorkspaceEnvironmentConflictException>(() => orchestrator.ProvisionAsync(snapshot));

            Assert.Contains("Marimo port 3818 is already in use.", exception.Message);
            Assert.Equal("# keep me\n", File.ReadAllText(userPath));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Fact]
    public void InvalidConfiguration_AnalyticsPortRangeAndCatalogMetadata_ReportClearErrors()
    {
        var validator = new CatalogValidator();

        var featureErrors = validator.ValidateFeatures([
            new FeatureManifest { Id = "bad", DisplayName = "Bad", Category = "invalid-category", Lifecycle = "invalid-lifecycle" }
        ]);
        var packErrors = validator.ValidateKnowledgePacks([
            new KnowledgePackManifest
            {
                Id = "broken-pack",
                Title = "Broken Pack",
                Category = "invalid-category",
                Lifecycle = "invalid-lifecycle",
                Sources = [new KnowledgePackSourceManifest { Name = "", Url = "", Category = "" }],
            }
        ]);
        var templateErrors = validator.ValidateTemplates([
            new TemplateManifest
            {
                Id = "broken-template",
                DisplayName = "Broken Template",
                Description = "desc",
                Features = ["missing-feature"],
                Services = ["missing-service"],
                Skills = [""] ,
                Mcp = [""] ,
            }
        ], [new FeatureManifest { Id = "core", DisplayName = "Core" }], Array.Empty<ServiceManifest>());

        var invalidPort = Assert.Throws<InvalidOperationException>(() => AnalyticsWorkspaceSettings.From(CreateDefinition("invalid-port", ["core", "analytics-reporting"], analyticsPort: -1)));
        var invalidZeroPort = Assert.Throws<InvalidOperationException>(() => AnalyticsWorkspaceSettings.From(CreateDefinition("invalid-zero-port", ["core", "analytics-reporting"], analyticsPort: 0)));
        var invalidUpperBoundaryPort = Assert.Throws<InvalidOperationException>(() => AnalyticsWorkspaceSettings.From(CreateDefinition("invalid-upper-boundary-port", ["core", "analytics-reporting"], analyticsPort: 65536)));
        var invalidLargePort = Assert.Throws<InvalidOperationException>(() => AnalyticsWorkspaceSettings.From(CreateDefinition("invalid-large-port", ["core", "analytics-reporting"], analyticsPort: 999999)));

        Assert.Contains("unsupported category", string.Join(Environment.NewLine, featureErrors), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unsupported lifecycle", string.Join(Environment.NewLine, featureErrors), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("without 'name'", string.Join(Environment.NewLine, packErrors), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing 'url'", string.Join(Environment.NewLine, packErrors), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unsupported category", string.Join(Environment.NewLine, packErrors), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unsupported lifecycle", string.Join(Environment.NewLine, packErrors), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("references unknown feature", string.Join(Environment.NewLine, templateErrors), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("references unknown service", string.Join(Environment.NewLine, templateErrors), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("empty skill id", string.Join(Environment.NewLine, templateErrors), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("empty MCP id", string.Join(Environment.NewLine, templateErrors), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("analytics.marimoPort", invalidPort.Message);
        Assert.Contains("analytics.marimoPort", invalidZeroPort.Message);
        Assert.Contains("analytics.marimoPort", invalidUpperBoundaryPort.Message);
        Assert.Contains("analytics.marimoPort", invalidLargePort.Message);
    }

    [Fact]
    public void InvalidConfiguration_WorkspaceYamlWithNonNumericAnalyticsPort_ThrowsClearError()
    {
        var path = Path.Combine(CreateTempPath("invalid-yaml"), "workspace.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            File.WriteAllText(path, """
workspace:
  name: invalid-yaml
provider:
  type: git
runtime:
  default: default
features:
  - core
  - analytics-reporting
analytics:
  marimoPort: abc
""");

            var exception = Assert.Throws<InvalidOperationException>(() => new WorkspaceYamlService().Read(path));
            Assert.Contains("invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(path, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempPath(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public async Task ProvisioningFailureRecovery_ReRunProvisioningPreservesUserContentAndSucceeds()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var root = CreateTempPath("provision-recovery");

        try
        {
            var runner = new FailOnceProvisionProcessRunner();
            var orchestrator = CreateOrchestratorWithProviderAndDocker(root, CreateCatalogResolver(), new FakeWorkspaceProvider(), new DockerService(runner));
            var snapshot = await orchestrator.CreateWorkspaceAsync(root, CreateDefinition("provision-recovery", ["core", "analytics-reporting", "publishing-tex"]), includeRuntimeInspection: false);

            var analysisPath = Path.Combine(snapshot.Paths.RootPath, "examples", "analytics", "analysis.py");
            File.WriteAllText(analysisPath, "# keep after failed provision\n");

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.ProvisionAsync(snapshot));
            Assert.Contains("Workspace provisioning failed.", failure.Message);

            await orchestrator.ProvisionAsync(snapshot);

            Assert.Equal("# keep after failed provision\n", File.ReadAllText(analysisPath));
            Assert.True(File.Exists(snapshot.Paths.AppliedStatePath));
            Assert.True(runner.ProvisionAttemptCount >= 2);
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Theory]
    [InlineData("workspace.yaml")]
    [InlineData("workspace.yml")]
    [InlineData(".opencode/profile.yaml")]
    [InlineData(".opencode/profile.yml")]
    public async Task OlderStyleConfigurations_LoadSaveRegenerateAndPreserveSettingsAndExtensions(string relativePath)
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var repositoryRoot = CreateTempPath("upgrade-config-repo");
        var appDataRoot = CreateTempPath("upgrade-config-appdata");

        try
        {
            Directory.CreateDirectory(repositoryRoot);
            await RunGitAsync(repositoryRoot, "init", "-b", "main");
            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "demo\n");
            await RunGitAsync(repositoryRoot, "add", "-A");
            await RunGitAsync(repositoryRoot, "-c", "user.name=Test User", "-c", "user.email=test@local.workspace", "commit", "-m", "Initial");

            var fullPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, """
workspace:
  name: upgraded-workspace
provider:
  type: git
runtime:
  default: default
features:
  - core
  - analytics-reporting
services: []
skills: []
mcp: []
oracle:
  hostPort: 1522
  ordsPort: 8182
analytics:
  marimoPort: 3818
x-legacy:
  keep: true
""");

            var orchestrator = CreateGitOrchestrator(appDataRoot, CreateCatalogResolver());
            var snapshot = await orchestrator.ImportExistingGitCheckoutAsync(new ExistingGitCheckoutImportRequest
            {
                RepositoryPath = repositoryRoot,
                WorkspaceName = "ignored",
                BranchMode = ExistingGitCheckoutBranchMode.UseCurrentBranch,
            });

            await orchestrator.RegenerateAsync(snapshot);
            var reloaded = await orchestrator.LoadSnapshotAsync(repositoryRoot, includeRuntimeInspection: false);
            var yaml = File.ReadAllText(fullPath);

            Assert.Equal(relativePath, snapshot.ConfigurationPath);
            Assert.Equal(relativePath, reloaded.ConfigurationPath);
            Assert.Contains("hostPort: 1522", yaml);
            Assert.Contains("ordsPort: 8182", yaml);
            Assert.Contains("marimoPort: 3818", yaml);
            Assert.Contains("x-legacy:", yaml);
            Assert.Contains("keep: true", yaml);
        }
        finally
        {
            DeleteTempPath(repositoryRoot);
            DeleteTempPath(appDataRoot);
        }
    }

    [Fact]
    public void OnboardingAndAgentsGeneration_AreDeterministicAcrossRepeatedRuns()
    {
        var resolver = CreateCatalogResolver();
        var generator = new WorkspaceContentGenerator();
        var definition = CreateDefinition("deterministic", ["core", "analytics-reporting", "education-knowledge-pack", "publishing-tex", "publishing-knowledge-pack"]);
        var resolved = resolver.Resolve(definition);

        var first = generator.Generate(resolved);
        var second = generator.Generate(resolved);
        var firstAgents = generator.BuildAgentsDocument(resolved, null);
        var secondAgents = generator.BuildAgentsDocument(resolved, null);

        Assert.Equal(first[Path.Combine("docs", "reference", "agent-onboarding", "analytics.md")], second[Path.Combine("docs", "reference", "agent-onboarding", "analytics.md")]);
        Assert.Equal(first[Path.Combine("docs", "reference", "agent-onboarding", "education.md")], second[Path.Combine("docs", "reference", "agent-onboarding", "education.md")]);
        Assert.Equal(first[Path.Combine("docs", "reference", "agent-onboarding", "publishing.md")], second[Path.Combine("docs", "reference", "agent-onboarding", "publishing.md")]);
        Assert.Equal(firstAgents, secondAgents);
        Assert.Equal(1, firstAgents.Split('\n').Count(line => line.Contains("docs/reference/agent-onboarding/analytics.md", StringComparison.Ordinal)));
        Assert.Equal(1, firstAgents.Split('\n').Count(line => line.Contains("docs/reference/agent-onboarding/education.md", StringComparison.Ordinal)));
        Assert.Equal(1, firstAgents.Split('\n').Count(line => line.Contains("docs/reference/agent-onboarding/publishing.md", StringComparison.Ordinal)));
    }

    [Fact]
    public void EducationStemDemoGeneration_IsDeterministic_AndLeavesAnalyticsAndOraclePathsAvailable()
    {
        var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        var resolver = CreateCatalogResolver(provider);
        var generator = new WorkspaceContentGenerator();
        var template = provider.LoadTemplates().Single(item => item.Id == "education-stem-demo");
        var definition = new TemplateExpander().Expand("education-demo", template);
        var resolved = resolver.Resolve(definition);

        var first = generator.Generate(resolved);
        var second = generator.Generate(resolved);

        Assert.Equal(first["README.md"], second["README.md"]);
        Assert.Equal(first[Path.Combine("docs", "learning-path.md")], second[Path.Combine("docs", "learning-path.md")]);
        Assert.Equal(first[Path.Combine("examples", "survey-analysis", "analysis.py")], second[Path.Combine("examples", "survey-analysis", "analysis.py")]);
        Assert.Equal(first[Path.Combine("examples", "machine-learning-intro", "dataset.csv")], second[Path.Combine("examples", "machine-learning-intro", "dataset.csv")]);
        Assert.True(first.ContainsKey(Path.Combine("examples", "analytics", "analysis.py")));
        Assert.True(first.ContainsKey(Path.Combine("examples", "publishing", "report.typ")));

        var oracleTemplateIds = provider.LoadTemplates().Select(item => item.Id).ToList();
        Assert.Contains("oracle-plsql-demo", oracleTemplateIds);
        Assert.Contains("oracle-apex-demo", oracleTemplateIds);
        Assert.Contains("oracle-apexlang-demo", oracleTemplateIds);
    }

    private static WorkspaceDefinition CreateDefinition(string workspaceName, IReadOnlyList<string> features, int? analyticsPort = null)
    {
        return new WorkspaceDefinition
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
            Analytics = new AnalyticsWorkspacePreferences { MarimoPort = analyticsPort },
        };
    }

    private static WorkspaceResolver CreateCatalogResolver(BuiltInCatalogProvider? provider = null)
    {
        provider ??= new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        return new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
    }

    private static WorkspaceOrchestrator CreateOrchestrator(string root, WorkspaceResolver resolver)
        => CreateOrchestratorWithProviderAndDocker(root, resolver, new FakeWorkspaceProvider(), new DockerService(new ProcessRunner()));

    private static WorkspaceOrchestrator CreateGitOrchestrator(string root, WorkspaceResolver resolver)
    {
        var ignorePolicy = new WorkspaceIgnorePolicyService();
        return CreateOrchestratorWithProviderAndDocker(root, resolver, new GitWorkspaceProvider(new ProcessRunner(), ignorePolicy), new DockerService(new ProcessRunner()), ignorePolicy);
    }

    private static WorkspaceOrchestrator CreateOrchestratorWithProviderAndDocker(string root, WorkspaceResolver resolver, IWorkspaceProvider provider, DockerService dockerService, WorkspaceIgnorePolicyService? ignorePolicy = null)
    {
        ignorePolicy ??= new WorkspaceIgnorePolicyService();
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
            provider,
            dockerService,
            new NoOpTerminalLauncher());
    }

    private static string GetAppDataRoot(string root)
        => Path.Combine(Path.GetDirectoryName(root) ?? Path.GetTempPath(), $"{Path.GetFileName(root)}-appdata");

    private static string CreateTempPath(string prefix) => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");

    private static void DeleteTempPath(string path)
    {
        if (Directory.Exists(path))
        {
            TestFileSystem.DeleteDirectoryIfExists(path);
        }

        var appDataPath = GetAppDataRoot(path);
        if (Directory.Exists(appDataPath))
        {
            TestFileSystem.DeleteDirectoryIfExists(appDataPath);
        }
    }

    private static bool CanRunGit()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
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

    private static async Task<ProcessResult> RunGitAsync(string workingDirectory, params string[] arguments)
        => await new ProcessRunner().RunAsync("git", arguments, workingDirectory);

    private static ProcessResult Result(string command, int exitCode, string standardOutput = "", string standardError = "")
        => new()
        {
            Command = command,
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            StandardOutputLines = string.IsNullOrWhiteSpace(standardOutput) ? Array.Empty<string>() : standardOutput.Split(Environment.NewLine),
            StandardErrorLines = string.IsNullOrWhiteSpace(standardError) ? Array.Empty<string>() : standardError.Split(Environment.NewLine),
            Duration = TimeSpan.FromMilliseconds(10),
        };

    private static ExpectedCommand Match(string fragment, ProcessResult result) => new(fragment, result);

    private static string HostPortProbeCommandFragment() => OperatingSystem.IsWindows() ? "powershell.exe" : "bash";

    private static string HostPortDiagnosticOutput(int port)
        => OperatingSystem.IsWindows()
            ? $"LISTEN port={port} pid=123 process=com.docker.backend"
            : $"State Recv-Q Send-Q Local Address:Port Peer Address:PortProcess\nLISTEN 0 4096 127.0.0.1:{port} 0.0.0.0:* users:((\"docker-proxy\",pid=123,fd=4))";

    private sealed record ExpectedCommand(string Fragment, ProcessResult Result);

    private sealed class SequenceProcessRunner(params ExpectedCommand[] expectedCommands) : IProcessRunner
    {
        private readonly Queue<ExpectedCommand> _expectedCommands = new(expectedCommands);

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            var command = string.Join(' ', new[] { fileName }.Concat(arguments));
            Assert.NotEmpty(_expectedCommands);
            var expected = _expectedCommands.Dequeue();
            Assert.Contains(expected.Fragment, command, StringComparison.Ordinal);
            return Task.FromResult(expected.Result);
        }
    }

    private sealed class ComposeOnlySuccessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            var argumentList = arguments.ToList();
            var command = string.Join(' ', new[] { fileName }.Concat(argumentList));
            if (string.Equals(fileName, "docker", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Contains("ps", StringComparer.Ordinal) && argumentList.Contains("--services", StringComparer.Ordinal))
                {
                    return Task.FromResult(Result(command, 0, standardOutput: "workspace"));
                }

                if (argumentList.Count > 0 && argumentList[0] == "ps")
                {
                    return Task.FromResult(Result(command, 0, standardOutput: "mixed-recovery-workspace"));
                }

                return Task.FromResult(Result(command, 0));
            }

            return Task.FromResult(Result(command, 0));
        }
    }

    private sealed class FailOnceProvisionProcessRunner : IProcessRunner
    {
        public int ProvisionAttemptCount { get; private set; }

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            var argumentList = arguments.ToList();
            var command = string.Join(' ', new[] { fileName }.Concat(argumentList));

            if (string.Equals(fileName, "docker", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Contains("ps", StringComparer.Ordinal) && argumentList.Contains("--services", StringComparer.Ordinal))
                {
                    return Task.FromResult(Result(command, 0, standardOutput: string.Join(Environment.NewLine, ["workspace"])));
                }

                if (argumentList.Count > 0 && argumentList[0] == "ps")
                {
                    return Task.FromResult(Result(command, 0, standardOutput: "provision-recovery-workspace"));
                }

                if (argumentList.Count >= 4 && argumentList[0] == "inspect" && argumentList[^1] == "{{.Image}}")
                {
                    return Task.FromResult(Result(command, 0, standardOutput: "sha256:test-image"));
                }

                if (argumentList.Count >= 4 && argumentList[0] == "inspect" && argumentList[^1] == "{{json .RepoTags}}")
                {
                    return Task.FromResult(Result(command, 0, standardOutput: "[\"ubuntu:24.04\"]"));
                }

                if (argumentList.Count >= 4 && argumentList[0] == "exec" && argumentList[2] == "bash" && argumentList[3] == "/opt/opencode-workspace/config/provision.sh")
                {
                    ProvisionAttemptCount++;
                    return Task.FromResult(ProvisionAttemptCount == 1
                        ? Result(command, 1, standardError: "simulated interrupted provisioning")
                        : Result(command, 0));
                }

                if (argumentList.Count >= 8 && argumentList[0] == "exec" && argumentList[1] == "--user" && argumentList[4] == "-w" && argumentList[7] == "-lc")
                {
                    var shellCommand = argumentList[8];
                    if (shellCommand.Contains("command -v opencode && opencode --version", StringComparison.Ordinal))
                    {
                        return Task.FromResult(Result(command, 0, standardOutput: string.Join(Environment.NewLine, "/usr/bin/opencode", "1.17.13")));
                    }
                }

                if (argumentList.Count >= 5 && argumentList[0] == "exec" && argumentList[2] == "bash" && argumentList[3] == "-lc")
                {
                    var shellCommand = argumentList[4];
                    if (shellCommand.Contains("which node && node --version && which npm && npm --version", StringComparison.Ordinal))
                    {
                        return Task.FromResult(Result(command, 0, standardOutput: string.Join(Environment.NewLine, "/usr/bin/node", "v22.15.0", "/usr/bin/npm", "10.9.2")));
                    }

                    if (shellCommand.Contains("apt-cache policy nodejs", StringComparison.Ordinal))
                    {
                        return Task.FromResult(Result(command, 0, standardOutput: "nodejs:\n  Installed: 22.15.0-1nodesource1"));
                    }

                    if (shellCommand.Contains("cat /etc/os-release", StringComparison.Ordinal))
                    {
                        return Task.FromResult(Result(command, 0, standardOutput: "PRETTY_NAME=\"Ubuntu 24.04 LTS\""));
                    }

                    if (shellCommand.Contains("command -v screen", StringComparison.Ordinal)
                        || shellCommand.Contains("command -v node", StringComparison.Ordinal)
                        || shellCommand.Contains("command -v npm", StringComparison.Ordinal)
                        || shellCommand.Contains("getent passwd opencode", StringComparison.Ordinal)
                        || shellCommand.Contains("command -v git", StringComparison.Ordinal)
                        || shellCommand.Contains("command -v bash", StringComparison.Ordinal))
                    {
                        return Task.FromResult(Result(command, 0, standardOutput: string.Join(Environment.NewLine, "/usr/bin/screen", "/usr/bin/node", "/usr/bin/npm", "opencode:x:1001:1001::/home/opencode:/bin/bash", "/usr/bin/git", "/usr/bin/bash")));
                    }
                }

                if (argumentList.Contains("config", StringComparer.Ordinal) || argumentList.Contains("up", StringComparer.Ordinal) || argumentList.Contains("down", StringComparer.Ordinal))
                {
                    return Task.FromResult(Result(command, 0));
                }
            }

            return Task.FromResult(Result(command, 0));
        }
    }

    private sealed class FakeWorkspaceProvider : IWorkspaceProvider
    {
        public string Type => "git";

        public Task InitializeWorkspaceAsync(WorkspacePaths paths, WorkspaceDefinition definition, bool createInitialSavePoint, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<WorkspaceGitState> GetGitStateAsync(WorkspacePaths paths, WorkspaceDefinition definition, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceGitState
            {
                IsRepository = true,
                WorkingCopyName = "users/test/demo-20260618-1200",
                CurrentBranch = "users/test/demo-20260618-1200",
                LatestCommitSha = "abc123",
                LatestCommitUtc = DateTimeOffset.UtcNow,
                IsSafeWorkingCopy = true,
                StatusSummary = "clean",
            });

        public Task<bool> CreateSavePointAsync(WorkspacePaths paths, WorkspaceDefinition definition, string message, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<WorkspacePublishReview> PublishAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspacePublishReview { Message = "Published" });

        public Task<WorkspacePublishReview> UpdateWorkingCopyAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspacePublishReview { Message = "Updated" });

        public Task<WorkspacePublishReview> PublishToReviewWorkingCopyAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspacePublishReview { Message = "Published review Working Copy." });

        public Task<string> ExportPatchAsync(WorkspacePaths paths, WorkspaceDefinition definition, string outputPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(outputPath);
    }

    private sealed class NoOpTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
