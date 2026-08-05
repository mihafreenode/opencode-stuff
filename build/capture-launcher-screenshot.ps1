$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WindowCapture {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);
  public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

$repoRoot = 'C:\Users\miha.pirnat\source\repos\opencode-stuff-init'
$exePath = Join-Path $repoRoot 'src\OpenCode.Workspace.Avalonia\bin\Debug\net10.0\OpenCode.Workspace.exe'
$outputPath = Join-Path $repoRoot 'docs\screenshots\launcher-window.png'

$env:OPENCODE_WORKSPACE_MANAGER_LANGUAGE = 'en'
$process = Start-Process -FilePath $exePath -PassThru

try {
  $deadline = (Get-Date).AddSeconds(20)
  do {
    Start-Sleep -Milliseconds 500
    $process.Refresh()
  } while ($process.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)

  if ($process.MainWindowHandle -eq 0) {
    throw 'The launcher window did not become ready in time.'
  }

  Start-Sleep -Seconds 2

  $rect = New-Object WindowCapture+RECT
  [WindowCapture]::GetWindowRect($process.MainWindowHandle, [ref]$rect) | Out-Null
  $width = $rect.Right - $rect.Left
  $height = $rect.Bottom - $rect.Top
  if ($width -le 0 -or $height -le 0) {
    throw 'Invalid launcher window size.'
  }

  $bitmap = New-Object System.Drawing.Bitmap $width, $height
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $hdc = $graphics.GetHdc()
  [WindowCapture]::PrintWindow($process.MainWindowHandle, $hdc, 2) | Out-Null
  $graphics.ReleaseHdc($hdc)
  $bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
  $graphics.Dispose()
  $bitmap.Dispose()

  Write-Output "Saved screenshot to $outputPath"
}
finally {
  Remove-Item Env:OPENCODE_WORKSPACE_MANAGER_LANGUAGE -ErrorAction SilentlyContinue
}
