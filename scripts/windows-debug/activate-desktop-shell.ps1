# Brings the newest visible OpenCode Workspace desktop window to the foreground.
param(
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@

function Get-VisibleDesktopShellProcess {
    Get-Process OpenCode.Workspace -ErrorAction SilentlyContinue |
        Sort-Object StartTime |
        Where-Object { $_.MainWindowHandle -ne 0 } |
        Select-Object -Last 1
}

$deadline = (Get-Date).AddSeconds([Math]::Max(1, $TimeoutSeconds))
$process = $null
do {
    $process = Get-VisibleDesktopShellProcess
    if ($process) {
        break
    }

    Start-Sleep -Milliseconds 300
} while ((Get-Date) -lt $deadline)

if (-not $process) {
    Write-Warning "No OpenCode desktop shell window is available to activate."
    exit 0
}

$handle = [IntPtr]$process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) {
    Write-Warning "Desktop shell process exists but MainWindowHandle is null."
    $process | Select-Object Id, ProcessName, StartTime | Format-List
    exit 0
}

[NativeMethods]::ShowWindow($handle, 9) | Out-Null
$activated = [NativeMethods]::SetForegroundWindow($handle)

if (-not $activated) {
    Write-Warning "Window activation was requested but Windows did not confirm foreground activation."
}

$process.Refresh()
$process | Select-Object Id, ProcessName, StartTime, MainWindowTitle, MainWindowHandle | Format-List
