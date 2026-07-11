param(
    [switch]$ReverseBuilderChange
)

$ErrorActionPreference = "Stop"

function Show-MissingConfigurationChecklist {
    $examplePath = ".opencode/local/oracle-apex-development-loop.env.example"
    $requiredVariables = @(
        "OPENCODE_APEX_DEVLOOP_ENABLED",
        "OPENCODE_APEX_DEVLOOP_WORKSPACE_ROOT",
        "OPENCODE_APEX_DEVLOOP_ENVIRONMENT",
        "OPENCODE_APEX_DEVLOOP_SQLCL_PROFILE",
        "OPENCODE_APEX_DEVLOOP_APPLICATION_ID",
        "OPENCODE_APEX_DEVLOOP_SOURCE_PATH",
        "OPENCODE_APEX_DEVLOOP_DEPLOYMENT_PROFILE",
        "OPENCODE_APEX_DEVLOOP_BUILDER_URL",
        "OPENCODE_APEX_DEVLOOP_APPLICATION_URL"
    )

    $missing = @()
    foreach ($name in $requiredVariables) {
        if ([string]::IsNullOrWhiteSpace((Get-Item -Path "Env:$name" -ErrorAction SilentlyContinue).Value)) {
            $missing += $name
        }
    }

    Write-Host "Oracle APEX development-loop configuration is missing." -ForegroundColor Red
    Write-Host ""
    Write-Host "Checklist:" -ForegroundColor Yellow
    Write-Host "1. Copy the local example file: $examplePath"
    Write-Host "2. Fill the placeholder values with your local development application details"
    Write-Host "3. Export the variables into the current PowerShell or cmd session"
    Write-Host "4. Run this wrapper again"
    Write-Host ""
    Write-Host "Missing variables:" -ForegroundColor Yellow
    foreach ($name in $missing) {
        Write-Host "- $name"
    }

    Write-Host ""
    Write-Host "Configuration is local only. Do not commit credentials or SQLcl secrets." -ForegroundColor Yellow
}

if ($env:OPENCODE_APEX_DEVLOOP_ENABLED -ne "1" -and $env:OPENCODE_APEX_DEVLOOP_ENABLED -ne "true") {
    Show-MissingConfigurationChecklist
    exit 2
}

$requiredVariables = @(
    "OPENCODE_APEX_DEVLOOP_WORKSPACE_ROOT",
    "OPENCODE_APEX_DEVLOOP_ENVIRONMENT",
    "OPENCODE_APEX_DEVLOOP_SQLCL_PROFILE",
    "OPENCODE_APEX_DEVLOOP_APPLICATION_ID",
    "OPENCODE_APEX_DEVLOOP_SOURCE_PATH",
    "OPENCODE_APEX_DEVLOOP_DEPLOYMENT_PROFILE",
    "OPENCODE_APEX_DEVLOOP_BUILDER_URL",
    "OPENCODE_APEX_DEVLOOP_APPLICATION_URL"
)

foreach ($name in $requiredVariables) {
    if ([string]::IsNullOrWhiteSpace((Get-Item -Path "Env:$name" -ErrorAction SilentlyContinue).Value)) {
        Show-MissingConfigurationChecklist
        exit 2
    }
}

if ($ReverseBuilderChange) {
    $env:OPENCODE_APEX_DEVLOOP_EXPECTS_BUILDER_CHANGE = "1"
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
Set-Location $repoRoot

Write-Host "Oracle APEX development loop" -ForegroundColor Cyan
Write-Host "- Configure once"
Write-Host "- Run Doctor"
Write-Host "- Run development-loop script"
Write-Host "- Prompt OpenCode"
Write-Host "- Review semantic plan"
Write-Host "- Validate"
Write-Host "- Import"
Write-Host "- Preview"
Write-Host "- Rollback if required"
Write-Host ""

dotnet test tests/OpenCode.Workspace.Core.Tests/OpenCode.Workspace.Core.Tests.csproj --filter "OracleApexAssistantIntegrationTests"
exit $LASTEXITCODE
