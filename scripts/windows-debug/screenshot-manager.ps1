param(
    [string]$OutputPath,
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    throw "OutputPath is required."
}

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
"@

function Resolve-AbsolutePath {
    param([string]$PathValue)

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $PathValue))
}

function Get-VisibleManagerProcess {
    Get-Process OpenCode.Workspace.Manager -ErrorAction SilentlyContinue |
        Sort-Object StartTime |
        Where-Object { $_.MainWindowHandle -ne 0 } |
        Select-Object -Last 1
}

$deadline = (Get-Date).AddSeconds([Math]::Max(1, $TimeoutSeconds))
$process = $null
do {
    $process = Get-VisibleManagerProcess
    if ($process) {
        break
    }

    Start-Sleep -Milliseconds 300
} while ((Get-Date) -lt $deadline)

if (-not $process) {
    Write-Warning "No visible OpenCode Workspace Manager window is available for screenshot capture."
    exit 0
}

$handle = [IntPtr]$process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) {
    Write-Warning "Manager process exists but MainWindowHandle is null."
    exit 0
}

[NativeMethods]::ShowWindow($handle, 9) | Out-Null
[NativeMethods]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds 500

$rect = New-Object NativeMethods+RECT
[NativeMethods]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

if ($width -le 0 -or $height -le 0) {
    Write-Warning "Window rectangle is invalid; screenshot was not captured."
    exit 0
}

$resolvedOutputPath = Resolve-AbsolutePath $OutputPath
$outputDirectory = Split-Path $resolvedOutputPath -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
$bitmap.Save($resolvedOutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

$process.Refresh()
$process | Select-Object Id, ProcessName, StartTime, MainWindowTitle, MainWindowHandle | Format-List
Write-Output "Screenshot: $resolvedOutputPath"
