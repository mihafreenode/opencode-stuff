using System.Text;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

public sealed class WorkspaceInitializationScriptGenerator
{
    private const string BashRcManagedStart = "# >>> OpenCode Workspace Manager managed block >>>";
    private const string BashRcManagedEnd = "# <<< OpenCode Workspace Manager managed block <<<";

    public string Generate(ResolvedWorkspace workspace)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine("set -euo pipefail");
        builder.AppendLine("export HOME=/home/opencode");
        builder.AppendLine("if ! id -u opencode >/dev/null 2>&1; then useradd -m -d /home/opencode -s /bin/bash opencode; fi");
        builder.AppendLine("mkdir -p \"$HOME\" /home/opencode/.local/share/opencode/log /home/opencode/.config/opencode /home/opencode/.cache/opencode");
        builder.AppendLine("chown -R opencode:opencode /home/opencode");
        builder.AppendLine("touch /home/opencode/.bashrc");
        builder.AppendLine("python3 - <<'PY'");
        builder.AppendLine($"start = {QuoteForPython(BashRcManagedStart)}");
        builder.AppendLine($"end = {QuoteForPython(BashRcManagedEnd)}");
        builder.AppendLine("from pathlib import Path");
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
        return builder.ToString();
    }

    public string GenerateValidation(ResolvedWorkspace workspace)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine("set -euo pipefail");
        builder.AppendLine("export HOME=/home/opencode");
        builder.AppendLine("git --version");
        builder.AppendLine("python --version");
        builder.AppendLine("python3 --version");
        builder.AppendLine("which python");
        builder.AppendLine("which python3");
        builder.AppendLine("node --version");
        builder.AppendLine("node -e \"console.log(process.version)\"");
        builder.AppendLine("npm --version");
        if (OracleWorkspaceFamily.IsOracleWorkspace(workspace.Definition))
        {
            builder.AppendLine("command -v sqlcl");
            builder.AppendLine("sqlcl -v");
        }
        builder.AppendLine("command -v opencode");
        builder.AppendLine("opencode --version");
        builder.AppendLine("su -s /bin/bash -c 'command -v opencode && opencode --version' opencode");
        builder.AppendLine("su -s /bin/bash -c 'screen --version' opencode");
        return builder.ToString();
    }

    private static string QuoteForPython(string value)
        => "'" + value.Replace("'", "\\'", StringComparison.Ordinal) + "'";
}
