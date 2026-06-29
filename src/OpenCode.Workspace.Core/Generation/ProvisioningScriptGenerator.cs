using System.Text;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

/// <summary>
/// Generates a provisioning script instead of baking custom images so the full
/// installation plan stays readable and reproducible from official Ubuntu images.
/// </summary>
public sealed class ProvisioningScriptGenerator
{
    private const string BashRcManagedStart = "# >>> OpenCode Workspace Manager managed block >>>";
    private const string BashRcManagedEnd = "# <<< OpenCode Workspace Manager managed block <<<";

    public string Generate(ResolvedWorkspace workspace, GeneratedArtifactRuntimeMetadata? runtimeMetadata = null)
    {
        var builder = new StringBuilder();
        // Ubuntu 24.04 renamed the old libaio1 package to libaio1t64, so Oracle-related
        // provisioning must not hardcode libaio1 in the generic apt package plan.
        var oracleWorkspaceKind = OracleWorkspaceFamily.Detect(workspace.Definition);
        var isOracleDemoWorkspace = oracleWorkspaceKind != OracleWorkspaceKind.None;
        var hasOracleApex = oracleWorkspaceKind is OracleWorkspaceKind.Apex or OracleWorkspaceKind.ApexLang;
        var aptPackages = workspace.AptPackages
            .Where(packageName => !isOracleDemoWorkspace || !string.Equals(packageName, "libaio1", StringComparison.OrdinalIgnoreCase))
            .ToList();

        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine(GeneratedArtifactRuntimeMetadataBuilder.BuildCommentHeader(
            runtimeMetadata,
            "Source inputs: workspace.yaml and catalog manifests under catalog/.",
            "User edits are not preserved. Edit workspace.yaml or catalog manifests instead."));
        builder.AppendLine("set -euo pipefail");
        builder.AppendLine();
        builder.AppendLine("export DEBIAN_FRONTEND=noninteractive");
        builder.AppendLine("export HOME=/home/opencode");
        builder.AppendLine("if [ -f /workspace/.env ]; then");
        builder.AppendLine("  while IFS= read -r env_line || [ -n \"${env_line}\" ]; do");
        builder.AppendLine("    env_line=${env_line%$'\\r'}");
        builder.AppendLine("    case \"${env_line}\" in");
        builder.AppendLine("      ''|'#'*) continue ;;");
        builder.AppendLine("    esac");
        builder.AppendLine("    if [[ \"${env_line}\" != *=* ]]; then");
        builder.AppendLine("      echo \"[runtime] Skipping invalid .env line: ${env_line}\" >&2");
        builder.AppendLine("      continue");
        builder.AppendLine("    fi");
        builder.AppendLine("    env_key=${env_line%%=*}");
        builder.AppendLine("    env_value=${env_line#*=}");
        builder.AppendLine("    export \"${env_key}=${env_value}\"");
        builder.AppendLine("  done < /workspace/.env");
        builder.AppendLine("fi");
        builder.AppendLine();
        builder.AppendLine("# Create the durable workspace user that owns the interactive OpenCode session.");
        builder.AppendLine("if ! id -u opencode >/dev/null 2>&1; then");
        builder.AppendLine("  useradd -m -d /home/opencode -s /bin/bash opencode");
        builder.AppendLine("fi");
        builder.AppendLine("mkdir -p \"$HOME\"");
        builder.AppendLine("chown -R opencode:opencode \"$HOME\"");
        builder.AppendLine("touch \"$HOME/.bashrc\"");
        builder.AppendLine();
        builder.AppendLine("# Initialize OpenCode user directories before the first interactive launch.");
        builder.AppendLine("mkdir -p /home/opencode/.local/share/opencode/log");
        builder.AppendLine("mkdir -p /home/opencode/.config/opencode");
        builder.AppendLine("mkdir -p /home/opencode/.cache/opencode");
        builder.AppendLine("chown -R opencode:opencode /home/opencode/.local");
        builder.AppendLine("chown -R opencode:opencode /home/opencode/.config");
        builder.AppendLine("chown -R opencode:opencode /home/opencode/.cache");
        builder.AppendLine("su -s /bin/bash -c 'test -d /home/opencode/.local/share/opencode/log && test -w /home/opencode/.local/share/opencode/log' opencode");
        builder.AppendLine();
        builder.AppendLine("# Install the apt package plan resolved from selected features.");
        builder.AppendLine("apt-get update");

        if (aptPackages.Count > 0)
        {
            builder.AppendLine($"apt-get install -y {string.Join(" ", aptPackages)}");
        }

        builder.AppendLine();
        builder.AppendLine("# Install the requested Node.js runtime from NodeSource so modern npm packages use a consistent LTS baseline.");
        builder.AppendLine($"echo \"[runtime] Requested Node.js major version: {workspace.Definition.Runtime.GetEffectiveNodeMajorVersion()}\"");
        builder.AppendLine("apt-get remove -y nodejs npm || true");
        builder.AppendLine($"curl -fsSL https://deb.nodesource.com/setup_{workspace.Definition.Runtime.GetEffectiveNodeMajorVersion()}.x | bash -");
        builder.AppendLine("apt-get install -y nodejs");
        builder.AppendLine("apt-cache policy nodejs | sed -n '1,20p'");

        if (isOracleDemoWorkspace)
        {
            builder.AppendLine();
            if (hasOracleApex)
            {
                builder.AppendLine("echo \"[oracle-apex] Stage: Preparing Workspace\"");
                builder.AppendLine("echo \"[oracle-apex] Stage: Downloading Dependencies\"");
            }

            builder.AppendLine("# Oracle SQLcl still needs libaio, but Ubuntu 24.04 renamed the package to libaio1t64.");
            builder.AppendLine(". /etc/os-release && echo \"[oracle] Detected Ubuntu version: ${VERSION_ID:-unknown} (${ID:-unknown})\"");
            builder.AppendLine("if apt-cache policy libaio1 | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then");
            builder.AppendLine("  oracle_libaio_pkg=libaio1");
            builder.AppendLine("elif apt-cache policy libaio1t64 | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then");
            builder.AppendLine("  oracle_libaio_pkg=libaio1t64");
            builder.AppendLine("else");
            builder.AppendLine("  echo \"[oracle] No compatible libaio package found for this Ubuntu image.\" >&2");
            builder.AppendLine("  exit 1");
            builder.AppendLine("fi");
            builder.AppendLine("echo \"[oracle] Selected libaio package: ${oracle_libaio_pkg}\"");
            builder.AppendLine("apt-get install -y \"${oracle_libaio_pkg}\"");
            builder.AppendLine("dpkg -L \"${oracle_libaio_pkg}\"");
            builder.AppendLine("if [ \"${oracle_libaio_pkg}\" = \"libaio1t64\" ] && [ -f /usr/lib/x86_64-linux-gnu/libaio.so.1t64 ] && [ ! -e /usr/lib/x86_64-linux-gnu/libaio.so.1 ]; then");
            builder.AppendLine("  ln -sf /usr/lib/x86_64-linux-gnu/libaio.so.1t64 /usr/lib/x86_64-linux-gnu/libaio.so.1");
            builder.AppendLine("fi");
            builder.AppendLine("ldconfig");
            builder.AppendLine();
            builder.AppendLine("# SQLcl requires Java 11+, and Ubuntu images may expose different OpenJDK package names.");
            builder.AppendLine("if apt-cache policy openjdk-21-jre-headless | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then");
            builder.AppendLine("  oracle_java_pkg=openjdk-21-jre-headless");
            builder.AppendLine("elif apt-cache policy openjdk-17-jre-headless | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then");
            builder.AppendLine("  oracle_java_pkg=openjdk-17-jre-headless");
            builder.AppendLine("else");
            builder.AppendLine("  echo \"[oracle] No compatible Java runtime package found for SQLcl.\" >&2");
            builder.AppendLine("  exit 1");
            builder.AppendLine("fi");
            builder.AppendLine("echo \"[oracle] Selected Java package: ${oracle_java_pkg}\"");
            builder.AppendLine("apt-get install -y \"${oracle_java_pkg}\"");
            builder.AppendLine("JAVA_BIN=$(command -v java)");
            builder.AppendLine("if [ -z \"${JAVA_BIN}\" ]; then");
            builder.AppendLine("  echo \"[oracle] Java runtime is still unavailable after installation.\" >&2");
            builder.AppendLine("  exit 1");
            builder.AppendLine("fi");
            builder.AppendLine("export JAVA_HOME=$(dirname \"$(dirname \"$(readlink -f \"${JAVA_BIN}\")\")\")");
            builder.AppendLine("echo \"[oracle] JAVA_HOME=${JAVA_HOME}\"");
            builder.AppendLine("java -version");
            builder.AppendLine();
            builder.AppendLine("# Install Oracle Instant Client SQL*Plus so demo verification works from the workspace terminal even if SQLcl is unavailable.");
            builder.AppendLine("apt-get install -y libnsl2");
            builder.AppendLine("oracle_sqlplus_root=/opt/oracle/instantclient");
            builder.AppendLine("oracle_sqlplus_stage=/tmp/oracle-instantclient-stage");
            builder.AppendLine("oracle_sqlplus_basic_url=https://download.oracle.com/otn_software/linux/instantclient/2390000/instantclient-basiclite-linux.x64-23.9.0.25.07.zip");
            builder.AppendLine("oracle_sqlplus_package_url=https://download.oracle.com/otn_software/linux/instantclient/2390000/instantclient-sqlplus-linux.x64-23.9.0.25.07.zip");
            builder.AppendLine("# Reuse a healthy SQLcl install when possible, and only replace it after a staged reinstall validates successfully.");
            builder.AppendLine("mkdir -p /workspace/.local/oracle/network/admin");
            builder.AppendLine("oracle_connection='demo_user/demo_password@//oracle-demo:1521/FREEPDB1'");
            if (hasOracleApex)
            {
                builder.AppendLine("echo \"[oracle-apex] Stage: Starting Oracle Database\"");
                builder.AppendLine("echo \"[oracle-apex] Stage: Waiting for Database Readiness\"");
            }

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
            builder.AppendLine("activate_sqlplus_install() {");
            builder.AppendLine("  local instantclient_root=\"$1\"");
            builder.AppendLine("  local sqlplus_launcher=\"${instantclient_root}/sqlplus\"");
            builder.AppendLine("  if [ ! -f \"${sqlplus_launcher}\" ]; then");
            builder.AppendLine("    return 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  chmod +x \"${sqlplus_launcher}\"");
            builder.AppendLine("  export LD_LIBRARY_PATH=\"${instantclient_root}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}\"");
            builder.AppendLine("  export PATH=\"${instantclient_root}:${PATH}\"");
            builder.AppendLine("  ln -sf \"${sqlplus_launcher}\" /usr/local/bin/sqlplus");
            builder.AppendLine("  ldd \"${sqlplus_launcher}\"");
            builder.AppendLine("}");
            builder.AppendLine("if command -v sqlplus >/dev/null 2>&1 && sqlplus -v; then");
            builder.AppendLine("  echo \"[oracle] SQL*Plus already installed and valid; skipping reinstall.\"");
            builder.AppendLine("elif activate_sqlplus_install \"${oracle_sqlplus_root}/current\" && sqlplus -v; then");
            builder.AppendLine("  echo \"[oracle] SQL*Plus already installed and valid; skipping reinstall.\"");
            builder.AppendLine("else");
            builder.AppendLine("  rm -rf \"${oracle_sqlplus_stage}\"");
            builder.AppendLine("  mkdir -p \"${oracle_sqlplus_stage}\"");
            builder.AppendLine("  if ! curl -fsSL \"${oracle_sqlplus_basic_url}\" -o /tmp/instantclient-basiclite.zip; then");
            builder.AppendLine("    echo \"[oracle] Failed to download Oracle Instant Client Basic Light.\" >&2");
            builder.AppendLine("    exit 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  if ! curl -fsSL \"${oracle_sqlplus_package_url}\" -o /tmp/instantclient-sqlplus.zip; then");
            builder.AppendLine("    echo \"[oracle] Failed to download Oracle Instant Client SQL*Plus.\" >&2");
            builder.AppendLine("    exit 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  if ! unzip -oq /tmp/instantclient-basiclite.zip -d \"${oracle_sqlplus_stage}\"; then");
            builder.AppendLine("    echo \"[oracle] Failed to extract Oracle Instant Client Basic Light.\" >&2");
            builder.AppendLine("    exit 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  if ! unzip -oq /tmp/instantclient-sqlplus.zip -d \"${oracle_sqlplus_stage}\"; then");
            builder.AppendLine("    echo \"[oracle] Failed to extract Oracle Instant Client SQL*Plus.\" >&2");
            builder.AppendLine("    exit 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  oracle_sqlplus_candidate=$(find \"${oracle_sqlplus_stage}\" -maxdepth 1 -mindepth 1 -type d -name 'instantclient_*' | head -n 1)");
            builder.AppendLine("  if [ -z \"${oracle_sqlplus_candidate}\" ]; then");
            builder.AppendLine("    echo \"[oracle] Oracle Instant Client extract did not produce an instantclient directory.\" >&2");
            builder.AppendLine("    exit 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  if ! activate_sqlplus_install \"${oracle_sqlplus_candidate}\" || ! sqlplus -v; then");
            builder.AppendLine("    echo \"[oracle] Staged SQL*Plus install failed version validation.\" >&2");
            builder.AppendLine("    exit 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  rm -rf \"${oracle_sqlplus_root}\"");
            builder.AppendLine("  mkdir -p \"${oracle_sqlplus_root}\"");
            builder.AppendLine("  cp -a \"${oracle_sqlplus_candidate}\" \"${oracle_sqlplus_root}/\"");
            builder.AppendLine("  ln -sfn \"${oracle_sqlplus_root}/$(basename \"${oracle_sqlplus_candidate}\")\" \"${oracle_sqlplus_root}/current\"");
            builder.AppendLine("  if ! activate_sqlplus_install \"${oracle_sqlplus_root}/current\" || ! sqlplus -v; then");
            builder.AppendLine("    echo \"[oracle] Reinstalled SQL*Plus failed version validation after activation.\" >&2");
            builder.AppendLine("    exit 1");
            builder.AppendLine("  fi");
            builder.AppendLine("fi");
            builder.AppendLine("oracle_client_home=$(find \"${oracle_sqlplus_root}\" -maxdepth 2 -type f -name 'libsqlplus.so' -printf '%h\n' 2>/dev/null | while read -r dir; do if ls \"$dir\"/libclntsh.so* >/dev/null 2>&1; then printf '%s\n' \"$dir\"; break; fi; done)");
            builder.AppendLine("if [ -n \"${oracle_client_home}\" ] && [ -d \"${oracle_client_home}\" ]; then");
            builder.AppendLine("  printf '%s\n' \"${oracle_client_home}\" > /etc/ld.so.conf.d/oracle-instantclient.conf");
            builder.AppendLine("  ldconfig");
            builder.AppendLine("  export ORACLE_CLIENT_HOME=${oracle_client_home}");
            builder.AppendLine("  export LD_LIBRARY_PATH=${oracle_client_home}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}");
            builder.AppendLine("  export PATH=${oracle_client_home}:${PATH}");
            builder.AppendLine("fi");
            builder.AppendLine("for attempt in 1 2 3 4 5; do");
            builder.AppendLine("  if sqlplus -S \"${oracle_connection}\" @\"${oracle_sqlplus_probe_script}\" > /tmp/sqlplus-probe.out 2>&1 && grep -Fq 'Connection OK' /tmp/sqlplus-probe.out; then");
            builder.AppendLine("    cat /tmp/sqlplus-probe.out");
            builder.AppendLine("    break");
            builder.AppendLine("  fi");
            builder.AppendLine("  if [ \"${attempt}\" -eq 5 ]; then");
            builder.AppendLine("    cat /tmp/sqlplus-probe.out >&2 || true");
            builder.AppendLine("    echo \"[oracle] SQL*Plus validation query failed.\" >&2");
            builder.AppendLine("    exit 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  echo \"[oracle] SQL*Plus connectivity probe failed on attempt ${attempt}/5. Waiting for Oracle service...\" >&2");
            builder.AppendLine("  sleep 10");
            builder.AppendLine("done");
            builder.AppendLine("validate_sqlcl_install() {");
            builder.AppendLine("  local sqlcl_root=\"$1\"");
            builder.AppendLine("  local sqlcl_launcher=\"${sqlcl_root}/sqlcl/bin/sql\"");
            builder.AppendLine("  if [ ! -f \"${sqlcl_launcher}\" ]; then");
            builder.AppendLine("    return 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  chmod +x \"${sqlcl_launcher}\"");
            builder.AppendLine("  ln -sf \"${sqlcl_launcher}\" /usr/local/bin/sql");
            builder.AppendLine("  if ! \"${sqlcl_launcher}\" -v; then");
            builder.AppendLine("    return 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  if ! sql -v; then");
            builder.AppendLine("    return 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  for attempt in 1 2 3 4 5; do");
            builder.AppendLine("    if sql -S \"${oracle_connection}\" @\"${oracle_probe_script}\" > /tmp/sqlcl-probe.out 2>&1 && grep -Fq 'Connection OK' /tmp/sqlcl-probe.out; then");
            builder.AppendLine("      cat /tmp/sqlcl-probe.out");
            builder.AppendLine("      return 0");
            builder.AppendLine("    fi");
            builder.AppendLine("    echo \"[oracle] SQLcl connectivity probe failed on attempt ${attempt}/5. Waiting for Oracle service...\" >&2");
            builder.AppendLine("    sleep 10");
            builder.AppendLine("  done");
            builder.AppendLine("  cat /tmp/sqlcl-probe.out >&2 || true");
            builder.AppendLine("  return 1");
            builder.AppendLine("}");
            builder.AppendLine("if validate_sqlcl_install /opt/sqlcl; then");
            builder.AppendLine("  echo \"[oracle] SQLcl already installed and valid; skipping reinstall.\"");
            builder.AppendLine("else");
            builder.AppendLine("  echo \"[oracle] Existing SQLcl install missing or invalid. Reinstalling.\" >&2");
            builder.AppendLine("  oracle_sqlcl_download=/tmp/sqlcl.zip");
            builder.AppendLine("  oracle_sqlcl_extract=/tmp/sqlcl-extract");
            builder.AppendLine("  rm -rf \"${oracle_sqlcl_extract}\"");
            builder.AppendLine("  mkdir -p \"${oracle_sqlcl_extract}\"");
            builder.AppendLine("  if ! curl -fsSL https://download.oracle.com/otn_software/java/sqldeveloper/sqlcl-latest.zip -o \"${oracle_sqlcl_download}\"; then");
            builder.AppendLine("    echo \"[oracle] Failed to download the official SQLcl zip archive.\" >&2");
            builder.AppendLine("    exit 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  if ! unzip -oq \"${oracle_sqlcl_download}\" -d \"${oracle_sqlcl_extract}\"; then");
            builder.AppendLine("    echo \"[oracle] Failed to extract the SQLcl archive into the staging directory.\" >&2");
            builder.AppendLine("    exit 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  if [ ! -f \"${oracle_sqlcl_extract}/sqlcl/bin/sql\" ]; then");
            builder.AppendLine("    echo \"[oracle] Unexpected SQLcl layout: staged sqlcl/bin/sql was not created.\" >&2");
            builder.AppendLine("    exit 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  if [ ! -f \"${oracle_sqlcl_extract}/sqlcl/lib/dbtools-sqlcl.jar\" ]; then");
            builder.AppendLine("    echo \"[oracle] Unexpected SQLcl layout: staged dbtools-sqlcl.jar is missing.\" >&2");
            builder.AppendLine("    exit 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  if ! unzip -l \"${oracle_sqlcl_extract}/sqlcl/lib/dbtools-sqlcl.jar\" | grep -q 'oracle/dbtools/raptor/scriptrunner/cmdline/SqlCli.class'; then");
            builder.AppendLine("    echo \"[oracle] Diagnostic only: SqlCli.class was not found in staged dbtools-sqlcl.jar. Continuing with runtime validation.\" >&2");
            builder.AppendLine("  fi");
            builder.AppendLine("  if ! validate_sqlcl_install \"${oracle_sqlcl_extract}\"; then");
            builder.AppendLine("    echo \"[oracle] Staged SQLcl install failed runtime validation.\" >&2");
            builder.AppendLine("    exit 1");
            builder.AppendLine("  fi");
            builder.AppendLine("  rm -rf /opt/sqlcl");
            builder.AppendLine("  mkdir -p /opt/sqlcl");
            builder.AppendLine("  cp -a \"${oracle_sqlcl_extract}/.\" /opt/sqlcl/");
            builder.AppendLine("  ln -sf /opt/sqlcl/sqlcl/bin/sql /usr/local/bin/sql");
            builder.AppendLine("  if ! validate_sqlcl_install /opt/sqlcl; then");
            builder.AppendLine("    echo \"[oracle] Reinstalled SQLcl failed runtime validation after activation.\" >&2");
            builder.AppendLine("    exit 1");
            builder.AppendLine("  fi");
            builder.AppendLine("fi");
            if (hasOracleApex)
            {
                builder.AppendLine("echo \"[oracle-apex] Stage: Installing ORDS\"");
                builder.AppendLine("echo \"[oracle-apex] Stage: Installing APEX\"");
                builder.AppendLine("echo \"[oracle-apex] Stage: Configuring Workspace\"");
                builder.AppendLine("echo \"[oracle-apex] Stage: Creating Sample Application\"");
                builder.AppendLine("echo \"[oracle-apex] Stage: Running Validation\"");
                builder.AppendLine($"oracle_apex_media_dir={OracleWorkspaceSettings.ApexDownloadsDirectory}");
                builder.AppendLine($"oracle_apex_media_preferred={OracleWorkspaceSettings.ApexPreferredZipName}");
                builder.AppendLine("oracle_apex_extract_root=/tmp/oracle-apex-install");
                builder.AppendLine("oracle_apex_extract_dir=${oracle_apex_extract_root}/apex");
                builder.AppendLine("oracle_apex_admin_email=admin@example.local");
                builder.AppendLine("oracle_apex_admin_password=${ORACLE_PASSWORD}");
                builder.AppendLine("find_apex_media() {");
                builder.AppendLine("  if [ -f \"${oracle_apex_media_dir}/${oracle_apex_media_preferred}\" ]; then");
                builder.AppendLine("    printf '%s\n' \"${oracle_apex_media_dir}/${oracle_apex_media_preferred}\"");
                builder.AppendLine("    return 0");
                builder.AppendLine("  fi");
                builder.AppendLine("  for candidate in \"${oracle_apex_media_dir}\"/apex_*.zip \"${oracle_apex_media_dir}\"/apex*.zip; do");
                builder.AppendLine("    if [ -f \"${candidate}\" ]; then");
                builder.AppendLine("      printf '%s\n' \"${candidate}\"");
                builder.AppendLine("      return 0");
                builder.AppendLine("    fi");
                builder.AppendLine("  done");
                builder.AppendLine("  return 1");
                builder.AppendLine("}");
                builder.AppendLine("query_apex_registry() {");
                builder.AppendLine("  sqlplus -S \"sys/change-on-first-demo@//oracle-demo:1521/FREEPDB1 as sysdba\" <<'SQL'");
                builder.AppendLine("SET PAGESIZE 0");
                builder.AppendLine("SET FEEDBACK OFF");
                builder.AppendLine("SET HEADING OFF");
                builder.AppendLine("SET VERIFY OFF");
                builder.AppendLine("SET TRIMSPOOL ON");
                builder.AppendLine("SELECT comp_id || '|' || version || '|' || status FROM dba_registry WHERE comp_id = 'APEX';");
                builder.AppendLine("EXIT");
                builder.AppendLine("SQL");
                builder.AppendLine("}");
                builder.AppendLine("query_apex_version() {");
                builder.AppendLine("  sqlplus -S \"sys/change-on-first-demo@//oracle-demo:1521/FREEPDB1 as sysdba\" <<'SQL'");
                builder.AppendLine("SET PAGESIZE 0");
                builder.AppendLine("SET FEEDBACK OFF");
                builder.AppendLine("SET HEADING OFF");
                builder.AppendLine("SET VERIFY OFF");
                builder.AppendLine("SET TRIMSPOOL ON");
                builder.AppendLine("SELECT version_no FROM apex_release;");
                builder.AppendLine("EXIT");
                builder.AppendLine("SQL");
                builder.AppendLine("}");
                builder.AppendLine("query_apex_schema_count() {");
                builder.AppendLine("  sqlplus -S \"sys/change-on-first-demo@//oracle-demo:1521/FREEPDB1 as sysdba\" <<'SQL'");
                builder.AppendLine("SET PAGESIZE 0");
                builder.AppendLine("SET FEEDBACK OFF");
                builder.AppendLine("SET HEADING OFF");
                builder.AppendLine("SET VERIFY OFF");
                builder.AppendLine("SET TRIMSPOOL ON");
                builder.AppendLine("SELECT COUNT(*) FROM dba_users WHERE username LIKE 'APEX\\_%' ESCAPE '\\';");
                builder.AppendLine("EXIT");
                builder.AppendLine("SQL");
                builder.AppendLine("}");
                builder.AppendLine("query_database_open_mode() {");
                builder.AppendLine("  sqlplus -S \"sys/change-on-first-demo@//oracle-demo:1521/FREEPDB1 as sysdba\" <<'SQL'");
                builder.AppendLine("SET PAGESIZE 0");
                builder.AppendLine("SET FEEDBACK OFF");
                builder.AppendLine("SET HEADING OFF");
                builder.AppendLine("SET VERIFY OFF");
                builder.AppendLine("SET TRIMSPOOL ON");
                builder.AppendLine("SELECT open_mode FROM v$database;");
                builder.AppendLine("EXIT");
                builder.AppendLine("SQL");
                builder.AppendLine("}");
                builder.AppendLine("query_pdb_open_mode() {");
                builder.AppendLine("  sqlplus -S \"sys/change-on-first-demo@//oracle-demo:1521/FREEPDB1 as sysdba\" <<'SQL'");
                builder.AppendLine("SET PAGESIZE 0");
                builder.AppendLine("SET FEEDBACK OFF");
                builder.AppendLine("SET HEADING OFF");
                builder.AppendLine("SET VERIFY OFF");
                builder.AppendLine("SET TRIMSPOOL ON");
                builder.AppendLine("SELECT open_mode FROM v$pdbs WHERE name = 'FREEPDB1';");
                builder.AppendLine("EXIT");
                builder.AppendLine("SQL");
                builder.AppendLine("}");
                builder.AppendLine("query_xdb_status() {");
                builder.AppendLine("  sqlplus -S \"sys/change-on-first-demo@//oracle-demo:1521/FREEPDB1 as sysdba\" <<'SQL'");
                builder.AppendLine("SET PAGESIZE 0");
                builder.AppendLine("SET FEEDBACK OFF");
                builder.AppendLine("SET HEADING OFF");
                builder.AppendLine("SET VERIFY OFF");
                builder.AppendLine("SET TRIMSPOOL ON");
                builder.AppendLine("SELECT status FROM dba_registry WHERE comp_id = 'XDB';");
                builder.AppendLine("EXIT");
                builder.AppendLine("SQL");
                builder.AppendLine("}");
                builder.AppendLine("recompile_invalid_oracle_components() {");
                builder.AppendLine("  sqlplus -S \"sys/change-on-first-demo@//oracle-demo:1521/FREEPDB1 as sysdba\" <<'SQL'");
                builder.AppendLine("ALTER SESSION SET CONTAINER = CDB$ROOT;");
                builder.AppendLine("BEGIN");
                builder.AppendLine("  utl_recomp.recomp_parallel(0);");
                builder.AppendLine("END;");
                builder.AppendLine("/");
                builder.AppendLine("EXECUTE dbms_registry_sys.validate_components;");
                builder.AppendLine("ALTER SESSION SET CONTAINER = FREEPDB1;");
                builder.AppendLine("BEGIN");
                builder.AppendLine("  utl_recomp.recomp_parallel(0);");
                builder.AppendLine("END;");
                builder.AppendLine("/");
                builder.AppendLine("EXECUTE dbms_registry_sys.validate_components;");
                builder.AppendLine("EXIT");
                builder.AppendLine("SQL");
                builder.AppendLine("}");
                builder.AppendLine("oracle_set_stage() {");
                builder.AppendLine("  oracle_stage=\"$1\"");
                builder.AppendLine("  echo \"[oracle-apex] Stage: ${oracle_stage}\"");
                builder.AppendLine("}");
                builder.AppendLine("oracle_fail() {");
                builder.AppendLine("  local reason=\"$1\"");
                builder.AppendLine("  local evidence=\"$2\"");
                builder.AppendLine("  local recommendation=\"$3\"");
                builder.AppendLine("  local confidence=\"${4:-high}\"");
                builder.AppendLine("  echo \"Workspace provisioning stopped.\" >&2");
                builder.AppendLine("  echo \"Stage: ${oracle_stage}\" >&2");
                builder.AppendLine("  echo \"Reason: ${reason}\" >&2");
                builder.AppendLine("  if [ -n \"${evidence}\" ]; then echo \"Evidence: ${evidence}\" >&2; fi");
                builder.AppendLine("  if [ -n \"${recommendation}\" ]; then echo \"Recommended action: ${recommendation}\" >&2; fi");
                builder.AppendLine("  echo \"Confidence: ${confidence}\" >&2");
                builder.AppendLine("  exit 1");
                builder.AppendLine("}");
                builder.AppendLine("validate_oracle_environment() {");
                builder.AppendLine("  local workspace_available_kb sysdba_probe");
                builder.AppendLine("  workspace_available_kb=$(df -Pk /workspace | awk 'NR==2 {print $4}')");
                builder.AppendLine("  if [ -z \"${workspace_available_kb}\" ] || [ \"${workspace_available_kb}\" -lt 1048576 ]; then");
                builder.AppendLine("    oracle_fail \"Workspace disk space is too low for Oracle provisioning.\" \"Available KB = ${workspace_available_kb:-unknown}\" \"Free disk space in the workspace volume and try Recover Workspace again.\" high");
                builder.AppendLine("  fi");
                builder.AppendLine("  if ! getent hosts oracle-demo >/dev/null 2>&1; then");
                builder.AppendLine("    oracle_fail \"Oracle database host is not reachable from the workspace container.\" \"Host lookup for oracle-demo failed\" \"Start the Oracle database container or repair Docker networking before retrying.\" high");
                builder.AppendLine("  fi");
                builder.AppendLine("  sysdba_probe=$(sqlplus -S \"sys/change-on-first-demo@//oracle-demo:1521/FREEPDB1 as sysdba\" <<'SQL' 2>&1 || true");
                builder.AppendLine("SET PAGESIZE 0");
                builder.AppendLine("SET FEEDBACK OFF");
                builder.AppendLine("SET HEADING OFF");
                builder.AppendLine("SET VERIFY OFF");
                builder.AppendLine("SELECT 'SYSDBA OK' FROM dual;");
                builder.AppendLine("EXIT");
                builder.AppendLine("SQL");
                builder.AppendLine("  )");
                builder.AppendLine("  if ! printf '%s' \"${sysdba_probe}\" | grep -Fq 'SYSDBA OK'; then");
                builder.AppendLine("    oracle_fail \"SYSDBA connection to Oracle failed.\" \"${sysdba_probe}\" \"Recreate the Oracle database container or restore a clean Oracle data volume before retrying.\" high");
                builder.AppendLine("  fi");
                builder.AppendLine("}");
                builder.AppendLine("validate_oracle_prerequisites() {");
                builder.AppendLine("  local database_open_mode pdb_open_mode xdb_status");
                builder.AppendLine("  database_open_mode=$(query_database_open_mode | tr -d '\r' | xargs || true)");
                builder.AppendLine("  if [ \"${database_open_mode}\" != 'READ WRITE' ]; then");
                builder.AppendLine("    oracle_fail \"Oracle database is not open for writes.\" \"open_mode = ${database_open_mode:-missing}\" \"Wait for the Oracle database to finish opening or recreate the Oracle data volume if it remains stuck.\" high");
                builder.AppendLine("  fi");
                builder.AppendLine("  pdb_open_mode=$(query_pdb_open_mode | tr -d '\r' | xargs || true)");
                builder.AppendLine("  if [ \"${pdb_open_mode}\" != 'READ WRITE' ]; then");
                builder.AppendLine("    oracle_fail \"Required pluggable database FREEPDB1 is not open.\" \"FREEPDB1 open_mode = ${pdb_open_mode:-missing}\" \"Open FREEPDB1 or recreate the Oracle database container before retrying.\" high");
                builder.AppendLine("  fi");
                builder.AppendLine("  xdb_status=$(query_xdb_status | tr -d '\r' | xargs || true)");
                builder.AppendLine("  if [ \"${xdb_status}\" != 'VALID' ]; then");
                builder.AppendLine("    echo \"[oracle] XDB status is ${xdb_status:-missing}. Attempting Oracle component recompile...\" >&2");
                builder.AppendLine("    recompile_invalid_oracle_components >/tmp/oracle-utlrp.out 2>&1 || true");
                builder.AppendLine("    cat /tmp/oracle-utlrp.out >&2 || true");
                builder.AppendLine("    xdb_status=$(query_xdb_status | tr -d '\r' | xargs || true)");
                builder.AppendLine("  fi");
                builder.AppendLine("  if [ \"${xdb_status}\" != 'VALID' ]; then");
                builder.AppendLine("    oracle_fail \"Oracle XML Database (XDB) is invalid.\" \"XDB status = ${xdb_status:-missing}\" \"Recreate the Oracle database container or restore a clean Oracle data volume.\" high");
                builder.AppendLine("  fi");
                builder.AppendLine("}");
                builder.AppendLine("configure_apex_cdn() {");
                builder.AppendLine("  sqlplus -S \"sys/change-on-first-demo@//oracle-demo:1521/FREEPDB1 as sysdba\" <<'SQL'");
                builder.AppendLine("BEGIN");
                builder.AppendLine("  FOR c1 IN (SELECT version_no FROM apex_release) LOOP");
                builder.AppendLine("    APEX_INSTANCE_ADMIN.set_parameter(");
                builder.AppendLine("      p_parameter => 'IMAGE_PREFIX',");
                builder.AppendLine("      p_value => 'https://static.oracle.com/cdn/apex/' || c1.version_no || '/');");
                builder.AppendLine("  END LOOP;");
                builder.AppendLine("  COMMIT;");
                builder.AppendLine("END;");
                builder.AppendLine("/");
                builder.AppendLine("EXIT");
                builder.AppendLine("SQL");
                builder.AppendLine("}");
                builder.AppendLine("install_apex_media() {");
                builder.AppendLine("  local apex_zip apex_registry apex_schema_count apex_version");
                builder.AppendLine("  apex_registry=$(query_apex_registry | tr -d '\r' | xargs || true)");
                builder.AppendLine("  apex_schema_count=$(query_apex_schema_count | tr -d '\r' | xargs || true)");
                builder.AppendLine("  if [ -n \"${apex_registry}\" ] && printf '%s' \"${apex_registry}\" | grep -Fq '|VALID'; then");
                builder.AppendLine("    echo \"[oracle-apex] APEX already installed: ${apex_registry}\"");
                builder.AppendLine("    configure_apex_cdn");
                builder.AppendLine("    return 0");
                builder.AppendLine("  fi");
                builder.AppendLine("  mkdir -p \"${oracle_apex_media_dir}\"");
                builder.AppendLine("  if ! apex_zip=$(find_apex_media); then");
                builder.AppendLine("    echo \"OracleRuntimeFailure: APEX installation media missing\" >&2");
                builder.AppendLine("    echo \"Expected path: ${oracle_apex_media_dir}/${oracle_apex_media_preferred}\" >&2");
                builder.AppendLine("    echo \"Supported filename patterns: ${oracle_apex_media_dir}/apex.zip, ${oracle_apex_media_dir}/apex_*.zip, ${oracle_apex_media_dir}/apex*.zip\" >&2");
                builder.AppendLine("    echo \"Download Oracle APEX ZIP from https://www.oracle.com/tools/downloads/apex-downloads.html and place it under ${oracle_apex_media_dir}.\" >&2");
                builder.AppendLine("    echo \"This repository does not include Oracle APEX media.\" >&2");
                builder.AppendLine("    exit 1");
                builder.AppendLine("  fi");
                builder.AppendLine("  echo \"[oracle-apex] Using APEX media: ${apex_zip}\"");
                builder.AppendLine("  rm -rf \"${oracle_apex_extract_root}\"");
                builder.AppendLine("  mkdir -p \"${oracle_apex_extract_root}\"");
                builder.AppendLine("  unzip -oq \"${apex_zip}\" -d \"${oracle_apex_extract_root}\"");
                builder.AppendLine("  if [ ! -f \"${oracle_apex_extract_dir}/apexins.sql\" ]; then");
                builder.AppendLine("    echo \"OracleRuntimeFailure: APEX installation media missing apexins.sql after extraction\" >&2");
                builder.AppendLine("    exit 1");
                builder.AppendLine("  fi");
                builder.AppendLine("  (cd \"${oracle_apex_extract_dir}\" && sqlplus -S \"sys/change-on-first-demo@//oracle-demo:1521/FREEPDB1 as sysdba\" <<'SQL'");
                builder.AppendLine("@apexins.sql SYSAUX SYSAUX TEMP /i/");
                builder.AppendLine("EXIT");
                builder.AppendLine("SQL");
                builder.AppendLine("  )");
                builder.AppendLine("  (cd \"${oracle_apex_extract_dir}\" && sqlplus -S \"sys/change-on-first-demo@//oracle-demo:1521/FREEPDB1 as sysdba\" <<SQL");
                builder.AppendLine("@apex_rest_config.sql");
                builder.AppendLine("${oracle_apex_admin_password}");
                builder.AppendLine("${oracle_apex_admin_password}");
                builder.AppendLine("EXIT");
                builder.AppendLine("SQL");
                builder.AppendLine("  )");
                builder.AppendLine("  (cd \"${oracle_apex_extract_dir}\" && sqlplus -S \"sys/change-on-first-demo@//oracle-demo:1521/FREEPDB1 as sysdba\" <<SQL");
                builder.AppendLine("@apxchpwd.sql");
                builder.AppendLine("ADMIN");
                builder.AppendLine("${oracle_apex_admin_password}");
                builder.AppendLine("${oracle_apex_admin_password}");
                builder.AppendLine("${oracle_apex_admin_email}");
                builder.AppendLine("EXIT");
                builder.AppendLine("SQL");
                builder.AppendLine("  )");
                builder.AppendLine("  configure_apex_cdn");
                builder.AppendLine("  sqlplus -S \"sys/change-on-first-demo@//oracle-demo:1521/FREEPDB1 as sysdba\" <<'SQL'");
                builder.AppendLine("BEGIN");
                builder.AppendLine("  sys.validate_apex;");
                builder.AppendLine("END;");
                builder.AppendLine("/");
                builder.AppendLine("EXIT");
                builder.AppendLine("SQL");
                builder.AppendLine("  apex_registry=$(query_apex_registry | tr -d '\r' | xargs || true)");
                builder.AppendLine("  apex_schema_count=$(query_apex_schema_count | tr -d '\r' | xargs || true)");
                builder.AppendLine("  apex_version=$(query_apex_version | tr -d '\r' | xargs || true)");
                builder.AppendLine("  if [ -z \"${apex_registry}\" ]; then");
                builder.AppendLine("    echo \"OracleRuntimeFailure: APEX not installed in database\" >&2");
                builder.AppendLine("    exit 1");
                builder.AppendLine("  fi");
                builder.AppendLine("  if ! printf '%s' \"${apex_registry}\" | grep -Fq '|VALID'; then");
                builder.AppendLine("    echo \"OracleRuntimeFailure: APEX registry invalid\" >&2");
                builder.AppendLine("    echo \"Registry state: ${apex_registry}\" >&2");
                builder.AppendLine("    exit 1");
                builder.AppendLine("  fi");
                builder.AppendLine("  if [ -z \"${apex_schema_count}\" ] || [ \"${apex_schema_count}\" = \"0\" ]; then");
                builder.AppendLine("    echo \"OracleRuntimeFailure: APEX not installed in database\" >&2");
                builder.AppendLine("    exit 1");
                builder.AppendLine("  fi");
                builder.AppendLine("  echo \"[oracle-apex] APEX installed: ${apex_registry}\"");
                builder.AppendLine("  echo \"[oracle-apex] APEX version: ${apex_version}\"");
                builder.AppendLine("}");
                builder.AppendLine("wait_for_ords_runtime() {");
                builder.AppendLine($"oracle_ords_url=http://oracle-ords:{OracleWorkspaceSettings.ContainerOrdsPort}/ords");
                builder.AppendLine($"oracle_apex_url=http://oracle-ords:{OracleWorkspaceSettings.ContainerOrdsPort}/ords/apex_admin");
                builder.AppendLine($"oracle_ords_landing_url=http://oracle-ords:{OracleWorkspaceSettings.ContainerOrdsPort}/ords/");
                builder.AppendLine("for attempt in 1 2 3 4 5 6; do");
                builder.AppendLine("  if curl -fsSL \"${oracle_ords_url}\" >/dev/null 2>&1; then");
                builder.AppendLine("    break");
                builder.AppendLine("  fi");
                builder.AppendLine("  if [ \"${attempt}\" -eq 6 ]; then");
                builder.AppendLine("    oracle_fail \"Oracle REST Data Services (ORDS) did not become reachable.\" \"ORDS endpoint ${oracle_ords_url} did not respond after 6 checks\" \"Check the oracle-ords container logs and recreate the ORDS container if configuration did not complete.\" medium");
                builder.AppendLine("  fi");
                builder.AppendLine("  echo \"[oracle] Waiting for ORDS endpoint ${attempt}/6...\" >&2");
                builder.AppendLine("  sleep 10");
                builder.AppendLine("done");
                builder.AppendLine("}");
                builder.AppendLine("verify_apex_login_route() {");
                builder.AppendLine("for attempt in 1 2 3 4 5 6; do");
                builder.AppendLine("  if curl -fsSL \"${oracle_apex_url}\" >/dev/null 2>&1; then");
                builder.AppendLine("    break");
                builder.AppendLine("  fi");
                builder.AppendLine("  if curl -fsSL \"${oracle_ords_landing_url}\" >/dev/null 2>&1; then");
                builder.AppendLine("    echo \"[oracle-apex] ORDS landing page is reachable after APEX validation; continuing without apex_admin route probe.\" >&2");
                builder.AppendLine("    break");
                builder.AppendLine("  fi");
                builder.AppendLine("  if [ \"${attempt}\" -eq 6 ]; then");
                builder.AppendLine("    oracle_fail \"Oracle APEX route is not reachable.\" \"APEX login URL ${oracle_apex_url} and ORDS landing URL ${oracle_ords_landing_url} did not respond after 6 checks\" \"Check ORDS configuration and confirm APEX installed successfully before retrying.\" medium");
                builder.AppendLine("  fi");
                builder.AppendLine("  echo \"[oracle] Waiting for APEX login page ${attempt}/6...\" >&2");
                builder.AppendLine("  sleep 10");
                builder.AppendLine("done");
                builder.AppendLine("}");
                builder.AppendLine("oracle_set_stage 'Validate environment'");
                builder.AppendLine("validate_oracle_environment");
                builder.AppendLine("oracle_set_stage 'Validate Oracle prerequisites'");
                builder.AppendLine("validate_oracle_prerequisites");
                builder.AppendLine("oracle_set_stage 'Install APEX'");
                builder.AppendLine("install_apex_media");
                builder.AppendLine("oracle_set_stage 'Configure ORDS'");
                builder.AppendLine("wait_for_ords_runtime");
                builder.AppendLine("oracle_set_stage 'Workspace configuration'");
                builder.AppendLine("configure_apex_cdn");
                builder.AppendLine("oracle_set_stage 'Final verification'");
                builder.AppendLine("verify_apex_login_route");
                builder.AppendLine("echo \"[oracle-apex] Stage: Ready\"");
            }

            builder.AppendLine("rm -f \"${oracle_probe_script}\" \"${oracle_sqlplus_probe_script}\" /tmp/sqlcl-probe.out /tmp/sqlplus-probe.out /tmp/sqlcl.zip /tmp/instantclient-basiclite.zip /tmp/instantclient-sqlplus.zip");
            builder.AppendLine("rm -rf /tmp/sqlcl-extract \"${oracle_sqlplus_stage}\"");
        }

        builder.AppendLine();
        builder.AppendLine("# Install OpenCode from the official npm package so the workspace stays close to upstream distribution.");
        builder.AppendLine("npm install -g opencode-ai");

        if (workspace.Definition.Terminal.InstallIfMissing && string.Equals(workspace.Definition.Terminal.Prompt.Provider, "starship", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine();
            builder.AppendLine("# Install Starship from the official installer when the workspace opts into the recommended prompt.");
            builder.AppendLine("curl -sS https://starship.rs/install.sh | sh -s -- -y");
        }

        if (workspace.NpmPackages.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("# Install additional npm dependencies declared by workspace features.");
            builder.AppendLine($"npm install -g {string.Join(" ", workspace.NpmPackages)}");
        }

        if (workspace.PipPackages.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("# Ubuntu 24.04 marks the system Python as externally managed, so disposable workspace runtimes must opt into global pip installs explicitly.");
            builder.AppendLine($"pip3 install --break-system-packages {string.Join(" ", workspace.PipPackages)}");
        }

        if (workspace.PostInstallCommands.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("# Run feature-specific post-install commands after base tooling is available.");
            foreach (var command in workspace.PostInstallCommands)
            {
                builder.AppendLine(command);
            }
        }

        builder.AppendLine();
        builder.AppendLine("# Add optional terminal utilities from official Ubuntu package repositories.");
        if (workspace.Definition.Terminal.InstallIfMissing && workspace.Definition.Terminal.Utilities.Zoxide)
        {
            builder.AppendLine("apt-get install -y zoxide");
        }

        if (workspace.Definition.Terminal.InstallIfMissing && workspace.Definition.Terminal.Utilities.Fzf)
        {
            builder.AppendLine("apt-get install -y fzf");
        }

        builder.AppendLine();
        builder.AppendLine("# Source the generated OpenCode shell initialization file through a clearly marked managed block.");
        builder.AppendLine("python3 - <<'PY'");
        builder.AppendLine("from pathlib import Path");
        builder.AppendLine($"start = {QuoteForPython(BashRcManagedStart)}");
        builder.AppendLine($"end = {QuoteForPython(BashRcManagedEnd)}");
        builder.AppendLine("block = start + '\\nsource /opt/opencode-workspace/config/opencode-shell-init.sh\\n' + end + '\\n'");
        builder.AppendLine("bashrc = Path('/home/opencode/.bashrc')");
        builder.AppendLine("text = bashrc.read_text() if bashrc.exists() else ''");
        builder.AppendLine("if start in text and end in text:");
        builder.AppendLine("    prefix = text.split(start, 1)[0]");
        builder.AppendLine("    suffix = text.split(end, 1)[1].lstrip('\\n')");
        builder.AppendLine("    bashrc.write_text(prefix + block + suffix)");
        builder.AppendLine("else:");
        builder.AppendLine("    if text and not text.endswith('\\n'):");
        builder.AppendLine("        text += '\\n'");
        builder.AppendLine("    bashrc.write_text(text + block)");
        builder.AppendLine("PY");

        builder.AppendLine();
        builder.AppendLine("# Verify the main tools that contributors expect to exist after provisioning.");
        builder.AppendLine("git --version");
        builder.AppendLine("python --version");
        builder.AppendLine("python3 --version");
        builder.AppendLine("which python");
        builder.AppendLine("which python3");
        builder.AppendLine("node --version");
        builder.AppendLine("node -e \"console.log(process.version)\"");
        builder.AppendLine("npm --version");
        builder.AppendLine("opencode --version");
        builder.AppendLine("su -s /bin/bash -c 'screen --version' opencode");

        if (workspace.Definition.Terminal.InstallIfMissing && string.Equals(workspace.Definition.Terminal.Prompt.Provider, "starship", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("starship --version");
        }

        if (workspace.Definition.Terminal.InstallIfMissing && workspace.Definition.Terminal.Utilities.Zoxide)
        {
            builder.AppendLine("zoxide --version");
        }

        if (workspace.Definition.Terminal.InstallIfMissing && workspace.Definition.Terminal.Utilities.Fzf)
        {
            builder.AppendLine("fzf --version");
        }

        return builder.ToString();
    }

    private static string QuoteForPython(string value)
        => "'" + value.Replace("'", "\\'", StringComparison.Ordinal) + "'";
}
