using System.Text;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

public sealed class WorkspaceImageToolingLayoutBuilder
{
    public const string BaseImageCategory = "base-image";
    public const string BaseOsCategory = "base-os-packages";
    public const string CommonToolingCategory = "common-development-tooling";
    public const string OptionalToolingCategory = "optional-document-analytics-tooling";
    public const string OracleToolingCategory = "oracle-tooling";

    private static readonly string[] CategoryOrder =
    [
        BaseImageCategory,
        BaseOsCategory,
        CommonToolingCategory,
        OptionalToolingCategory,
        OracleToolingCategory,
    ];

    public WorkspaceImageToolingLayout Build(ResolvedWorkspace workspace)
    {
        var collectors = new Dictionary<string, ToolingCollector>(StringComparer.OrdinalIgnoreCase)
        {
            [BaseOsCategory] = new(GetDisplayName(BaseOsCategory)),
            [CommonToolingCategory] = new(GetDisplayName(CommonToolingCategory)),
            [OptionalToolingCategory] = new(GetDisplayName(OptionalToolingCategory)),
            [OracleToolingCategory] = new(GetDisplayName(OracleToolingCategory)),
        };

        var seenAptPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNpmPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPipPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenImageCommands = new HashSet<string>(StringComparer.Ordinal);
        var seenRuntimeCommands = new HashSet<string>(StringComparer.Ordinal);
        var runtimeInitializationCommands = new List<string>();

        if (workspace.Features.Count == 0)
        {
            PopulateFromFlattenedWorkspace(workspace, collectors, seenAptPackages, seenNpmPackages, seenPipPackages, seenImageCommands, seenRuntimeCommands, runtimeInitializationCommands);
        }
        else
        {
            foreach (var feature in workspace.Features.OrderBy(static feature => feature.Id, StringComparer.OrdinalIgnoreCase))
            {
                var category = ClassifyFeature(feature);
                var collector = collectors[category];

                foreach (var packageName in feature.Dependencies.Apt.OrderBy(static packageName => packageName, StringComparer.OrdinalIgnoreCase))
                {
                    if (seenAptPackages.Add(packageName))
                    {
                        collector.AptPackages.Add(packageName);
                    }
                }

                foreach (var packageName in feature.Dependencies.Npm.OrderBy(static packageName => packageName, StringComparer.OrdinalIgnoreCase))
                {
                    if (seenNpmPackages.Add(packageName))
                    {
                        collector.NpmPackages.Add(packageName);
                    }
                }

                foreach (var packageName in feature.Dependencies.Pip.OrderBy(static packageName => packageName, StringComparer.OrdinalIgnoreCase))
                {
                    if (seenPipPackages.Add(packageName))
                    {
                        collector.PipPackages.Add(packageName);
                    }
                }

                foreach (var command in feature.PostInstall)
                {
                    if (string.IsNullOrWhiteSpace(command))
                    {
                        continue;
                    }

                    if (TouchesWorkspacePath(command))
                    {
                        if (seenRuntimeCommands.Add(command))
                        {
                            runtimeInitializationCommands.Add(command);
                        }

                        continue;
                    }

                    if (seenImageCommands.Add(command))
                    {
                        collector.ImageCommands.Add(command);
                    }
                }
            }
        }

        var commonCollector = collectors[CommonToolingCategory];
        AddCommonTooling(workspace, commonCollector, seenAptPackages, seenNpmPackages);
        AddOracleTooling(workspace, collectors[OracleToolingCategory]);

        var layerScripts = CategoryOrder
            .Where(static category => !string.Equals(category, BaseImageCategory, StringComparison.OrdinalIgnoreCase))
            .Select(category => BuildLayerScript(category, collectors[category]))
            .Where(static layer => layer is not null)
            .Select(static layer => layer!)
            .ToList();

        return new WorkspaceImageToolingLayout
        {
            LayerScripts = layerScripts,
            RuntimeInitializationCommands = runtimeInitializationCommands,
            CombinedScript = BuildCombinedScript(layerScripts),
        };
    }

    private static void PopulateFromFlattenedWorkspace(
        ResolvedWorkspace workspace,
        IReadOnlyDictionary<string, ToolingCollector> collectors,
        ISet<string> seenAptPackages,
        ISet<string> seenNpmPackages,
        ISet<string> seenPipPackages,
        ISet<string> seenImageCommands,
        ISet<string> seenRuntimeCommands,
        ICollection<string> runtimeInitializationCommands)
    {
        var baseCollector = collectors[BaseOsCategory];
        foreach (var packageName in workspace.AptPackages)
        {
            if (OracleWorkspaceFamily.IsOracleWorkspace(workspace.Definition) && string.Equals(packageName, "libaio1", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seenAptPackages.Add(packageName))
            {
                baseCollector.AptPackages.Add(packageName);
            }
        }

        var commonCollector = collectors[CommonToolingCategory];
        foreach (var packageName in workspace.NpmPackages)
        {
            if (seenNpmPackages.Add(packageName))
            {
                commonCollector.NpmPackages.Add(packageName);
            }
        }

        foreach (var packageName in workspace.PipPackages)
        {
            if (seenPipPackages.Add(packageName))
            {
                commonCollector.PipPackages.Add(packageName);
            }
        }

        foreach (var command in workspace.PostInstallCommands)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            if (TouchesWorkspacePath(command))
            {
                if (seenRuntimeCommands.Add(command))
                {
                    runtimeInitializationCommands.Add(command);
                }

                continue;
            }

            if (seenImageCommands.Add(command))
            {
                commonCollector.ImageCommands.Add(command);
            }
        }
    }

    public static IReadOnlyList<string> GetOrderedCategories() => CategoryOrder;

    public static string GetDisplayName(string category)
        => category switch
        {
            BaseImageCategory => "base image",
            BaseOsCategory => "base OS packages",
            CommonToolingCategory => "common development tooling",
            OptionalToolingCategory => "optional document/analytics tooling",
            OracleToolingCategory => "Oracle tooling",
            _ => category,
        };

    private static string ClassifyFeature(FeatureManifest feature)
    {
        if (string.Equals(feature.Id, "core", StringComparison.OrdinalIgnoreCase))
        {
            return BaseOsCategory;
        }

        if (feature.Capabilities.Any(static capability => string.Equals(capability, "oracle", StringComparison.OrdinalIgnoreCase)))
        {
            return OracleToolingCategory;
        }

        if (feature.Capabilities.Any(capability => string.Equals(capability, "documentation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(capability, "document-processing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(capability, "analytics", StringComparison.OrdinalIgnoreCase)
            || string.Equals(capability, "reporting", StringComparison.OrdinalIgnoreCase)))
        {
            return OptionalToolingCategory;
        }

        return CommonToolingCategory;
    }

    private static void AddCommonTooling(ResolvedWorkspace workspace, ToolingCollector collector, ISet<string> seenAptPackages, ISet<string> seenNpmPackages)
    {
        if (workspace.Definition.Terminal.InstallIfMissing && workspace.Definition.Terminal.Utilities.Zoxide && seenAptPackages.Add("zoxide"))
        {
            collector.AptPackages.Add("zoxide");
        }

        if (workspace.Definition.Terminal.InstallIfMissing && workspace.Definition.Terminal.Utilities.Fzf && seenAptPackages.Add("fzf"))
        {
            collector.AptPackages.Add("fzf");
        }

        collector.BootstrapCommands.Add($"echo \"[runtime] Requested Node.js major version: {workspace.Definition.Runtime.GetEffectiveNodeMajorVersion()}\"");
        collector.BootstrapCommands.Add("apt-get remove -y nodejs npm || true");
        collector.BootstrapCommands.Add($"curl -fsSL https://deb.nodesource.com/setup_{workspace.Definition.Runtime.GetEffectiveNodeMajorVersion()}.x | bash -");
        collector.BootstrapCommands.Add("apt-get install -y nodejs");
        collector.NpmPackages.Add("opencode-ai");

        if (workspace.Definition.Terminal.InstallIfMissing && string.Equals(workspace.Definition.Terminal.Prompt.Provider, "starship", StringComparison.OrdinalIgnoreCase))
        {
            collector.ImageCommands.Add("curl -sS https://starship.rs/install.sh | sh -s -- -y");
        }
        collector.ValidationCommands.Add("git --version");
        collector.ValidationCommands.Add("python --version");
        collector.ValidationCommands.Add("python3 --version");
        collector.ValidationCommands.Add("node --version");
        collector.ValidationCommands.Add("npm --version");
        collector.ValidationCommands.Add("command -v opencode");
        collector.ValidationCommands.Add("opencode --version");
    }

    private static void AddOracleTooling(ResolvedWorkspace workspace, ToolingCollector collector)
    {
        if (!OracleWorkspaceFamily.IsOracleWorkspace(workspace.Definition))
        {
            return;
        }

        collector.ImageCommands.AddRange(
        [
            "oracle_log_phase() { echo \"[oracle] PHASE: $1\"; }",
            "oracle_fail() { echo \"[oracle] ERROR: $1\" >&2; exit 1; }",
            "oracle_require_file() { local path=$1; local description=$2; if [ ! -f \"${path}\" ]; then oracle_fail \"Missing ${description}: ${path}\"; fi; }",
            "oracle_require_dir() { local path=$1; local description=$2; if [ ! -d \"${path}\" ]; then oracle_fail \"Missing ${description}: ${path}\"; fi; }",
            "oracle_print_directory_listing() { local root=$1; local description=$2; echo \"[oracle] ${description}:\" >&2; find \"${root}\" -maxdepth 3 -mindepth 1 \\( -type d -o -type l \\) -print | sort >&2 || true; }",
            "oracle_resolve_client_home() { local root=$1; local libsqlplus_paths candidate_homes candidate_count resolved; libsqlplus_paths=$(find \"${root}\" -type f -name 'libsqlplus.so' -print | sort); if [ -z \"${libsqlplus_paths}\" ]; then echo \"[oracle] Searched for libsqlplus.so under ${root} but did not find any matches.\" >&2; oracle_print_directory_listing \"${root}\" 'Oracle Instant Client layout'; return 1; fi; echo \"[oracle] Located libsqlplus.so files:\" >&2; printf '%s\\n' \"${libsqlplus_paths}\" >&2; candidate_homes=$(printf '%s\\n' \"${libsqlplus_paths}\" | while read -r libsqlplus_path; do candidate_dir=$(dirname \"${libsqlplus_path}\"); if ls \"${candidate_dir}\"/libclntsh.so* >/dev/null 2>&1; then printf '%s\\n' \"${candidate_dir}\"; fi; done | sort -u); candidate_count=$(printf '%s\\n' \"${candidate_homes}\" | grep -c . || true); if [ \"${candidate_count}\" -eq 0 ]; then echo \"[oracle] Searched for libsqlplus.so under ${root} but did not find a usable Oracle client home.\" >&2; printf '%s\\n' \"${libsqlplus_paths}\" >&2; return 1; fi; if [ \"${candidate_count}\" -gt 1 ]; then echo \"[oracle] Multiple Oracle client homes were discovered under ${root}:\" >&2; printf '%s\\n' \"${candidate_homes}\" >&2; return 1; fi; resolved=$(printf '%s\\n' \"${candidate_homes}\" | head -n 1); echo \"[oracle] Discovered Oracle client home: ${resolved}\" >&2; echo \"[oracle] Using libsqlplus.so: ${resolved}/libsqlplus.so\" >&2; printf '%s\\n' \"${resolved}\"; }",
            "oracle_validate_sqlplus_runtime() { local client_home=$1; local sqlplus_binary=\"${client_home}/sqlplus\"; oracle_require_file \"${sqlplus_binary}\" 'sqlplus binary'; oracle_require_file \"${client_home}/libsqlplus.so\" 'libsqlplus.so'; if ldd \"${sqlplus_binary}\" | grep -F 'not found' >/dev/null 2>&1; then echo \"[oracle] sqlplus shared library diagnostics:\" >&2; ldd \"${sqlplus_binary}\" >&2 || true; echo \"[oracle] LD_LIBRARY_PATH during validation: ${LD_LIBRARY_PATH:-<empty>}\" >&2; oracle_fail \"sqlplus still has unresolved shared libraries\"; fi; if ! ldconfig -p | grep -F 'libsqlplus.so' >/dev/null 2>&1; then echo \"[oracle] ldconfig output does not list libsqlplus.so.\" >&2; cat /etc/ld.so.conf.d/oracle-instantclient.conf >&2 || true; ldconfig -p >&2 || true; oracle_fail \"ldconfig did not register libsqlplus.so\"; fi; }",
            "oracle_log_validation_context() { local client_home=$1; echo \"[oracle] Validation client home: ${client_home}\" >&2; echo \"[oracle] Validation LD_LIBRARY_PATH: ${LD_LIBRARY_PATH:-<empty>}\" >&2; echo \"[oracle] readlink -f /opt/oracle/instantclient/current\" >&2; readlink -f /opt/oracle/instantclient/current >&2 || true; echo \"[oracle] ls -la /opt/oracle/instantclient/current\" >&2; ls -la /opt/oracle/instantclient/current >&2 || true; echo \"[oracle] ls -la ${client_home}\" >&2; ls -la \"${client_home}\" >&2 || true; echo \"[oracle] ldd $(command -v sqlplus)\" >&2; ldd \"$(command -v sqlplus)\" >&2 || true; }",
            ". /etc/os-release && echo \"[oracle] Detected Ubuntu version: ${VERSION_ID:-unknown} (${ID:-unknown})\"",
            "oracle_log_phase 'Installing Oracle runtime libraries'",
            "if apt-cache policy libaio1 | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then oracle_libaio_pkg=libaio1; elif apt-cache policy libaio1t64 | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then oracle_libaio_pkg=libaio1t64; else echo \"[oracle] No compatible libaio package found for this Ubuntu image.\" >&2; exit 1; fi",
            "echo \"[oracle] Selected libaio package: ${oracle_libaio_pkg}\"",
            "apt-get install -y \"${oracle_libaio_pkg}\" libnsl2",
            "dpkg -L \"${oracle_libaio_pkg}\"",
            "if [ \"${oracle_libaio_pkg}\" = \"libaio1t64\" ] && [ -f /usr/lib/x86_64-linux-gnu/libaio.so.1t64 ] && [ ! -e /usr/lib/x86_64-linux-gnu/libaio.so.1 ]; then ln -sf /usr/lib/x86_64-linux-gnu/libaio.so.1t64 /usr/lib/x86_64-linux-gnu/libaio.so.1; fi",
            "ldconfig",
            "oracle_log_phase 'Installing Java'",
            "if apt-cache policy openjdk-21-jre-headless | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then oracle_java_pkg=openjdk-21-jre-headless; elif apt-cache policy openjdk-17-jre-headless | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then oracle_java_pkg=openjdk-17-jre-headless; else echo \"[oracle] No compatible Java runtime package found for SQLcl.\" >&2; exit 1; fi",
            "echo \"[oracle] Selected Java package: ${oracle_java_pkg}\"",
            "apt-get install -y \"${oracle_java_pkg}\"",
            "java -version",
            "oracle_sqlplus_stage=/tmp/oracle-instantclient-stage",
            "oracle_sqlplus_root=/opt/oracle/instantclient",
            "oracle_sqlplus_basic_url=https://download.oracle.com/otn_software/linux/instantclient/2390000/instantclient-basiclite-linux.x64-23.9.0.25.07.zip",
            "oracle_sqlplus_package_url=https://download.oracle.com/otn_software/linux/instantclient/2390000/instantclient-sqlplus-linux.x64-23.9.0.25.07.zip",
            "oracle_log_phase 'Downloading SQLPlus runtime libraries'",
            "rm -rf \"${oracle_sqlplus_stage}\" && mkdir -p \"${oracle_sqlplus_stage}\"",
            "curl -fsSL \"${oracle_sqlplus_basic_url}\" -o /tmp/instantclient-basiclite.zip",
            "curl -fsSL \"${oracle_sqlplus_package_url}\" -o /tmp/instantclient-sqlplus.zip",
            "oracle_require_file /tmp/instantclient-basiclite.zip 'Oracle Instant Client basic archive'",
            "oracle_require_file /tmp/instantclient-sqlplus.zip 'Oracle Instant Client sqlplus archive'",
            "oracle_log_phase 'Extracting SQLPlus runtime libraries'",
            "unzip -oq /tmp/instantclient-basiclite.zip -d \"${oracle_sqlplus_stage}\"",
            "unzip -oq /tmp/instantclient-sqlplus.zip -d \"${oracle_sqlplus_stage}\"",
            "if ! find \"${oracle_sqlplus_stage}\" -maxdepth 1 -mindepth 1 -type d -name 'instantclient_*' | grep -q .; then echo \"[oracle] Oracle Instant Client extraction did not produce an instantclient_* directory.\" >&2; find \"${oracle_sqlplus_stage}\" -maxdepth 3 -print >&2 || true; oracle_fail 'Oracle Instant Client extraction failed'; fi",
            "oracle_print_directory_listing \"${oracle_sqlplus_stage}\" 'Extracted Oracle Instant Client directories'",
            "oracle_log_phase 'Configuring SQLPlus runtime libraries'",
            "rm -rf \"${oracle_sqlplus_root}\" && mkdir -p \"${oracle_sqlplus_root}\"",
            "cp -a \"${oracle_sqlplus_stage}/.\" \"${oracle_sqlplus_root}/\"",
            "oracle_print_directory_listing \"${oracle_sqlplus_root}\" 'Installed Oracle Instant Client directories'",
            "oracle_client_home=$(oracle_resolve_client_home \"${oracle_sqlplus_root}\")",
            "ln -sfn \"${oracle_client_home}\" \"${oracle_sqlplus_root}/current\"",
            "oracle_require_dir \"${oracle_sqlplus_root}/current\" 'Oracle Instant Client current symlink target'",
            "echo \"[oracle] current symlink -> $(readlink -f \"${oracle_sqlplus_root}/current\")\"",
            "printf '%s\\n' \"${oracle_client_home}\" > /etc/ld.so.conf.d/oracle-instantclient.conf",
            "ldconfig",
            "export ORACLE_CLIENT_HOME=${oracle_client_home}",
            "cat > /usr/local/bin/opencode-oracle-client-home <<'EOF'\n#!/usr/bin/env bash\nset -euo pipefail\noracle_sqlplus_root=${ORACLE_SQLPLUS_ROOT:-/opt/oracle/instantclient}\noracle_current_link=\"${oracle_sqlplus_root}/current\"\nif [ ! -L \"${oracle_current_link}\" ] && [ ! -d \"${oracle_current_link}\" ]; then printf 'Oracle Instant Client current path is missing: %s\\n' \"${oracle_current_link}\" >&2; exit 1; fi\noracle_client_home=$(readlink -f \"${oracle_current_link}\")\nif [ -z \"${oracle_client_home}\" ] || [ ! -d \"${oracle_client_home}\" ]; then printf 'Oracle Instant Client current path did not resolve to a directory: %s\\n' \"${oracle_current_link}\" >&2; readlink -f \"${oracle_current_link}\" >&2 || true; ls -la \"${oracle_sqlplus_root}\" >&2 || true; exit 1; fi\nif [ ! -f \"${oracle_client_home}/libsqlplus.so\" ]; then printf 'Oracle Instant Client libsqlplus.so was not found under %s\\n' \"${oracle_client_home}\" >&2; ls -la \"${oracle_client_home}\" >&2 || true; exit 1; fi\nif ! ls \"${oracle_client_home}\"/libclntsh.so* >/dev/null 2>&1; then printf 'Oracle Instant Client libclntsh.so was not found under %s\\n' \"${oracle_client_home}\" >&2; ls -la \"${oracle_client_home}\" >&2 || true; exit 1; fi\nprintf '%s\\n' \"${oracle_client_home}\"\nEOF",
            "chmod +x /usr/local/bin/opencode-oracle-client-home",
            "oracle_validate_sqlplus_runtime \"${oracle_client_home}\"",
            "cat > /usr/local/bin/sqlplus <<'EOF'\n#!/usr/bin/env bash\nset -euo pipefail\noracle_client_home=$(/usr/local/bin/opencode-oracle-client-home)\nexport LD_LIBRARY_PATH=\"${oracle_client_home}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}\"\nexec \"${oracle_client_home}/sqlplus\" \"$@\"\nEOF",
            "chmod +x /usr/local/bin/sqlplus",
            "oracle_sqlcl_download=/tmp/sqlcl.zip",
            "oracle_sqlcl_extract=/tmp/sqlcl-extract",
            "oracle_log_phase 'Downloading SQLcl'",
            "rm -rf \"${oracle_sqlcl_extract}\" /opt/sqlcl && mkdir -p \"${oracle_sqlcl_extract}\" /opt/sqlcl",
            "curl -fsSL https://download.oracle.com/otn_software/java/sqldeveloper/sqlcl-latest.zip -o \"${oracle_sqlcl_download}\"",
            "oracle_require_file \"${oracle_sqlcl_download}\" 'SQLcl archive'",
            "oracle_log_phase 'Extracting SQLcl'",
            "unzip -oq \"${oracle_sqlcl_download}\" -d \"${oracle_sqlcl_extract}\"",
            "if [ ! -d \"${oracle_sqlcl_extract}/sqlcl\" ]; then echo \"[oracle] SQLcl extraction did not produce ${oracle_sqlcl_extract}/sqlcl.\" >&2; find \"${oracle_sqlcl_extract}\" -maxdepth 3 -print >&2 || true; oracle_fail 'SQLcl extraction failed'; fi",
            "oracle_log_phase 'Configuring SQLcl'",
            "cp -a \"${oracle_sqlcl_extract}/.\" /opt/sqlcl/",
            "oracle_require_file /opt/sqlcl/sqlcl/bin/sql 'SQLcl launcher'",
            "cat > /usr/local/bin/sql <<'EOF'\n#!/usr/bin/env bash\nset -euo pipefail\noracle_client_home=$(/usr/local/bin/opencode-oracle-client-home)\nexport LD_LIBRARY_PATH=\"${oracle_client_home}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}\"\nexec /opt/sqlcl/sqlcl/bin/sql \"$@\"\nEOF",
            "chmod +x /usr/local/bin/sql",
            "ln -sf /usr/local/bin/sql /usr/local/bin/sqlcl",
            "oracle_log_phase 'Validating SQLPlus'",
            "oracle_validate_sqlplus_runtime \"${oracle_client_home}\"",
            "oracle_log_validation_context \"${oracle_client_home}\"",
            "echo \"[oracle] Running validation command: sqlplus -version\" >&2",
            "sqlplus -version",
            "oracle_log_phase 'Validating SQLcl'",
            "echo \"[oracle] Running validation command: sql -version\" >&2",
            "sql -version",
            "echo \"[oracle] Running validation command: sqlcl -version\" >&2",
            "sqlcl -version",
        ]);

        collector.ValidationCommands.Add("command -v sqlplus");
        collector.ValidationCommands.Add("sqlplus -version");
        collector.ValidationCommands.Add("command -v sqlcl");
        collector.ValidationCommands.Add("sql -version");
        collector.ValidationCommands.Add("sqlcl -version");
    }

    private static WorkspaceImageLayerScript? BuildLayerScript(string category, ToolingCollector collector)
    {
        if (collector.IsEmpty)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine(string.Equals(category, OracleToolingCategory, StringComparison.OrdinalIgnoreCase)
            ? "set -Eeuo pipefail"
            : "set -euo pipefail");
        if (string.Equals(category, OracleToolingCategory, StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("trap 'echo \"[oracle] ERROR: command failed on line ${LINENO}: ${BASH_COMMAND}\" >&2' ERR");
        }
        builder.AppendLine("export DEBIAN_FRONTEND=noninteractive");
        builder.AppendLine($"echo \"[workspace-image] Installing {collector.DisplayName}.\"");

        if (collector.AptPackages.Count > 0 || collector.RequiresAptMetadataRefresh)
        {
            builder.AppendLine("apt-get update");
        }

        if (collector.AptPackages.Count > 0)
        {
            builder.AppendLine($"apt-get install -y {string.Join(" ", collector.AptPackages)}");
        }

        AppendCommands(builder, collector.BootstrapCommands);

        if (collector.NpmPackages.Count > 0)
        {
            builder.AppendLine($"npm install -g {string.Join(" ", collector.NpmPackages)}");
        }

        if (collector.PipPackages.Count > 0)
        {
            builder.AppendLine($"pip3 install --break-system-packages {string.Join(" ", collector.PipPackages)}");
        }

        AppendCommands(builder, collector.ImageCommands);
        AppendCommands(builder, collector.ValidationCommands);

        return new WorkspaceImageLayerScript
        {
            CategoryId = category,
            DisplayName = collector.DisplayName,
            RelativePath = $"mounts/config/workspace-image-tooling.{category}.sh",
            Content = builder.ToString(),
        };
    }

    private static string BuildCombinedScript(IReadOnlyList<WorkspaceImageLayerScript> layerScripts)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine("set -euo pipefail");
        builder.AppendLine("export DEBIAN_FRONTEND=noninteractive");

        foreach (var layerScript in layerScripts)
        {
            builder.AppendLine();
            builder.AppendLine($"# Layer: {layerScript.DisplayName}");
            var lines = layerScript.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            for (var index = 3; index < lines.Length; index++)
            {
                if (index == lines.Length - 1 && lines[index].Length == 0)
                {
                    continue;
                }

                builder.AppendLine(lines[index]);
            }
        }

        return builder.ToString();
    }

    private static void AppendCommands(StringBuilder builder, IEnumerable<string> commands)
    {
        foreach (var command in commands)
        {
            builder.AppendLine(command);
        }
    }

    private static bool TouchesWorkspacePath(string command)
        => command.Contains("/workspace", StringComparison.Ordinal);

    private sealed class ToolingCollector
    {
        public ToolingCollector(string displayName)
        {
            DisplayName = displayName;
        }

        public string DisplayName { get; }
        public List<string> AptPackages { get; } = [];
        public List<string> BootstrapCommands { get; } = [];
        public List<string> NpmPackages { get; } = [];
        public List<string> PipPackages { get; } = [];
        public List<string> ImageCommands { get; } = [];
        public List<string> ValidationCommands { get; } = [];

        public bool RequiresAptMetadataRefresh
            => AptPackages.Count > 0
               || BootstrapCommands.Any(static command => command.Contains("apt-", StringComparison.Ordinal))
               || ImageCommands.Any(static command => command.Contains("apt-", StringComparison.Ordinal));

        public bool IsEmpty
            => AptPackages.Count == 0
               && BootstrapCommands.Count == 0
               && NpmPackages.Count == 0
               && PipPackages.Count == 0
               && ImageCommands.Count == 0
               && ValidationCommands.Count == 0;
    }
}

public sealed class WorkspaceImageToolingLayout
{
    public required IReadOnlyList<WorkspaceImageLayerScript> LayerScripts { get; init; }
    public required IReadOnlyList<string> RuntimeInitializationCommands { get; init; }
    public required string CombinedScript { get; init; }
}
