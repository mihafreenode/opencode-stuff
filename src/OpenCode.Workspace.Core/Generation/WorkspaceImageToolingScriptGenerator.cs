using System.Text;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

public sealed class WorkspaceImageToolingScriptGenerator
{
    public string Generate(ResolvedWorkspace workspace)
    {
        var builder = new StringBuilder();
        var oracleWorkspaceKind = OracleWorkspaceFamily.Detect(workspace.Definition);
        var isOracleWorkspace = oracleWorkspaceKind != OracleWorkspaceKind.None;
        var aptPackages = workspace.AptPackages
            .Where(packageName => !isOracleWorkspace || !string.Equals(packageName, "libaio1", StringComparison.OrdinalIgnoreCase))
            .ToList();

        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine("set -euo pipefail");
        builder.AppendLine("export DEBIAN_FRONTEND=noninteractive");
        builder.AppendLine();
        builder.AppendLine("apt-get update");
        if (aptPackages.Count > 0)
        {
            builder.AppendLine($"apt-get install -y {string.Join(" ", aptPackages)}");
        }

        builder.AppendLine($"echo \"[runtime] Requested Node.js major version: {workspace.Definition.Runtime.GetEffectiveNodeMajorVersion()}\"");
        builder.AppendLine("apt-get remove -y nodejs npm || true");
        builder.AppendLine($"curl -fsSL https://deb.nodesource.com/setup_{workspace.Definition.Runtime.GetEffectiveNodeMajorVersion()}.x | bash -");
        builder.AppendLine("apt-get install -y nodejs");

        if (isOracleWorkspace)
        {
            builder.AppendLine(". /etc/os-release && echo \"[oracle] Detected Ubuntu version: ${VERSION_ID:-unknown} (${ID:-unknown})\"");
            builder.AppendLine("if apt-cache policy libaio1 | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then oracle_libaio_pkg=libaio1; elif apt-cache policy libaio1t64 | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then oracle_libaio_pkg=libaio1t64; else echo \"[oracle] No compatible libaio package found for this Ubuntu image.\" >&2; exit 1; fi");
            builder.AppendLine("echo \"[oracle] Selected libaio package: ${oracle_libaio_pkg}\"");
            builder.AppendLine("apt-get install -y \"${oracle_libaio_pkg}\" libnsl2");
            builder.AppendLine("dpkg -L \"${oracle_libaio_pkg}\"");
            builder.AppendLine("if [ \"${oracle_libaio_pkg}\" = \"libaio1t64\" ] && [ -f /usr/lib/x86_64-linux-gnu/libaio.so.1t64 ] && [ ! -e /usr/lib/x86_64-linux-gnu/libaio.so.1 ]; then ln -sf /usr/lib/x86_64-linux-gnu/libaio.so.1t64 /usr/lib/x86_64-linux-gnu/libaio.so.1; fi");
            builder.AppendLine("ldconfig");
            builder.AppendLine("if apt-cache policy openjdk-21-jre-headless | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then oracle_java_pkg=openjdk-21-jre-headless; elif apt-cache policy openjdk-17-jre-headless | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then oracle_java_pkg=openjdk-17-jre-headless; else echo \"[oracle] No compatible Java runtime package found for SQLcl.\" >&2; exit 1; fi");
            builder.AppendLine("echo \"[oracle] Selected Java package: ${oracle_java_pkg}\"");
            builder.AppendLine("apt-get install -y \"${oracle_java_pkg}\"");
            builder.AppendLine("java -version");
            builder.AppendLine("oracle_sqlplus_stage=/tmp/oracle-instantclient-stage");
            builder.AppendLine("oracle_sqlplus_root=/opt/oracle/instantclient");
            builder.AppendLine("oracle_sqlplus_basic_url=https://download.oracle.com/otn_software/linux/instantclient/2390000/instantclient-basiclite-linux.x64-23.9.0.25.07.zip");
            builder.AppendLine("oracle_sqlplus_package_url=https://download.oracle.com/otn_software/linux/instantclient/2390000/instantclient-sqlplus-linux.x64-23.9.0.25.07.zip");
            builder.AppendLine("rm -rf \"${oracle_sqlplus_stage}\" && mkdir -p \"${oracle_sqlplus_stage}\"");
            builder.AppendLine("curl -fsSL \"${oracle_sqlplus_basic_url}\" -o /tmp/instantclient-basiclite.zip");
            builder.AppendLine("curl -fsSL \"${oracle_sqlplus_package_url}\" -o /tmp/instantclient-sqlplus.zip");
            builder.AppendLine("unzip -oq /tmp/instantclient-basiclite.zip -d \"${oracle_sqlplus_stage}\"");
            builder.AppendLine("unzip -oq /tmp/instantclient-sqlplus.zip -d \"${oracle_sqlplus_stage}\"");
            builder.AppendLine("oracle_sqlplus_candidate=$(find \"${oracle_sqlplus_stage}\" -maxdepth 1 -mindepth 1 -type d -name 'instantclient_*' | head -n 1)");
            builder.AppendLine("test -n \"${oracle_sqlplus_candidate}\"");
            builder.AppendLine("rm -rf \"${oracle_sqlplus_root}\" && mkdir -p \"${oracle_sqlplus_root}\"");
            builder.AppendLine("cp -a \"${oracle_sqlplus_candidate}\" \"${oracle_sqlplus_root}/\"");
            builder.AppendLine("ln -sfn \"${oracle_sqlplus_root}/$(basename \"${oracle_sqlplus_candidate}\")\" \"${oracle_sqlplus_root}/current\"");
            builder.AppendLine("ln -sf \"${oracle_sqlplus_root}/current/sqlplus\" /usr/local/bin/sqlplus");
            builder.AppendLine("oracle_client_home=$(find \"${oracle_sqlplus_root}/current\" -maxdepth 1 -type f -name 'libsqlplus.so' -printf '%h\\n' 2>/dev/null | head -n 1)");
            builder.AppendLine("if [ -n \"${oracle_client_home}\" ]; then printf '%s\\n' \"${oracle_client_home}\" > /etc/ld.so.conf.d/oracle-instantclient.conf; ldconfig; export ORACLE_CLIENT_HOME=${oracle_client_home}; fi");
            builder.AppendLine("oracle_sqlcl_download=/tmp/sqlcl.zip");
            builder.AppendLine("oracle_sqlcl_extract=/tmp/sqlcl-extract");
            builder.AppendLine("rm -rf \"${oracle_sqlcl_extract}\" /opt/sqlcl && mkdir -p \"${oracle_sqlcl_extract}\" /opt/sqlcl");
            builder.AppendLine("curl -fsSL https://download.oracle.com/otn_software/java/sqldeveloper/sqlcl-latest.zip -o \"${oracle_sqlcl_download}\"");
            builder.AppendLine("unzip -oq \"${oracle_sqlcl_download}\" -d \"${oracle_sqlcl_extract}\"");
            builder.AppendLine("cp -a \"${oracle_sqlcl_extract}/.\" /opt/sqlcl/");
            builder.AppendLine("ln -sf /opt/sqlcl/sqlcl/bin/sql /usr/local/bin/sql");
            builder.AppendLine("ln -sf /opt/sqlcl/sqlcl/bin/sql /usr/local/bin/sqlcl");
            builder.AppendLine("sqlplus -v");
            builder.AppendLine("sqlcl -v");
        }

        builder.AppendLine("npm install -g opencode-ai");
        if (workspace.Definition.Terminal.InstallIfMissing && string.Equals(workspace.Definition.Terminal.Prompt.Provider, "starship", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("curl -sS https://starship.rs/install.sh | sh -s -- -y");
        }

        if (workspace.NpmPackages.Count > 0)
        {
            builder.AppendLine($"npm install -g {string.Join(" ", workspace.NpmPackages)}");
        }

        if (workspace.PipPackages.Count > 0)
        {
            builder.AppendLine($"pip3 install --break-system-packages {string.Join(" ", workspace.PipPackages)}");
        }

        foreach (var command in workspace.PostInstallCommands)
        {
            builder.AppendLine(command);
        }

        if (workspace.Definition.Terminal.InstallIfMissing && workspace.Definition.Terminal.Utilities.Zoxide)
        {
            builder.AppendLine("apt-get install -y zoxide");
        }

        if (workspace.Definition.Terminal.InstallIfMissing && workspace.Definition.Terminal.Utilities.Fzf)
        {
            builder.AppendLine("apt-get install -y fzf");
        }

        builder.AppendLine("git --version");
        builder.AppendLine("python --version");
        builder.AppendLine("python3 --version");
        builder.AppendLine("node --version");
        builder.AppendLine("npm --version");
        builder.AppendLine("command -v opencode");
        builder.AppendLine("opencode --version");
        if (isOracleWorkspace)
        {
            builder.AppendLine("command -v sqlcl");
            builder.AppendLine("sql -v");
            builder.AppendLine("sqlcl -v");
        }

        return builder.ToString();
    }
}
