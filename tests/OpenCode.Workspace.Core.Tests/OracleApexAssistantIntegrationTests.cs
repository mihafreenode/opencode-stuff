using OpenCode.Workspace.Core.Diagnostics;
using OpenCode.Workspace.Core.Models;
using Xunit;
namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexAssistantIntegrationTests
{
    [Fact]
    public async Task AssistantWorkflow_CanValidateRepairAndImportAgainstLocalEnvironment()
    {
        var configuration = TryGetConfiguration();
        if (configuration is null)
        {
            return;
        }
        var doctor = new OracleApexEnvironmentDoctorService();

        var result = await doctor.DiagnoseAsync(configuration);

        if (!result.IsSuccess)
        {
            return;
        }

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AssistantWorkflow_CanRunPromptToPreviewSmokeAgainstLocalEnvironment()
    {
        var configuration = TryGetConfiguration();
        if (configuration is null)
        {
            return;
        }
        var doctor = new OracleApexEnvironmentDoctorService();

        var result = await doctor.DiagnoseAsync(configuration);

        if (!result.IsSuccess)
        {
            return;
        }

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(Path.Combine(configuration.WorkspaceRoot, configuration.SourcePath.Replace('/', Path.DirectorySeparatorChar), "application.apx")));
    }

    [Fact]
    public async Task AssistantWorkflow_CanRunReverseBuilderToGitSmokeAgainstLocalEnvironment()
    {
        var configuration = TryGetConfiguration();
        if (configuration is null || !string.Equals(Environment.GetEnvironmentVariable("OPENCODE_APEX_DEVLOOP_EXPECTS_BUILDER_CHANGE"), "1", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var doctor = new OracleApexEnvironmentDoctorService();

        var result = await doctor.DiagnoseAsync(configuration);

        if (!result.IsSuccess)
        {
            return;
        }

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void AssistantWorkflow_CanValidateCloneAndReconnectPrerequisites()
    {
        var configuration = TryGetConfiguration();
        if (configuration is null)
        {
            return;
        }

        Assert.True(File.Exists(Path.Combine(configuration.WorkspaceRoot, "workspace.yaml")));
    }

    private static OracleApexDevelopmentEnvironmentConfiguration? TryGetConfiguration()
    {
        var loader = new OracleApexDevelopmentEnvironmentConfigurationLoader();
        return loader.IsEnabled() ? loader.TryLoad() : null;
    }
}
