# Paths And State

## Installation Root

The extracted package root contains `OpenCode.Workspace.exe`, `bin/`, `catalog/`, `config/`, `docs/`, and `Localization/`. See [Package Layout](package-layout.md).

## App Data

The application intentionally retains its historical app-data folder for compatibility:

```text
Windows: %LOCALAPPDATA%\OpenCode.Workspace.Manager
Linux/macOS: the platform LocalApplicationData location/OpenCode.Workspace.Manager
```

Important entries include:

```text
workspaces.json
avalonia-startup.log
operation-transcripts/
local-host/host.json
local-host/host.lock
workspace-instances/
controller-sessions/
interactive-agent-sessions/
operations/
remote-bridge/appsettings.json
```

`workspaces.json` is the local registration index, not the durable workspace content. LocalHost descriptors, locks, session records, and operation records are machine-local state.

## Workspace Root

Important durable or user-reviewable locations:

```text
workspace.yaml                 canonical configuration in new workspaces
.git/                          Git history and Working Copies
history/timeline.yaml          workspace activity timeline
history/checkpoints/           checkpoint snapshots and index
artifacts/                     durable run artifacts and index
runtimes/                      runtime definitions
mounts/inbox/                  workspace inbox
mounts/user/                   durable user mount
mounts/home/                   durable runtime home mount
```

An imported repository may keep configuration at `workspace.yml`, `.opencode/profile.yaml`, or `.opencode/profile.yml` instead.

## Generated And Machine-Local Workspace State

```text
compose.yaml
.env
attach-workspace.ps1
attach-diagnostics.log
terminal-diagnostics.ps1
mounts/config/provision.sh
mounts/config/starship.toml
mounts/config/opencode-shell-init.sh
mounts/config/opencode-workspace-shell.sh
mounts/config/screenrc
mounts/config/applied-state.yaml
.opencode/local/runtime-state.yaml
```

Generated files describe the resolved runtime plan and can be recreated. `.opencode/local/` is machine-local and ignored by Git. `applied-state.yaml` records the successfully applied desired/generated state so a simple restart does not incorrectly imply an update.

`attach-workspace.ps1` and `screenrc` remain generated for compatibility with older workspace attachment paths. The canonical interactive terminal is LocalHost-owned; new terminal behavior must not depend on those compatibility files.

Do not delete generated or local state as a substitute for using the app's diagnostics, prepare, repair, or rebuild actions. Do not expect runtime repair to restore deleted durable files.

## Backup Boundary

Full backup exports include `backup-manifest.yaml` so content can be reviewed as durable, generated, or ephemeral. Store the archive outside the workspace before destructive removal.
