$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $repoRoot
Set-Location $repoRoot

dotnet run --project src/OpenCode.Workspace.Cli -- debug-workspace-discovery
