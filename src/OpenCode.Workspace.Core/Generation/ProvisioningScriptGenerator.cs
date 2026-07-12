using System.Text;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

/// <summary>
/// Generates the runtime provisioning orchestrator. Immutable tooling lives in the
/// workspace image layer, while this script coordinates initialization and any
/// provider-specific runtime provisioning inside the running workspace container.
/// </summary>
public sealed class ProvisioningScriptGenerator
{
    public string Generate(ResolvedWorkspace workspace, GeneratedArtifactRuntimeMetadata? runtimeMetadata = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine(GeneratedArtifactRuntimeMetadataBuilder.BuildCommentHeader(
            runtimeMetadata,
            "Source inputs: workspace.yaml and catalog manifests under catalog/.",
            "User edits are not preserved. Edit workspace.yaml or catalog manifests instead."));
        builder.AppendLine("set -euo pipefail");
        builder.AppendLine("export DEBIAN_FRONTEND=noninteractive");
        builder.AppendLine("export HOME=/home/opencode");
        builder.AppendLine("if [ -f /workspace/.env ]; then");
        builder.AppendLine("  while IFS= read -r env_line || [ -n \"${env_line}\" ]; do");
        builder.AppendLine("    env_line=${env_line%$'\\r'}");
        builder.AppendLine("    case \"${env_line}\" in ''|'#'*) continue ;; esac");
        builder.AppendLine("    if [[ \"${env_line}\" != *=* ]]; then continue; fi");
        builder.AppendLine("    env_key=${env_line%%=*}");
        builder.AppendLine("    env_value=${env_line#*=}");
        builder.AppendLine("    export \"${env_key}=${env_value}\"");
        builder.AppendLine("  done < /workspace/.env");
        builder.AppendLine("fi");
        builder.AppendLine("stage_name='' ");
        builder.AppendLine("stage_started_at=0");
        builder.AppendLine("stage_active=0");
        builder.AppendLine("timestamp_utc() { date -u +'%Y-%m-%dT%H:%M:%SZ'; }");
        builder.AppendLine("begin_stage() { stage_name=\"$1\"; stage_started_at=$(date +%s); stage_active=1; echo \"[stage] name=${stage_name} status=started started_at=$(timestamp_utc)\"; }");
        builder.AppendLine("complete_stage() { if [ \"${stage_active:-0}\" -ne 1 ]; then return 0; fi; echo \"[stage] name=${stage_name} status=completed completed_at=$(timestamp_utc) elapsed_seconds=$(( $(date +%s) - stage_started_at ))\"; stage_active=0; }");
        builder.AppendLine("fail_stage() { local failure_point=\"$1\"; local evidence=\"${2:-}\"; local recommendation=\"${3:-Inspect the provisioning log and retry reprovision.}\"; echo \"[stage] name=${stage_name:-Provisioning} status=failed failed_at=$(timestamp_utc) elapsed_seconds=$(( $(date +%s) - ${stage_started_at:-0} ))\" >&2; echo \"Failure point: ${failure_point}\" >&2; if [ -n \"${evidence}\" ]; then echo \"Last observed status: ${evidence}\" >&2; fi; echo \"Suggested next action: ${recommendation}\" >&2; exit 1; }");
        builder.AppendLine("trap 'fail_stage \"Provisioning command failed.\" \"Last command: ${BASH_COMMAND}\" \"Inspect the stage log and retry reprovision.\"' ERR");
        builder.AppendLine("begin_stage 'Initializing Workspace'");
        builder.AppendLine("bash /opt/opencode-workspace/config/workspace-init.sh");
        builder.AppendLine("complete_stage");
        if (OracleWorkspaceFamily.IsOracleWorkspace(workspace.Definition))
        {
            builder.AppendLine("bash /opt/opencode-workspace/config/oracle-provision.sh");
        }
        builder.AppendLine("begin_stage 'Final Validation'");
        builder.AppendLine("bash /opt/opencode-workspace/config/workspace-validate.sh");
        builder.AppendLine("complete_stage");
        return builder.ToString();
    }
}
