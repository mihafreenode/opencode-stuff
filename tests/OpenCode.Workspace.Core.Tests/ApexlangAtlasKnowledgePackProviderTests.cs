using System.Text.Json;
using OpenCode.Workspace.Core.Knowledge;
using OpenCode.Workspace.Core.Knowledge.Providers;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using YamlDotNet.RepresentationModel;

namespace OpenCode.Workspace.Core.Tests;

public sealed class ApexlangAtlasKnowledgePackProviderTests
{
    [Fact]
    public async Task GenerateAsync_UsesWorkspaceLocalFilesBeforeCache()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var localSourceRoot = Path.Combine(paths.OpencodePath, "apexlang", "source");
            Directory.CreateDirectory(localSourceRoot);
            File.Copy(GetFixturePath("apexlang_meta_data.json"), Path.Combine(localSourceRoot, "apexlang_meta_data.json"));
            File.Copy(GetFixturePath("builtin_catalog.json"), Path.Combine(localSourceRoot, "builtin_catalog.json"));

            var provider = new ApexlangAtlasKnowledgePackProvider(new FakeRemoteSourceFetcher());
            var result = await provider.GenerateAsync(CreateContext(paths));

            Assert.EndsWith(Path.Combine(".opencode", "apexlang", "source", "apexlang_meta_data.json"), result.SourceLocations["apexlang_meta_data.json"], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("indexes/required-properties.json", result.GeneratedFiles.Keys);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GenerateAsync_UsesCacheWhenWorkspaceLocalFilesAreMissing()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var cacheRoot = Path.Combine(paths.OpencodePath, "cache", "apexlang", "26.1.0+3102");
            Directory.CreateDirectory(cacheRoot);
            File.Copy(GetFixturePath("apexlang_meta_data.json"), Path.Combine(cacheRoot, "apexlang_meta_data.json"));
            File.Copy(GetFixturePath("builtin_catalog.json"), Path.Combine(cacheRoot, "builtin_catalog.json"));

            var provider = new ApexlangAtlasKnowledgePackProvider(new FakeRemoteSourceFetcher());
            var result = await provider.GenerateAsync(CreateContext(paths));

            Assert.EndsWith(Path.Combine(".opencode", "cache", "apexlang", "26.1.0+3102", "apexlang_meta_data.json"), result.SourceLocations["apexlang_meta_data.json"], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GenerateAsync_DownloadsAndCachesWhenNeeded()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var remoteFetcher = new FakeRemoteSourceFetcher
            {
                Responses =
                {
                    ["https://example.test/meta.json"] = File.ReadAllText(GetFixturePath("apexlang_meta_data.json")),
                    ["https://example.test/catalog.json"] = File.ReadAllText(GetFixturePath("builtin_catalog.json")),
                },
            };
            var provider = new ApexlangAtlasKnowledgePackProvider(remoteFetcher);

            var result = await provider.GenerateAsync(CreateContext(paths));

            Assert.Equal("https://example.test/meta.json", result.SourceLocations["apexlang_meta_data.json"]);
            Assert.True(File.Exists(Path.Combine(paths.OpencodePath, "cache", "apexlang", "26.1.0+3102", "apexlang_meta_data.json")));
            Assert.True(File.Exists(Path.Combine(paths.OpencodePath, "cache", "apexlang", "26.1.0+3102", "builtin_catalog.json")));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GenerateAsync_WhenJsonIsInvalid_Throws()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var localSourceRoot = Path.Combine(paths.OpencodePath, "apexlang", "source");
            Directory.CreateDirectory(localSourceRoot);
            File.Copy(GetFixturePath("invalid_metadata.json"), Path.Combine(localSourceRoot, "apexlang_meta_data.json"));
            File.Copy(GetFixturePath("builtin_catalog.json"), Path.Combine(localSourceRoot, "builtin_catalog.json"));

            var provider = new ApexlangAtlasKnowledgePackProvider(new FakeRemoteSourceFetcher());

            await Assert.ThrowsAnyAsync<JsonException>(() => provider.GenerateAsync(CreateContext(paths)));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GenerateAsync_ExtractsRequiredPropertiesAndDependencyRules()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var localSourceRoot = Path.Combine(paths.OpencodePath, "apexlang", "source");
            Directory.CreateDirectory(localSourceRoot);
            File.Copy(GetFixturePath("apexlang_meta_data.json"), Path.Combine(localSourceRoot, "apexlang_meta_data.json"));
            File.Copy(GetFixturePath("builtin_catalog.json"), Path.Combine(localSourceRoot, "builtin_catalog.json"));

            var provider = new ApexlangAtlasKnowledgePackProvider(new FakeRemoteSourceFetcher());
            var result = await provider.GenerateAsync(CreateContext(paths));

            var requiredProperties = result.GeneratedFiles["indexes/required-properties.json"];
            var dependencyRules = result.GeneratedFiles["indexes/dependency-rules.json"];
            var componentDoc = result.GeneratedFiles["docs/components/classic-report.md"];

            Assert.Contains("TITLE", requiredProperties, StringComparison.Ordinal);
            Assert.Contains("SQL_QUERY", requiredProperties, StringComparison.Ordinal);
            Assert.Contains("SOURCE_TYPE", dependencyRules, StringComparison.Ordinal);
            Assert.Contains("depends on", result.GeneratedFiles["prompts/apexlang-context.md"], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("required: TITLE, SQL_QUERY", componentDoc, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ProvisionAsync_WritesStateJsonForAtlasProvider()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var localSourceRoot = Path.Combine(paths.OpencodePath, "apexlang", "source");
            Directory.CreateDirectory(localSourceRoot);
            File.Copy(GetFixturePath("apexlang_meta_data.json"), Path.Combine(localSourceRoot, "apexlang_meta_data.json"));
            File.Copy(GetFixturePath("builtin_catalog.json"), Path.Combine(localSourceRoot, "builtin_catalog.json"));

            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "atlas-provision", Image = "ubuntu:24.04" },
                Provider = new WorkspaceProviderDefinition { Type = "git" },
                Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
                Features = ["core"],
                Services = [],
                Skills = [],
                Mcp = [],
                Agent = new AgentPreferences { Profile = AgentProfileResolver.BuiltInDefault.ProfileId },
                Terminal = new TerminalPreferences(),
                KnowledgePacks = [CreateConfiguration()],
            };
            var provisioner = new KnowledgePackProvisioner([new ApexlangAtlasKnowledgePackProvider(new FakeRemoteSourceFetcher())]);

            var result = await provisioner.ProvisionAsync(definition, paths);

            Assert.False(result.HasRequiredFailures);
            var statePath = Path.Combine(paths.OpencodePath, "knowledge", "apexlang-atlas", "state.json");
            using var document = JsonDocument.Parse(File.ReadAllText(statePath));
            Assert.Equal("26.1.0+3102", document.RootElement.GetProperty("metadata").GetProperty("buildId").GetString());
            Assert.Equal("1.0", document.RootElement.GetProperty("metadata").GetProperty("schemaVersion").GetString());
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static KnowledgePackContext CreateContext(WorkspacePaths paths)
    {
        var configuration = CreateConfiguration();
        var providerRoot = Path.Combine(paths.OpencodePath, "knowledge", "apexlang-atlas");
        return new KnowledgePackContext
        {
            Definition = new WorkspaceDefinition { Workspace = new WorkspaceMetadata { Name = "atlas", Image = "ubuntu:24.04" } },
            Paths = paths,
            Configuration = configuration,
            ProviderRootPath = providerRoot,
            GeneratedRootPath = Path.Combine(providerRoot, "generated"),
            DocsRootPath = Path.Combine(providerRoot, "docs"),
            IndexesRootPath = Path.Combine(providerRoot, "indexes"),
            PromptsRootPath = Path.Combine(providerRoot, "prompts"),
            SharedCacheRootPath = Path.Combine(paths.OpencodePath, "cache", "knowledge", "apexlang-atlas"),
        };
    }

    private static WorkspaceKnowledgePackDefinition CreateConfiguration()
        => new()
        {
            Provider = "apexlang-atlas",
            Enabled = true,
            Mode = WorkspaceKnowledgePackModes.Optional,
            Settings = ParseYamlNode("""
buildId: "26.1.0+3102"
metadataUrl: "https://example.test/meta.json"
builtinCatalogUrl: "https://example.test/catalog.json"
"""),
        };

    private static YamlNode ParseYamlNode(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        return stream.Documents[0].RootNode;
    }

    private static string GetFixturePath(string fileName)
        => Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Core.Tests", "Fixtures", "ApexlangAtlas", fileName);

    private static string CreateTempRoot() => Path.Combine(Path.GetTempPath(), $"apexlang-atlas-tests-{Guid.NewGuid():N}");

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            TestFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    private sealed class FakeRemoteSourceFetcher : IKnowledgePackRemoteSourceFetcher
    {
        public Dictionary<string, string> Responses { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<string> FetchAsync(string url, CancellationToken cancellationToken = default)
        {
            if (!Responses.TryGetValue(url, out var response))
            {
                throw new InvalidOperationException($"No fake response configured for '{url}'.");
            }

            return Task.FromResult(response);
        }
    }
}
