using System.Text;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Generation;

/// <summary>
/// Generates terminal-related runtime artifacts that the workspace owns. The app
/// can regenerate these safely because they are derived from workspace.yaml.
/// </summary>
public sealed class TerminalArtifactsGenerator
{
    public string GenerateStarshipConfig(WorkspaceDefinition definition)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES");
        builder.AppendLine("# Source inputs: workspace.yaml terminal settings.");
        builder.AppendLine("# User edits are not preserved. Edit workspace.yaml instead.");
        builder.AppendLine("format = '$directory$git_branch$git_status$docker_context$dotnet$nodejs$python$character'");
        builder.AppendLine("add_newline = false");
        builder.AppendLine();
        builder.AppendLine("[directory]");
        builder.AppendLine("truncate_to_repo = true");
        builder.AppendLine("style = 'bold cyan'");
        builder.AppendLine();
        builder.AppendLine("[git_branch]");
        builder.AppendLine("symbol = 'git '");
        builder.AppendLine("style = 'bold purple'");
        builder.AppendLine();
        builder.AppendLine("[git_status]");
        builder.AppendLine("style = 'bold yellow'");
        builder.AppendLine();
        builder.AppendLine("[docker_context]");
        builder.AppendLine("symbol = 'docker '");
        builder.AppendLine("style = 'blue'");
        builder.AppendLine();
        builder.AppendLine("[dotnet]");
        builder.AppendLine("symbol = '.net '");
        builder.AppendLine("style = 'bold blue'");
        builder.AppendLine();
        builder.AppendLine("[nodejs]");
        builder.AppendLine("symbol = 'node '");
        builder.AppendLine("style = 'bold green'");
        builder.AppendLine();
        builder.AppendLine("[python]");
        builder.AppendLine("symbol = 'py '");
        builder.AppendLine("style = 'bold yellow'");
        builder.AppendLine();
        builder.AppendLine("[character]");
        builder.AppendLine("success_symbol = '>'");
        builder.AppendLine("error_symbol = 'x'");
        return builder.ToString();
    }

    public string GenerateShellInitScript(WorkspaceDefinition definition)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES");
        builder.AppendLine("# Source inputs: workspace.yaml terminal settings.");
        builder.AppendLine("# This file is sourced from a clearly marked managed block in ~/.bashrc.");
        builder.AppendLine();
        builder.AppendLine("export STARSHIP_CONFIG=/opt/opencode-workspace/config/starship.toml");
        builder.AppendLine("export TERM=${TERM:-xterm-256color}");
        builder.AppendLine("export COLORTERM=truecolor");
        builder.AppendLine("export LANG=${LANG:-C.UTF-8}");
        builder.AppendLine("export LC_ALL=${LC_ALL:-C.UTF-8}");
        builder.AppendLine("export SCREENRC=/opt/opencode-workspace/config/screenrc");

        if (string.Equals(definition.Terminal.Prompt.Provider, "starship", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("if command -v starship >/dev/null 2>&1; then");
            builder.AppendLine("  eval \"$(starship init bash)\"");
            builder.AppendLine("fi");
        }

        if (definition.Terminal.Utilities.Zoxide)
        {
            builder.AppendLine("if command -v zoxide >/dev/null 2>&1; then");
            builder.AppendLine("  eval \"$(zoxide init bash)\"");
            builder.AppendLine("fi");
        }

        if (definition.Terminal.Utilities.Fzf)
        {
            builder.AppendLine("if [ -f /usr/share/doc/fzf/examples/key-bindings.bash ]; then");
            builder.AppendLine("  source /usr/share/doc/fzf/examples/key-bindings.bash");
            builder.AppendLine("fi");
            builder.AppendLine("if [ -f /usr/share/doc/fzf/examples/completion.bash ]; then");
            builder.AppendLine("  source /usr/share/doc/fzf/examples/completion.bash");
            builder.AppendLine("fi");
        }

        return builder.ToString();
    }

    public string GenerateOpencodeWorkspaceShellScript()
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES");
        builder.AppendLine("# Source inputs: workspace.yaml attach/session behavior and launcher generation.");
        builder.AppendLine("# User edits are not preserved. Edit workspace.yaml or code generation instead.");
        builder.AppendLine("set -euo pipefail");
        builder.AppendLine();
        builder.AppendLine("# This helper starts OpenCode directly for the v0.1 attach workflow.");
        builder.AppendLine("# Screen-based durable restore remains generated separately, but the default");
        builder.AppendLine("# release path prefers the simpler direct OpenCode shell because it currently");
        builder.AppendLine("# renders more reliably in Windows Terminal.");
        builder.AppendLine("export HOME=/home/opencode");
        builder.AppendLine("export TERM=${TERM:-xterm-256color}");
        builder.AppendLine("export COLORTERM=truecolor");
        builder.AppendLine("export LANG=${LANG:-C.UTF-8}");
        builder.AppendLine("export LC_ALL=${LC_ALL:-C.UTF-8}");
        builder.AppendLine("if [ ! -d /home/opencode/.local/share/opencode/log ] || [ ! -w /home/opencode/.local/share/opencode/log ]; then");
        builder.AppendLine("  printf '[attach] Initializing OpenCode user directories.\\n'");
        builder.AppendLine("fi");
        builder.AppendLine("mkdir -p /home/opencode/.local/share/opencode/log /home/opencode/.config/opencode /home/opencode/.cache/opencode");
        builder.AppendLine("test -d /home/opencode/.local/share/opencode/log");
        builder.AppendLine("test -w /home/opencode/.local/share/opencode/log");
        builder.AppendLine("cd /workspace");
        builder.AppendLine();
        builder.AppendLine("opencode -s || true");
        builder.AppendLine("exec bash --rcfile /home/opencode/.bashrc -i");
        return builder.ToString();
    }

    public string GenerateScreenConfiguration()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES");
        builder.AppendLine("# Source inputs: workspace.yaml terminal/session settings.");
        builder.AppendLine("# User edits are not preserved. Edit workspace.yaml or code generation instead.");
        builder.AppendLine("defutf8 on");
        builder.AppendLine("encoding utf8 utf8");
        builder.AppendLine("term screen-256color");
        return builder.ToString();
    }
}
