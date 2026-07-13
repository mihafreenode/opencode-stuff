using System.Text;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

public sealed class OracleWorkspaceProvisioningScriptGenerator
{
    public string Generate(ResolvedWorkspace workspace)
    {
        if (!OracleWorkspaceFamily.IsOracleWorkspace(workspace.Definition))
        {
            return string.Empty;
        }

        var oracle = OracleRuntimeConfiguration.From(workspace.Definition);
        var hasOracleApex = oracle.HasApex;
        var hasOracleApexLang = oracle.WorkspaceKind == OracleWorkspaceKind.ApexLang;
        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine("set -euo pipefail");
        builder.AppendLine("export HOME=/home/opencode");
        builder.AppendLine(OracleSqlExecutionScriptSupport.BuildShellLibrary());
        builder.AppendLine("oracle_probe_script=/tmp/sqlcl-probe.sql");
        builder.AppendLine("cat > \"${oracle_probe_script}\" <<'SQL'");
        builder.AppendLine("SET HEADING OFF");
        builder.AppendLine("SET FEEDBACK OFF");
        builder.AppendLine("SET PAGESIZE 0");
        builder.AppendLine("SET VERIFY OFF");
        builder.AppendLine("SELECT 'Connection OK' AS status FROM dual;");
        builder.AppendLine("EXIT");
        builder.AppendLine("SQL");
        builder.AppendLine("oracle_sqlplus_probe_script=/tmp/sqlplus-probe.sql");
        builder.AppendLine("cat > \"${oracle_sqlplus_probe_script}\" <<'SQL'");
        builder.AppendLine("SELECT 'Connection OK' AS status FROM dual;");
        builder.AppendLine("EXIT;");
        builder.AppendLine("SQL");
        builder.AppendLine("oracle_demo_user_setup_script=/tmp/oracle-demo-user-setup.sql");
        builder.AppendLine("cat > \"${oracle_demo_user_setup_script}\" <<SQL");
        builder.AppendLine("ALTER SESSION SET CONTAINER = ${ORACLE_SERVICE_NAME};");
        builder.AppendLine("DECLARE");
        builder.AppendLine("  l_exists NUMBER := 0;");
        builder.AppendLine("BEGIN");
        builder.AppendLine("  SELECT COUNT(*) INTO l_exists FROM dba_users WHERE username = UPPER('${ORACLE_DEMO_USERNAME}');");
        builder.AppendLine("  IF l_exists = 0 THEN EXECUTE IMMEDIATE 'CREATE USER ${ORACLE_DEMO_USERNAME} IDENTIFIED BY \"${ORACLE_DEMO_PASSWORD}\" QUOTA UNLIMITED ON USERS'; ELSE EXECUTE IMMEDIATE 'ALTER USER ${ORACLE_DEMO_USERNAME} IDENTIFIED BY \"${ORACLE_DEMO_PASSWORD}\" ACCOUNT UNLOCK'; END IF;");
        builder.AppendLine("END;");
        builder.AppendLine("/");
        builder.AppendLine("GRANT CREATE SESSION, CREATE TABLE, CREATE VIEW, CREATE PROCEDURE, CREATE TRIGGER, CREATE SEQUENCE, UNLIMITED TABLESPACE TO ${ORACLE_DEMO_USERNAME};");
        builder.AppendLine("EXIT");
        builder.AppendLine("SQL");
        builder.AppendLine("timestamp_utc() { date -u +'%Y-%m-%dT%H:%M:%SZ'; }");
        builder.AppendLine("stage_name='' ");
        builder.AppendLine("stage_started_at=0");
        builder.AppendLine("stage_active=0");
        builder.AppendLine("begin_stage() { stage_name=\"$1\"; stage_started_at=$(date +%s); stage_active=1; echo \"[stage] name=${stage_name} status=started started_at=$(timestamp_utc)\"; }");
        builder.AppendLine("complete_stage() { if [ \"${stage_active:-0}\" -ne 1 ]; then return 0; fi; echo \"[stage] name=${stage_name} status=completed completed_at=$(timestamp_utc) elapsed_seconds=$(( $(date +%s) - stage_started_at ))\"; stage_active=0; }");
        builder.AppendLine("oracle_set_stage() { complete_stage || true; begin_stage \"$1\"; }");
        builder.AppendLine("oracle_fail() { local reason=\"$1\"; local evidence=\"$2\"; local recommendation=\"$3\"; echo \"Workspace provisioning stopped.\" >&2; echo \"Stage: ${stage_name}\" >&2; echo \"Reason: ${reason}\" >&2; if [ -n \"${evidence}\" ]; then echo \"Evidence: ${evidence}\" >&2; fi; echo \"Recommended action: ${recommendation}\" >&2; echo \"Confidence: high\" >&2; exit 1; }");
        builder.AppendLine($"oracle_required_config=({string.Join(" ", oracle.GetProvisioningRequiredEnvironmentVariables().Select(name => $"\"{name}\""))})");
        builder.AppendLine("validate_oracle_runtime_config() { missing=(); for name in \"${oracle_required_config[@]}\"; do if [ -z \"${!name:-}\" ]; then missing+=(\"$name\"); fi; done; if [ \"${#missing[@]}\" -gt 0 ]; then printf 'Missing configuration values: %s\\n' \"${missing[*]}\" >&2; oracle_fail 'Oracle provisioning configuration is incomplete.' \"Missing values: ${missing[*]}\" 'Regenerate runtime artifacts and restart the workspace so the workspace container receives the generated Oracle configuration.'; fi; printf '[oracle] Active deployment profile: %s\\n' \"${ORACLE_DEPLOYMENT_PROFILE}\" >&2; printf '[oracle] Oracle host: %s\\n' \"${ORACLE_HOST}\" >&2; printf '[oracle] Oracle port: %s\\n' \"${ORACLE_PORT}\" >&2; printf '[oracle] Oracle service name: %s\\n' \"${ORACLE_SERVICE_NAME}\" >&2; printf '[oracle] Oracle administrative user: %s\\n' \"${ORACLE_ADMIN_USER}\" >&2; }");
        builder.AppendLine("build_oracle_connections() { oracle_connection=\"${ORACLE_DEMO_USERNAME}/${ORACLE_DEMO_PASSWORD}@//${ORACLE_HOST}:${ORACLE_PORT}/${ORACLE_SERVICE_NAME}\"; oracle_sysdba_connection=\"${ORACLE_ADMIN_USER}/${ORACLE_PASSWORD}@//${ORACLE_HOST}:${ORACLE_PORT}/${ORACLE_SERVICE_NAME} as sysdba\"; }");
        builder.AppendLine("ensure_demo_user_ready() { local setup_script=/tmp/oracle-demo-user-setup.sql; oracle_sql_run_file 'Provisioning Oracle' sqlplus \"${oracle_sysdba_connection}\" plsql-block 'oracle-demo-user-setup.sql' \"${setup_script}\" || oracle_fail \"Oracle administrator password does not match the running database.\" 'oracle-demo-user-setup.sql failed; see oracle-sql diagnostics above.' \"Rebuild Runtime.\"; }");
        builder.AppendLine("query_database_open_mode() { local query_script=/tmp/query-database-open-mode.sql; printf 'SELECT open_mode FROM v$database;\n' > \"${query_script}\"; oracle_sql_run_file 'Provisioning Oracle' sqlplus \"${oracle_sysdba_connection}\" single-sql-statement 'query_database_open_mode' \"${query_script}\"; }");
        builder.AppendLine("query_pdb_open_mode() { local query_script=/tmp/query-pdb-open-mode.sql; printf \"SELECT open_mode FROM v\\$pdbs WHERE name = UPPER('%s');\\n\" \"${ORACLE_SERVICE_NAME}\" > \"${query_script}\"; oracle_sql_run_file 'Provisioning Oracle' sqlplus \"${oracle_sysdba_connection}\" single-sql-statement 'query_pdb_open_mode' \"${query_script}\"; }");
        builder.AppendLine("query_xdb_status() { local query_script=/tmp/query-xdb-status.sql; printf \"SELECT status FROM dba_registry WHERE comp_id = 'XDB';\\n\" > \"${query_script}\"; oracle_sql_run_file 'Provisioning Oracle' sqlplus \"${oracle_sysdba_connection}\" single-sql-statement 'query_xdb_status' \"${query_script}\"; }");
        builder.AppendLine("sqlplus_demo_probe() { oracle_sql_run_file 'Provisioning Oracle' sqlplus \"${oracle_connection}\" script 'sqlplus-probe.sql' \"${oracle_sqlplus_probe_script}\" > /tmp/sqlplus-probe.out 2>&1 && grep -Fq 'Connection OK' /tmp/sqlplus-probe.out; }");
        builder.AppendLine("sqlcl_demo_probe() { oracle_sql_run_file 'Provisioning Oracle' sqlcl \"${oracle_connection}\" script 'sqlcl-probe.sql' \"${oracle_probe_script}\" > /tmp/sqlcl-probe.out 2>&1 && grep -Fq 'Connection OK' /tmp/sqlcl-probe.out; }");
        builder.AppendLine("oracle_set_stage 'Provisioning Oracle'");
        builder.AppendLine("command -v sqlplus >/dev/null 2>&1 || oracle_fail 'SQL*Plus is missing from the workspace image.' 'command -v sqlplus failed' 'Rebuild the workspace image before retrying.'");
        builder.AppendLine("command -v sqlcl >/dev/null 2>&1 || oracle_fail 'SQLcl is missing from the workspace image.' 'command -v sqlcl failed' 'Rebuild the workspace image before retrying.'");
        builder.AppendLine("validate_oracle_runtime_config");
        builder.AppendLine("build_oracle_connections");
        builder.AppendLine("ensure_demo_user_ready");
        builder.AppendLine("for _ in $(seq 1 30); do sqlplus_demo_probe && break; sleep 10; done");
        builder.AppendLine("sqlplus_demo_probe || oracle_fail 'Oracle SQL*Plus readiness probe failed.' 'demo user connection did not succeed' 'Rebuild Runtime if the Oracle data volume contains stale credentials.'");
        builder.AppendLine("for _ in $(seq 1 30); do sqlcl_demo_probe && break; sleep 10; done");
        builder.AppendLine("sqlcl_demo_probe || oracle_fail 'Oracle SQLcl readiness probe failed.' 'demo user SQLcl connection did not succeed' 'Rebuild Runtime if the Oracle data volume contains stale credentials.'");
        builder.AppendLine("database_open_mode=$(query_database_open_mode | tr -d '\\r' | xargs || true)");
        builder.AppendLine("pdb_open_mode=$(query_pdb_open_mode | tr -d '\\r' | xargs || true)");
        builder.AppendLine("xdb_status=$(query_xdb_status | tr -d '\\r' | xargs || true)");
        builder.AppendLine("[ \"${database_open_mode}\" = 'READ WRITE' ] || oracle_fail 'Oracle database is not open for writes.' \"open_mode=${database_open_mode:-missing}\" 'Wait for Oracle startup to finish or recreate the Oracle data volume.'");
        builder.AppendLine("[ \"${pdb_open_mode}\" = 'READ WRITE' ] || oracle_fail 'Required pluggable database is not open.' \"${ORACLE_SERVICE_NAME} open_mode=${pdb_open_mode:-missing}\" 'Open the configured pluggable database or recreate the Oracle database container.'");
        builder.AppendLine("[ \"${xdb_status}\" = 'VALID' ] || oracle_fail 'Oracle XML Database (XDB) is invalid.' \"XDB status=${xdb_status:-missing}\" 'Recreate the Oracle database container or restore a clean Oracle data volume.'");

        if (hasOracleApex)
        {
            builder.AppendLine("oracle_apex_extract_root=/tmp/oracle-apex-install");
            builder.AppendLine("oracle_apex_extract_dir=${oracle_apex_extract_root}/apex");
            builder.AppendLine("find_apex_media() { if [ -f \"${ORACLE_APEX_MEDIA_DIR}/${ORACLE_APEX_MEDIA_PREFERRED_ZIP}\" ]; then printf '%s\\n' \"${ORACLE_APEX_MEDIA_DIR}/${ORACLE_APEX_MEDIA_PREFERRED_ZIP}\"; return 0; fi; for candidate in \"${ORACLE_APEX_MEDIA_DIR}\"/apex_*.zip \"${ORACLE_APEX_MEDIA_DIR}\"/apex*.zip; do if [ -f \"${candidate}\" ]; then printf '%s\\n' \"${candidate}\"; return 0; fi; done; return 1; }");
            builder.AppendLine("query_apex_registry() { local query_script=/tmp/query-apex-registry.sql; cat > \"${query_script}\" <<'SQL'\nSELECT comp_name || '|' || version || '|' || status FROM dba_registry WHERE comp_id = 'APEX';\nSQL\noracle_sql_run_file 'Installing APEX' sqlplus \"${oracle_sysdba_connection}\" single-sql-statement 'query_apex_registry' \"${query_script}\"; }");
            builder.AppendLine("query_apex_schema_count() { local query_script=/tmp/query-apex-schema-count.sql; cat > \"${query_script}\" <<'SQL'\nSELECT COUNT(*) FROM dba_users WHERE username LIKE 'APEX_%';\nSQL\noracle_sql_run_file 'Installing APEX' sqlplus \"${oracle_sysdba_connection}\" single-sql-statement 'query_apex_schema_count' \"${query_script}\"; }");
            builder.AppendLine("query_ords_public_user_count() { local query_script=/tmp/query-ords-public-user-count.sql; cat > \"${query_script}\" <<SQL\nSELECT COUNT(*) FROM dba_users WHERE username = UPPER('${ORACLE_ORDS_PUBLIC_USER}');\nSQL\noracle_sql_run_file 'Configuring ORDS' sqlplus \"${oracle_sysdba_connection}\" single-sql-statement 'query_ords_public_user_count' \"${query_script}\"; }");
            builder.AppendLine("query_ords_metadata_count() { local query_script=/tmp/query-ords-metadata-count.sql; cat > \"${query_script}\" <<'SQL'\nSELECT COUNT(*) FROM dba_users WHERE username = 'ORDS_METADATA';\nSQL\noracle_sql_run_file 'Configuring ORDS' sqlplus \"${oracle_sysdba_connection}\" single-sql-statement 'query_ords_metadata_count' \"${query_script}\"; }");
            builder.AppendLine("prepare_apex_extract_dir() { local apex_zip; mkdir -p \"${ORACLE_APEX_MEDIA_DIR}\"; apex_zip=$(find_apex_media) || oracle_fail 'APEX installation media missing.' 'No supported apex zip was found.' 'Download Oracle APEX ZIP and place it under the managed downloads directory.'; rm -rf \"${oracle_apex_extract_root}\"; mkdir -p \"${oracle_apex_extract_root}\"; unzip -oq \"${apex_zip}\" -d \"${oracle_apex_extract_root}\"; [ -f \"${oracle_apex_extract_dir}/apexins.sql\" ] || oracle_fail 'APEX installation media missing apexins.sql after extraction.' 'apexins.sql missing' 'Replace the downloaded APEX media and retry.'; }");
            builder.AppendLine("install_apex_media() { local apex_registry apex_schema_count apex_registry_status apex_runner; apex_registry=$(query_apex_registry | tr -d '\\r' | xargs || true); apex_schema_count=$(query_apex_schema_count | tr -d '\\r' | xargs || true); apex_registry_status=$(printf '%s' \"${apex_registry}\" | awk -F'|' '{print $3}'); if [ -n \"${apex_registry}\" ] && [ \"${apex_registry_status}\" = 'VALID' ] && [ -n \"${apex_schema_count}\" ] && [ \"${apex_schema_count}\" != '0' ]; then echo \"[oracle-apex] APEX already installed: ${apex_registry}\"; return 0; fi; prepare_apex_extract_dir; apex_runner=/tmp/install-apex-media.sql; cat > \"${apex_runner}\" <<'SQL'\n@apexins.sql SYSAUX SYSAUX TEMP /i/\nSQL\n(cd \"${oracle_apex_extract_dir}\" && oracle_sql_run_file 'Installing APEX' sqlplus \"${oracle_sysdba_connection}\" script 'apexins.sql' \"${apex_runner}\") || oracle_fail 'Oracle APEX installation failed.' 'apexins.sql failed; see oracle-sql diagnostics above.' 'Replace the APEX media or recreate the Oracle database volume.'; }");
            builder.AppendLine("ensure_ords_public_user_ready() { local ords_user_script=/tmp/ords-public-user.sql; cat > \"${ords_user_script}\" <<SQL\nALTER SESSION SET CONTAINER = ${ORACLE_SERVICE_NAME};\nALTER USER ${ORACLE_ORDS_PUBLIC_USER} IDENTIFIED BY \"${ORACLE_ORDS_PUBLIC_PASSWORD}\" ACCOUNT UNLOCK;\nSQL\noracle_sql_run_file 'Configuring ORDS' sqlplus \"${oracle_sysdba_connection}\" script 'ords-public-user.sql' \"${ords_user_script}\" || oracle_fail 'Oracle REST Data Services database user could not be synchronized.' 'ords-public-user.sql failed; see oracle-sql diagnostics above.' 'Reset Runtime to recreate the Oracle data volume, or manually reset the ORDS public user.'; }");
            builder.AppendLine("wait_for_ords_runtime() { oracle_ords_landing_url=\"${ORACLE_ORDS_INTERNAL_BASE_URL}/_/landing\"; local started_at current_elapsed ords_http_status; started_at=$(date +%s); while true; do ords_http_status=$(curl -sS -o /tmp/ords-health-body.txt -w '%{http_code}' \"${oracle_ords_landing_url}\" || true); if [ \"${ords_http_status}\" = '200' ] || [ \"${ords_http_status}\" = '302' ]; then return 0; fi; current_elapsed=$(( $(date +%s) - started_at )); if [ \"${current_elapsed}\" -ge 180 ]; then oracle_fail 'ORDS landing endpoint timed out after 180s.' \"HTTP ${ords_http_status:-missing}\" 'Check the oracle-ords container logs and recreate the ORDS container if configuration did not complete.'; fi; sleep 10; done; }");
            builder.AppendLine("oracle_set_stage 'Installing APEX'");
            builder.AppendLine("install_apex_media");
            builder.AppendLine("oracle_set_stage 'Configuring ORDS'");
            builder.AppendLine("ords_public_user_count=$(query_ords_public_user_count | tr -d '\\r' | xargs || true)");
            builder.AppendLine("ords_metadata_count=$(query_ords_metadata_count | tr -d '\\r' | xargs || true)");
            builder.AppendLine("[ \"${ords_public_user_count:-0}\" != '0' ] && [ \"${ords_metadata_count:-0}\" != '0' ] || oracle_fail 'Oracle REST Data Services is not installed in the database.' \"ORDS_PUBLIC_USER count=${ords_public_user_count:-0}; ORDS_METADATA count=${ords_metadata_count:-0}\" 'Inspect ORDS installation logs before retrying.'");
            builder.AppendLine("ensure_ords_public_user_ready");
            builder.AppendLine("wait_for_ords_runtime");
            if (hasOracleApexLang)
            {
                builder.AppendLine("oracle_set_stage 'Creating Sample Application'");
                builder.AppendLine("[ -x /workspace/scripts/apexlang-hello-world.sh ] || oracle_fail 'APEXlang Hello World provisioning script is missing.' 'Expected /workspace/scripts/apexlang-hello-world.sh to exist and be executable.' 'Regenerate the managed workspace files and retry provisioning.'");
                builder.AppendLine("/workspace/scripts/apexlang-hello-world.sh");
            }
        }

        builder.AppendLine("oracle_set_stage 'Final Validation'");
        builder.AppendLine("complete_stage");
        builder.AppendLine("rm -f \"${oracle_probe_script}\" \"${oracle_sqlplus_probe_script}\" \"${oracle_demo_user_setup_script}\" /tmp/sqlcl-probe.out /tmp/sqlplus-probe.out /tmp/oracle-demo-user-setup.out /tmp/ords-public-user.out /tmp/ords-health-body.txt");
        return builder.ToString();
    }
}
