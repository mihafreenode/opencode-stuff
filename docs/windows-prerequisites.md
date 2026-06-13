# Windows Setup

This guide is for first-time setup on Windows.

The goal is simple: get the machine ready so you can create or open a workspace and start working.

## You Need

1. WSL2 available
2. Docker Desktop installed and running
3. Git installed with credentials configured
4. Windows Terminal installed
5. .NET 10 Desktop Runtime installed

## Quick Install

```powershell
winget install Docker.DockerDesktop
winget install Microsoft.WindowsTerminal
winget install Git.Git
winget install Microsoft.DotNet.DesktopRuntime.10
```

If WSL is not installed yet:

```powershell
wsl --install
```

Reboot if Windows asks you to.

## Verify The Basics

Run these commands:

```powershell
wsl --status
docker version
docker compose version
git --version
wt --version
dotnet --list-runtimes
```

You are ready for first use when:

- `wsl --status` works
- Docker Desktop is running
- `git --version` works
- `wt --version` works
- `Microsoft.WindowsDesktop.App 10.x` appears in `dotnet --list-runtimes`

## Configure Git Credentials

Before creating or publishing workspaces, configure Git with your name and email:

```powershell
git config --global user.name "Your Name"
git config --global user.email "you@example.com"
```

## Next Step

Once the checks pass, continue with:

- [First Workspace Guide](first-workspace.md)
- [WSL Windows Interop Troubleshooting](troubleshooting/wsl-windows-interop.md)

## Where People Usually Get Stuck

### Docker Desktop is installed but not running

Action:

1. Start Docker Desktop.
2. Wait for it to finish starting.
3. Run `docker version` again.

### WSL is missing or not ready

Action:

1. Run `wsl --install` if needed.
2. Reboot if prompted.
3. Run `wsl --status` again.

### Git credentials are missing

Action:

1. Run the `git config --global` commands above.
2. Try again.

### Windows Terminal is missing

Action:

1. Install Windows Terminal.
2. Run `wt --version` again.

### The launcher will not start

Action:

1. Check `dotnet --list-runtimes`.
2. Confirm `Microsoft.WindowsDesktop.App 10.x` is installed.

### Windows executables fail from Ubuntu/WSL

If `cmd.exe`, `powershell.exe`, or `pwsh.exe` are found on `PATH` but fail with `Exec format error`, see [Debugging WSL Windows Interop](troubleshooting/wsl-windows-interop.md).
