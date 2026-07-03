using System.Text;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

/// <summary>
/// Generates terminal-related runtime artifacts that the workspace owns. The app
/// can regenerate these safely because they are derived from workspace.yaml.
/// </summary>
public sealed class TerminalArtifactsGenerator
{
    public string GenerateStarshipConfig(WorkspaceDefinition definition, GeneratedArtifactRuntimeMetadata? runtimeMetadata = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine(GeneratedArtifactRuntimeMetadataBuilder.BuildCommentHeader(
            runtimeMetadata,
            "Source inputs: workspace.yaml terminal settings.",
            "User edits are not preserved. Edit workspace.yaml instead."));
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

    public string GenerateShellInitScript(WorkspaceDefinition definition, GeneratedArtifactRuntimeMetadata? runtimeMetadata = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine(GeneratedArtifactRuntimeMetadataBuilder.BuildCommentHeader(
            runtimeMetadata,
            "Source inputs: workspace.yaml terminal settings.",
            "This file is sourced from a clearly marked managed block in ~/.bashrc."));
        builder.AppendLine();
        builder.AppendLine("export STARSHIP_CONFIG=/opt/opencode-workspace/config/starship.toml");
        builder.AppendLine("export TERM=${TERM:-xterm-256color}");
        builder.AppendLine("export COLORTERM=truecolor");
        builder.AppendLine("export LANG=${LANG:-C.UTF-8}");
        builder.AppendLine("export LC_ALL=${LC_ALL:-C.UTF-8}");
        builder.AppendLine("export SCREENRC=/opt/opencode-workspace/config/screenrc");
        builder.AppendLine("oracle_client_home='' ");
        builder.AppendLine("if [ -d /opt/oracle/instantclient ]; then");
        builder.AppendLine("  oracle_client_home=$(find /opt/oracle/instantclient -maxdepth 2 -type f -name 'libsqlplus.so' -printf '%h\n' 2>/dev/null | while read -r dir; do if ls \"$dir\"/libclntsh.so* >/dev/null 2>&1; then printf '%s\n' \"$dir\"; break; fi; done)");
        builder.AppendLine("fi");
        builder.AppendLine("if [ -d /workspace/.local/oracle/network/admin ]; then");
        builder.AppendLine("  export TNS_ADMIN=/workspace/.local/oracle/network/admin");
        builder.AppendLine("fi");
        builder.AppendLine("if [ -n \"${oracle_client_home}\" ] && [ -d \"${oracle_client_home}\" ]; then");
        builder.AppendLine("  export ORACLE_CLIENT_HOME=${oracle_client_home}");
        builder.AppendLine("  export LD_LIBRARY_PATH=${oracle_client_home}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}");
        builder.AppendLine("  export PATH=${oracle_client_home}:${PATH}");
        builder.AppendLine("fi");
        builder.AppendLine("if command -v java >/dev/null 2>&1; then");
        builder.AppendLine("  export JAVA_HOME=$(dirname \"$(dirname \"$(readlink -f \"$(command -v java)\")\")\")");
        builder.AppendLine("fi");

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

    public string GenerateOpencodeWorkspaceShellScript(WorkspaceDefinition definition, GeneratedArtifactRuntimeMetadata? runtimeMetadata = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine(GeneratedArtifactRuntimeMetadataBuilder.BuildCommentHeader(
            runtimeMetadata,
            "Source inputs: workspace.yaml attach/session behavior and launcher generation.",
            "User edits are not preserved. Edit workspace.yaml or code generation instead."));
        builder.AppendLine("set -euo pipefail");
        builder.AppendLine();
        builder.AppendLine("# This helper resumes the latest matching OpenCode session for the workspace.");
        builder.AppendLine("export HOME=/home/opencode");
        builder.AppendLine("export TERM=${TERM:-xterm-256color}");
        builder.AppendLine("export COLORTERM=truecolor");
        builder.AppendLine("export LANG=${LANG:-C.UTF-8}");
        builder.AppendLine("export LC_ALL=${LC_ALL:-C.UTF-8}");
        builder.AppendLine("export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin${PATH:+:${PATH}}");
        builder.AppendLine("npm_global_prefix='' ");
        builder.AppendLine("if command -v npm >/dev/null 2>&1; then");
        builder.AppendLine("  npm_global_prefix=$(npm prefix -g 2>/dev/null || printf '')");
        builder.AppendLine("fi");
        builder.AppendLine("if [ -n \"${npm_global_prefix}\" ] && [ -d \"${npm_global_prefix}/bin\" ]; then");
        builder.AppendLine("  export PATH=${npm_global_prefix}/bin:${PATH}");
        builder.AppendLine("fi");
        builder.AppendLine("oracle_client_home='' ");
        builder.AppendLine("if [ -d /opt/oracle/instantclient ]; then");
        builder.AppendLine("  oracle_client_home=$(find /opt/oracle/instantclient -maxdepth 2 -type f -name 'libsqlplus.so' -printf '%h\n' 2>/dev/null | while read -r dir; do if ls \"$dir\"/libclntsh.so* >/dev/null 2>&1; then printf '%s\n' \"$dir\"; break; fi; done)");
        builder.AppendLine("fi");
        builder.AppendLine("if [ -n \"${oracle_client_home}\" ] && [ -d \"${oracle_client_home}\" ]; then");
        builder.AppendLine("  export ORACLE_CLIENT_HOME=${oracle_client_home}");
        builder.AppendLine("  export LD_LIBRARY_PATH=${oracle_client_home}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}");
        builder.AppendLine("  export PATH=${oracle_client_home}:${PATH}");
        builder.AppendLine("fi");
        builder.AppendLine("cleanup_terminal_state() {");
        builder.AppendLine("  printf '\\e[?1000l\\e[?1002l\\e[?1003l\\e[?1006l'");
        builder.AppendLine("  stty sane || true");
        builder.AppendLine("}");
        builder.AppendLine("run_opencode() {");
        builder.AppendLine("  cleanup_terminal_state");
        builder.AppendLine("  \"$@\"");
        builder.AppendLine("  local exit_code=$?");
        builder.AppendLine("  cleanup_terminal_state");
        builder.AppendLine("  return $exit_code");
        builder.AppendLine("}");
        builder.AppendLine("trap cleanup_terminal_state EXIT");
        builder.AppendLine("if [ ! -d /home/opencode/.local/share/opencode/log ] || [ ! -w /home/opencode/.local/share/opencode/log ]; then");
        builder.AppendLine("  printf '[attach] Initializing OpenCode user directories.\\n'");
        builder.AppendLine("fi");
        builder.AppendLine("mkdir -p /home/opencode/.local/share/opencode/log /home/opencode/.config/opencode /home/opencode/.cache/opencode");
        builder.AppendLine("test -d /home/opencode/.local/share/opencode/log");
        builder.AppendLine("test -w /home/opencode/.local/share/opencode/log");
        builder.AppendLine("if ! command -v opencode >/dev/null 2>&1; then");
        builder.AppendLine("  printf '[attach] OpenCode CLI is missing from PATH. Provision or rebuild the workspace runtime before attaching.\\n' >&2");
        builder.AppendLine("  printf '[attach] PATH=%s\\n' \"$PATH\" >&2");
        builder.AppendLine("  npm_global_root='' ");
        builder.AppendLine("  if command -v npm >/dev/null 2>&1; then");
        builder.AppendLine("    npm_global_prefix=$(npm prefix -g 2>/dev/null || printf '')");
        builder.AppendLine("    npm_global_root=$(npm root -g 2>/dev/null || printf '')");
        builder.AppendLine("    printf '[attach] npm prefix -g: %s\\n' \"${npm_global_prefix:-unavailable}\" >&2");
        builder.AppendLine("    printf '[attach] npm root -g: %s\\n' \"${npm_global_root:-unavailable}\" >&2");
        builder.AppendLine("  fi");
        builder.AppendLine("  for candidate in /usr/bin/opencode /usr/local/bin/opencode; do");
        builder.AppendLine("    if [ -e \"$candidate\" ]; then");
        builder.AppendLine("      printf '[attach] candidate: %s\\n' \"$candidate\" >&2");
        builder.AppendLine("      ls -l \"$candidate\" >&2 || true");
        builder.AppendLine("    fi");
        builder.AppendLine("  done");
        builder.AppendLine("  if [ -n \"${npm_global_prefix}\" ] && [ -e \"${npm_global_prefix}/bin/opencode\" ]; then");
        builder.AppendLine("    printf '[attach] candidate: %s\\n' \"${npm_global_prefix}/bin/opencode\" >&2");
        builder.AppendLine("    ls -l \"${npm_global_prefix}/bin/opencode\" >&2 || true");
        builder.AppendLine("  fi");
        builder.AppendLine("  if [ -n \"${npm_global_root}\" ] && [ -e \"${npm_global_root}/.bin/opencode\" ]; then");
        builder.AppendLine("    printf '[attach] candidate: %s\\n' \"${npm_global_root}/.bin/opencode\" >&2");
        builder.AppendLine("    ls -l \"${npm_global_root}/.bin/opencode\" >&2 || true");
        builder.AppendLine("  fi");
        builder.AppendLine("  exit 127");
        builder.AppendLine("fi");
        builder.AppendLine("cd /workspace");
        builder.AppendLine();
        builder.AppendLine("resume_session='' ");
        builder.AppendLine("session_count=0");
        builder.AppendLine("if command -v opencode >/dev/null 2>&1; then");
        builder.AppendLine("  session_output=$(opencode session list 2>/dev/null)");
        builder.AppendLine("  session_status=$?");
        builder.AppendLine("  if [ \"$session_status\" -eq 0 ]; then");
        builder.AppendLine("    mapfile -t session_ids < <(printf '%s\n' \"$session_output\" | tail -n +3 | awk 'NF {print $1}')");
        builder.AppendLine("    session_count=${#session_ids[@]}");
        builder.AppendLine("    if [ \"$session_count\" -gt 0 ]; then");
        builder.AppendLine("      for session_id in \"${session_ids[@]}\"; do");
        builder.AppendLine("        if opencode export \"$session_id\" 2>/dev/null | node -e \"let data='';process.stdin.on('data',d=>data+=d);process.stdin.on('end',()=>{try{const j=JSON.parse(data);process.exit(j && j.info && j.info.directory === '/workspace' ? 0 : 1)}catch{process.exit(2)}})\"; then");
        builder.AppendLine("          resume_session=\"$session_id\"");
        builder.AppendLine("          break");
        builder.AppendLine("        fi");
        builder.AppendLine("      done");
        builder.AppendLine("    fi");
        builder.AppendLine("  fi");
        builder.AppendLine("else");
        builder.AppendLine("  session_status=1");
        builder.AppendLine("fi");
        builder.AppendLine("if [ \"${session_status:-1}\" -ne 0 ]; then");
        builder.AppendLine("  printf '[attach] Failed to query OpenCode sessions. Starting new session.\\n'");
        builder.AppendLine("  run_opencode opencode");
        builder.AppendLine("  exit $?");
        builder.AppendLine("fi");
        builder.AppendLine("if [ \"$session_count\" -eq 0 ]; then");
        builder.AppendLine("  printf '[attach] Found 0 OpenCode sessions. Starting new session.\\n'");
        builder.AppendLine("  run_opencode opencode");
        builder.AppendLine("  exit $?");
        builder.AppendLine("fi");
        builder.AppendLine("if [ -n \"$resume_session\" ]; then");
        builder.AppendLine("  printf '[attach] Found %s OpenCode sessions. Resuming %s.\\n' \"$session_count\" \"$resume_session\"");
        builder.AppendLine("  if run_opencode opencode --session \"$resume_session\"; then");
        builder.AppendLine("    exit 0");
        builder.AppendLine("  fi");
        builder.AppendLine("  printf '[attach] Failed to resume OpenCode session %s. Starting new session.\\n' \"$resume_session\"");
        builder.AppendLine("else");
        builder.AppendLine("  printf '[attach] Found %s OpenCode sessions. Starting new session.\\n' \"$session_count\"");
        builder.AppendLine("fi");
        builder.AppendLine("run_opencode opencode");
        builder.AppendLine("exit $?");
        return builder.ToString();
    }

    public string GenerateScreenConfiguration(GeneratedArtifactRuntimeMetadata? runtimeMetadata = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine(GeneratedArtifactRuntimeMetadataBuilder.BuildCommentHeader(
            runtimeMetadata,
            "Source inputs: workspace.yaml terminal/session settings.",
            "User edits are not preserved. Edit workspace.yaml or code generation instead."));
        builder.AppendLine("defutf8 on");
        builder.AppendLine("encoding utf8 utf8");
        builder.AppendLine("term screen-256color");
        return builder.ToString();
    }
}
