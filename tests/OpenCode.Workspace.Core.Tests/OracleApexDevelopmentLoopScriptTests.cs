using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexDevelopmentLoopScriptTests
{
    [Fact]
    public void ExampleConfigurationFile_ContainsSupportedVariables()
    {
        var examplePath = Path.Combine(GetRepositoryRoot(), ".opencode", "local", "oracle-apex-development-loop.env.example");

        var content = File.ReadAllText(examplePath);

        Assert.Contains("OPENCODE_APEX_DEVLOOP_ENABLED=1", content, StringComparison.Ordinal);
        Assert.Contains("OPENCODE_APEX_DEVLOOP_SQLCL_PROFILE=local-apex-dev", content, StringComparison.Ordinal);
        Assert.Contains("OPENCODE_APEX_DEVLOOP_BUILDER_URL=", content, StringComparison.Ordinal);
        Assert.Contains("OPENCODE_APEX_DEVLOOP_APPLICATION_URL=", content, StringComparison.Ordinal);
    }

    [Fact]
    public void WrapperScript_PrintsChecklistAndExamplePathWhenConfigurationMissing()
    {
        var scriptPath = Path.Combine(GetRepositoryRoot(), "scripts", "testing", "oracle-apex-development-loop.ps1");
        var content = File.ReadAllText(scriptPath);

        Assert.Contains("Show-MissingConfigurationChecklist", content, StringComparison.Ordinal);
        Assert.Contains(".opencode/local/oracle-apex-development-loop.env.example", content, StringComparison.Ordinal);
        Assert.Contains("exit 2", content, StringComparison.Ordinal);
        Assert.Contains("Configuration is local only", content, StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
