using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class CiWorkflowTests
{
    [Fact]
    public void IntegrationWorkflow_UsesMandatoryFinalCleanup_AndAvoidsBroadDockerPrune()
    {
        var workflow = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "integration.yml"));

        Assert.Contains("if: always()", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke cleanup --all --format json", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke cleanup --dry-run --all --format json", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("docker system prune", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker volume prune", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IntegrationWorkflow_RunsOracleSequentially_OnDedicatedRunner()
    {
        var workflow = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "integration.yml"));

        Assert.Contains("oracle-smoke-matrix:", workflow, StringComparison.Ordinal);
        Assert.Contains("self-hosted", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("oracle-smoke-matrix:\n    strategy:", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke run oracle-plsql-demo", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke run oracle-apex-demo", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke run oracle-apexlang-demo", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationWorkflow_UploadsArtifacts_AndUsesCanonicalSmokeCommands()
    {
        var workflow = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "integration.yml"));

        Assert.Contains("uses: actions/upload-artifact@v4", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke run --family lightweight", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke run --family postgresql", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke run --family analytics", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke run --family document-processing", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationBoundaryTests_UseRealHttpAndProtocolHarnesses()
    {
        var apiTests = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Api.IntegrationTests", "ApiTestFactory.cs"));
        var mcpTests = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Mcp.Tests", "McpProtocolIntegrationTests.cs"));

        Assert.Contains("WebApplicationFactory<Program>", apiTests, StringComparison.Ordinal);
        Assert.Contains("McpClient.CreateAsync", mcpTests, StringComparison.Ordinal);
        Assert.Contains("StdioClientTransport", mcpTests, StringComparison.Ordinal);
    }
}
