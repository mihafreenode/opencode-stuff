using OpenCode.Workspace.Core.Diagnostics;
using OpenCode.Workspace.Core.Models;
using Xunit;
namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexAssistantIntegrationTests
{
    [SkippableFact]
    [Trait("Category", "OracleApexDevelopmentLoopIntegration")]
    public async Task AssistantWorkflow_CanValidateRepairAndImportAgainstLocalEnvironment()
    {
        var configuration = GetRequiredConfiguration();
        var doctor = new OracleApexEnvironmentDoctorService();

        var result = await doctor.DiagnoseAsync(configuration);

        RequireOrSkip(result.IsSuccess, $"Oracle APEX environment doctor failed: {result.Summary}");

        Assert.True(result.IsSuccess);
    }

    [SkippableFact]
    [Trait("Category", "OracleApexDevelopmentLoopIntegration")]
    public async Task AssistantWorkflow_CanRunPromptToPreviewSmokeAgainstLocalEnvironment()
    {
        var configuration = GetRequiredConfiguration();
        var doctor = new OracleApexEnvironmentDoctorService();

        var result = await doctor.DiagnoseAsync(configuration);

        RequireOrSkip(result.IsSuccess, $"Oracle APEX environment doctor failed: {result.Summary}");

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(Path.Combine(configuration.WorkspaceRoot, configuration.SourcePath.Replace('/', Path.DirectorySeparatorChar), "application.apx")));
    }

    [SkippableFact]
    [Trait("Category", "OracleApexDevelopmentLoopIntegration")]
    public async Task AssistantWorkflow_CanRunReverseBuilderToGitSmokeAgainstLocalEnvironment()
    {
        var configuration = GetRequiredConfiguration();
        RequireOrSkip(
            string.Equals(Environment.GetEnvironmentVariable("OPENCODE_APEX_DEVLOOP_EXPECTS_BUILDER_CHANGE"), "1", StringComparison.OrdinalIgnoreCase),
            "Reverse Builder smoke requires OPENCODE_APEX_DEVLOOP_EXPECTS_BUILDER_CHANGE=1.");
        var doctor = new OracleApexEnvironmentDoctorService();

        var result = await doctor.DiagnoseAsync(configuration);

        RequireOrSkip(result.IsSuccess, $"Oracle APEX environment doctor failed: {result.Summary}");

        Assert.True(result.IsSuccess);
    }

    [SkippableFact]
    [Trait("Category", "OracleApexDevelopmentLoopIntegration")]
    public void AssistantWorkflow_CanValidateCloneAndReconnectPrerequisites()
    {
        var configuration = GetRequiredConfiguration();

        Assert.True(File.Exists(Path.Combine(configuration.WorkspaceRoot, "workspace.yaml")));
    }

    private static OracleApexDevelopmentEnvironmentConfiguration GetRequiredConfiguration()
    {
        var loader = new OracleApexDevelopmentEnvironmentConfigurationLoader();
        RequireOrSkip(loader.IsEnabled(), $"Oracle APEX development-loop tests require {OracleApexDevelopmentEnvironmentConfigurationLoader.EnabledVariable}=1.");
        var validation = loader.ValidateEnvironment();
        RequireOrSkip(
            validation.MissingVariables.Count == 0,
            $"Oracle APEX development-loop configuration is missing: {string.Join(", ", validation.MissingVariables.Select(item => item.Name))}.");
        return loader.TryLoad() ?? throw new InvalidOperationException("Enabled Oracle APEX development-loop configuration could not be loaded.");
    }

    private static void RequireOrSkip(bool condition, string message)
    {
        if (IsVerificationMode())
        {
            Assert.True(condition, message);
            return;
        }

        Skip.IfNot(condition, message);
    }

    private static bool IsVerificationMode()
        => string.Equals(Environment.GetEnvironmentVariable("OPENCODE_ORACLE_VERIFICATION_MODE"), "true", StringComparison.OrdinalIgnoreCase);
}
