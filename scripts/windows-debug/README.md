# Windows Debug Scripts

These scripts help debug the Windows WPF app from WSL without relying on fragile inline PowerShell.

They are debugging-only utilities. They do not change app runtime behavior.

## Scripts

- `rebuild-and-launch-manager.ps1`
- `launch-manager.ps1`
- `activate-manager.ps1`
- `inspect-manager.ps1`
- `screenshot-manager.ps1`
- `kill-manager.ps1`

## WSL Examples

Use `wslpath -w` so PowerShell receives a Windows path to the script.

Rebuild and launch the exact binary you just built:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/rebuild-and-launch-manager.ps1)" \
  -Configuration Debug
```

Rebuild and launch Release explicitly:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/rebuild-and-launch-manager.ps1)" \
  -Configuration Release \
  -Language en \
  -TimeoutSeconds 20
```

Launch an already-built specific app path:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/launch-manager.ps1)" \
  -AppPath "src/OpenCode.Workspace.Manager/bin/Debug/net10.0-windows/OpenCode.Workspace.Manager.exe"
```

Inspect the running instance:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/inspect-manager.ps1)"
```

Bring the window forward:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/activate-manager.ps1)"
```

Capture a screenshot:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/screenshot-manager.ps1)" \
  -OutputPath "docs/walkthrough/images/debug-manager.png"
```

Kill repo-scoped manager test instances:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/kill-manager.ps1)"
```

Kill all manager instances explicitly:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/kill-manager.ps1)" -AllInstances
```

## Notes

- `launch-manager.ps1` waits for a visible main window, but it does not crash if the window handle is still null.
- `launch-manager.ps1` prints the exact executable path and inferred configuration before launch.
- `rebuild-and-launch-manager.ps1` is the preferred script for "rebuild and run app" because it builds and launches one consistent configuration.
- the app launches Windows Terminal attach sessions with `wt.exe new-tab` and `ProcessStartInfo.ArgumentList`; do not rebuild that handoff from a single manually quoted command string.
- if Windows Terminal exits before handoff completes, use the logged `powershell.exe -NoExit -ExecutionPolicy Bypass -File "<attach-script>"` fallback first to distinguish Windows Terminal integration issues from attach-wrapper or Docker issues.
- `activate-manager.ps1` retries briefly and prints a warning if no visible window exists.
- `screenshot-manager.ps1` saves a PNG only when a valid visible window is available.
- `kill-manager.ps1` defaults to stopping repo-scoped manager instances only. Use `-AllInstances` if you really want to stop everything.

## Screenshot Guidance

Use `screenshot-manager.ps1` for the WPF app when possible.

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
