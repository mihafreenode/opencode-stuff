using System.Text.Json;
using OpenCode.Workspace.Core.Knowledge;
using OpenCode.Workspace.Core.Knowledge.Providers;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using YamlDotNet.RepresentationModel;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexDevelopersCompanionKnowledgePackProviderTests
{
    [Fact]
    public async Task GenerateAsync_SplitsSectionsPreservesProvenanceAndBuildsIndex()
    {
        var root = CreateTempRoot();
        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var localSourceRoot = Path.Combine(paths.OpencodePath, "apex-developers-companion", "source");
            Directory.CreateDirectory(localSourceRoot);
            File.Copy(GetFixturePath("oracle-apex-developers-companion.extracted.json"), Path.Combine(localSourceRoot, "oracle-apex-developers-companion.extracted.json"));
            File.Copy(GetFixturePath("oracle-apex-developers-companion.pdf"), Path.Combine(localSourceRoot, "oracle-apex-developers-companion.pdf"));

            var provider = new OracleApexDevelopersCompanionKnowledgePackProvider(new FakeRemoteSourceFetcher());
            var result = await provider.GenerateAsync(CreateContext(paths));

            Assert.Contains("docs/working-with-apexlang/reading-apexlang-syntax.md", result.GeneratedFiles.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("docs/working-with-apexlang/builder-and-external-tools.md", result.GeneratedFiles.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(result.GeneratedFiles.Keys, key => key.Contains('\\', StringComparison.Ordinal));
            var syntaxDoc = result.GeneratedFiles["docs/working-with-apexlang/reading-apexlang-syntax.md"];
            Assert.Contains("Source page: 12", syntaxDoc, StringComparison.Ordinal);
            Assert.Contains("Previous section: Overview", syntaxDoc, StringComparison.Ordinal);
            Assert.Contains("Next section: Using Coding Agents", syntaxDoc, StringComparison.Ordinal);
            Assert.Contains("```sql", syntaxDoc, StringComparison.Ordinal);
            Assert.Contains("```javascript-browser", syntaxDoc, StringComparison.Ordinal);

            using var indexDocument = JsonDocument.Parse(result.GeneratedFiles["index.json"]);
            var readingEntry = indexDocument.RootElement.EnumerateArray().Single(entry => entry.GetProperty("title").GetString() == "Reading APEXlang Syntax");
            Assert.Equal("Working with APEXlang", readingEntry.GetProperty("chapter").GetString());
            Assert.Equal("docs/working-with-apexlang/reading-apexlang-syntax.md", readingEntry.GetProperty("path").GetString());
            Assert.Contains(readingEntry.GetProperty("concepts").EnumerateArray().Select(item => item.GetString()).Where(item => item is not null), item => string.Equals(item, "@ reference", StringComparison.Ordinal));
            Assert.Contains(readingEntry.GetProperty("concepts").EnumerateArray().Select(item => item.GetString()).Where(item => item is not null), item => string.Equals(item, "@/ reference", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GenerateAsync_BuildsCompactContextAndManagedOverwriteRespectsUserEdits()
    {
        var root = CreateTempRoot();
        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var localSourceRoot = Path.Combine(paths.OpencodePath, "apex-developers-companion", "source");
            Directory.CreateDirectory(localSourceRoot);
            File.Copy(GetFixturePath("oracle-apex-developers-companion.extracted.json"), Path.Combine(localSourceRoot, "oracle-apex-developers-companion.extracted.json"));

            var provider = new OracleApexDevelopersCompanionKnowledgePackProvider(new FakeRemoteSourceFetcher());
            var definition = CreateDefinition();
            var provisioner = new KnowledgePackProvisioner([provider]);

            await provisioner.ProvisionAsync(definition, paths);

            var promptPath = Path.Combine(paths.OpencodePath, "knowledge", "apex-developers-companion", "prompts", "compact-context.md");
            var prompt = File.ReadAllText(promptPath);
            Assert.Contains("Retrieve only the smallest relevant Markdown section", prompt, StringComparison.Ordinal);
            Assert.True(prompt.Split('\n').Length <= 24);

            var sectionPath = Path.Combine(paths.OpencodePath, "knowledge", "apex-developers-companion", "docs", "working-with-apexlang", "overview.md");
            File.WriteAllText(sectionPath, "user-edit\n");
            var secondRun = await provisioner.ProvisionAsync(definition, paths);
            Assert.Equal("user-edit\n", File.ReadAllText(sectionPath));
            Assert.Contains(secondRun.Warnings, warning => warning.Contains("preserved", StringComparison.OrdinalIgnoreCase));

            var statePath = Path.Combine(paths.OpencodePath, "knowledge", "apex-developers-companion", "state.json");
            using var stateDocument = JsonDocument.Parse(File.ReadAllText(statePath));
            Assert.DoesNotContain(stateDocument.RootElement.GetProperty("generatedFileHashes").EnumerateObject().Select(item => item.Name), name => name.Contains('\\', StringComparison.Ordinal));
            Assert.DoesNotContain(stateDocument.RootElement.GetProperty("skippedFiles").EnumerateArray().Select(item => item.GetString() ?? string.Empty), name => name.Contains('\\', StringComparison.Ordinal));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static WorkspaceDefinition CreateDefinition()
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = "companion", Image = "ubuntu:24.04" },
            Provider = new WorkspaceProviderDefinition { Type = "git" },
            Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
            Features = ["core"],
            Services = [],
            Skills = [],
            Mcp = [],
            Agent = new AgentPreferences { Profile = AgentProfileResolver.BuiltInDefault.ProfileId },
            Terminal = new TerminalPreferences(),
            KnowledgePacks = [new WorkspaceKnowledgePackDefinition { Provider = "apex-developers-companion", Enabled = true, Settings = ParseYamlNode("apexVersion: \"26.1\"\npdfUrl: \"https://docs.oracle.com/en/database/oracle/apex/26.1/apxdc/oracle-apex-developers-companion.pdf\"\n") }],
        };

    private static KnowledgePackContext CreateContext(WorkspacePaths paths)
    {
        var providerRoot = Path.Combine(paths.OpencodePath, "knowledge", "apex-developers-companion");
        return new KnowledgePackContext
        {
            Definition = CreateDefinition(),
            Paths = paths,
            Configuration = CreateDefinition().KnowledgePacks[0],
            ProviderRootPath = providerRoot,
            GeneratedRootPath = Path.Combine(providerRoot, "generated"),
            DocsRootPath = Path.Combine(providerRoot, "docs"),
            IndexesRootPath = Path.Combine(providerRoot, "indexes"),
            PromptsRootPath = Path.Combine(providerRoot, "prompts"),
            SharedCacheRootPath = Path.Combine(paths.OpencodePath, "cache", "knowledge", "apex-developers-companion"),
        };
    }

    private static YamlNode ParseYamlNode(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        return stream.Documents[0].RootNode;
    }

    private static string GetFixturePath(string fileName)
        => Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Core.Tests", "Fixtures", "ApexDevelopersCompanion", fileName);

    private static string CreateTempRoot() => Path.Combine(Path.GetTempPath(), $"apex-developers-companion-tests-{Guid.NewGuid():N}");

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            TestFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    private sealed class FakeRemoteSourceFetcher : IKnowledgePackRemoteSourceFetcher
    {
        public Task<string> FetchAsync(string url, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException($"Unexpected remote fetch '{url}'.");
    }
}
