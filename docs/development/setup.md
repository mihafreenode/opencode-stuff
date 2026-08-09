# Development setup

## Requirements

- Git
- .NET 10 SDK
- Windows 10/11 for desktop-shell, Windows Terminal, and Windows platform validation
- Docker Desktop with the WSL2 backend for live workspace and container tests
- Windows Terminal for manual attach validation
- PowerShell for repository Windows scripts

Linux and macOS can build portable projects and run their native platform tests. They do not replace Windows-host validation of the Windows desktop product. Node.js is not a repository build prerequisite; when a contributor workflow needs it, use modern LTS or newer (the current local baseline is Node.js 22).

## Checkout and build

```bash
dotnet restore OpenCode.Workspace.slnx
dotnet build OpenCode.Workspace.slnx
```

From WSL, build the Windows solution with Windows `dotnet.exe`:

```bash
WINPWD=$(wslpath -w "$PWD")
powershell.exe -NoProfile -Command "Set-Location '$WINPWD'; dotnet build OpenCode.Workspace.slnx"
```

Do not treat Linux `dotnet` output from WSL as Windows desktop validation.

## Repository inputs

Edit canonical inputs rather than generated runtime artifacts:

- `workspace.yaml`
- catalog YAML under `catalog/`
- localization PO files under `Localization/`
- source and tests under `src/` and `tests/`

Generated `compose.yaml`, `.env`, shell scripts, and applied state are outputs. Keep secrets out of source, logs, test artifacts, screenshots, and workspace Save Points.

## First validation

Run portable tests first, then Windows solution tests where applicable. Continue with package and live-runtime validation only after static tests pass. See [Testing](testing.md), [Packaging](packaging.md), and [Windows debugging](windows-debugging.md).
