# Reports running OpenCode Workspace desktop processes and window handles.
param(
    [int]$TimeoutSeconds = 1
)

$ErrorActionPreference = "Stop"

function Get-DesktopShellProcesses {
    Get-Process OpenCode.Workspace -ErrorAction SilentlyContinue |
        Sort-Object StartTime
}

$deadline = (Get-Date).AddSeconds([Math]::Max(0, $TimeoutSeconds))
$processes = @()
do {
    $processes = @(Get-DesktopShellProcesses)
    if ($processes.Count -gt 0) {
        break
    }

    Start-Sleep -Milliseconds 300
} while ((Get-Date) -lt $deadline)

if ($processes.Count -eq 0) {
    Write-Warning "No OpenCode desktop shell processes are running."
    exit 0
}

$processes |
    Select-Object Id, ProcessName, StartTime, MainWindowTitle, MainWindowHandle, Responding |
    Format-List
