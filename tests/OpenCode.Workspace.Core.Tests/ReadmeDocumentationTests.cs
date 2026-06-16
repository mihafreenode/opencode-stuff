namespace OpenCode.Workspace.Core.Tests;

public sealed class ReadmeDocumentationTests
{
    [Fact]
    public void Readme_ReferencesOracleFamilyAndCurrentDocs()
    {
        var repositoryRoot = TestPaths.RepositoryRoot;
        var readmePath = Path.Combine(repositoryRoot, "README.md");
        var readme = File.ReadAllText(readmePath);

        Assert.Contains("## Oracle Family", readme);
        Assert.Contains("Oracle PL/SQL Demo", readme);
        Assert.Contains("Oracle APEX Demo", readme);
        Assert.Contains("Oracle APEXlang Demo", readme);
        Assert.Contains("Repository", readme);
        Assert.Contains("Workspace Discovery", readme);
        Assert.Contains("Provision Environment", readme);
        Assert.Contains("Read Documentation", readme);
        Assert.Contains("Start Working", readme);

        Assert.DoesNotContain("[Oracle PL/SQL Demo Workspace](docs/oracle-demo.md)", readme);

        var expectedLinks = new[]
        {
            "docs/oracle-plsql-demo.md",
            "docs/oracle-apex-demo.md",
            "docs/oracle-apexlang-demo.md",
            "docs/oracle-lifecycle-workflows.md",
            "docs/workspace-yaml.md",
            "docs/agents-guide.md",
            "AGENTS.md",
        };

        foreach (var relativePath in expectedLinks)
        {
            Assert.Contains($"({relativePath})", readme);
            Assert.True(File.Exists(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))), $"Expected README link target to exist: {relativePath}");
        }
    }
}
