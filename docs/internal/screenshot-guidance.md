# Screenshot Guidance

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

For Oracle demo documentation screenshots:

1. prepare the exact target state first
2. keep the existing window and connection state in place
3. capture the WPF app with `scripts/windows-debug/screenshot-manager.ps1` when the target is the manager window
4. capture Windows Terminal or SQL Developer by targeting the existing visible window
5. use single-quoted PowerShell script blocks from WSL/bash when a command contains `$_`
6. save directly to the final `artifacts/screenshots/` or docs image path

Do not reopen SQL Developer, reprovision Oracle, or modify the Oracle runtime just to take a screenshot unless the capture workflow is blocked.
