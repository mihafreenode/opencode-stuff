using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Diagnostics;

public sealed class OracleApexDevelopmentEnvironmentConfigurationLoader
{
    public const string ExampleConfigurationRelativePath = ".opencode/local/oracle-apex-development-loop.env.example";
    public const string EnabledVariable = "OPENCODE_APEX_DEVLOOP_ENABLED";
    public const string WorkspaceRootVariable = "OPENCODE_APEX_DEVLOOP_WORKSPACE_ROOT";
    public const string EnvironmentVariable = "OPENCODE_APEX_DEVLOOP_ENVIRONMENT";
    public const string SqlclProfileVariable = "OPENCODE_APEX_DEVLOOP_SQLCL_PROFILE";
    public const string ApplicationIdVariable = "OPENCODE_APEX_DEVLOOP_APPLICATION_ID";
    public const string SourcePathVariable = "OPENCODE_APEX_DEVLOOP_SOURCE_PATH";
    public const string DeploymentProfileVariable = "OPENCODE_APEX_DEVLOOP_DEPLOYMENT_PROFILE";
    public const string BuilderUrlVariable = "OPENCODE_APEX_DEVLOOP_BUILDER_URL";
    public const string ApplicationUrlVariable = "OPENCODE_APEX_DEVLOOP_APPLICATION_URL";

    public IReadOnlyList<OracleApexDevelopmentEnvironmentVariableRequirement> GetRequirements()
        =>
        [
            new(EnabledVariable, "Enable the local-only Oracle APEX development loop.", "1"),
            new(WorkspaceRootVariable, "Point to the workspace root that contains workspace.yaml.", "C:\\Users\\name\\source\\repos\\oracle-apex-workspace"),
            new(EnvironmentVariable, "Select the configured Oracle APEX environment.", "dev"),
            new(SqlclProfileVariable, "Reference a local SQLcl connection profile.", "local-apex-dev"),
            new(ApplicationIdVariable, "Identify the target development application.", "100"),
            new(SourcePathVariable, "Point to the exported APEXlang source root.", "src/apex"),
            new(DeploymentProfileVariable, "Choose the deployment profile used by validate/import.", "development"),
            new(BuilderUrlVariable, "Open the APEX Builder page for the development app.", "https://example.test/ords/r/apex/app-builder/home?session=LOCAL"),
            new(ApplicationUrlVariable, "Open the running development application.", "https://example.test/ords/r/demo/home"),
        ];

    public bool IsEnabled()
        => string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "true", StringComparison.OrdinalIgnoreCase);

    public OracleApexDevelopmentEnvironmentConfigurationValidationResult ValidateEnvironment()
    {
        var requirements = GetRequirements();
        var missing = requirements
            .Where(requirement => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(requirement.Name)))
            .ToList();

        return new OracleApexDevelopmentEnvironmentConfigurationValidationResult
        {
            IsEnabled = IsEnabled(),
            MissingVariables = missing,
            ExampleConfigurationRelativePath = ExampleConfigurationRelativePath,
            NextAction = missing.Count == 0
                ? "Configuration is present. Run Oracle APEX Doctor or the development-loop wrapper."
                : $"Copy '{ExampleConfigurationRelativePath}', fill the placeholder values locally, then set the environment variables in your shell before running the wrapper.",
        };
    }

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

public sealed record OracleApexDevelopmentEnvironmentVariableRequirement(string Name, string Purpose, string PlaceholderValue);

public sealed class OracleApexDevelopmentEnvironmentConfigurationValidationResult
{
    public bool IsEnabled { get; init; }
    public IReadOnlyList<OracleApexDevelopmentEnvironmentVariableRequirement> MissingVariables { get; init; } = Array.Empty<OracleApexDevelopmentEnvironmentVariableRequirement>();
    public string ExampleConfigurationRelativePath { get; init; } = OracleApexDevelopmentEnvironmentConfigurationLoader.ExampleConfigurationRelativePath;
    public string NextAction { get; init; } = string.Empty;
    public bool HasRequiredConfiguration => IsEnabled && MissingVariables.Count == 0;
}
