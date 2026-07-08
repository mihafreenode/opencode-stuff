using System.Text.Json;
using OpenCode.Workspace.Core.Knowledge;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class KnowledgePackProvisionerTests
{
    [Fact]
    public async Task ProvisionAsync_WhenProviderIsMissingInOptionalMode_WarnsAndContinues()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var definition = CreateDefinition(new WorkspaceKnowledgePackDefinition { Provider = "missing-provider" });
            var provisioner = new KnowledgePackProvisioner(Array.Empty<IKnowledgePackProvider>());

            var result = await provisioner.ProvisionAsync(definition, paths);

            Assert.False(result.HasRequiredFailures);
            Assert.Single(result.Warnings);
            Assert.Contains("missing-provider", result.Warnings[0], StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ProvisionAsync_WhenProviderFailsInRequiredMode_FailsResult()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var definition = CreateDefinition(new WorkspaceKnowledgePackDefinition
            {
                Provider = "fake-provider",
                Mode = WorkspaceKnowledgePackModes.Required,
            });
            var provisioner = new KnowledgePackProvisioner([new FakeKnowledgePackProvider(_ => throw new InvalidOperationException("provider failed"))]);

            var result = await provisioner.ProvisionAsync(definition, paths);

            Assert.True(result.HasRequiredFailures);
            Assert.Single(result.Errors);
            Assert.Contains("provider failed", result.Errors[0], StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ProvisionAsync_PreservesUserEditedFilesUntilExplicitRegeneration()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var definition = CreateDefinition(new WorkspaceKnowledgePackDefinition { Provider = "fake-provider" });
            var provider = new FakeKnowledgePackProvider(_ => CreateContent("first"));
            var provisioner = new KnowledgePackProvisioner([provider]);

            await provisioner.ProvisionAsync(definition, paths);

            var guidePath = Path.Combine(paths.OpencodePath, "knowledge", "fake-provider", "docs", "guide.md");
            File.WriteAllText(guidePath, "user-edit\n");
            provider.ContentFactory = _ => CreateContent("second");

            var secondRun = await provisioner.ProvisionAsync(definition, paths);

            Assert.Equal("user-edit\n", File.ReadAllText(guidePath));
            Assert.Contains(secondRun.Warnings, warning => warning.Contains("preserved", StringComparison.OrdinalIgnoreCase));

            await provisioner.ProvisionAsync(definition, paths, explicitRegenerationRequested: true);

            Assert.Equal("second\n", File.ReadAllText(guidePath));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ProvisionAsync_WritesStateJson()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var definition = CreateDefinition(new WorkspaceKnowledgePackDefinition { Provider = "fake-provider" });
            var provisioner = new KnowledgePackProvisioner([new FakeKnowledgePackProvider(_ => CreateContent("first"))]);

            await provisioner.ProvisionAsync(definition, paths);

            var statePath = Path.Combine(paths.OpencodePath, "knowledge", "fake-provider", "state.json");
            Assert.True(File.Exists(statePath));
            using var document = JsonDocument.Parse(File.ReadAllText(statePath));
            Assert.Equal("1", document.RootElement.GetProperty("providerVersion").GetString());
            Assert.Equal("demo", document.RootElement.GetProperty("metadata").GetProperty("buildId").GetString());
            Assert.Equal("LOCAL", document.RootElement.GetProperty("sourceHashes").GetProperty("meta.json").GetString());
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static ProvisionedKnowledgePackContent CreateContent(string body)
    {
        return new ProvisionedKnowledgePackContent
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["buildId"] = "demo" },
            SourceHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["meta.json"] = "LOCAL" },
            SourceLocations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["meta.json"] = "workspace-local" },
            GeneratedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["docs/guide.md"] = body + "\n",
            },
        };
    }

    private static WorkspaceDefinition CreateDefinition(WorkspaceKnowledgePackDefinition pack)
    {
        return new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "knowledge-test", Image = "ubuntu:24.04" },
            Provider = new WorkspaceProviderDefinition { Type = "git" },
            Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
            Features = ["core"],
            Services = [],
            Skills = [],
            Mcp = [],
            Agent = new AgentPreferences { Profile = AgentProfileResolver.BuiltInDefault.ProfileId },
            Terminal = new TerminalPreferences(),
            KnowledgePacks = [pack],
        };
    }

    private static string CreateTempRoot() => Path.Combine(Path.GetTempPath(), $"knowledge-pack-tests-{Guid.NewGuid():N}");

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            TestFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    private sealed class FakeKnowledgePackProvider(Func<KnowledgePackContext, ProvisionedKnowledgePackContent> contentFactory) : IKnowledgePackProvider
    {
        public string ProviderId => "fake-provider";

        public string Version => "1";

        public Func<KnowledgePackContext, ProvisionedKnowledgePackContent> ContentFactory { get; set; } = contentFactory;

        public bool IsApplicable(WorkspaceDefinition definition, WorkspaceKnowledgePackDefinition configuration)
            => true;

        public Task<ProvisionedKnowledgePackContent> GenerateAsync(KnowledgePackContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ContentFactory(context));
    }
}
