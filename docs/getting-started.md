# Getting Started

This is the authoritative first-use workflow for the current Windows release.

## 1. Check Prerequisites

You need Windows 10 or 11 with hardware virtualization enabled, WSL2, Docker Desktop using its WSL2 backend, Git, and Windows Terminal.

Install common prerequisites from PowerShell if needed:

```powershell
winget install Docker.DockerDesktop
winget install Git.Git
winget install Microsoft.WindowsTerminal
wsl --install
```

Restart Windows if requested. Start Docker Desktop and wait until its engine is ready.

Verify:

```powershell
wsl --status
docker version
docker compose version
git --version
wt --version
```

Configure Git identity before creating Save Points:

```powershell
git config --global user.name "Your Name"
git config --global user.email "you@example.com"
```

Configure HTTPS credentials or SSH access for your Git provider before cloning or publishing. A self-contained Windows release does not require a separately installed .NET runtime.

## 2. Download And Verify

Download these adjacent release assets:

- `opencode-workspace-<version>-win-x64.zip`
- `opencode-workspace-<version>-win-x64.zip.sha256`

Verify the archive:

```powershell
Get-FileHash .\opencode-workspace-<version>-win-x64.zip -Algorithm SHA256
Get-Content .\opencode-workspace-<version>-win-x64.zip.sha256
```

The hexadecimal values must match. Extract the complete ZIP to a stable folder, then run:

```powershell
.\OpenCode.Workspace.exe
```

The package is self-contained. Keep `OpenCode.Workspace.exe` beside its `bin`, `catalog`, `config`, `docs`, and `Localization` directories.

## 3. Create Or Import

Choose one path.

### Create Workspace

1. Select `Create Workspace`.
2. Enter a name and choose a folder.
3. Select a template and any features or services.
4. Review the choices and create the workspace.

The resulting workspace owns its `workspace.yaml`, repository files, and local recovery history.

### Open Existing Repository

1. Select `Open Existing Repository`.
2. Choose an existing local Git checkout.
3. Review its current branch, local changes, remote, and discovered configuration.
4. Prefer a Safe Working Copy when the checkout is on a protected or mainline branch.
5. Complete the import.

The app discovers `workspace.yaml`, `workspace.yml`, `.opencode/profile.yaml`, or `.opencode/profile.yml`. It keeps using the discovered path and does not silently replace repository-owned configuration.

## 4. Prepare And Start

1. Select the workspace.
2. Choose `Open Workspace`.
3. Follow any prompt to generate, update, provision, or start its runtime.
4. Wait for readiness validation to complete.

`Open Workspace` prepares and starts the workspace. It does not automatically open Windows Terminal or attach an interactive session.

If setup fails, open the operation transcript. Provisioning is inspectable in `mounts/config/provision.sh`; do not edit generated files as the durable fix.

## 5. Open A Session

After the workspace is ready:

1. Open the workspace's interactive sessions area.
2. Create a session, or select an existing recoverable session.
3. Choose the attach action.
4. Windows Terminal opens as a presentation of that session.

The provider conversation and terminal runtime are managed separately from the workspace lifecycle. Detaching or closing Windows Terminal does not delete workspace files. See [Sessions](user/sessions.md).

## 6. Protect Progress

Create a Save Point before a risky change and after reaching a useful milestone. Use a Checkpoint when you need additional local recovery capture. Export a Backup for a portable archive, and Publish only when you intentionally want to send a Working Copy to its configured remote.

See [Backup And Publish](user/backup-and-publish.md).

## Next Steps

- [Workspaces](user/workspaces.md)
- [Sessions](user/sessions.md)
- [Troubleshooting](user/troubleshooting.md)
- [Workspace YAML](reference/workspace-yaml.md)
- [Local MCP](integrations/mcp.md)
