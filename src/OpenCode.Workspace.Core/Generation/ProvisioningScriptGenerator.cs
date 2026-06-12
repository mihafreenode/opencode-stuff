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

        if (workspace.AptPackages.Count > 0)
        {
            builder.AppendLine($"apt-get install -y {string.Join(" ", workspace.AptPackages)}");
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
