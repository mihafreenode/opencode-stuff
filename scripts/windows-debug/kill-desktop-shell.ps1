param(
    [switch]$AllInstances
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$candidates = @(Get-Process OpenCode.Workspace.Avalonia -ErrorAction SilentlyContinue)

if ($candidates.Count -eq 0) {
    Write-Warning "No OpenCode desktop shell processes are running."
    exit 0
}

$targets = foreach ($process in $candidates) {
    try {
        $path = $process.MainModule.FileName
        if ($AllInstances -or $path.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            $process
        }
    }
    catch {
        Write-Warning "Could not inspect process $($process.Id): $($_.Exception.Message)"
    }
}

if (-not $targets) {
    Write-Warning "No desktop-shell processes matched the repo-scoped filter. Use -AllInstances to stop all of them."
    exit 0
}

$targets | ForEach-Object {
    $_ | Stop-Process -Force
    Write-Output ("Stopped PID " + $_.Id)
}
