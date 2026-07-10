param(
    [switch]$ReverseBuilderChange
)

$ErrorActionPreference = "Stop"

if ($env:OPENCODE_APEX_DEVLOOP_ENABLED -ne "1" -and $env:OPENCODE_APEX_DEVLOOP_ENABLED -ne "true") {
    throw "Set OPENCODE_APEX_DEVLOOP_ENABLED=1 and the related OPENCODE_APEX_DEVLOOP_* variables before running this workflow."
}

if ($ReverseBuilderChange) {
    $env:OPENCODE_APEX_DEVLOOP_EXPECTS_BUILDER_CHANGE = "1"
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
Set-Location $repoRoot

dotnet test tests/OpenCode.Workspace.Core.Tests/OpenCode.Workspace.Core.Tests.csproj --filter "OracleApexAssistantIntegrationTests"
exit $LASTEXITCODE
