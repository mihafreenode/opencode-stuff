namespace OpenCode.Workspace.Core.Tests;

public sealed class ReadmeDocumentationTests
{
    [Fact]
    public void Readme_IsAConciseFrontDoorToCurrentDocumentation()
    {
        var readme = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "README.md"));

        foreach (var heading in new[]
        {
            "# OpenCode Workspace Manager",
            "## Install On Windows",
            "## Create A Workspace",
            "## Open An Existing Repository",
            "## Open, Then Attach",
            "## Protect And Share Work",
            "## Workspace Configuration",
            "## MCP Integration",
            "## Oracle And APEX",
            "## Documentation",
        })
        {
            Assert.Contains(heading, readme, StringComparison.Ordinal);
        }

        foreach (var target in new[]
        {
            "docs/index.md",
            "docs/getting-started.md",
            "docs/user/workspaces.md",
            "docs/user/sessions.md",
            "docs/user/backup-and-publish.md",
            "docs/user/troubleshooting.md",
            "docs/architecture/overview.md",
            "docs/integrations/mcp.md",
            "docs/integrations/oracle-apex.md",
            "docs/integrations/cloudflare-remote-access.md",
            "docs/reference/cli.md",
            "docs/reference/package-layout.md",
            "docs/reference/workspace-yaml.md",
        })
        {
            Assert.Contains($"({target})", readme, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("## Featured Workspace", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Philosophy_AndDesignPrinciples_StateCurrentDurabilityAndOwnershipRules()
    {
        var root = TestPaths.RepositoryRoot;
        var philosophy = File.ReadAllText(Path.Combine(root, "docs", "philosophy.md"));
        var principles = File.ReadAllText(Path.Combine(root, "docs", "design-principles.md"));

        foreach (var heading in new[]
        {
            "# Philosophy",
            "## Durable Workspaces",
            "## Git As Persistence",
            "## Recoverability Over Convenience",
            "## Visible And Transferable Knowledge",
            "## Product Direction",
        })
        {
            Assert.Contains(heading, philosophy, StringComparison.Ordinal);
        }

        Assert.Contains("Conflict is not failure. Lost work is failure.", philosophy, StringComparison.Ordinal);
        Assert.Contains("workspace is the durable asset", philosophy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[Design Principles](design-principles.md)", philosophy, StringComparison.Ordinal);
        Assert.Contains("[Architecture Overview](architecture/overview.md)", philosophy, StringComparison.Ordinal);
        Assert.Contains("[Recovery Model](architecture/recovery-model.md)", philosophy, StringComparison.Ordinal);

        foreach (var heading in new[]
        {
            "# Design Principles",
            "## Workspace Before Runtime",
            "## One Canonical Owner",
            "## Inspectable Automation",
            "## Recoverability First",
            "## Narrow Trust Boundaries",
            "## Portable Understanding",
            "## Validate The Owning Environment",
        })
        {
            Assert.Contains(heading, principles, StringComparison.Ordinal);
        }

        Assert.Contains("[project philosophy](philosophy.md)", principles, StringComparison.Ordinal);
        Assert.Contains("Shared state and mutations use LocalHost", principles, StringComparison.Ordinal);
        Assert.Contains("MCP is local control, never a PTY or remote transport", principles, StringComparison.Ordinal);
    }
}
