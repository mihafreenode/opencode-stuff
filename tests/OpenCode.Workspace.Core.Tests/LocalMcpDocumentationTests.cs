namespace OpenCode.Workspace.Core.Tests;

public sealed class LocalMcpDocumentationTests
{
    [Fact]
    public void McpGuide_UsesPackagedHostsAndCliConfigurationCommands()
    {
        var doc = ReadGuide();

        foreach (var path in new[]
        {
            "bin/cli/OpenCode.Workspace.Cli",
            "bin/local-host/OpenCode.Workspace.LocalHost",
            "bin/mcp/OpenCode.Workspace.Mcp",
            "config/mcp/appsettings.json",
        })
        {
            Assert.Contains(path, doc, StringComparison.Ordinal);
        }

        Assert.Contains("mcp configure codex", doc, StringComparison.Ordinal);
        Assert.Contains("mcp configure claude", doc, StringComparison.Ordinal);
        Assert.Contains("mcp configure opencode", doc, StringComparison.Ordinal);
        Assert.Contains("mcp doctor", doc, StringComparison.Ordinal);
        Assert.Contains("Do not configure `dotnet run`", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void McpGuide_StatesLocalHostControllerAndOperationPollingContracts()
    {
        var doc = ReadGuide();

        Assert.Contains("LocalHost owns the canonical workspace inventory", doc, StringComparison.Ordinal);
        Assert.Contains("## Controller and multi-client behavior", doc, StringComparison.Ordinal);
        Assert.Contains("distinct controller session", doc, StringComparison.Ordinal);
        Assert.Contains("several desktop or MCP clients", doc, StringComparison.Ordinal);
        Assert.Contains("afterSequence", doc, StringComparison.Ordinal);
        Assert.Contains("An operation ID is not completion", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void McpGuide_StatesSecurityAndOracleProductSurfaceBoundaries()
    {
        var doc = ReadGuide();

        Assert.Contains("MCP has no terminal or PTY API", doc, StringComparison.Ordinal);
        Assert.Contains("RemoteBridge and Cloudflare do not expose MCP", doc, StringComparison.Ordinal);
        Assert.Contains("no Oracle discovery, synchronization, or Oracle Assistant tools", doc, StringComparison.Ordinal);
        Assert.Contains("canonical `LocalHost` routes", doc, StringComparison.Ordinal);
        Assert.Contains("There is no supported in-process fallback", doc, StringComparison.Ordinal);
    }

    private static string ReadGuide()
        => File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "docs", "integrations", "mcp.md"));
}
