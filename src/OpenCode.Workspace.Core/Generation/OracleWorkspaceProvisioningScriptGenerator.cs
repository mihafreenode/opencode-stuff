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
        builder.AppendLine("oracle_query_container_name_body=/tmp/oracle-query-container-name.sql");
        builder.AppendLine("cat > \"${oracle_query_container_name_body}\" <<'SQL'");
        builder.AppendLine("SELECT sys_context('USERENV', 'CON_NAME') AS container_name FROM dual;");
        builder.AppendLine("SQL");
        builder.AppendLine("oracle_query_xdb_registry_body=/tmp/oracle-query-xdb-registry.sql");
        builder.AppendLine("cat > \"${oracle_query_xdb_registry_body}\" <<'SQL'");
        builder.AppendLine("SELECT comp_id || '|' || comp_name || '|' || version || '|' || status || '|' || modified FROM dba_registry WHERE comp_id = 'XDB';");
        builder.AppendLine("SQL");
        builder.AppendLine("oracle_query_xdb_invalid_objects_body=/tmp/oracle-query-xdb-invalid-objects.sql");
        builder.AppendLine("cat > \"${oracle_query_xdb_invalid_objects_body}\" <<'SQL'");
        builder.AppendLine("SELECT owner || '|' || object_name || '|' || object_type || '|' || status FROM dba_objects WHERE owner = 'XDB' AND status = 'INVALID' ORDER BY object_type, object_name;");
        builder.AppendLine("SQL");
        builder.AppendLine("oracle_query_xdb_errors_body=/tmp/oracle-query-xdb-errors.sql");
        builder.AppendLine("cat > \"${oracle_query_xdb_errors_body}\" <<'SQL'");
        builder.AppendLine("SELECT owner || '|' || name || '|' || type || '|' || line || '|' || position || '|' || REPLACE(REPLACE(text, CHR(10), ' '), CHR(13), ' ') FROM dba_errors WHERE owner = 'XDB' ORDER BY name, sequence;");
        builder.AppendLine("SQL");
        builder.AppendLine("oracle_query_cdb_xdb_registry_body=/tmp/oracle-query-cdb-xdb-registry.sql");
        builder.AppendLine("cat > \"${oracle_query_cdb_xdb_registry_body}\" <<'SQL'");
        builder.AppendLine("SELECT con_id || '|' || comp_id || '|' || comp_name || '|' || version || '|' || status FROM cdb_registry WHERE comp_id = 'XDB' ORDER BY con_id;");
        builder.AppendLine("SQL");
        builder.AppendLine("oracle_query_xdb_sqlpatch_body=/tmp/oracle-query-xdb-sqlpatch.sql");
        builder.AppendLine("cat > \"${oracle_query_xdb_sqlpatch_body}\" <<'SQL'");
        builder.AppendLine("SELECT patch_id || '|' || status || '|' || action || '|' || TO_CHAR(action_time, 'YYYY-MM-DD\"T\"HH24:MI:SS') || '|' || description FROM dba_registry_sqlpatch ORDER BY action_time;");
        builder.AppendLine("SQL");
        builder.AppendLine("oracle_query_xdb_dbms_registry_status_body=/tmp/oracle-query-xdb-dbms-registry-status.sql");
        builder.AppendLine("cat > \"${oracle_query_xdb_dbms_registry_status_body}\" <<'SQL'");
        builder.AppendLine("SELECT dbms_registry.status('XDB') FROM dual;");
        builder.AppendLine("SQL");
        builder.AppendLine("oracle_query_xdb_functional_probe_body=/tmp/oracle-query-xdb-functional-probe.sql");
        builder.AppendLine("cat > \"${oracle_query_xdb_functional_probe_body}\" <<'SQL'");
        builder.AppendLine("SELECT 'XMLTYPE=' || XMLTYPE('<a>ok</a>').extract('/a/text()').getStringVal() || '|HTTPPORT=' || DBMS_XDB.GETHTTPPORT FROM dual;");
        builder.AppendLine("SQL");
        builder.AppendLine("oracle_query_pdb_violations_body=/tmp/oracle-query-pdb-violations.sql");
        builder.AppendLine("cat > \"${oracle_query_pdb_violations_body}\" <<'SQL'");
        builder.AppendLine("SELECT message || '|' || status || '|' || type || '|' || action FROM pdb_plug_in_violations WHERE status <> 'RESOLVED' ORDER BY time;");
        builder.AppendLine("SQL");
        builder.AppendLine("oracle_query_database_version_body=/tmp/oracle-query-database-version.sql");
        builder.AppendLine("cat > \"${oracle_query_database_version_body}\" <<'SQL'");
        builder.AppendLine("SELECT banner_full FROM v$version WHERE banner_full LIKE 'Oracle Database%';");
        builder.AppendLine("SQL");
        builder.AppendLine("timestamp_utc() { date -u +'%Y-%m-%dT%H:%M:%SZ'; }");
        builder.AppendLine("stage_name='' ");
        builder.AppendLine("stage_started_at=0");
        builder.AppendLine("stage_active=0");
        builder.AppendLine("oracle_xdb_wait_timeout_seconds=90");
        builder.AppendLine("oracle_xdb_poll_interval_seconds=5");
        builder.AppendLine("oracle_pdb_ready_timeout_seconds=180");
        builder.AppendLine("oracle_volume_state=${OPENCODE_ORACLE_VOLUME_STATE:-unknown}");
        builder.AppendLine("oracle_volume_scope=${OPENCODE_ORACLE_VOLUME_SCOPE:-unknown}");
        builder.AppendLine("oracle_volume_reset_allowed=${OPENCODE_ORACLE_VOLUME_RESET_ALLOWED:-false}");
        builder.AppendLine("oracle_database_image=${OPENCODE_ORACLE_DATABASE_IMAGE:-unknown}");
        builder.AppendLine("oracle_healthy_utc=${OPENCODE_ORACLE_HEALTHY_UTC:-}");
        builder.AppendLine("oracle_target_container=${ORACLE_SERVICE_NAME}");
        builder.AppendLine("oracle_root_container='CDB$ROOT'");
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
        builder.AppendLine("oracle_compact_output() { tr -d '\\r' | sed '/^[[:space:]]*$/d' | xargs || true; }");
        builder.AppendLine("oracle_elapsed_since_healthy_seconds() { if [ -z \"${oracle_healthy_utc}\" ]; then printf 'unknown'; return 0; fi; local healthy_epoch; healthy_epoch=$(date -u -d \"${oracle_healthy_utc}\" +%s 2>/dev/null || true); if [ -z \"${healthy_epoch}\" ]; then printf 'unknown'; return 0; fi; printf '%s' \"$(( $(date -u +%s) - healthy_epoch ))\"; }");
        builder.AppendLine("oracle_container_token() { printf '%s' \"$1\" | tr -c '[:alnum:]' '_'; }");
        builder.AppendLine("oracle_build_container_wrapper() { local container_name=$1; local body_file=$2; local wrapper_file=$3; local query_mode=$4; : >\"${wrapper_file}\"; printf 'ALTER SESSION SET CONTAINER = %s;\\n' \"${container_name}\" >>\"${wrapper_file}\"; if [ \"${query_mode}\" = 'query' ]; then cat >>\"${wrapper_file}\" <<'SQL'\nSET PAGESIZE 0\nSET FEEDBACK OFF\nSET HEADING OFF\nSET VERIFY OFF\nSET ECHO OFF\nSET TRIMSPOOL ON\nPROMPT __OPENCODE_RESULT_BEGIN__\nSQL\ncat \"${body_file}\" >>\"${wrapper_file}\"; printf 'PROMPT __OPENCODE_RESULT_END__\\n' >>\"${wrapper_file}\"; else cat \"${body_file}\" >>\"${wrapper_file}\"; fi; }");
        builder.AppendLine("oracle_run_container_query() { local phase=$1; local container_name=$2; local source_id=$3; local body_file=$4; local wrapper_file; wrapper_file=$(mktemp); oracle_build_container_wrapper \"${container_name}\" \"${body_file}\" \"${wrapper_file}\" query; oracle_sql_run_file \"${phase}\" sqlplus \"${oracle_sysdba_connection}\" query-script \"${source_id}\" \"${wrapper_file}\"; local exit_code=$?; rm -f \"${wrapper_file}\"; return ${exit_code}; }");
        builder.AppendLine("oracle_run_container_script() { local phase=$1; local container_name=$2; local source_id=$3; local body_file=$4; local wrapper_file; wrapper_file=$(mktemp); oracle_build_container_wrapper \"${container_name}\" \"${body_file}\" \"${wrapper_file}\" script; oracle_sql_run_file \"${phase}\" sqlplus \"${oracle_sysdba_connection}\" script \"${source_id}\" \"${wrapper_file}\"; local exit_code=$?; rm -f \"${wrapper_file}\"; return ${exit_code}; }");
        builder.AppendLine("query_container_name_in_container() { local container_name=$1; local token; token=$(oracle_container_token \"${container_name}\"); oracle_run_container_query 'Provisioning Oracle' \"${container_name}\" \"query_container_name_${token}\" \"${oracle_query_container_name_body}\"; }");
        builder.AppendLine("query_xdb_registry_in_container() { local container_name=$1; local token; token=$(oracle_container_token \"${container_name}\"); oracle_run_container_query 'Provisioning Oracle' \"${container_name}\" \"query_xdb_registry_${token}\" \"${oracle_query_xdb_registry_body}\"; }");
        builder.AppendLine("query_xdb_invalid_objects_in_container() { local container_name=$1; local token; token=$(oracle_container_token \"${container_name}\"); oracle_run_container_query 'Provisioning Oracle' \"${container_name}\" \"query_xdb_invalid_objects_${token}\" \"${oracle_query_xdb_invalid_objects_body}\"; }");
        builder.AppendLine("query_xdb_errors_in_container() { local container_name=$1; local token; token=$(oracle_container_token \"${container_name}\"); oracle_run_container_query 'Provisioning Oracle' \"${container_name}\" \"query_xdb_errors_${token}\" \"${oracle_query_xdb_errors_body}\"; }");
        builder.AppendLine("query_cdb_xdb_registry() { oracle_run_container_query 'Provisioning Oracle' \"${oracle_root_container}\" 'query_cdb_xdb_registry' \"${oracle_query_cdb_xdb_registry_body}\"; }");
        builder.AppendLine("query_xdb_sqlpatch() { oracle_run_container_query 'Provisioning Oracle' \"${oracle_root_container}\" 'query_xdb_sqlpatch' \"${oracle_query_xdb_sqlpatch_body}\"; }");
        builder.AppendLine("query_xdb_dbms_registry_status_in_container() { local container_name=$1; local token; token=$(oracle_container_token \"${container_name}\"); oracle_run_container_query 'Provisioning Oracle' \"${container_name}\" \"query_xdb_dbms_registry_status_${token}\" \"${oracle_query_xdb_dbms_registry_status_body}\"; }");
        builder.AppendLine("query_xdb_functional_probe_in_container() { local container_name=$1; local token; token=$(oracle_container_token \"${container_name}\"); oracle_run_container_query 'Provisioning Oracle' \"${container_name}\" \"query_xdb_functional_probe_${token}\" \"${oracle_query_xdb_functional_probe_body}\"; }");
        builder.AppendLine("query_pdb_plugin_violations() { oracle_run_container_query 'Provisioning Oracle' \"${oracle_root_container}\" 'query_pdb_plugin_violations' \"${oracle_query_pdb_violations_body}\"; }");
        builder.AppendLine("query_database_version() { oracle_run_container_query 'Provisioning Oracle' \"${oracle_root_container}\" 'query_database_version' \"${oracle_query_database_version_body}\"; }");
        builder.AppendLine("oracle_extract_registry_status() { printf '%s' \"$1\" | awk -F'|' 'NF >= 4 { print $4; exit }'; }");
        builder.AppendLine("oracle_count_lines() { if [ -z \"${1:-}\" ]; then printf '0'; else printf '%s\\n' \"$1\" | sed '/^[[:space:]]*$/d' | wc -l | tr -d ' '; fi; }");
        builder.AppendLine("oracle_first_lines() { if [ -z \"${1:-}\" ]; then return 0; fi; printf '%s\\n' \"$1\" | sed -n '1,5p' | paste -sd ';' - | sed 's/[[:space:]]\\+/ /g'; }");
        builder.AppendLine("wait_for_pdb_read_write() { local started_at current_elapsed pdb_status last_pdb_status=''; started_at=$(date +%s); while true; do pdb_status=$(query_pdb_open_mode | oracle_compact_output); if [ \"${pdb_status}\" != \"${last_pdb_status}\" ]; then printf '[oracle-xdb] pdb=%s open_mode=%s elapsed_since_healthy_seconds=%s\\n' \"${ORACLE_SERVICE_NAME}\" \"${pdb_status:-missing}\" \"$(oracle_elapsed_since_healthy_seconds)\" >&2; last_pdb_status=\"${pdb_status}\"; fi; if [ \"${pdb_status}\" = 'READ WRITE' ]; then return 0; fi; current_elapsed=$(( $(date +%s) - started_at )); if [ \"${current_elapsed}\" -ge \"${oracle_pdb_ready_timeout_seconds}\" ]; then oracle_fail 'Required pluggable database is not open.' \"${ORACLE_SERVICE_NAME} open_mode=${pdb_status:-missing}\" 'Open the configured pluggable database or recreate the Oracle database container.'; fi; sleep \"${oracle_xdb_poll_interval_seconds}\"; done; }");
        builder.AppendLine("oracle_database_version='' ");
        builder.AppendLine("oracle_root_registry='' ");
        builder.AppendLine("oracle_pdb_registry='' ");
        builder.AppendLine("oracle_root_invalid_objects='' ");
        builder.AppendLine("oracle_pdb_invalid_objects='' ");
        builder.AppendLine("oracle_root_errors='' ");
        builder.AppendLine("oracle_pdb_errors='' ");
        builder.AppendLine("oracle_pdb_violations='' ");
        builder.AppendLine("oracle_root_container_name='' ");
        builder.AppendLine("oracle_pdb_container_name='' ");
        builder.AppendLine("oracle_capture_xdb_diagnostics() { oracle_root_container_name=$(query_container_name_in_container \"${oracle_root_container}\" | oracle_compact_output); oracle_pdb_container_name=$(query_container_name_in_container \"${oracle_target_container}\" | oracle_compact_output); oracle_root_registry=$(query_xdb_registry_in_container \"${oracle_root_container}\" | tr -d '\\r' | sed '/^[[:space:]]*$/d'); oracle_pdb_registry=$(query_xdb_registry_in_container \"${oracle_target_container}\" | tr -d '\\r' | sed '/^[[:space:]]*$/d'); oracle_root_invalid_objects=$(query_xdb_invalid_objects_in_container \"${oracle_root_container}\" | tr -d '\\r' | sed '/^[[:space:]]*$/d'); oracle_pdb_invalid_objects=$(query_xdb_invalid_objects_in_container \"${oracle_target_container}\" | tr -d '\\r' | sed '/^[[:space:]]*$/d'); oracle_root_errors=$(query_xdb_errors_in_container \"${oracle_root_container}\" | tr -d '\\r' | sed '/^[[:space:]]*$/d'); oracle_pdb_errors=$(query_xdb_errors_in_container \"${oracle_target_container}\" | tr -d '\\r' | sed '/^[[:space:]]*$/d'); oracle_pdb_violations=$(query_pdb_plugin_violations | tr -d '\\r' | sed '/^[[:space:]]*$/d'); oracle_database_version=$(query_database_version | oracle_compact_output); printf '[oracle-xdb] database_image=%s database_version=%s volume_state=%s volume_scope=%s elapsed_since_healthy_seconds=%s\\n' \"${oracle_database_image}\" \"${oracle_database_version:-unknown}\" \"${oracle_volume_state}\" \"${oracle_volume_scope}\" \"$(oracle_elapsed_since_healthy_seconds)\" >&2; printf '[oracle-xdb] container=%s registry=%s invalid_object_count=%s representative_invalid_objects=%s representative_dba_errors=%s\\n' \"${oracle_root_container_name:-${oracle_root_container}}\" \"$(oracle_compact_output <<<\"${oracle_root_registry}\")\" \"$(oracle_count_lines \"${oracle_root_invalid_objects}\")\" \"$(oracle_first_lines \"${oracle_root_invalid_objects}\")\" \"$(oracle_first_lines \"${oracle_root_errors}\")\" >&2; printf '[oracle-xdb] container=%s registry=%s invalid_object_count=%s representative_invalid_objects=%s representative_dba_errors=%s\\n' \"${oracle_pdb_container_name:-${oracle_target_container}}\" \"$(oracle_compact_output <<<\"${oracle_pdb_registry}\")\" \"$(oracle_count_lines \"${oracle_pdb_invalid_objects}\")\" \"$(oracle_first_lines \"${oracle_pdb_invalid_objects}\")\" \"$(oracle_first_lines \"${oracle_pdb_errors}\")\" >&2; if [ -n \"${oracle_pdb_violations}\" ]; then printf '[oracle-xdb] unresolved_pdb_plug_in_violations=%s\\n' \"$(oracle_first_lines \"${oracle_pdb_violations}\")\" >&2; fi; }");
        builder.AppendLine("wait_for_xdb_ready() { local started_at root_registry pdb_registry root_status pdb_status last_root_status='' last_pdb_status='' saw_invalid='false'; started_at=$(date +%s); while true; do root_registry=$(query_xdb_registry_in_container \"${oracle_root_container}\" | oracle_compact_output); pdb_registry=$(query_xdb_registry_in_container \"${oracle_target_container}\" | oracle_compact_output); root_status=$(oracle_extract_registry_status \"${root_registry}\"); pdb_status=$(oracle_extract_registry_status \"${pdb_registry}\"); if [ \"${root_status}\" != \"${last_root_status}\" ]; then printf '[oracle-xdb] container=%s registry=%s elapsed_since_healthy_seconds=%s\\n' \"${oracle_root_container}\" \"${root_registry:-missing}\" \"$(oracle_elapsed_since_healthy_seconds)\" >&2; last_root_status=\"${root_status}\"; fi; if [ \"${pdb_status}\" != \"${last_pdb_status}\" ]; then printf '[oracle-xdb] container=%s registry=%s elapsed_since_healthy_seconds=%s\\n' \"${oracle_target_container}\" \"${pdb_registry:-missing}\" \"$(oracle_elapsed_since_healthy_seconds)\" >&2; last_pdb_status=\"${pdb_status}\"; fi; if [ \"${root_status}\" = 'VALID' ] && [ \"${pdb_status}\" = 'VALID' ]; then if [ \"${saw_invalid}\" = 'true' ]; then printf '[oracle-xdb] XDB was initially INVALID but became VALID while Oracle initialization completed.\\n' >&2; fi; return 0; fi; if [ \"${root_status:-missing}\" != 'VALID' ] || [ \"${pdb_status:-missing}\" != 'VALID' ]; then saw_invalid='true'; fi; if [ $(( $(date +%s) - started_at )) -ge \"${oracle_xdb_wait_timeout_seconds}\" ]; then return 1; fi; sleep \"${oracle_xdb_poll_interval_seconds}\"; done; }");
        builder.AppendLine("oracle_recommend_xdb_action() { if [ \"${oracle_volume_reset_allowed}\" = 'true' ] && [ \"${oracle_volume_state}\" = 'new' ]; then printf 'Reset Runtime.'; else printf 'Investigate the Oracle XDB compilation errors or restore a known-good backup.'; fi; }");
        builder.AppendLine("fail_for_invalid_xdb() { oracle_capture_xdb_diagnostics; local invalid_containers=''; local root_status pdb_status invalid_object_count representative_objects representative_errors violations recommendation; root_status=$(oracle_extract_registry_status \"${oracle_root_registry}\"); pdb_status=$(oracle_extract_registry_status \"${oracle_pdb_registry}\"); if [ \"${root_status:-missing}\" != 'VALID' ]; then invalid_containers=\"${oracle_root_container_name:-${oracle_root_container}}\"; fi; if [ \"${pdb_status:-missing}\" != 'VALID' ]; then invalid_containers=\"${invalid_containers}${invalid_containers:+,}${oracle_pdb_container_name:-${oracle_target_container}}\"; fi; invalid_object_count=$(( $(oracle_count_lines \"${oracle_root_invalid_objects}\") + $(oracle_count_lines \"${oracle_pdb_invalid_objects}\") )); representative_objects=$(oracle_first_lines \"${oracle_root_invalid_objects}\"); if [ -z \"${representative_objects}\" ]; then representative_objects=$(oracle_first_lines \"${oracle_pdb_invalid_objects}\"); fi; representative_errors=$(oracle_first_lines \"${oracle_root_errors}\"); if [ -z \"${representative_errors}\" ]; then representative_errors=$(oracle_first_lines \"${oracle_pdb_errors}\"); fi; violations=$(oracle_first_lines \"${oracle_pdb_violations}\"); recommendation=$(oracle_recommend_xdb_action); oracle_fail 'Oracle XML Database (XDB) is invalid.' \"containers=${invalid_containers:-unknown}; root_registry=${oracle_root_registry:-missing}; pdb_registry=${oracle_pdb_registry:-missing}; invalid_object_count=${invalid_object_count}; invalid_objects=${representative_objects:-none}; dba_errors=${representative_errors:-none}; pdb_plug_in_violations=${violations:-none}; volume_state=${oracle_volume_state}; elapsed_since_healthy_seconds=$(oracle_elapsed_since_healthy_seconds); database_image=${oracle_database_image}; database_version=${oracle_database_version:-unknown}\" \"${recommendation}\"; }");
        builder.AppendLine("fail_for_invalid_xdb() { oracle_capture_xdb_diagnostics; local invalid_containers=''; local root_status pdb_status invalid_object_count representative_objects representative_errors violations recommendation; root_status=$(oracle_extract_registry_status \"${oracle_root_registry}\"); pdb_status=$(oracle_extract_registry_status \"${oracle_pdb_registry}\"); if [ \"${root_status:-missing}\" != 'VALID' ]; then invalid_containers=\"${oracle_root_container_name:-${oracle_root_container}}\"; fi; if [ \"${pdb_status:-missing}\" != 'VALID' ]; then invalid_containers=\"${invalid_containers}${invalid_containers:+,}${oracle_pdb_container_name:-${oracle_target_container}}\"; fi; invalid_object_count=$(( $(oracle_count_lines \"${oracle_root_invalid_objects}\") + $(oracle_count_lines \"${oracle_pdb_invalid_objects}\") )); representative_objects=$(oracle_first_lines \"${oracle_root_invalid_objects}\"); if [ -z \"${representative_objects}\" ]; then representative_objects=$(oracle_first_lines \"${oracle_pdb_invalid_objects}\"); fi; representative_errors=$(oracle_first_lines \"${oracle_root_errors}\"); if [ -z \"${representative_errors}\" ]; then representative_errors=$(oracle_first_lines \"${oracle_pdb_errors}\"); fi; violations=$(oracle_first_lines \"${oracle_pdb_violations}\"); recommendation=$(oracle_recommend_xdb_action); printf '[oracle-xdb] XDB remained INVALID after readiness polling.\\n' >&2; oracle_fail 'Oracle XML Database (XDB) is invalid.' \"containers=${invalid_containers:-unknown}; root_registry=${oracle_root_registry:-missing}; pdb_registry=${oracle_pdb_registry:-missing}; invalid_object_count=${invalid_object_count}; invalid_objects=${representative_objects:-none}; dba_errors=${representative_errors:-none}; pdb_plug_in_violations=${violations:-none}; volume_state=${oracle_volume_state}; elapsed_since_healthy_seconds=$(oracle_elapsed_since_healthy_seconds); database_image=${oracle_database_image}; database_version=${oracle_database_version:-unknown}\" \"${recommendation}\"; }");
        builder.AppendLine("oracle_allow_invalid_xdb_if_functional() { oracle_capture_xdb_diagnostics; local invalid_object_count root_dbms_registry_status pdb_dbms_registry_status root_functional_probe pdb_functional_probe cdb_registry sqlpatch violations representative_objects representative_errors recommendation; invalid_object_count=$(( $(oracle_count_lines \"${oracle_root_invalid_objects}\") + $(oracle_count_lines \"${oracle_pdb_invalid_objects}\") )); if [ \"${invalid_object_count}\" != '0' ] || [ -n \"${oracle_root_errors:-}\" ] || [ -n \"${oracle_pdb_errors:-}\" ] || [ -n \"${oracle_pdb_violations:-}\" ]; then fail_for_invalid_xdb; fi; root_dbms_registry_status=$(query_xdb_dbms_registry_status_in_container \"${oracle_root_container}\" | oracle_compact_output || true); pdb_dbms_registry_status=$(query_xdb_dbms_registry_status_in_container \"${oracle_target_container}\" | oracle_compact_output || true); if ! root_functional_probe=$(query_xdb_functional_probe_in_container \"${oracle_root_container}\" | oracle_compact_output); then root_functional_probe='failed'; fi; if ! pdb_functional_probe=$(query_xdb_functional_probe_in_container \"${oracle_target_container}\" | oracle_compact_output); then pdb_functional_probe='failed'; fi; if [ \"${root_functional_probe}\" != 'failed' ] && [ \"${pdb_functional_probe}\" != 'failed' ]; then printf '[oracle-xdb] registry remained INVALID but XMLType/DBMS_XDB probes succeeded in root and pdb; continuing.\\n' >&2; return 0; fi; cdb_registry=$(query_cdb_xdb_registry | tr -d '\\r' | sed '/^[[:space:]]*$/d' || true); sqlpatch=$(query_xdb_sqlpatch | tr -d '\\r' | sed '/^[[:space:]]*$/d' || true); violations=$(oracle_first_lines \"${oracle_pdb_violations}\"); representative_objects=$(oracle_first_lines \"${oracle_root_invalid_objects}\"); if [ -z \"${representative_objects}\" ]; then representative_objects=$(oracle_first_lines \"${oracle_pdb_invalid_objects}\"); fi; representative_errors=$(oracle_first_lines \"${oracle_root_errors}\"); if [ -z \"${representative_errors}\" ]; then representative_errors=$(oracle_first_lines \"${oracle_pdb_errors}\"); fi; recommendation=$(oracle_recommend_xdb_action); oracle_fail 'Oracle XML Database (XDB) is invalid.' \"root_registry=${oracle_root_registry:-missing}; pdb_registry=${oracle_pdb_registry:-missing}; cdb_registry=${cdb_registry:-none}; sqlpatch=${sqlpatch:-none}; root_dbms_registry_status=${root_dbms_registry_status:-missing}; pdb_dbms_registry_status=${pdb_dbms_registry_status:-missing}; root_functional_probe=${root_functional_probe:-failed}; pdb_functional_probe=${pdb_functional_probe:-failed}; invalid_object_count=${invalid_object_count}; invalid_objects=${representative_objects:-none}; dba_errors=${representative_errors:-none}; pdb_plug_in_violations=${violations:-none}; volume_state=${oracle_volume_state}; elapsed_since_healthy_seconds=$(oracle_elapsed_since_healthy_seconds); database_image=${oracle_database_image}; database_version=${oracle_database_version:-unknown}\" \"${recommendation}\"; }");
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
        builder.AppendLine("database_open_mode=$(query_database_open_mode | oracle_compact_output)");
        builder.AppendLine("[ \"${database_open_mode}\" = 'READ WRITE' ] || oracle_fail 'Oracle database is not open for writes.' \"open_mode=${database_open_mode:-missing}\" 'Wait for Oracle startup to finish or recreate the Oracle data volume.'");
        builder.AppendLine("wait_for_pdb_read_write");
        builder.AppendLine("wait_for_xdb_ready || oracle_allow_invalid_xdb_if_functional");

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
        builder.AppendLine("rm -f \"${oracle_probe_script}\" \"${oracle_sqlplus_probe_script}\" \"${oracle_demo_user_setup_script}\" \"${oracle_query_container_name_body}\" \"${oracle_query_xdb_registry_body}\" \"${oracle_query_xdb_invalid_objects_body}\" \"${oracle_query_xdb_errors_body}\" \"${oracle_query_pdb_violations_body}\" \"${oracle_query_database_version_body}\" /tmp/sqlcl-probe.out /tmp/sqlplus-probe.out /tmp/oracle-demo-user-setup.out /tmp/ords-public-user.out /tmp/ords-health-body.txt");
        return builder.ToString();
    }
}
