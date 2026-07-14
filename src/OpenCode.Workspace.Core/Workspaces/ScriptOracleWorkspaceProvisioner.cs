using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class ScriptOracleWorkspaceProvisioner : IOracleWorkspaceProvisioner
{
    private const string OracleDemoImage = "gvenzl/oracle-free:23-slim-faststart";
    private const string XdbInvalidReason = "Reason: Oracle XML Database (XDB) is invalid.";
    private readonly IContainerRuntime _containerRuntime;

    public ScriptOracleWorkspaceProvisioner(IContainerRuntime containerRuntime)
    {
        _containerRuntime = containerRuntime;
    }

    public async Task ProvisionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        if (!OracleWorkspaceFamily.IsOracleWorkspace(snapshot.Definition))
        {
            return;
        }

        log?.Invoke(new CommandLogEntry { Source = "app", Message = "Provisioning Oracle" });
        var volumeState = snapshot.LocalRuntimeState?.LastSuccessfulProvision is null ? "new" : "reused";
        var resetAllowed = snapshot.LocalRuntimeState?.LastSuccessfulProvision is null ? "true" : "false";
        var healthyUtc = DateTimeOffset.UtcNow.ToString("O");
        var result = await RunWorkspaceProvisioningScriptAsync(snapshot, log, cancellationToken, healthyUtc, volumeState, resetAllowed);
        if (result.IsSuccess)
        {
            return;
        }

        if (!ContainsXdbInvalidFailure(result))
        {
            throw new InvalidOperationException($"Oracle workspace provisioning failed.{Environment.NewLine}{result.StandardError}{Environment.NewLine}{result.StandardOutput}".Trim());
        }

        var repairResult = await RunServerSideXdbRepairAsync(snapshot, log, cancellationToken);
        if (!repairResult.IsSuccess)
        {
            throw new InvalidOperationException(BuildServerSideRepairFailureMessage(repairResult));
        }

        log?.Invoke(new CommandLogEntry { Source = "app", Message = "Retrying Oracle provisioning after server-side XDB recompilation." });
        result = await RunWorkspaceProvisioningScriptAsync(snapshot, log, cancellationToken, healthyUtc, volumeState, resetAllowed);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Oracle workspace provisioning failed.{Environment.NewLine}{result.StandardError}{Environment.NewLine}{result.StandardOutput}".Trim());
        }
    }

    private async Task<ProcessResult> RunWorkspaceProvisioningScriptAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log, CancellationToken cancellationToken, string healthyUtc, string volumeState, string resetAllowed)
        => await _containerRuntime.RunSimpleDockerCommandAsync(
        [
            "exec",
            "-e",
            $"OPENCODE_ORACLE_HEALTHY_UTC={healthyUtc}",
            "-e",
            $"OPENCODE_ORACLE_VOLUME_STATE={volumeState}",
            "-e",
            $"OPENCODE_ORACLE_VOLUME_RESET_ALLOWED={resetAllowed}",
            "-e",
            "OPENCODE_ORACLE_VOLUME_SCOPE=managed-workspace-exclusive",
            "-e",
            $"OPENCODE_ORACLE_DATABASE_IMAGE={OracleDemoImage}",
            _containerRuntime.GetWorkspaceContainerName(snapshot.Definition),
            "bash",
            "/opt/opencode-workspace/config/oracle-provision.sh",
        ], log, cancellationToken);

    private static bool ContainsXdbInvalidFailure(ProcessResult result)
        => result.StandardError.Contains(XdbInvalidReason, StringComparison.Ordinal)
            || result.StandardOutput.Contains(XdbInvalidReason, StringComparison.Ordinal);

    private async Task<ProcessResult> RunServerSideXdbRepairAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        var projectName = WorkspacePathBuilder.Slugify(snapshot.Definition.Workspace.Name);
        var serviceName = OracleWorkspaceFamily.OracleDatabaseServiceId;
        var targetPdb = OpenCode.Workspace.Core.Generation.OracleRuntimeConfiguration.From(snapshot.Definition).ServiceName;
        var containerName = _containerRuntime.GetServiceContainerName(snapshot.Definition, serviceName);

        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"Running Oracle server-side XDB recompilation. compose_project={projectName} service={serviceName} container={containerName}" });

        var command = BuildServerSideRepairCommand(projectName, serviceName, containerName, targetPdb);
        return await _containerRuntime.RunCommandInServiceContainerAsync(
            snapshot.Definition,
            serviceName,
            ["bash", "-lc", command],
            log,
            cancellationToken);
    }

    private static string BuildServerSideRepairCommand(string projectName, string serviceName, string containerName, string targetPdb)
        => $$"""
set -euo pipefail
target_project='{{projectName}}'
target_service='{{serviceName}}'
target_container='{{containerName}}'
target_pdb='{{targetPdb}}'

resolve_oracle_home() {
  if [ -n "${ORACLE_HOME:-}" ] && [ -d "${ORACLE_HOME}" ]; then
    printf '%s\n' "${ORACLE_HOME}"
    return 0
  fi

  local sqlplus_path candidate
  sqlplus_path=$(command -v sqlplus 2>/dev/null || true)
  if [ -n "${sqlplus_path}" ]; then
    sqlplus_path=$(readlink -f "${sqlplus_path}")
    candidate=$(cd "$(dirname "${sqlplus_path}")/.." && pwd)
    if [ -f "${candidate}/rdbms/admin/utlrp.sql" ]; then
      printf '%s\n' "${candidate}"
      return 0
    fi
  fi

  return 1
}

oracle_home=$(resolve_oracle_home || true)
if [ -z "${oracle_home}" ]; then
  printf '[oracle-server-maintenance] project=%s service=%s container=%s error=oracle_home_not_found\n' "$target_project" "$target_service" "$target_container" >&2
  exit 1
fi

utlrp_path="$oracle_home/rdbms/admin/utlrp.sql"
printf '[oracle-server-maintenance] project=%s service=%s container=%s oracle_home=%s utlrp_path=%s\n' "$target_project" "$target_service" "$target_container" "$oracle_home" "$utlrp_path" >&2

if [ ! -f "$utlrp_path" ]; then
  printf '[oracle-server-maintenance] project=%s service=%s container=%s error=missing_utlrp target=%s\n' "$target_project" "$target_service" "$target_container" "$utlrp_path" >&2
  exit 1
fi

run_utlrp() {
  local target_container=$1
  local token sql_file exit_code
  token=$(printf '%s' "$target_container" | tr -c '[:alnum:]' '_')
  sql_file="/tmp/opencode-utlrp-${token}.sql"
  cat >"$sql_file" <<SQL
WHENEVER SQLERROR EXIT SQL.SQLCODE
ALTER SESSION SET CONTAINER = $target_container;
@$utlrp_path
EXIT
SQL
  printf '[oracle-server-maintenance] target_container=%s status=started\n' "$target_container" >&2
  set +e
  sqlplus -s / as sysdba @"$sql_file"
  exit_code=$?
  set -e
  printf '[oracle-server-maintenance] target_container=%s status=completed exit_code=%s\n' "$target_container" "$exit_code" >&2
  rm -f "$sql_file"
  return "$exit_code"
}

run_utlrp 'CDB$ROOT'
run_utlrp "$target_pdb"
""";

    private static string BuildServerSideRepairFailureMessage(ProcessResult result)
    {
        var evidence = string.Join(" | ", result.StandardErrorLines.Concat(result.StandardOutputLines).Where(line => !string.IsNullOrWhiteSpace(line)).Take(20));
        return $"Workspace provisioning stopped.{Environment.NewLine}Stage: Provisioning Oracle{Environment.NewLine}Reason: Oracle XML Database (XDB) recompilation could not start.{Environment.NewLine}Evidence: {evidence}{Environment.NewLine}Recommended action: Inspect the managed Oracle database container and verify ORACLE_HOME and utlrp.sql inside the current workspace runtime.{Environment.NewLine}Confidence: high";
    }
}
