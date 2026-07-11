using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Diagnostics;

public sealed class OracleApexEnvironmentDoctorService
{
    private readonly OracleApexDevelopmentEnvironmentConfigurationLoader _configurationLoader;
    private readonly WorkspaceYamlService _workspaceYamlService;
    private readonly WorkspaceSynchronizationStateService _stateService;
    private readonly OracleApexWorkspaceIndexBuilder _workspaceIndexBuilder;
    private readonly IProcessRunner _processRunner;

    public OracleApexEnvironmentDoctorService(
        OracleApexDevelopmentEnvironmentConfigurationLoader? configurationLoader = null,
        WorkspaceYamlService? workspaceYamlService = null,
        WorkspaceSynchronizationStateService? stateService = null,
        OracleApexWorkspaceIndexBuilder? workspaceIndexBuilder = null,
        IProcessRunner? processRunner = null)
    {
        _configurationLoader = configurationLoader ?? new OracleApexDevelopmentEnvironmentConfigurationLoader();
        _workspaceYamlService = workspaceYamlService ?? new WorkspaceYamlService();
        _stateService = stateService ?? new WorkspaceSynchronizationStateService();
        _workspaceIndexBuilder = workspaceIndexBuilder ?? new OracleApexWorkspaceIndexBuilder();
        _processRunner = processRunner ?? new ProcessRunner();
    }

    public Task<OracleApexDoctorResult> DiagnoseLocalConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var validation = _configurationLoader.ValidateEnvironment();
        if (validation.HasRequiredConfiguration)
        {
            return DiagnoseAsync(_configurationLoader.TryLoad()!, cancellationToken);
        }

        var checks = new List<OracleApexDoctorCheckResult>();
        if (!validation.IsEnabled)
        {
            checks.Add(Fail(
                "Local development loop enabled",
                $"Set '{OracleApexDevelopmentEnvironmentConfigurationLoader.EnabledVariable}' to '1' or 'true' before running the local Oracle APEX workflow.",
                $"Copy '{OracleApexDevelopmentEnvironmentConfigurationLoader.ExampleConfigurationRelativePath}', fill the placeholder values locally, then export the variables into your shell."));
        }

        foreach (var missing in validation.MissingVariables)
        {
            checks.Add(Fail(
                missing.Name,
                $"'{missing.Name}' is missing. {missing.Purpose}",
                $"Set '{missing.Name}' in your local shell or session. Example template: '{validation.ExampleConfigurationRelativePath}'."));
        }

        return Task.FromResult(BuildResult(Environment.CurrentDirectory, "dev", checks, validation.NextAction));
    }

    public async Task<OracleApexDoctorResult> DiagnoseAsync(OracleApexDevelopmentEnvironmentConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var checks = new List<OracleApexDoctorCheckResult>();
        var workspaceRoot = Path.GetFullPath(configuration.WorkspaceRoot);
        var workspaceYamlPath = Path.Combine(workspaceRoot, "workspace.yaml");
        if (!File.Exists(workspaceYamlPath))
        {
            checks.Add(Fail("Workspace definition", "workspace.yaml was not found.", "Point the Doctor to a workspace root that contains workspace.yaml."));
            return BuildResult(workspaceRoot, configuration.EnvironmentName, checks);
        }

        var definition = _workspaceYamlService.Read(workspaceYamlPath);
        if (!definition.Oracle.Apex.Environments.TryGetValue(configuration.EnvironmentName, out var environment))
        {
            checks.Add(Fail("Environment", $"Oracle APEX environment '{configuration.EnvironmentName}' is not configured.", "Add the environment to workspace.yaml or choose a configured environment."));
            return BuildResult(workspaceRoot, configuration.EnvironmentName, checks);
        }

        checks.Add(await CheckSqlclAvailableAsync(cancellationToken).ConfigureAwait(false));
        checks.Add(await CheckSqlclCommandSupportAsync(cancellationToken).ConfigureAwait(false));
        checks.Add(string.IsNullOrWhiteSpace(configuration.SqlclProfile)
            ? Fail("SQLcl profile", "SQLcl profile is missing.", "Set OPENCODE_APEX_DEVLOOP_SQLCL_PROFILE to a resolvable development profile.")
            : Pass("SQLcl profile", $"Using SQLcl profile '{configuration.SqlclProfile}'."));
        checks.Add(string.IsNullOrWhiteSpace(environment.SourcePath) || !File.Exists(Path.Combine(workspaceRoot, environment.SourcePath.Replace('/', Path.DirectorySeparatorChar), "application.apx"))
            ? Fail("Source metadata", "APEXlang source metadata is incomplete or application.apx is missing.", "Verify sourcePath and export the application into the configured source folder.")
            : Pass("Source metadata", $"APEXlang source found at '{environment.SourcePath}'."));
        checks.Add(environment.ApplicationId is > 0 || configuration.ApplicationId > 0
            ? Pass("Application identity", $"Application id '{environment.ApplicationId ?? configuration.ApplicationId}' is configured.")
            : Fail("Application identity", "Application id is missing.", "Set applicationId in workspace.yaml or OPENCODE_APEX_DEVLOOP_APPLICATION_ID for the local smoke workflow."));

        var index = _workspaceIndexBuilder.Build(workspaceRoot, environment, configuration.EnvironmentName);
        var deploymentProfileName = string.IsNullOrWhiteSpace(configuration.DeploymentProfile) ? environment.DeploymentProfile ?? string.Empty : configuration.DeploymentProfile;
        checks.Add(index.DeploymentProfiles.Count == 0
            ? Fail("Deployment profile", "No deployment profiles were discovered in source.", "Add a deployment profile under src/apex/deployments and reference it from the environment." )
            : string.IsNullOrWhiteSpace(deploymentProfileName) || index.DeploymentProfiles.Any(profile => string.Equals(profile.Name, deploymentProfileName, StringComparison.OrdinalIgnoreCase))
                ? Pass("Deployment profile", string.IsNullOrWhiteSpace(deploymentProfileName) ? "Deployment profiles were discovered." : $"Deployment profile '{deploymentProfileName}' is available.")
                : Fail("Deployment profile", $"Deployment profile '{deploymentProfileName}' was not found.", "Create the deployment profile file or update the configured deployment profile name."));

        var atlasStatePath = Path.Combine(workspaceRoot, ".opencode", "knowledge", "apexlang-atlas", "state.json");
        checks.Add(File.Exists(atlasStatePath)
            ? Pass("Atlas catalog", "Atlas knowledge state is available.")
            : Warn("Atlas catalog", "Atlas knowledge state is missing.", "Rebuild Atlas knowledge by exporting or validating the workspace before using the assistant."));
        checks.Add(File.Exists(atlasStatePath)
            ? Pass("Atlas compatibility", "Atlas/APEXlang knowledge files are present for this workspace.")
            : Warn("Atlas compatibility", "Atlas/APEXlang compatibility could not be confirmed.", "Rebuild Atlas knowledge and rerun Doctor."));
        checks.Add(Uri.TryCreate(configuration.BuilderUrl, UriKind.Absolute, out _) ? Pass("Builder URL", configuration.BuilderUrl) : Warn("Builder URL", "Builder URL is not configured or invalid.", "Set OPENCODE_APEX_DEVLOOP_BUILDER_URL for local smoke validation."));
        checks.Add(Uri.TryCreate(configuration.ApplicationUrl, UriKind.Absolute, out _) ? Pass("Application URL", configuration.ApplicationUrl) : Warn("Application URL", "Application URL is not configured or invalid.", "Set OPENCODE_APEX_DEVLOOP_APPLICATION_URL for local smoke validation."));

        var state = _stateService.Read(WorkspacePathBuilder.Build(workspaceRoot).ApexMetadataPath);
        var syncState = state is not null && state.DefaultEnvironment is not null && state.Environments.TryGetValue(state.DefaultEnvironment, out var storedEnvironment)
            ? Enum.TryParse<WorkspaceSynchronizationState>(storedEnvironment.SynchronizationState, ignoreCase: true, out var parsedState) ? parsedState : WorkspaceSynchronizationState.Unknown
            : WorkspaceSynchronizationState.Unknown;
        checks.Add(syncState is WorkspaceSynchronizationState.Diverged or WorkspaceSynchronizationState.DeploymentAhead or WorkspaceSynchronizationState.ValidationFailed
            ? Warn("Synchronization state", $"Synchronization state is '{syncState}'.", "Resolve pull/push/validation drift before using automatic import in the smoke workflow.")
            : Pass("Synchronization state", $"Synchronization state is '{syncState}'."));

        return BuildResult(workspaceRoot, configuration.EnvironmentName, checks);
    }

    private async Task<OracleApexDoctorCheckResult> CheckSqlclAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync("sql", ["-version"], cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.IsSuccess || result.StandardOutput.Contains("SQLcl", StringComparison.OrdinalIgnoreCase) || result.StandardError.Contains("SQLcl", StringComparison.OrdinalIgnoreCase)
                ? Pass("SQLcl available", "SQLcl command is available.")
                : Fail("SQLcl available", "SQLcl command could not be executed successfully.", "Install SQLcl and ensure the `sql` command is on PATH for this machine.");
        }
        catch (Exception exception)
        {
            return Fail("SQLcl available", exception.Message, "Install SQLcl and ensure the `sql` command is on PATH for this machine.");
        }
    }

    private async Task<OracleApexDoctorCheckResult> CheckSqlclCommandSupportAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync("sql", ["-help"], cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? Pass("SQLcl command support", "SQLcl help executed successfully.")
                : Warn("SQLcl command support", "SQLcl help did not exit cleanly.", "Verify that the installed SQLcl version supports the APEXlang workflow commands used by validate/import/export.");
        }
        catch (Exception exception)
        {
            return Warn("SQLcl command support", exception.Message, "Verify that SQLcl is installed and supports the commands required by this workflow.");
        }
    }

    private static OracleApexDoctorResult BuildResult(string workspaceRoot, string environmentName, IReadOnlyList<OracleApexDoctorCheckResult> checks, string? overrideSummary = null)
    {
        var hasErrors = checks.Any(check => check.Severity == DiagnosticSeverity.Error);
        var hasWarnings = checks.Any(check => check.Severity == DiagnosticSeverity.Warning);
        return new OracleApexDoctorResult
        {
            WorkspaceRootPath = workspaceRoot,
            EnvironmentName = environmentName,
            Checks = checks,
            IsSuccess = !hasErrors,
            HasWarnings = hasWarnings,
            Summary = overrideSummary ?? (hasErrors
                ? "Oracle APEX development environment is not ready for the full assistant workflow."
                : hasWarnings
                    ? "Oracle APEX development environment is usable with warnings."
                    : "Oracle APEX development environment is ready."),
        };
    }

    private static OracleApexDoctorCheckResult Pass(string name, string message)
        => new() { Name = name, Severity = DiagnosticSeverity.Information, Message = message };

    private static OracleApexDoctorCheckResult Warn(string name, string message, string remediation)
        => new() { Name = name, Severity = DiagnosticSeverity.Warning, Message = message, Remediation = remediation };

    private static OracleApexDoctorCheckResult Fail(string name, string message, string remediation)
        => new() { Name = name, Severity = DiagnosticSeverity.Error, Message = message, Remediation = remediation };
}
