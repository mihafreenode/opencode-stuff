# Windows Debug Scripts

These scripts help debug the Windows desktop shell from WSL without relying on fragile inline PowerShell.

They are debugging-only utilities. They do not change app runtime behavior.

## Scripts

- `rebuild-and-launch-desktop-shell.ps1`
- `launch-desktop-shell.ps1`
- `activate-desktop-shell.ps1`
- `inspect-desktop-shell.ps1`
- `screenshot-desktop-shell.ps1`
- `kill-desktop-shell.ps1`

## WSL Examples

Use `wslpath -w` so PowerShell receives a Windows path to the script.

Rebuild and launch the exact binary you just built:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/rebuild-and-launch-desktop-shell.ps1)" \
  -Configuration Debug
```

Rebuild and launch Release explicitly:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/rebuild-and-launch-desktop-shell.ps1)" \
  -Configuration Release \
  -Language en \
  -TimeoutSeconds 20
```

Launch an already-built specific app path:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/launch-desktop-shell.ps1)" \
   -AppPath "src/OpenCode.Workspace.Avalonia/bin/Debug/net10.0/OpenCode.Workspace.exe"
```

Inspect the running instance:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/inspect-desktop-shell.ps1)"
```

Bring the window forward:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/activate-desktop-shell.ps1)"
```

Capture a screenshot:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/screenshot-desktop-shell.ps1)" \
  -OutputPath "docs/walkthrough/images/debug-desktop-shell.png"
```

Kill repo-scoped manager test instances:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/kill-desktop-shell.ps1)"
```

Kill all manager instances explicitly:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/kill-desktop-shell.ps1)" -AllInstances
```

## Notes

- `launch-desktop-shell.ps1` waits for a visible main window, but it does not crash if the window handle is still null.
- `launch-desktop-shell.ps1` prints the exact executable path and inferred configuration before launch.
- `rebuild-and-launch-desktop-shell.ps1` is the preferred script for "rebuild and run app" because it builds and launches one consistent configuration.
- the app launches Windows Terminal attach sessions with `wt.exe new-tab` and `ProcessStartInfo.ArgumentList`; do not rebuild that handoff from a single manually quoted command string.
- if Windows Terminal exits before handoff completes, use the logged `powershell.exe -NoExit -ExecutionPolicy Bypass -File "<attach-script>"` fallback first to distinguish Windows Terminal integration issues from attach-wrapper or Docker issues.
- `activate-desktop-shell.ps1` retries briefly and prints a warning if no visible window exists.
- `screenshot-desktop-shell.ps1` saves a PNG only when a valid visible window is available.
- `kill-desktop-shell.ps1` defaults to stopping repo-scoped desktop-shell instances only. Use `-AllInstances` if you really want to stop everything.

## Screenshot Guidance

Use `screenshot-desktop-shell.ps1` for the desktop shell when possible.

For other Windows windows such as Windows Terminal or SQL Developer:

1. prepare the exact UI state manually first
2. reuse the existing window instead of reopening or reconfiguring it
3. run PowerShell with a single-quoted script block from WSL/bash so `$_` is preserved literally

Safe process-query pattern:

```bash
powershell.exe -NoProfile -Command '
Get-Process |
Where-Object { $_.MainWindowHandle -ne 0 } |
Select-Object Id,ProcessName,MainWindowTitle |
Format-List
'
```

Avoid double-quoted PowerShell command strings from bash when they contain `$_`, because bash can expand `$` before PowerShell executes the script.

For documentation screenshots:

1. stage the target window manually
2. verify the visible title is correct
3. capture to the final docs or artifacts path directly
4. do not change runtime state unless capture is blocked
