using System.Reflection;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleRuntimeSmokeToolTests
{
    [Theory]
    [InlineData("oracle-plsql-demo")]
    [InlineData("oracle-apex-demo")]
    [InlineData("oracle-apexlang-demo")]
    public void Parse_AcceptsSupportedTemplateIds(string templateId)
    {
        var options = OracleRuntimeSmokeCli.Parse(["--template", templateId, "--dry-run"]);

        Assert.Equal(templateId, options.TemplateId);
        Assert.True(options.DryRun);
        Assert.Equal(SmokeValidationHost.Auto, options.Host);
    }

    [Fact]
    public void Parse_RejectsUnsupportedTemplateIds()
    {
        var exception = Assert.Throws<ArgumentException>(() => OracleRuntimeSmokeCli.Parse(["--template", "oracle-unknown-demo"]));
        Assert.Contains("Unsupported template", exception.Message);
    }

    [Fact]
    public void Parse_RecognizesWorkspaceArtifactsHostAndDryRunArguments()
    {
        var options = OracleRuntimeSmokeCli.Parse(
        [
            "--template", "oracle-apex-demo",
            "--workspace-root", "/tmp/workspace",
            "--artifacts-root", "/tmp/artifacts",
            "--host", "windows",
            "--dry-run",
            "--invoked-from-wrapper",
        ]);

        Assert.Equal("/tmp/workspace", options.WorkspaceRoot);
        Assert.Equal("/tmp/artifacts", options.ArtifactsRoot);
        Assert.Equal(SmokeValidationHost.Windows, options.Host);
        Assert.True(options.DryRun);
        Assert.True(options.InvokedFromWrapper);
    }

    [Fact]
    public void ArtifactRunDirectoryName_IsDeterministic()
    {
        var timestamp = new DateTimeOffset(2026, 6, 16, 20, 15, 42, TimeSpan.Zero);
        Assert.Equal("20260616-201542", OracleRuntimeSmokeCli.CreateArtifactRunDirectoryName(timestamp));
    }

    [Fact]
    public void FailureClassificationLabels_Exist()
    {
        Assert.Equal(
        [
            "ValidationToolingFailure",
            "EnvironmentFailure",
            "ProductFailure",
            "OracleRuntimeFailure",
        ],
            Enum.GetNames<SmokeFailureClassification>());
    }

    [Fact]
    public void RuntimeSmokeDocs_ExplainWslAndWindowsHostSelection()
    {
        var root = TestPaths.RepositoryRoot;
        var smokeDoc = File.ReadAllText(Path.Combine(root, "docs", "testing", "oracle-apex-runtime-smoke.md"));
        var agentsDoc = File.ReadAllText(Path.Combine(root, "AGENTS.md"));
        var troubleshootingDoc = File.ReadAllText(Path.Combine(root, "docs", "troubleshooting", "wsl-windows-interop.md"));

        Assert.Contains("docker version", smokeDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("powershell.exe -NoProfile -Command \"docker version\"", smokeDoc);
        Assert.Contains("Windows Docker Desktop", smokeDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Static Tests", smokeDoc);
        Assert.Contains("Smoke Runner Dry Run", smokeDoc);
        Assert.Contains("Live Runtime Smoke", smokeDoc);
        Assert.Contains("Validation Tooling Failure", smokeDoc);
        Assert.Contains("Environment Failure", smokeDoc);
        Assert.Contains("Product Failure", smokeDoc);
        Assert.Contains("Oracle Runtime Failure", smokeDoc);

        Assert.Contains("Runtime Validation: WSL vs Windows Host", agentsDoc);
        Assert.Contains("Runtime Validation Ladder", agentsDoc);
        Assert.Contains("Validation Tooling Is Part Of The Product", agentsDoc);
        Assert.Contains("Use Windows Docker Desktop result as authoritative", agentsDoc);
        Assert.Contains("tools/OracleRuntimeSmoke/", agentsDoc);
        Assert.Contains("scripts/testing/oracle-runtime-smoke.ps1", agentsDoc);

        Assert.Contains("Windows host validation as authoritative", troubleshootingDoc, StringComparison.OrdinalIgnoreCase);
    }
}
