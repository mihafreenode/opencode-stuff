param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$Language = "en",
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$solutionPath = Join-Path $repoRoot "OpenCode.Workspace.slnx"
$appPath = Join-Path $repoRoot "src\OpenCode.Workspace.Avalonia\bin\$Configuration\net10.0\OpenCode.Workspace.exe"
$killScript = Join-Path $PSScriptRoot "kill-desktop-shell.ps1"
$launchScript = Join-Path $PSScriptRoot "launch-desktop-shell.ps1"

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
