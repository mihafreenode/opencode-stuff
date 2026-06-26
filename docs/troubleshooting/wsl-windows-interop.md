# Debugging WSL Windows Interop

Use this guide when Windows executables are visible from Ubuntu/WSL but fail to launch.

## Symptoms

- `cmd.exe`, `powershell.exe`, `pwsh.exe`, or `wsl.exe` are found on `PATH`
- the files exist under `/mnt/c`
- launching them fails with `cannot execute binary file: Exec format error`

## Quick Checks

Run these commands from Ubuntu/WSL:

```bash
uname -a
cat /proc/version
echo "$WSL_INTEROP"
echo "$WSL_DISTRO_NAME"
which cmd.exe
which powershell.exe
cat /proc/sys/fs/binfmt_misc/WSLInterop
```

If `which` finds the executables but `WSLInterop` is missing, the problem is interop registration rather than `PATH`.

## Root Cause

WSL uses `binfmt_misc` so Linux can hand Windows PE executables to the WSL host runtime.

Expected `WSLInterop` details include:

- `interpreter /init`
- `flags: P`
- `magic 4d5a`

If `/proc/sys/fs/binfmt_misc/WSLInterop` is missing, Linux sees `.exe` files as unsupported binaries and returns `Exec format error` instead of invoking Windows.

## Known Cause In This Dev Setup

A local systemd override blocked `systemd-binfmt.service`:

```text
/etc/systemd/system/systemd-binfmt.service.d/00-wsl.conf
```

Contents:

```ini
[Unit]
ConditionVirtualization=!container
```

That local override caused `systemd-binfmt.service` to be skipped, which prevented WSL's generated systemd override from registering `WSLInterop`.

## Safe Fix

Back up and remove the local override, then reload and restart `systemd-binfmt`:

```bash
sudo cp /etc/systemd/system/systemd-binfmt.service.d/00-wsl.conf \
  /etc/systemd/system/systemd-binfmt.service.d/00-wsl.conf.bak

sudo rm /etc/systemd/system/systemd-binfmt.service.d/00-wsl.conf

sudo systemctl daemon-reload
sudo systemctl restart systemd-binfmt.service
```

If `WSLInterop` is still missing afterward, run this from Windows PowerShell:

```powershell
wsl --shutdown
```

Then reopen Ubuntu.

## Verification

Run:

```bash
cat /proc/sys/fs/binfmt_misc/WSLInterop
cmd.exe /c ver
powershell.exe -NoProfile -Command '$PSVersionTable.PSVersion'
pwsh.exe -NoProfile -Command '$PSVersionTable.PSVersion'
```

Expected `WSLInterop` output should include:

```text
enabled
interpreter /init
flags: P
magic 4d5a
```

## Running Windows Validation From WSL

When Windows-specific validation is required, run it through Windows PowerShell rather than Linux `dotnet`.

```bash
WINPWD=$(wslpath -w "$PWD")
powershell.exe -NoProfile -Command "Set-Location '$WINPWD'; dotnet test OpenCode.Workspace.slnx"
```

Use Windows PowerShell for:

- Windows Desktop runtime validation
- packaging
- screenshots
- Windows capability tests

When runtime validation also depends on Docker Desktop, Oracle containers, Windows Terminal, or SQL Developer integration, treat Windows host validation as authoritative even if the initiating workflow started in WSL.

If `docker version` fails in WSL but succeeds through:

```powershell
powershell.exe -NoProfile -Command "docker version"
```

classify that as an environment difference between WSL and Windows host validation, not as a product defect by itself.

Report Linux and Windows results separately. Do not treat Linux `dotnet` results as a substitute for Windows validation.
