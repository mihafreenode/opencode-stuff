using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Smoke;

public interface IWorkspaceSmokeValidator
{
    string ValidatorId { get; }
    Task<WorkspaceSmokeValidatorResult> ValidateAsync(WorkspaceSmokeContext context, CancellationToken cancellationToken = default);
}

public interface IWorkspaceSmokeValidatorProvider
{
    IReadOnlyList<IWorkspaceSmokeValidator> ResolveValidators(WorkspaceSmokeDefinition definition);
}

public sealed class DefaultWorkspaceSmokeValidatorProvider : IWorkspaceSmokeValidatorProvider
{
    private readonly IReadOnlyDictionary<string, IWorkspaceSmokeValidator> _validators;

    public DefaultWorkspaceSmokeValidatorProvider()
    {
        _validators = new IWorkspaceSmokeValidator[]
        {
            new WorkspaceRecordCreatedSmokeValidator(),
            new GeneratedFilesPresentSmokeValidator(),
            new ComposeConfigurationSmokeValidator(),
            new WorkspaceContainerRunningSmokeValidator(),
            new ExpectedServicesRunningSmokeValidator(),
            new RuntimeInventoryOwnedSmokeValidator(),
            new WorkspaceCoreToolingSmokeValidator(),
            new DocumentProcessingSmokeValidator(),
            new AnalyticsSmokeValidator(),
            new PostgreSqlSmokeValidator(),
            new OraclePlSqlSmokeValidator(),
            new OracleApexSmokeValidator(includeApexLang: false),
            new OracleApexSmokeValidator(includeApexLang: true),
        }.ToDictionary(item => item.ValidatorId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IWorkspaceSmokeValidator> ResolveValidators(WorkspaceSmokeDefinition definition)
        => definition.ValidatorIds.Select(id => _validators.TryGetValue(id, out var validator)
                ? validator
                : throw new InvalidOperationException($"Validation tooling failure: smoke validator '{id}' is not registered."))
            .ToArray();
}

internal static class WorkspaceSmokeValidatorHelpers
{
    public static WorkspaceSmokeValidatorResult FromProcess(string validatorId, string successMessage, ProcessResult result)
        => new()
        {
            ValidatorId = validatorId,
            Succeeded = result.IsSuccess,
            Message = result.IsSuccess ? successMessage : result.StandardError,
            Command = new WorkspaceSmokeCommandResult
            {
                Command = result.Command,
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
                Duration = result.Duration,
            },
        };

    public static WorkspaceSmokeCommandResult ToCommandResult(ProcessResult result)
        => new()
        {
            Command = result.Command,
            ExitCode = result.ExitCode,
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError,
            Duration = result.Duration,
        };

    public static string ExtractFirstMatchingLine(string content, string fragment)
        => (content ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            ?? string.Empty;
}

public sealed class WorkspaceRecordCreatedSmokeValidator : IWorkspaceSmokeValidator
{
    public string ValidatorId => "workspace-record-created";

    public Task<WorkspaceSmokeValidatorResult> ValidateAsync(WorkspaceSmokeContext context, CancellationToken cancellationToken = default)
    {
        var exists = context.WorkspaceService.LoadWorkspaceRecords().Any(item => string.Equals(item.RootPath, context.Snapshot.Paths.RootPath, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(new WorkspaceSmokeValidatorResult
        {
            ValidatorId = ValidatorId,
            Succeeded = exists,
            Message = exists ? "Workspace record was created." : "Workspace record was not created.",
        });
    }
}

public sealed class GeneratedFilesPresentSmokeValidator : IWorkspaceSmokeValidator
{
    public string ValidatorId => "generated-files-present";

    public Task<WorkspaceSmokeValidatorResult> ValidateAsync(WorkspaceSmokeContext context, CancellationToken cancellationToken = default)
    {
        var requiredFiles = new[]
        {
            context.Snapshot.Paths.WorkspaceYamlPath,
            context.Snapshot.Paths.ComposePath,
            context.Snapshot.Paths.ProvisionScriptPath,
        };
        var missing = requiredFiles.Where(path => !File.Exists(path)).ToArray();
        return Task.FromResult(new WorkspaceSmokeValidatorResult
        {
            ValidatorId = ValidatorId,
            Succeeded = missing.Length == 0,
            Message = missing.Length == 0 ? "Generated workspace files are present." : $"Missing generated files: {string.Join(", ", missing)}",
        });
    }
}

public sealed class ComposeConfigurationSmokeValidator : IWorkspaceSmokeValidator
{
    public string ValidatorId => "compose-configuration-valid";

    public Task<WorkspaceSmokeValidatorResult> ValidateAsync(WorkspaceSmokeContext context, CancellationToken cancellationToken = default)
    {
        var inspection = ComposeProjectInspector.InspectFile(context.Snapshot.Paths.ComposePath);
        return Task.FromResult(new WorkspaceSmokeValidatorResult
        {
            ValidatorId = ValidatorId,
            Succeeded = inspection.IsValid,
            Message = inspection.IsValid ? "Compose configuration is valid." : string.Join(" | ", inspection.Errors),
        });
    }
}

public sealed class WorkspaceContainerRunningSmokeValidator : IWorkspaceSmokeValidator
{
    public string ValidatorId => "workspace-container-running";

    public async Task<WorkspaceSmokeValidatorResult> ValidateAsync(WorkspaceSmokeContext context, CancellationToken cancellationToken = default)
    {
        var inventory = await context.RuntimeOwnershipService.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke", RunId = context.RunId }, cancellationToken);
        var workspaceContainerName = context.ContainerRuntime.GetWorkspaceContainerName(context.WorkspaceDefinition);
        var running = inventory.Resources.Any(item => item.Type == RuntimeResourceType.Container && string.Equals(item.Name, workspaceContainerName, StringComparison.OrdinalIgnoreCase) && string.Equals(item.Status, "running", StringComparison.OrdinalIgnoreCase));
        return new WorkspaceSmokeValidatorResult
        {
            ValidatorId = ValidatorId,
            Succeeded = running,
            Message = running ? "Workspace container is running." : $"Workspace container '{workspaceContainerName}' is not running.",
        };
    }
}

public sealed class ExpectedServicesRunningSmokeValidator : IWorkspaceSmokeValidator
{
    public string ValidatorId => "expected-services-running";

    public async Task<WorkspaceSmokeValidatorResult> ValidateAsync(WorkspaceSmokeContext context, CancellationToken cancellationToken = default)
    {
        var inventory = await context.RuntimeOwnershipService.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke", RunId = context.RunId }, cancellationToken);
        var missing = new List<string>();
        foreach (var service in context.SmokeDefinition.ExpectedServices)
        {
            var expectedName = string.Equals(service, "workspace", StringComparison.OrdinalIgnoreCase)
                ? context.ContainerRuntime.GetWorkspaceContainerName(context.WorkspaceDefinition)
                : context.ContainerRuntime.GetServiceContainerName(context.WorkspaceDefinition, service);
            if (!inventory.Resources.Any(item => item.Type == RuntimeResourceType.Container && string.Equals(item.Name, expectedName, StringComparison.OrdinalIgnoreCase) && string.Equals(item.Status, "running", StringComparison.OrdinalIgnoreCase)))
            {
                missing.Add(service);
            }
        }

        return new WorkspaceSmokeValidatorResult
        {
            ValidatorId = ValidatorId,
            Succeeded = missing.Count == 0,
            Message = missing.Count == 0 ? "Expected services are running." : $"Expected services not running: {string.Join(", ", missing)}",
        };
    }
}

public sealed class RuntimeInventoryOwnedSmokeValidator : IWorkspaceSmokeValidator
{
    public string ValidatorId => "runtime-inventory-owned";

    public async Task<WorkspaceSmokeValidatorResult> ValidateAsync(WorkspaceSmokeContext context, CancellationToken cancellationToken = default)
    {
        var inventory = await context.RuntimeOwnershipService.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke", RunId = context.RunId }, cancellationToken);
        var missingLabels = inventory.MissingRequiredLabels.Count;
        var orphans = inventory.Orphans.Count;
        return new WorkspaceSmokeValidatorResult
        {
            ValidatorId = ValidatorId,
            Succeeded = inventory.Resources.Count > 0 && missingLabels == 0 && orphans == 0,
            Message = inventory.Resources.Count == 0
                ? "No owned smoke resources were discovered for the active run."
                : missingLabels == 0 && orphans == 0
                    ? "Owned runtime inventory matches the active smoke run."
                    : $"Inventory issues: missing_labels={missingLabels} orphans={orphans}",
        };
    }
}

public sealed class WorkspaceCoreToolingSmokeValidator : IWorkspaceSmokeValidator
{
    public string ValidatorId => "core-tooling";

    public async Task<WorkspaceSmokeValidatorResult> ValidateAsync(WorkspaceSmokeContext context, CancellationToken cancellationToken = default)
    {
        var result = await context.RunWorkspaceCommandAsync("node --version && python --version && opencode --version", cancellationToken);
        return WorkspaceSmokeValidatorHelpers.FromProcess(ValidatorId, "Core tooling is available.", result);
    }
}

public sealed class DocumentProcessingSmokeValidator : IWorkspaceSmokeValidator
{
    public string ValidatorId => "document-processing-tools";

    public async Task<WorkspaceSmokeValidatorResult> ValidateAsync(WorkspaceSmokeContext context, CancellationToken cancellationToken = default)
    {
        var command = "pandoc --version >/tmp/pandoc.version && (libreoffice --version || soffice --version) >/tmp/libreoffice.version && printf '# Smoke\n' >/tmp/smoke.md && pandoc /tmp/smoke.md -o /tmp/smoke.html && test -s /tmp/smoke.html && pdfinfo -v >/tmp/pdfinfo.version";
        var result = await context.RunWorkspaceCommandAsync(command, cancellationToken);
        return WorkspaceSmokeValidatorHelpers.FromProcess(ValidatorId, "Document processing tooling is available.", result);
    }
}

public sealed class AnalyticsSmokeValidator : IWorkspaceSmokeValidator
{
    public string ValidatorId => "analytics-tools";

    public async Task<WorkspaceSmokeValidatorResult> ValidateAsync(WorkspaceSmokeContext context, CancellationToken cancellationToken = default)
    {
        var command = "python - <<'PY'\nimport pandas as pd\nimport matplotlib.pyplot as plt\nimport seaborn as sns\nfrom pathlib import Path\npath = Path('/tmp/analytics-smoke.csv')\npath.write_text('value\\n1\\n2\\n3\\n', encoding='utf-8')\ndf = pd.read_csv(path)\nplt.figure()\nsns.lineplot(data=df, x=df.index, y='value')\nplt.savefig('/tmp/analytics-smoke.png')\nprint(df['value'].sum())\nPY\ntest -s /tmp/analytics-smoke.png";
        var result = await context.RunWorkspaceCommandAsync(command, cancellationToken);
        return WorkspaceSmokeValidatorHelpers.FromProcess(ValidatorId, "Analytics tooling is available.", result);
    }
}

public sealed class PostgreSqlSmokeValidator : IWorkspaceSmokeValidator
{
    public string ValidatorId => "postgresql-runtime";

    public async Task<WorkspaceSmokeValidatorResult> ValidateAsync(WorkspaceSmokeContext context, CancellationToken cancellationToken = default)
    {
        var result = await context.RunServiceCommandAsync("postgres", ["psql", "-U", "app", "-d", "app", "-v", "ON_ERROR_STOP=1", "-c", "create table if not exists smoke_check(id int); insert into smoke_check values (1); select count(*) from smoke_check; drop table smoke_check;"], cancellationToken);
        return WorkspaceSmokeValidatorHelpers.FromProcess(ValidatorId, "PostgreSQL service is healthy.", result);
    }
}

public sealed class OraclePlSqlSmokeValidator : IWorkspaceSmokeValidator
{
    public string ValidatorId => "oracle-plsql-runtime";

    public async Task<WorkspaceSmokeValidatorResult> ValidateAsync(WorkspaceSmokeContext context, CancellationToken cancellationToken = default)
    {
        var command = "command -v sqlcl >/dev/null 2>&1 && test -f /workspace/docs/oracle-plsql-demo.md && test -f /workspace/docs/oracle-tools/sqlcl.md && sqlcl -version >/tmp/sqlcl-version.txt && sqlcl -S \"${ORACLE_DEMO_CONNECTION}\" <<'SQL'\nset heading off feedback off verify off serveroutput on\nselect 'SQL_OK' from dual;\nbegin dbms_output.put_line('PLSQL_OK'); end;\n/\nselect banner_full from v$version where banner_full like 'Oracle Database%' fetch first 1 rows only;\nexit\nSQL";
        var result = await context.RunWorkspaceCommandAsync(command, cancellationToken);
        var combined = result.StandardOutput + Environment.NewLine + result.StandardError;
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sqlcl_available"] = result.IsSuccess.ToString(),
            ["sql_execution"] = combined.Contains("SQL_OK", StringComparison.Ordinal) ? "passed" : "failed",
            ["plsql_execution"] = combined.Contains("PLSQL_OK", StringComparison.Ordinal) ? "passed" : "failed",
            ["oracle_plsql_docs_present"] = File.Exists(Path.Combine(context.Snapshot.Paths.RootPath, "docs", "oracle-plsql-demo.md")).ToString(),
            ["oracle_sqlcl_docs_present"] = File.Exists(Path.Combine(context.Snapshot.Paths.RootPath, "docs", "oracle-tools", "sqlcl.md")).ToString(),
        };
        var databaseVersion = WorkspaceSmokeValidatorHelpers.ExtractFirstMatchingLine(combined, "Oracle Database");
        if (!string.IsNullOrWhiteSpace(databaseVersion))
        {
            data["oracle_database_version"] = databaseVersion;
        }

        return new WorkspaceSmokeValidatorResult
        {
            ValidatorId = ValidatorId,
            Succeeded = result.IsSuccess && combined.Contains("SQL_OK", StringComparison.Ordinal) && combined.Contains("PLSQL_OK", StringComparison.Ordinal),
            Message = result.IsSuccess ? "Oracle PL/SQL runtime is healthy." : result.StandardError,
            Command = WorkspaceSmokeValidatorHelpers.ToCommandResult(result),
            Data = data,
        };
    }
}

public sealed class OracleApexSmokeValidator : IWorkspaceSmokeValidator
{
    private static readonly string[] ApexRouteProbeUrls = ["/ords", "/ords/", "/ords/apex", "/ords/apex/", "/ords/r", "/ords/f?p=4550"];
    private readonly bool _includeApexLang;

    public OracleApexSmokeValidator(bool includeApexLang)
    {
        _includeApexLang = includeApexLang;
    }

    public string ValidatorId => _includeApexLang ? "oracle-apexlang-runtime" : "oracle-apex-runtime";

    public async Task<WorkspaceSmokeValidatorResult> ValidateAsync(WorkspaceSmokeContext context, CancellationToken cancellationToken = default)
    {
        var oracleSettings = OracleWorkspaceSettings.From(context.WorkspaceDefinition);
        var routeResults = await ProbeApexRoutesAsync(oracleSettings);
        var ordsLandingProbe = await ProbeStatusCodeAsync($"http://localhost:{oracleSettings.OrdsPort}/ords/_/landing");
        var xdbStateResult = await context.RunServiceCommandAsync("oracle-demo", [
            "bash",
            "-lc",
            "sqlplus -s 'sys/change-on-first-demo@//localhost:1521/FREEPDB1 as sysdba' <<'SQL'\nset pagesize 200 linesize 200 trimspool on feedback off verify off heading on\nprompt ==XDB==\nselect comp_id, comp_name, version, status from dba_registry where comp_id = 'XDB';\nprompt ==CDB_XDB==\nselect con_id, comp_id, comp_name, version, status from cdb_registry where comp_id = 'XDB' order by con_id;\nprompt ==DBVERSION==\nselect banner_full from v$version where banner_full like 'Oracle Database%' fetch first 1 rows only;\nexit\nSQL"
        ], cancellationToken);
        var databaseStateResult = await context.RunServiceCommandAsync("oracle-demo", [
            "bash",
            "-lc",
            "sqlplus -s 'sys/change-on-first-demo@//localhost:1521/FREEPDB1 as sysdba' <<'SQL'\nset pagesize 200 linesize 200 trimspool on feedback off verify off heading on\nprompt ==REGISTRY==\nselect comp_id, comp_name, version, status from dba_registry where comp_id = 'APEX';\nprompt ==USERS==\nselect username from dba_users where username like 'APEX\\_%' escape '\\' order by username;\nprompt ==INVALID==\nselect owner, object_name, object_type, status from dba_objects where owner like 'APEX\\_%' escape '\\' and status <> 'VALID' fetch first 20 rows only;\nprompt ==VERSION==\nselect version_no from apex_release;\nexit\nSQL"
        ], cancellationToken);
        var combinedDatabaseState = databaseStateResult.StandardOutput + Environment.NewLine + databaseStateResult.StandardError;
        var combinedXdbState = xdbStateResult.StandardOutput + Environment.NewLine + xdbStateResult.StandardError;
        var installationState = ClassifyApexInstallationState(combinedDatabaseState);
        var ordsHealthy = ordsLandingProbe is HttpStatusCode.OK or HttpStatusCode.Found or HttpStatusCode.MovedPermanently;
        var apexRouteStatus = routeResults.FirstOrDefault(item => item.Url.Contains("/ords/apex", StringComparison.OrdinalIgnoreCase)).StatusCode;
        var apexRelatedReachable = routeResults.Any(item => item.Url.Contains("/ords/apex", StringComparison.OrdinalIgnoreCase)
            || item.Url.Contains("/ords/r", StringComparison.OrdinalIgnoreCase)
            || item.Url.Contains("/ords/f?p=4550", StringComparison.OrdinalIgnoreCase))
            && routeResults.Any(item => item.StatusCode is not null && (item.Url.Contains("/ords/apex", StringComparison.OrdinalIgnoreCase)
            || item.Url.Contains("/ords/r", StringComparison.OrdinalIgnoreCase)
            || item.Url.Contains("/ords/f?p=4550", StringComparison.OrdinalIgnoreCase)));
        var apexLangEvidence = _includeApexLang ? await ValidateApexLangArtifactsAsync(context, cancellationToken) : null;
        var success = ordsHealthy
            && apexRelatedReachable
            && string.Equals(installationState, "APEX installed", StringComparison.OrdinalIgnoreCase)
            && ExtractXdbStatuses(combinedXdbState).All(status => string.Equals(status, "VALID", StringComparison.OrdinalIgnoreCase))
            && (apexLangEvidence?.Succeeded ?? true);
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ords_landing_status_code"] = ordsLandingProbe is null ? string.Empty : ((int)ordsLandingProbe.Value).ToString(),
            ["ords_endpoint_reachable"] = ordsHealthy.ToString(),
            ["apex_endpoint_reachable"] = apexRelatedReachable.ToString(),
            ["apex_http_status_code"] = apexRouteStatus is null ? string.Empty : ((int)apexRouteStatus.Value).ToString(),
            ["apex_installed"] = string.Equals(installationState, "APEX installed", StringComparison.OrdinalIgnoreCase).ToString(),
            ["apex_installation_state"] = installationState,
            ["apex_version"] = ExtractApexVersion(combinedDatabaseState),
            ["apex_registry_status"] = ExtractApexRegistryStatus(combinedDatabaseState),
            ["apex_schemas_present"] = ExtractApexSchemasPresent(combinedDatabaseState).ToString(),
            ["xdb_statuses"] = string.Join(",", ExtractXdbStatuses(combinedXdbState)),
            ["oracle_database_version"] = WorkspaceSmokeValidatorHelpers.ExtractFirstMatchingLine(combinedXdbState, "Oracle Database")
        };
        if (apexLangEvidence is not null)
        {
            foreach (var pair in apexLangEvidence.Data)
            {
                data[pair.Key] = pair.Value;
            }
        }

        return new WorkspaceSmokeValidatorResult
        {
            ValidatorId = ValidatorId,
            Succeeded = success,
            Message = success
                ? (_includeApexLang ? "Oracle APEXlang runtime is healthy." : "Oracle APEX runtime is healthy.")
                : $"ORDS reachable={ordsHealthy} apex_reachable={apexRelatedReachable} installation_state={installationState}",
            Command = new WorkspaceSmokeCommandResult
            {
                Command = "Oracle APEX route and registry probes",
                ExitCode = success ? 0 : 1,
                StandardOutput = FormatApexRouteDiagnostics(routeResults) + Environment.NewLine + xdbStateResult.StandardOutput + Environment.NewLine + databaseStateResult.StandardOutput + Environment.NewLine + (apexLangEvidence?.Command?.StandardOutput ?? string.Empty),
                StandardError = string.Join(Environment.NewLine, new[] { xdbStateResult.StandardError, databaseStateResult.StandardError, apexLangEvidence?.Command?.StandardError }.Where(value => !string.IsNullOrWhiteSpace(value))),
                Duration = databaseStateResult.Duration + xdbStateResult.Duration + (apexLangEvidence?.Command?.Duration ?? TimeSpan.Zero),
            },
            Data = data,
        };
    }

    private static async Task<WorkspaceSmokeValidatorResult> ValidateApexLangArtifactsAsync(WorkspaceSmokeContext context, CancellationToken cancellationToken)
    {
        var workspaceCommand = "test -x /workspace/scripts/apexlang-hello-world.sh && test -f /workspace/sql/hello-apexlang/generate-hello-apexlang.sql && test -f /workspace/sql/hello-apexlang/validate-hello-apexlang.sql && test -f /workspace/sql/hello-apexlang/import-hello-apexlang.sql && test -f /workspace/sql/hello-apexlang/export-hello-apexlang.sql && test -f /workspace/exports/apexlang/hello-apexlang/application.apx && test -f /workspace/exports/apexlang/hello-apexlang/pages/p00001-home.apx && test -f /workspace/docs/reference/oracle-apexlang-navigation.md && test -f /workspace/docs/oracle-apexlang-demo.md && /workspace/scripts/sqlcl.sh -S \"${ORACLE_DEMO_CONNECTION}\" @/workspace/sql/hello-apexlang/validate-hello-apexlang.sql";
        var workspaceResult = await context.RunWorkspaceCommandAsync(workspaceCommand, cancellationToken);
        var databaseResult = await context.RunServiceCommandAsync("oracle-demo", [
            "bash",
            "-lc",
            "sqlplus -s 'sys/change-on-first-demo@//localhost:1521/FREEPDB1 as sysdba' <<'SQL'\nset heading off feedback off verify off\nselect application_id || '|' || application_name from apex_applications where application_id = 101;\nexit\nSQL"
        ], cancellationToken);
        var combined = workspaceResult.StandardOutput + Environment.NewLine + databaseResult.StandardOutput;
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["apexlang_script_exists"] = File.Exists(Path.Combine(context.Snapshot.Paths.RootPath, "scripts", "apexlang-hello-world.sh")).ToString(),
            ["apexlang_export_exists"] = File.Exists(Path.Combine(context.Snapshot.Paths.RootPath, "exports", "apexlang", "hello-apexlang", "application.apx")).ToString(),
            ["apexlang_page_exists"] = File.Exists(Path.Combine(context.Snapshot.Paths.RootPath, "exports", "apexlang", "hello-apexlang", "pages", "p00001-home.apx")).ToString(),
            ["apexlang_navigation_doc_exists"] = File.Exists(Path.Combine(context.Snapshot.Paths.RootPath, "docs", "reference", "oracle-apexlang-navigation.md")).ToString(),
            ["apexlang_demo_doc_exists"] = File.Exists(Path.Combine(context.Snapshot.Paths.RootPath, "docs", "oracle-apexlang-demo.md")).ToString(),
        };
        var appIdentity = combined.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(line => line.Contains('|', StringComparison.Ordinal) && line.Contains("Hello APEXlang", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(appIdentity))
        {
            var parts = appIdentity.Split('|', 2);
            data["apexlang_application_id"] = parts[0].Trim();
            data["apexlang_application_name"] = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        }

        return new WorkspaceSmokeValidatorResult
        {
            ValidatorId = "oracle-apexlang-artifacts",
            Succeeded = workspaceResult.IsSuccess && databaseResult.IsSuccess && !string.IsNullOrWhiteSpace(appIdentity),
            Message = workspaceResult.IsSuccess && databaseResult.IsSuccess ? "APEXlang sample artifacts are healthy." : "APEXlang sample artifacts are incomplete.",
            Command = new WorkspaceSmokeCommandResult
            {
                Command = workspaceResult.Command + " && " + databaseResult.Command,
                ExitCode = workspaceResult.IsSuccess && databaseResult.IsSuccess ? 0 : 1,
                StandardOutput = workspaceResult.StandardOutput + Environment.NewLine + databaseResult.StandardOutput,
                StandardError = workspaceResult.StandardError + Environment.NewLine + databaseResult.StandardError,
                Duration = workspaceResult.Duration + databaseResult.Duration,
            },
            Data = data,
        };
    }

    private static async Task<IReadOnlyList<(string Url, HttpStatusCode? StatusCode, string Body)>> ProbeApexRoutesAsync(OracleWorkspaceSettings oracleSettings)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var results = new List<(string, HttpStatusCode?, string)>();
        foreach (var relativeUrl in ApexRouteProbeUrls)
        {
            var absoluteUrl = $"http://localhost:{oracleSettings.OrdsPort}{relativeUrl}";
            try
            {
                using var response = await client.GetAsync(absoluteUrl);
                results.Add((absoluteUrl, response.StatusCode, await response.Content.ReadAsStringAsync()));
            }
            catch (Exception exception)
            {
                results.Add((absoluteUrl, null, exception.Message));
            }
        }

        return results;
    }

    private static async Task<HttpStatusCode?> ProbeStatusCodeAsync(string url)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            using var response = await client.GetAsync(url);
            return response.StatusCode;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatApexRouteDiagnostics(IReadOnlyList<(string Url, HttpStatusCode? StatusCode, string Body)> routeResults)
    {
        var builder = new StringBuilder();
        foreach (var result in routeResults)
        {
            builder.AppendLine($"URL={result.Url}");
            builder.AppendLine($"STATUS={(result.StatusCode is null ? "ERROR" : (int)result.StatusCode.Value)}");
            builder.AppendLine($"BODY={string.Join(" | ", result.Body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(3))}");
            builder.AppendLine("---");
        }

        return builder.ToString();
    }

    private static string ClassifyApexInstallationState(string databaseDiagnosticOutput)
    {
        var output = databaseDiagnosticOutput ?? string.Empty;
        if (output.Contains("ORA-00942", StringComparison.OrdinalIgnoreCase)
            && !output.Contains("APEX\n", StringComparison.Ordinal))
        {
            return "APEX not installed";
        }

        if (output.Contains("==REGISTRY==", StringComparison.Ordinal)
            && output.Contains("==USERS==", StringComparison.Ordinal)
            && !output.Contains("APEX\n", StringComparison.Ordinal)
            && !output.Contains("APEX_", StringComparison.Ordinal))
        {
            return "APEX not installed";
        }

        if (output.Contains("APEX", StringComparison.OrdinalIgnoreCase)
            && output.Contains("VALID", StringComparison.OrdinalIgnoreCase))
        {
            return "APEX installed";
        }

        if (output.Contains("INVALID", StringComparison.OrdinalIgnoreCase))
        {
            return "APEX registry invalid";
        }

        return "APEX state unknown";
    }

    private static string ExtractApexVersion(string databaseDiagnosticOutput)
    {
        var lines = (databaseDiagnosticOutput ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var versionMarker = Array.FindIndex(lines, line => string.Equals(line.Trim(), "==VERSION==", StringComparison.Ordinal));
        if (versionMarker < 0)
        {
            return string.Empty;
        }

        for (var index = versionMarker + 1; index < lines.Length; index++)
        {
            var value = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(value)
                || value.StartsWith("VERSION_NO", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            return value;
        }

        return string.Empty;
    }

    private static string ExtractApexRegistryStatus(string databaseDiagnosticOutput)
    {
        if (databaseDiagnosticOutput.Contains("VALID", StringComparison.OrdinalIgnoreCase))
        {
            return "VALID";
        }

        if (databaseDiagnosticOutput.Contains("INVALID", StringComparison.OrdinalIgnoreCase))
        {
            return "INVALID";
        }

        return string.Empty;
    }

    private static bool ExtractApexSchemasPresent(string databaseDiagnosticOutput)
        => (databaseDiagnosticOutput ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.TrimStart().StartsWith("APEX_", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> ExtractXdbStatuses(string xdbDiagnosticOutput)
    {
        var content = xdbDiagnosticOutput ?? string.Empty;
        if (content.Contains("INVALID", StringComparison.OrdinalIgnoreCase))
        {
            return ["INVALID"];
        }

        return content.Contains("VALID", StringComparison.OrdinalIgnoreCase) ? ["VALID"] : [string.Empty];
    }

}
