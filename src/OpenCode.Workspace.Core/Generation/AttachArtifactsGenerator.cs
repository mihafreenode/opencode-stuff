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
    public string GenerateWindowsTerminalWrapper(WorkspaceDefinition definition, WorkspacePaths paths, GeneratedArtifactRuntimeMetadata? runtimeMetadata = null)
    {
        var containerName = DockerServiceName(definition);
        var projectName = WorkspacePathBuilder.Slugify(definition.Workspace.Name);
        var attachPrefix = $"[attach:{definition.Workspace.Name}]";
        var builder = new StringBuilder();

        builder.AppendLine(GeneratedArtifactRuntimeMetadataBuilder.BuildCommentHeader(
            runtimeMetadata,
            "Source inputs: workspace.yaml attach settings and workspace naming.",
            "User edits are not preserved. Edit workspace.yaml or code generation instead."));
        builder.AppendLine("$ErrorActionPreference = 'Stop'");
        builder.AppendLine($"$attachPrefix = '{EscapePowerShell(attachPrefix)}'");
        builder.AppendLine($"$containerName = '{EscapePowerShell(containerName)}'");
        builder.AppendLine($"$projectName = '{EscapePowerShell(projectName)}'");
        builder.AppendLine($"$composeFile = '{EscapePowerShell(paths.ComposePath)}'");
        builder.AppendLine($"$attachLogPath = '{EscapePowerShell(paths.AttachDiagnosticsLogPath)}'");
        builder.AppendLine("$workspaceShellScript = '/opt/opencode-workspace/config/opencode-workspace-shell.sh'");
        builder.AppendLine("$workspaceDirectory = '/workspace'");
        builder.AppendLine("$attachUser = 'opencode'");
        builder.AppendLine("$dockerExe = (Get-Command docker.exe -ErrorAction Stop).Source");
        builder.AppendLine("$ansiEscape = [char]27");
        builder.AppendLine("$disableMouseReporting = \"${ansiEscape}[?1000l${ansiEscape}[?1002l${ansiEscape}[?1003l${ansiEscape}[?1006l\"");
        builder.AppendLine("$dockerExecArgs = @('exec', '-it', '--user', $attachUser, '-w', $workspaceDirectory, $containerName, 'bash', $workspaceShellScript)");
        builder.AppendLine("$dockerPsArgs = @('ps', '--filter', \"name=$containerName\", '--format', 'table {{.Names}}\t{{.Status}}')");
        builder.AppendLine("$composePsArgs = @('compose', '--project-name', $projectName, '--file', $composeFile, 'ps')");
        builder.AppendLine("$attemptedCommand = \"$dockerExe exec -it --user $attachUser -w $workspaceDirectory $containerName bash $workspaceShellScript\"");
        builder.AppendLine("$originalOutputEncoding = [Console]::OutputEncoding");
        builder.AppendLine("function Write-AttachMessage {");
        builder.AppendLine("  param([string]$Message)");
        builder.AppendLine("  Write-Host \"$attachPrefix $Message\"");
        builder.AppendLine("}");
        builder.AppendLine("function Invoke-DockerCheck {");
        builder.AppendLine("  param([string[]]$Arguments)");
        builder.AppendLine("  $output = & $dockerExe @Arguments 2>&1");
        builder.AppendLine("  return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = @($output) }");
        builder.AppendLine("}");
        builder.AppendLine("function Test-AttachPreconditions {");
        builder.AppendLine("  $scriptCheck = Invoke-DockerCheck -Arguments @('exec', $containerName, 'ls', '-l', $workspaceShellScript)");
        builder.AppendLine("  if ($scriptCheck.ExitCode -ne 0) {");
        builder.AppendLine("    Write-AttachMessage \"Script not found: $workspaceShellScript\"");
        builder.AppendLine("    $scriptCheck.Output | ForEach-Object { if ($_ -ne $null) { Write-Host $_ } }");
        builder.AppendLine("    return $false");
        builder.AppendLine("  }");
        builder.AppendLine("  Write-AttachMessage \"Verified script exists: $workspaceShellScript\"");
        builder.AppendLine("  $executableCheck = Invoke-DockerCheck -Arguments @('exec', $containerName, 'test', '-x', $workspaceShellScript)");
        builder.AppendLine("  if ($executableCheck.ExitCode -ne 0) {");
        builder.AppendLine("    Write-AttachMessage \"Script is not marked executable: $workspaceShellScript\"");
        builder.AppendLine("    $scriptCheck.Output | ForEach-Object { if ($_ -ne $null) { Write-Host $_ } }");
        builder.AppendLine("    $executableCheck.Output | ForEach-Object { if ($_ -ne $null) { Write-Host $_ } }");
        builder.AppendLine("    return $false");
        builder.AppendLine("  }");
        builder.AppendLine("  Write-AttachMessage \"Verified script is executable: $workspaceShellScript\"");
        builder.AppendLine("  $userCheck = Invoke-DockerCheck -Arguments @('exec', $containerName, 'id', $attachUser)");
        builder.AppendLine("  if ($userCheck.ExitCode -ne 0) {");
        builder.AppendLine("    Write-AttachMessage \"User $attachUser does not exist.\"");
        builder.AppendLine("    $userCheck.Output | ForEach-Object { if ($_ -ne $null) { Write-Host $_ } }");
        builder.AppendLine("    return $false");
        builder.AppendLine("  }");
        builder.AppendLine("  Write-AttachMessage \"Verified user exists: $attachUser\"");
        builder.AppendLine("  $directoryCheck = Invoke-DockerCheck -Arguments @('exec', $containerName, 'ls', '-ld', $workspaceDirectory)");
        builder.AppendLine("  if ($directoryCheck.ExitCode -ne 0) {");
        builder.AppendLine("    Write-AttachMessage \"Working directory missing: $workspaceDirectory\"");
        builder.AppendLine("    $directoryCheck.Output | ForEach-Object { if ($_ -ne $null) { Write-Host $_ } }");
        builder.AppendLine("    return $false");
        builder.AppendLine("  }");
        builder.AppendLine("  Write-AttachMessage \"Verified working directory exists: $workspaceDirectory\"");
        builder.AppendLine("  return $true");
        builder.AppendLine("}");
        builder.AppendLine("function Write-AttachDiagnostics {");
        builder.AppendLine("  param([int]$ExitCode)");
        builder.AppendLine("  Write-AttachMessage \"docker exec failed with exit code $ExitCode\"");
        builder.AppendLine("  Write-AttachMessage \"Expected container name: $containerName\"");
        builder.AppendLine("  Write-AttachMessage \"Attempted command: $attemptedCommand\"");
        builder.AppendLine("  Write-AttachMessage \"docker ps:\"");
        builder.AppendLine("  try { & $dockerExe @dockerPsArgs } catch { Write-AttachMessage \"docker ps failed: $($_.Exception.Message)\" }");
        builder.AppendLine("  Write-AttachMessage \"docker compose ps:\"");
        builder.AppendLine("  try { & $dockerExe @composePsArgs } catch { Write-AttachMessage \"docker compose ps failed: $($_.Exception.Message)\" }");
        builder.AppendLine("}");
        builder.AppendLine("try { Start-Transcript -Path $attachLogPath -Force | Out-Null } catch { Write-AttachMessage \"Failed to start transcript: $($_.Exception.Message)\" }");
        builder.AppendLine("if (-not (Test-AttachPreconditions)) {");
        builder.AppendLine("  Write-AttachDiagnostics -ExitCode 1");
        builder.AppendLine("  exit 1");
        builder.AppendLine("}");
        builder.AppendLine("Write-AttachMessage \"Preflight checks passed.\"");
        builder.AppendLine("Write-AttachMessage \"Attempted command: $attemptedCommand\"");
        builder.AppendLine("try {");
        builder.AppendLine("  [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)");
        builder.AppendLine("  [Console]::Write($disableMouseReporting)");
        builder.AppendLine("  & $dockerExe @dockerExecArgs");
        builder.AppendLine("  $exitCode = $LASTEXITCODE");
        builder.AppendLine("}");
        builder.AppendLine("finally {");
        builder.AppendLine("  [Console]::Write($disableMouseReporting)");
        builder.AppendLine("  [Console]::OutputEncoding = $originalOutputEncoding");
        builder.AppendLine("  try { Stop-Transcript | Out-Null } catch { } ");
        builder.AppendLine("}");
        builder.AppendLine("if ($exitCode -ne 0) {");
        builder.AppendLine("  Write-AttachDiagnostics -ExitCode $exitCode");
        builder.AppendLine("  exit $exitCode");
        builder.AppendLine("}");
        builder.AppendLine("Write-AttachMessage 'Attach session completed successfully.'");

        return builder.ToString();
    }

    public string GenerateTerminalDiagnosticsWrapper(WorkspaceDefinition definition, GeneratedArtifactRuntimeMetadata? runtimeMetadata = null)
    {
        var containerName = DockerServiceName(definition);
        var builder = new StringBuilder();

        builder.AppendLine(GeneratedArtifactRuntimeMetadataBuilder.BuildCommentHeader(
            runtimeMetadata,
            "Source inputs: workspace.yaml terminal diagnostics generation.",
            "User edits are not preserved. Edit workspace.yaml or code generation instead."));
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
