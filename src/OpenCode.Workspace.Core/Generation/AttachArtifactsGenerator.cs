using System.Text;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

/// <summary>
/// Generates a host-side wrapper for Windows Terminal attach. Keeping the wrapper
/// on disk makes the launched command inspectable and avoids fragile nested
/// quoting in `wt.exe` arguments.
/// </summary>
public sealed class AttachArtifactsGenerator
{
    public string GenerateWindowsTerminalWrapper(WorkspaceDefinition definition)
    {
        var containerName = DockerServiceName(definition);
        var builder = new StringBuilder();

        builder.AppendLine("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES");
        builder.AppendLine("# Source inputs: workspace.yaml attach settings and workspace naming.");
        builder.AppendLine("# User edits are not preserved. Edit workspace.yaml or code generation instead.");
        builder.AppendLine("$ErrorActionPreference = 'Stop'");
        builder.AppendLine($"$containerName = '{EscapePowerShell(containerName)}'");
        builder.AppendLine("$workspaceShellScript = '/opt/opencode-workspace/config/opencode-workspace-shell.sh'");
        builder.AppendLine("$dockerExe = (Get-Command docker.exe -ErrorAction Stop).Source");
        builder.AppendLine("& $dockerExe exec -it --user opencode -w /workspace $containerName bash $workspaceShellScript");
        builder.AppendLine("$exitCode = $LASTEXITCODE");
        builder.AppendLine("if ($exitCode -ne 0) {");
        builder.AppendLine("  Write-Host \"[attach] docker exec failed with exit code $exitCode\"");
        builder.AppendLine("  exit $exitCode");
        builder.AppendLine("}");

        return builder.ToString();
    }

    public string GenerateTerminalDiagnosticsWrapper(WorkspaceDefinition definition)
    {
        var containerName = DockerServiceName(definition);
        var builder = new StringBuilder();

        builder.AppendLine("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES");
        builder.AppendLine("# Source inputs: workspace.yaml terminal diagnostics generation.");
        builder.AppendLine("# User edits are not preserved. Edit workspace.yaml or code generation instead.");
        builder.AppendLine("$ErrorActionPreference = 'Stop'");
        builder.AppendLine($"$workspaceName = '{EscapePowerShell(definition.Workspace.Name)}'");
        builder.AppendLine($"$containerName = '{EscapePowerShell(containerName)}'");
        builder.AppendLine("$dockerExe = (Get-Command docker.exe -ErrorAction Stop).Source");
        builder.AppendLine("Write-Host \"[attach] Workspace: $workspaceName\"");
        builder.AppendLine("Write-Host \"[attach] User: opencode\"");
        builder.AppendLine("Write-Host \"[attach] Container: $containerName\"");
        builder.AppendLine("Write-Host \"[attach] Command: $dockerExe exec -it --user opencode -w /workspace $containerName bash -lc <diagnostics>\"");
        builder.AppendLine("& $dockerExe exec -it --user opencode -w /workspace $containerName bash -lc 'echo TERM=$TERM; echo LANG=$LANG; echo LC_ALL=$LC_ALL; opencode session list || true; printf '\''UTF8: ✓ λ € — • │ ─  \\n'\''; exec bash'");
        return builder.ToString();
    }

    private static string DockerServiceName(WorkspaceDefinition definition)
        => $"{WorkspacePathBuilder.Slugify(definition.Workspace.Name)}-workspace";

    private static string EscapePowerShell(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}
