using OpenCode.Workspace.Core.Diagnostics;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexDevelopmentEnvironmentConfigurationLoaderTests
{
    [Fact]
    public void TryLoad_ReturnsNullWhenDisabled()
    {
        Environment.SetEnvironmentVariable(OracleApexDevelopmentEnvironmentConfigurationLoader.EnabledVariable, null);
        var loader = new OracleApexDevelopmentEnvironmentConfigurationLoader();

        var config = loader.TryLoad();

        Assert.Null(config);
    }

    [Fact]
    public void TryLoad_ReadsConfiguredEnvironmentVariables()
    {
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.EnabledVariable, "1");
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.WorkspaceRootVariable, "C:/work/apex");
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.EnvironmentVariable, "dev");
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.SqlclProfileVariable, "local-apex-dev");
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.ApplicationIdVariable, "100");
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.SourcePathVariable, "src/apex");
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.DeploymentProfileVariable, "development");
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.BuilderUrlVariable, "https://example.test/ords/r/apex/app-builder/home");
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.ApplicationUrlVariable, "https://example.test/ords/r/demo/home");
        var loader = new OracleApexDevelopmentEnvironmentConfigurationLoader();

        var config = loader.TryLoad();

        Assert.NotNull(config);
        Assert.Equal("C:/work/apex", config!.WorkspaceRoot);
        Assert.Equal("local-apex-dev", config.SqlclProfile);
        Assert.Equal(100, config.ApplicationId);
        Assert.Equal("development", config.DeploymentProfile);
    }

    [Fact]
    public void ValidateEnvironment_ReportsMissingVariablesAndExamplePath()
    {
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.EnabledVariable, null);
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.WorkspaceRootVariable, null);
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.EnvironmentVariable, null);
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.SqlclProfileVariable, null);
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.ApplicationIdVariable, null);
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.SourcePathVariable, null);
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.DeploymentProfileVariable, null);
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.BuilderUrlVariable, null);
        SetEnv(OracleApexDevelopmentEnvironmentConfigurationLoader.ApplicationUrlVariable, null);
        var loader = new OracleApexDevelopmentEnvironmentConfigurationLoader();

        var result = loader.ValidateEnvironment();

        Assert.False(result.HasRequiredConfiguration);
        Assert.Contains(result.MissingVariables, item => item.Name == OracleApexDevelopmentEnvironmentConfigurationLoader.EnabledVariable);
        Assert.Equal(OracleApexDevelopmentEnvironmentConfigurationLoader.ExampleConfigurationRelativePath, result.ExampleConfigurationRelativePath);
        Assert.Contains("Copy '.opencode/local/oracle-apex-development-loop.env.example'", result.NextAction, StringComparison.Ordinal);
    }

    private static void SetEnv(string name, string? value)
        => Environment.SetEnvironmentVariable(name, value);
}
