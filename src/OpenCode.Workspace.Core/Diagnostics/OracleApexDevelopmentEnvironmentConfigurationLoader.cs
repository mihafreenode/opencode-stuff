using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Diagnostics;

public sealed class OracleApexDevelopmentEnvironmentConfigurationLoader
{
    public const string EnabledVariable = "OPENCODE_APEX_DEVLOOP_ENABLED";
    public const string WorkspaceRootVariable = "OPENCODE_APEX_DEVLOOP_WORKSPACE_ROOT";
    public const string EnvironmentVariable = "OPENCODE_APEX_DEVLOOP_ENVIRONMENT";
    public const string SqlclProfileVariable = "OPENCODE_APEX_DEVLOOP_SQLCL_PROFILE";
    public const string ApplicationIdVariable = "OPENCODE_APEX_DEVLOOP_APPLICATION_ID";
    public const string SourcePathVariable = "OPENCODE_APEX_DEVLOOP_SOURCE_PATH";
    public const string DeploymentProfileVariable = "OPENCODE_APEX_DEVLOOP_DEPLOYMENT_PROFILE";
    public const string BuilderUrlVariable = "OPENCODE_APEX_DEVLOOP_BUILDER_URL";
    public const string ApplicationUrlVariable = "OPENCODE_APEX_DEVLOOP_APPLICATION_URL";

    public bool IsEnabled()
        => string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "true", StringComparison.OrdinalIgnoreCase);

    public OracleApexDevelopmentEnvironmentConfiguration? TryLoad()
    {
        if (!IsEnabled())
        {
            return null;
        }

        var workspaceRoot = Environment.GetEnvironmentVariable(WorkspaceRootVariable)?.Trim() ?? string.Empty;
        var sqlclProfile = Environment.GetEnvironmentVariable(SqlclProfileVariable)?.Trim() ?? string.Empty;
        var sourcePath = Environment.GetEnvironmentVariable(SourcePathVariable)?.Trim() ?? "src/apex";
        var builderUrl = Environment.GetEnvironmentVariable(BuilderUrlVariable)?.Trim() ?? string.Empty;
        var applicationUrl = Environment.GetEnvironmentVariable(ApplicationUrlVariable)?.Trim() ?? string.Empty;
        var deploymentProfile = Environment.GetEnvironmentVariable(DeploymentProfileVariable)?.Trim() ?? string.Empty;
        var environmentName = Environment.GetEnvironmentVariable(EnvironmentVariable)?.Trim() ?? "dev";
        var applicationIdValue = Environment.GetEnvironmentVariable(ApplicationIdVariable)?.Trim() ?? string.Empty;
        return new OracleApexDevelopmentEnvironmentConfiguration
        {
            WorkspaceRoot = workspaceRoot,
            EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? "dev" : environmentName,
            SqlclProfile = sqlclProfile,
            ApplicationId = int.TryParse(applicationIdValue, out var applicationId) ? applicationId : 0,
            SourcePath = string.IsNullOrWhiteSpace(sourcePath) ? "src/apex" : sourcePath.Replace('\\', '/'),
            DeploymentProfile = deploymentProfile,
            BuilderUrl = builderUrl,
            ApplicationUrl = applicationUrl,
        };
    }
}
