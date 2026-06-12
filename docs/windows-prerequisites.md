# Windows Prerequisites

## Overview

OpenCode Stuff runs OpenCode inside Docker on Windows.

That means the Windows WPF app depends on a working Docker Desktop + WSL2 setup, a supported terminal, and the .NET Desktop Runtime needed to run the launcher itself.

## Required Components

You need:

1. hardware virtualization enabled in BIOS or UEFI
2. Docker Desktop for Windows
3. WSL2 backend enabled for Docker Desktop
4. Windows Terminal
5. .NET 10 Desktop Runtime

## Recommended Install Commands

```powershell
winget install Docker.DockerDesktop
winget install Microsoft.WindowsTerminal
winget install Microsoft.DotNet.DesktopRuntime.10
```

If the .NET package id differs on your system:

```powershell
winget search "Microsoft .NET Desktop Runtime 10"
```

## Virtualization

Docker Desktop on Windows depends on WSL2, and WSL2 depends on hardware virtualization.

### How to check virtualization

- open Task Manager
- go to the `Performance` tab
- select `CPU`
- confirm `Virtualization: Enabled`

### High-level BIOS or UEFI guidance

If virtualization is disabled:

1. reboot into BIOS or UEFI firmware settings
2. look for Intel VT-x, Intel Virtualization Technology, SVM Mode, or AMD-V
3. enable the virtualization option
4. save and reboot

The exact setting name varies by motherboard and laptop vendor.

## Docker Desktop

Install Docker Desktop and make sure it is configured to use WSL2.

### How to confirm WSL2 backend

Open Docker Desktop settings and verify:

- `Use the WSL 2 based engine` is enabled

### Commands to verify Docker

```powershell
docker version
docker compose version
```

If Docker Desktop has just enabled WSL2 integration, a reboot may be required.

## WSL2

Verify WSL is available and using version 2.

```powershell
wsl --status
```

Expected output should indicate:

- a default distribution
- default version `2`

## Windows Terminal

Install Windows Terminal for the interactive attach experience.

Verification command:

```powershell
wt --version
```

If `wt` is not found, install Windows Terminal or enable its App Execution Alias.

## .NET 10 Desktop Runtime

The WPF launcher requires the Windows Desktop runtime.

Verification command:

```powershell
dotnet --list-runtimes
```

Look for a line similar to:

```text
Microsoft.WindowsDesktop.App 10.x
```

## Verify Everything Together

These commands are useful after setup:

```powershell
wsl --status
docker version
docker compose version
wt --version
dotnet --list-runtimes
```

## Common Failures

### Virtualization disabled

Symptoms:

- WSL2 unavailable
- Docker Desktop cannot start correctly

Fix:

- enable virtualization in BIOS or UEFI
- reboot Windows

### Docker Desktop installed but engine unreachable

Symptoms:

- `docker version` shows client output but server connection fails
- OpenCode Stuff reports Docker Engine unavailable

Fix:

- start Docker Desktop
- wait for startup to complete
- confirm WSL2 backend is enabled

### WSL2 unavailable

Symptoms:

- `wsl --status` fails
- Docker Desktop reports WSL issues

Fix:

- enable WSL and Virtual Machine Platform features
- reboot if prompted
- make sure Docker Desktop uses the WSL2 backend

### Windows Terminal missing

Symptoms:

- attach cannot launch
- `wt --version` fails

Fix:

- install Windows Terminal
- enable the App Execution Alias if necessary

### .NET Desktop Runtime missing

Symptoms:

- the launcher does not start
- `Microsoft.WindowsDesktop.App 10.x` is missing from `dotnet --list-runtimes`

Fix:

- install the .NET 10 Desktop Runtime

### Nerd Font installed incorrectly

Symptoms:

- OpenCode launches but decorative glyphs render as diamonds or replacement characters
- the OpenCode Stuff terminal profile exists but the configured font face is not actually registered in Windows

Fix:

- install the recommended Nerd Font from the app or manually
- confirm the font family is registered, not just copied into the fonts folder
- reopen Windows Terminal after installation
