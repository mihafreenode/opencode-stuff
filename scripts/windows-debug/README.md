# Windows debug scripts

These debugging-only scripts rebuild, launch, activate, inspect, capture, or stop repository desktop-shell instances from Windows or WSL:

- `rebuild-and-launch-desktop-shell.ps1`
- `launch-desktop-shell.ps1`
- `activate-desktop-shell.ps1`
- `inspect-desktop-shell.ps1`
- `screenshot-desktop-shell.ps1`
- `kill-desktop-shell.ps1`

Use `wslpath -w` when invoking them through `powershell.exe`. `kill-desktop-shell.ps1` is repo-scoped by default; do not use `-AllInstances` casually.

The canonical commands, safety rules, troubleshooting order, and screenshot procedure are in [Windows debugging](../../docs/development/windows-debugging.md).
