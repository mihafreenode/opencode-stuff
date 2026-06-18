using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OraclePlSqlTemplateRegressionTests
{
    [Fact]
    public void OraclePlSqlDemoTemplate_GeneratesExpectedArtifacts_WithoutApexArtifacts()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
            var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
            var template = provider.LoadTemplates().Single(item => item.Id == "oracle-plsql-demo");
            var definition = new TemplateExpander().Expand("oracle-demo-workspace", template);
            var snapshot = CreateOrchestrator(tempRoot, resolver).CreateWorkspace(tempRoot, definition);

            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-demo.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "ORACLE-DEMO.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "verify-oracle-demo.sh")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "tutorial", "oracle", "START-HERE-ORACLE.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, ".opencode", "context", "oracle-demo.json")));

            Assert.False(Directory.Exists(Path.Combine(snapshot.Paths.RootPath, "apex")));
            Assert.False(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "export-apex.sh")));
            Assert.False(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "import-apex.sh")));
            Assert.False(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "validate-apex.sh")));
            Assert.False(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-apex-demo.md")));
            Assert.False(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-apexlang-demo.md")));

            var workspaceYaml = File.ReadAllText(snapshot.Paths.WorkspaceYamlPath);
            Assert.Contains("oracle-demo", workspaceYaml);
            Assert.DoesNotContain("apexlang", workspaceYaml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ords", workspaceYaml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    private static WorkspaceOrchestrator CreateOrchestrator(string tempRoot, WorkspaceResolver resolver)
    {
        var ignorePolicyService = new WorkspaceIgnorePolicyService();
        return new WorkspaceOrchestrator(
            new WorkspaceYamlService(),
            new WorkspaceDiscoveryService(),
            new WorkspaceRepository(Path.Combine(tempRoot, ".appdata")),
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
            ignorePolicyService,
            new GitWorkspaceProvider(new ProcessRunner(), ignorePolicyService),
            new DockerService(new ProcessRunner()),
            new NoOpTerminalLauncher());
    }

    private static string CreateTempRoot() => Path.Combine(Path.GetTempPath(), $"oracle-plsql-regression-{Guid.NewGuid():N}");

    private static void DeleteTempRoot(string path)
    {
        if (Directory.Exists(path))
        {
            TestFileSystem.DeleteDirectoryIfExists(path);
        }
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

    private sealed class NoOpTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
