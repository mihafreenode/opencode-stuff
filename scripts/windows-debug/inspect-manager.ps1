param(
    [int]$TimeoutSeconds = 1
)

$ErrorActionPreference = "Stop"

function Get-ManagerProcesses {
    Get-Process OpenCode.Workspace.Manager -ErrorAction SilentlyContinue |
        Sort-Object StartTime
}

$deadline = (Get-Date).AddSeconds([Math]::Max(0, $TimeoutSeconds))
$processes = @()
do {
    $processes = @(Get-ManagerProcesses)
    if ($processes.Count -gt 0) {
        break
    }

    Start-Sleep -Milliseconds 300
} while ((Get-Date) -lt $deadline)

if ($processes.Count -eq 0) {
    Write-Warning "No OpenCode Workspace Manager processes are running."
    exit 0
}

$processes |
    Select-Object Id, ProcessName, StartTime, MainWindowTitle, MainWindowHandle, Responding |
    Format-List
