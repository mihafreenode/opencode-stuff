param(
    [Parameter(Mandatory = $true)]
    [string]$Template,
    [string]$WorkspaceRoot,
    [string]$ArtifactsRoot,
    [ValidateSet("auto", "current", "windows")]
    [string]$Host = "auto",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$projectPath = Join-Path $repoRoot "tools\OracleRuntimeSmoke\OracleRuntimeSmoke.csproj"

$arguments = @(
    "run",
    "--project", $projectPath,
    "--",
    "--template", $Template,
    "--host", $Host,
    "--invoked-from-wrapper"
)

if (-not [string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $arguments += @("--workspace-root", $WorkspaceRoot)
}

if (-not [string]::IsNullOrWhiteSpace($ArtifactsRoot)) {
    $arguments += @("--artifacts-root", $ArtifactsRoot)
}

if ($DryRun) {
    $arguments += "--dry-run"
}

& dotnet @arguments
exit $LASTEXITCODE
