# Windows debugging

Use the checked-in scripts under `scripts/windows-debug/` from WSL instead of fragile inline PowerShell. For a fresh build, stop repo-scoped test instances, wait for the build to complete, then launch the exact output; never launch stale output in parallel with a rebuild.

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/rebuild-and-launch-desktop-shell.ps1)" -Configuration Debug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/inspect-desktop-shell.ps1)"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/activate-desktop-shell.ps1)"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w scripts/windows-debug/screenshot-desktop-shell.ps1)" -OutputPath "artifacts/screenshots/desktop-shell.png"
```

`launch-desktop-shell.ps1` launches an already-built path; `kill-desktop-shell.ps1` defaults to repo-scoped instances. Do not use `-AllInstances` unless stopping every manager instance is explicitly intended. Automated agents must not kill Windows Terminal, the host shell, or unrelated app processes.

## Debugging order

When an action appears to do nothing, inspect evidence before adding click retries:

1. Startup diagnostics and application responsiveness.
2. Command `CanExecute` and command-start logging.
3. Unhandled, task, and dialog-creation exceptions with stack traces.
4. Manual reproduction.
5. UI automation only after exception paths are understood.

The main window must remain interactive while background refresh runs. Treat `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` on startup UI paths as high-priority defects.

Windows Terminal attach uses `ProcessStartInfo.ArgumentList` to launch the packaged CLI presentation helper. If handoff exits early, inspect the structured helper arguments, LocalHost attachment state, and operation transcript before changing quoting. The generated `attach-workspace.ps1` path is compatibility-only and is not the canonical terminal-runtime path.

## Screenshots

Use real screenshots and `screenshot-desktop-shell.ps1` for the app where possible. Stage and verify the exact visible state first, reuse existing Terminal or SQL Developer windows, and capture directly to the final docs or `artifacts/screenshots/` path. Do not reprovision or alter runtime state only to take a screenshot.

When querying Windows processes from WSL/bash, use a single-quoted PowerShell script block so bash does not expand `$_`:

```bash
powershell.exe -NoProfile -Command '
Get-Process |
Where-Object { $_.MainWindowHandle -ne 0 } |
Select-Object Id,ProcessName,MainWindowTitle |
Format-List
'
```

Before walkthrough capture, verify Create Workspace, Help/Quick Tutorial, startup diagnostics, and basic navigation are responsive. Never generate fake screenshots; mark unavailable images as `TODO`.
