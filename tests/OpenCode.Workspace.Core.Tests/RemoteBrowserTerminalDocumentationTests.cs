namespace OpenCode.Workspace.Core.Tests;

public sealed class RemoteBrowserTerminalDocumentationTests
{
    [Fact]
    public void RemoteAccessDocs_KeepSetupOwnershipAcceptanceAndHistoryInTheirAuthorities()
    {
        var root = TestPaths.RepositoryRoot;
        var setup = File.ReadAllText(Path.Combine(root, "docs", "integrations", "cloudflare-remote-access.md"));
        var architecture = File.ReadAllText(Path.Combine(root, "docs", "architecture", "remote-access.md"));
        var testing = File.ReadAllText(Path.Combine(root, "docs", "development", "testing.md"));
        var history = File.ReadAllText(Path.Combine(root, "docs", "history", "release-notes.md"));

        Assert.Contains("service: http://127.0.0.1:38443", setup, StringComparison.Ordinal);
        Assert.Contains("service: http_status:404", setup, StringComparison.Ordinal);
        Assert.Contains("Application Audience (AUD) Tag", setup, StringComparison.Ordinal);
        Assert.Contains("cdn-cgi/access/certs", setup, StringComparison.Ordinal);
        Assert.Contains("disabled by default", setup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("optional, manual, and not a CI requirement", setup, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("## Trust Boundary", architecture, StringComparison.Ordinal);
        Assert.Contains("LocalHost remains loopback-only and owns sessions", architecture, StringComparison.Ordinal);
        Assert.Contains("RemoteBridge owns no runtime", architecture, StringComparison.Ordinal);
        Assert.Contains("RemoteBridge is not:", architecture, StringComparison.Ordinal);
        Assert.Contains("No PTY stream travels through MCP", architecture, StringComparison.Ordinal);

        Assert.Contains("The real Cloudflare smoke is optional", testing, StringComparison.Ordinal);
        Assert.Contains("RemoteBridge disabled-by-default behavior", testing, StringComparison.Ordinal);

        Assert.Contains("historical chronology", history, StringComparison.Ordinal);
        Assert.Contains("## Remote browser terminal milestone", history, StringComparison.Ordinal);
        Assert.Contains("[Cloudflare remote access](../integrations/cloudflare-remote-access.md)", history, StringComparison.Ordinal);
    }
}
