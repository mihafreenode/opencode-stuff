using System.Text;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Generation;

/// <summary>
/// Generates a provisioning script instead of baking custom images so the full
/// installation plan stays readable and reproducible from official Ubuntu images.
/// </summary>
public sealed class ProvisioningScriptGenerator
{
    private const string BashRcManagedStart = "# >>> OpenCode Workspace Manager managed block >>>";
    private const string BashRcManagedEnd = "# <<< OpenCode Workspace Manager managed block <<<";

    public string Generate(ResolvedWorkspace workspace)
    {
        var builder = new StringBuilder();
        // Ubuntu 24.04 renamed the old libaio1 package to libaio1t64, so Oracle-related
        // provisioning must not hardcode libaio1 in the generic apt package plan.
        var isOracleDemoWorkspace = workspace.Definition.Features.Contains("oracle-demo", StringComparer.OrdinalIgnoreCase)
            || workspace.Definition.Services.Contains("oracle-demo", StringComparer.OrdinalIgnoreCase);
        var aptPackages = workspace.AptPackages
            .Where(packageName => !isOracleDemoWorkspace || !string.Equals(packageName, "libaio1", StringComparison.OrdinalIgnoreCase))
            .ToList();

        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES");
        builder.AppendLine("# Source inputs: workspace.yaml and catalog manifests under catalog/.");
        builder.AppendLine("# User edits are not preserved. Edit workspace.yaml or catalog manifests instead.");
        builder.AppendLine("set -euo pipefail");
        builder.AppendLine();
        builder.AppendLine("export DEBIAN_FRONTEND=noninteractive");
        builder.AppendLine("export HOME=/home/opencode");
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

        if (isOracleDemoWorkspace)
        {
            builder.AppendLine();
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
            builder.AppendLine("# Install pip packages declared by workspace features.");
            builder.AppendLine($"pip3 install {string.Join(" ", workspace.PipPackages)}");
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
        builder.AppendLine("python3 --version");
        builder.AppendLine("node --version");
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
