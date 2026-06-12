$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class TerminalWindowCapture {
  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

function Get-WindowsTerminalHandles {
  $handles = New-Object System.Collections.Generic.List[System.IntPtr]
  [TerminalWindowCapture]::EnumWindows({ param($hWnd, $lParam)
    $procId = 0
    [TerminalWindowCapture]::GetWindowThreadProcessId($hWnd, [ref]$procId) | Out-Null
    if ([TerminalWindowCapture]::IsWindowVisible($hWnd)) {
      try {
        $proc = Get-Process -Id $procId -ErrorAction Stop
        if ($proc.ProcessName -eq 'WindowsTerminal') {
          $handles.Add($hWnd)
        }
      } catch {}
    }
    return $true
  }, [IntPtr]::Zero) | Out-Null
  return $handles
}

$repoRoot = 'C:\Users\miha.pirnat\source\repos\opencode-stuff-init'
$outputPath = Join-Path $repoRoot 'docs\screenshots\terminal-window-current.png'
$handles = Get-WindowsTerminalHandles
if ($handles.Count -eq 0) {
  throw 'No visible Windows Terminal window found.'
}

$foreground = [TerminalWindowCapture]::GetForegroundWindow()
$target = $null
foreach ($handle in $handles) {
  if ($handle -eq $foreground) {
    $target = $handle
    break
  }
}

if ($null -eq $target) {
  $target = $handles[$handles.Count - 1]
}

$rect = New-Object TerminalWindowCapture+RECT
[TerminalWindowCapture]::GetWindowRect($target, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) {
  throw 'Invalid terminal window size.'
}

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
[TerminalWindowCapture]::PrintWindow($target, $hdc, 2) | Out-Null
$graphics.ReleaseHdc($hdc)
$bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

Write-Output "Saved screenshot to $outputPath"
