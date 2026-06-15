param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$Language = "en",
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$solutionPath = Join-Path $repoRoot "OpenCode.Workspace.Manager.slnx"
$appPath = Join-Path $repoRoot "src\OpenCode.Workspace.Manager\bin\$Configuration\net10.0-windows\OpenCode.Workspace.Manager.exe"
$killScript = Join-Path $PSScriptRoot "kill-manager.ps1"
$launchScript = Join-Path $PSScriptRoot "launch-manager.ps1"

& $killScript

pushd $repoRoot
try {
    dotnet build $solutionPath -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for configuration '$Configuration'."
    }
}
finally {
    popd
}

& $launchScript -AppPath $appPath -Language $Language -TimeoutSeconds $TimeoutSeconds
