using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class LocalMcpDocumentationTests
{
    [Fact]
    public void Readme_Links_To_LocalMcp_Guide_And_Uses_Release_Wording()
    {
        var readme = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "README.md"));

        Assert.Contains("## Local MCP integration", readme, StringComparison.Ordinal);
        Assert.Contains("[Local MCP setup](docs/local-mcp.md)", readme, StringComparison.Ordinal);
        Assert.Contains("local stdio MCP server", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bin/mcp/", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalMcp_Guide_Covers_All_Clients_And_Polling_Guidance()
    {
        var doc = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "docs", "local-mcp.md"));

        Assert.Contains("## Codex", doc, StringComparison.Ordinal);
        Assert.Contains("## Claude Code", doc, StringComparison.Ordinal);
        Assert.Contains("## OpenCode", doc, StringComparison.Ordinal);
        Assert.Contains("afterSequence", doc, StringComparison.Ordinal);
        Assert.Contains("an operation ID does not mean completion", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local-only", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stdio", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("single-user", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not run Docker Compose manually.", doc, StringComparison.Ordinal);
        Assert.Contains("Use the OpenCode Workspace MCP server.", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalMcp_Guide_Uses_Packaged_Paths_And_Avoids_Source_Based_Execution()
    {
        var doc = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "docs", "local-mcp.md"));
        var textBlocks = ExtractFencedBlocks(doc, "text");

        Assert.Contains("C:\\Tools\\OpenCode Workspace\\bin\\mcp\\OpenCode.Workspace.Mcp.exe", doc, StringComparison.Ordinal);
        Assert.Contains("/home/<user>/tools/opencode-workspace/bin/mcp/OpenCode.Workspace.Mcp", doc, StringComparison.Ordinal);
        Assert.Contains("/Users/<user>/tools/opencode-workspace/bin/mcp/OpenCode.Workspace.Mcp", doc, StringComparison.Ordinal);
        Assert.DoesNotContain(textBlocks, block => block.Contains("dotnet run", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(textBlocks, block => block.Contains("src/OpenCode.Workspace.Mcp", StringComparison.Ordinal));
        Assert.DoesNotContain(textBlocks, block => block.Contains("bin/Debug", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(textBlocks, block => block.Contains("bin/Release", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("C:\\Users\\", doc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/mnt/", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalMcp_Guide_Contains_Valid_OpenCode_Jsonc_Examples()
    {
        var doc = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "docs", "local-mcp.md"));
        var examples = ExtractFencedBlocks(doc, "jsonc").Where(block => block.Contains("\"opencode_workspace\"", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(examples);
        foreach (var example in examples)
        {
            var normalized = Regex.Replace(example, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);
            using var parsed = JsonDocument.Parse(normalized);
            var root = parsed.RootElement;
            Assert.True(root.TryGetProperty("mcp", out var mcp));
            Assert.True(mcp.TryGetProperty("opencode_workspace", out var server));
            Assert.Equal("local", server.GetProperty("type").GetString());
            Assert.True(server.GetProperty("enabled").GetBoolean());
            Assert.True(server.GetProperty("command").EnumerateArray().Any());
        }
    }

    [Fact]
    public void LocalMcp_Guide_Contains_Codex_And_Claude_Code_Examples_With_Supported_Syntax()
    {
        var doc = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "docs", "local-mcp.md"));

        Assert.Contains("~/.codex/config.toml", doc, StringComparison.Ordinal);
        Assert.Contains("[mcp_servers.opencode_workspace]", doc, StringComparison.Ordinal);
        Assert.Contains("startup_timeout_sec = 60", doc, StringComparison.Ordinal);
        Assert.Contains("tool_timeout_sec = 14400", doc, StringComparison.Ordinal);
        Assert.Contains("enabled = true", doc, StringComparison.Ordinal);
        Assert.Contains("required = true", doc, StringComparison.Ordinal);
        Assert.Contains("codex mcp list", doc, StringComparison.Ordinal);
        Assert.Contains("codex mcp add opencode_workspace --", doc, StringComparison.Ordinal);

        Assert.Contains("claude mcp add --scope user --transport stdio opencode-workspace --", doc, StringComparison.Ordinal);
        Assert.Contains("claude mcp list", doc, StringComparison.Ordinal);
        Assert.Contains("claude mcp remove opencode-workspace", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalMcp_Guide_Links_To_Official_Client_Documentation()
    {
        var doc = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "docs", "local-mcp.md"));

        Assert.Contains("https://developers.openai.com/codex/extend/mcp", doc, StringComparison.Ordinal);
        Assert.Contains("https://developers.openai.com/codex/config-file/config-reference", doc, StringComparison.Ordinal);
        Assert.Contains("https://docs.anthropic.com/en/docs/claude-code/mcp", doc, StringComparison.Ordinal);
        Assert.Contains("https://opencode.ai/docs/config/", doc, StringComparison.Ordinal);
        Assert.Contains("https://opencode.ai/docs/mcp-servers/", doc, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ExtractFencedBlocks(string text, string language)
    {
        var matches = Regex.Matches(text, $"```{Regex.Escape(language)}\\r?\\n(.*?)```", RegexOptions.Singleline);
        return matches.Select(match => match.Groups[1].Value).ToArray();
    }
}
