using System.IO;
using OpenCode.Workspace.Platform.Windows;

namespace OpenCode.Workspace.Platform.Windows.Tests;

public sealed class TmpReprovisionWorkflowServiceTests
{
    [Fact]
    public void ResolveRepositoryRoot_FindsRepositoryFromNestedOutputPath()
    {
        var nestedPath = Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Manager", "bin", "Debug", "net10.0-windows");

        var repositoryRoot = TmpReprovisionWorkflowService.ResolveRepositoryRoot(nestedPath);

        Assert.Equal(TestPaths.RepositoryRoot, repositoryRoot);
    }

    [Fact]
    public void EnsureProjectGenerated_WritesTmpReprovisionProjectFiles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"ocwm-tmp-reprovision-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            File.Copy(Path.Combine(TestPaths.RepositoryRoot, "OpenCode.Workspace.Manager.slnx"), Path.Combine(tempRoot, "OpenCode.Workspace.Manager.slnx"));

            var projectPath = TmpReprovisionWorkflowService.EnsureProjectGenerated(tempRoot);
            var programPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "Program.cs");

            Assert.True(File.Exists(projectPath));
            Assert.True(File.Exists(programPath));

            var projectText = File.ReadAllText(projectPath);
            var programText = File.ReadAllText(programPath);

            Assert.Contains("../../src/OpenCode.Workspace.AppSupport/OpenCode.Workspace.AppSupport.csproj", projectText);
            Assert.Contains("../../src/OpenCode.Workspace.Core/OpenCode.Workspace.Core.csproj", projectText);
            Assert.Contains("provider.LoadCapabilities()", programText);
            Assert.Contains("provider.LoadKnowledgePacks()", programText);
            Assert.Contains("await orchestrator.ProvisionAsync(snapshot, entry => Console.WriteLine($\"[{entry.Source}] {entry.Message}\"));", programText);
            Assert.Contains("NoOpTerminalLauncher", programText);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
