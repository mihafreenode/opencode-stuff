using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexTemplateRegressionTests
{
    [Fact]
    public void OracleApexDemoTemplate_GeneratesExpectedApexArtifacts()
    {
        Assert.True(OracleTemplateTestHelpers.CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = OracleTemplateTestHelpers.CreateTempRoot("oracle-apex-regression");

        try
        {
            var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
            var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices());
            var template = provider.LoadTemplates().Single(item => item.Id == "oracle-apex-demo");
            var definition = new TemplateExpander().Expand("oracle-apex-workspace", template);
            var snapshot = OracleTemplateTestHelpers.CreateOrchestrator(tempRoot, resolver).CreateWorkspace(tempRoot, definition);

            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-apex-demo.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "health-check-ords.sh")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "open-ords.ps1")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "tutorial", "oracle", "init", "03-customers-schema.sql")));
            Assert.False(File.Exists(Path.Combine(snapshot.Paths.RootPath, "apex", "application.apx")));
        }
        finally
        {
            OracleTemplateTestHelpers.DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void OracleApexLangDemoTemplate_GeneratesExpectedSourceControlledArtifacts()
    {
        Assert.True(OracleTemplateTestHelpers.CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = OracleTemplateTestHelpers.CreateTempRoot("oracle-apexlang-regression");

        try
        {
            var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
            var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices());
            var template = provider.LoadTemplates().Single(item => item.Id == "oracle-apexlang-demo");
            var definition = new TemplateExpander().Expand("oracle-apexlang-workspace", template);
            var snapshot = OracleTemplateTestHelpers.CreateOrchestrator(tempRoot, resolver).CreateWorkspace(tempRoot, definition);

            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-apexlang-demo.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "apexlang-introduction.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "apex", "application.apx")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "export-apex.sh")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "import-apex.sh")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "validate-apex.sh")));

            var workspaceYaml = File.ReadAllText(snapshot.Paths.WorkspaceYamlPath);
            Assert.Contains("oracle-ords", workspaceYaml);
            Assert.Contains("oracle-apexlang-demo", workspaceYaml);
        }
        finally
        {
            OracleTemplateTestHelpers.DeleteTempRoot(tempRoot);
        }
    }
}

internal static class OracleTemplateTestHelpers
{
    public static WorkspaceOrchestrator CreateOrchestrator(string tempRoot, WorkspaceResolver resolver)
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

    public static string CreateTempRoot(string prefix) => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");

    public static void DeleteTempRoot(string path)
    {
        if (Directory.Exists(path))
        {
            TestFileSystem.DeleteDirectoryIfExists(path);
        }
    }

    public static bool CanRunGit()
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
