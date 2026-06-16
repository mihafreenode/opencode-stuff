# Windows Setup

This guide is for first-time setup on Windows.

The goal is simple: get the machine ready so you can create or open a workspace and start working.

## You Need

1. WSL2 available
2. Docker Desktop installed and running
3. Docker Desktop WSL integration enabled for your Ubuntu distribution
4. WSL version compatible with Docker Desktop
3. Git installed with credentials configured
4. SSH key configured for your Git hosting provider
5. Windows Terminal installed
6. .NET 10 Desktop Runtime installed

## Quick Install

```powershell
winget install Docker.DockerDesktop
winget install Microsoft.WindowsTerminal
winget install Git.Git
winget install Microsoft.DotNet.DesktopRuntime.10
```

Optional:

```powershell
winget install TortoiseGit.TortoiseGit
```

TortoiseGit is optional, but some developers find it useful for visual Git history, visual merge support, branch management, and tag management.

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

## Configure SSH Access

Many people configure Git name and email successfully, then get stuck later when clone, fetch, or push uses SSH.

### Default SSH Location

```text
%USERPROFILE%\.ssh
```

Typical files:

```text
id_ed25519
id_ed25519.pub

or

id_rsa
id_rsa.pub
```

For new keys, prefer `ed25519`.

### Generate SSH Key

```powershell
ssh-keygen -t ed25519 -C "you@example.com"
```

### Display Public Key

```powershell
type %USERPROFILE%\.ssh\id_ed25519.pub
```

### Verify Connectivity

```powershell
ssh -T git@github.com
```

The exact SSH host depends on your Git provider.

## Optional Documentation Tools

Most project documentation is written in Markdown (`.md`).

### Simple Approach

Many users prefer:

- Notepad++
- Notepad3
- Sublime Text

combined with:

- Google Chrome
- Microsoft Edge

using a Markdown Viewer extension.

This provides a fast workflow for reading onboarding guides, architecture documents, tutorials, and workspace documentation without installing a full development environment.

### Visual Studio Code

Visual Studio Code is a popular option for users who regularly edit documentation.

Benefits include:

- Markdown preview
- Git integration
- repository-wide search
- extension support

Optional installation:

```powershell
winget install Microsoft.VisualStudioCode
```

### Documentation-Only Users

If your primary goal is reading onboarding guides, architecture documents, tutorials, or workspace documentation, a browser with Markdown viewing support is often sufficient.

## Next Step

Once the checks pass, continue with:

- [First Workspace Guide](first-workspace.md)
- [WSL Windows Interop Troubleshooting](troubleshooting/wsl-windows-interop.md)

## Docker Desktop And WSL Integration

Docker Desktop can be installed and running while Ubuntu integration is still disabled.

### Check WSL

```powershell
wsl --status
wsl -l -v
```

Expected example:

```text
Ubuntu-24.04           Running   2
docker-desktop         Running   2
docker-desktop-data    Running   2
```

- Ubuntu should use WSL2.
- Docker Desktop relies on WSL2.

### Verify Docker

```powershell
docker version
docker compose version
```

### Verify WSL Integration

In Docker Desktop, open:

`Settings` -> `Resources` -> `WSL Integration`

Confirm `Ubuntu-24.04` or your selected distribution is enabled.

Disabled integration can cause workspace provisioning failures even when Docker itself appears healthy.

## Keep WSL Updated

Sometimes Docker Desktop expects newer WSL components than the machine currently has.

Check version:

```powershell
wsl --version
```

Update:

```powershell
wsl --update
```

Restart WSL:

```powershell
wsl --shutdown
```

Windows updates, Docker Desktop upgrades, and WSL upgrades can occasionally leave components out of sync. Updating WSL is a common repair step.

## Optional WSL Resource Limits

If WSL is using too much memory on a developer workstation, you can set a simple cap in:

```text
%USERPROFILE%\.wslconfig
```

Example:

```ini
[wsl2]
memory=8GB
processors=4
swap=2GB
```

Apply changes:

```powershell
wsl --shutdown
```

This is often useful on 16 GB and 32 GB systems. It limits maximum WSL memory usage, and active WSL sessions restart when WSL shuts down.

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

### Docker Desktop WSL integration is disabled

Symptoms:

- Docker appears healthy
- Workspace provisioning fails
- Containers fail to start correctly

Action:

1. Open Docker Desktop.
2. Go to `Settings` -> `Resources` -> `WSL Integration`.
3. Enable Ubuntu integration.

### SSH authentication fails

Symptoms:

- Clone fails
- Fetch fails
- Push fails

Action:

1. Verify the SSH key exists under `%USERPROFILE%\.ssh`.
2. Verify the public key is registered with your Git provider.
3. Test connectivity with `ssh -T`.

### WSL version is too old

Symptoms:

- Docker integration issues
- Unexpected startup failures
- Workspace provisioning problems

Action:

```powershell
wsl --update
wsl --shutdown
```

Then restart Docker Desktop.

### WSL uses too much memory

Symptoms:

- Windows becomes slow
- High memory usage
- Docker performance issues

Action:

1. Configure `%USERPROFILE%\.wslconfig`.
2. Run `wsl --shutdown`.
3. Start Docker Desktop again.

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

## Full Health Check

If you need to report a setup issue or collect the basics before asking for help, run:

```powershell
wsl --status
wsl -l -v
wsl --version
docker version
docker compose version
git --version
ssh -V
wt --version
dotnet --list-runtimes
```

This output is useful when reporting setup issues or requesting support.

## Future App Checks

Future versions of the application may validate Git installation, SSH configuration, WSL installation, WSL version, Docker availability, Docker Desktop WSL integration, and available system resources from inside the app itself.
