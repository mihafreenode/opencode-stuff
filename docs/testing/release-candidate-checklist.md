# Release Candidate Checklist

Use this checklist before tagging a post-WPF Avalonia release candidate.

This checklist is intentionally manual for now. The goal is to validate the packaged desktop app exactly as a user would receive it without blocking the release on full GUI automation.

## Local Windows Release Build

Use the checked-in local release packaging script to reproduce the Windows tagged-release layout before creating a GitHub tag.

Windows PowerShell:

```powershell
.\tools\build-release.ps1 -Clean
```

From WSL:

```bash
./tools/build-release-from-wsl.sh -Clean
```

Direct WSL to Windows PowerShell invocation:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass \
  -File "$(wslpath -w ./tools/build-release.ps1)"
```

The default local build targets `win-x64`, publishes desktop, CLI, API, and MCP hosts, assembles the normal package layout, validates the packaged hosts, and creates a ZIP plus SHA-256 checksum under `artifacts\release`.

Resulting packaged MCP path:

```text
artifacts\release\win-x64\package\
opencode-workspace-<version>-win-x64\
bin\mcp\OpenCode.Workspace.Mcp.exe
```

Use this locally assembled package when testing Codex, Claude Code, and OpenCode MCP integration before creating a GitHub release tag.

## Package Under Test

- Windows package: `opencode-workspace-<version>-win-x64.zip`
- Linux package: `opencode-workspace-linux-x64.tar.gz`
- macOS package: `opencode-workspace-osx-arm64.tar.gz`

Record:

- commit hash
- package filename
- validation date
- validator
- host OS/version

## Windows Package Smoke

- Extract `opencode-workspace-<version>-win-x64.zip` into `C:\Tools\OpenCode Workspace\` or another clean path containing spaces.
- Verify the extracted folder contains `OpenCode.Workspace.exe`.
- Verify `bin\local-host\OpenCode.Workspace.LocalHost.exe`, `bin\cli\OpenCode.Workspace.Cli.exe`, and `bin\mcp\OpenCode.Workspace.Mcp.exe` exist.
- Verify `bin\api` does not exist.
- Verify `hostfxr.dll` is present beside each Windows executable, confirming the package is self-contained.
- Verify the extracted folder contains `catalog/`, `Localization/`, and `docs/`.
- Verify required platform assemblies are present.
- Verify debug symbol files are not included in the release package.
- Launch the app from the extracted folder, not from repo build output.
- Verify the window title is `opencode stuff`.
- Verify the app starts without depending on the repository source tree.
- Verify existing workspace index load works.
- Verify missing-index first run works without crashing.
- Verify startup/build info is discoverable from the package or app diagnostics.

## First Workspace Workflow

- Create a workspace from the packaged app.
- Verify template names are friendly and readable.
- Start the workspace.
- Attach to the workspace.
- Create a Save Point.
- Create a backup.
- Remove the workspace from the list.
- Relaunch the app.
- Verify the expected state after relaunch.

## Recovery Workflow

- Create a disposable workspace for recovery testing.
- Add a user-owned file that must be preserved.
- Damage or delete managed runtime files only.
- Run Recover.
- Verify the user-owned file is preserved.
- Verify runtime files are regenerated.
- Verify transcript and result messaging are clear.

## Publish Workflow

- Create a disposable workspace with a local bare remote.
- Create a Save Point before publishing.
- Publish from the packaged app.
- Verify the remote branch exists afterward.
- Verify publish messaging is clear.

## Prerequisites And Diagnostics

Verify the app surfaces clear, actionable state for:

- Git
- Docker
- Docker Compose
- Windows Terminal
- fonts / Nerd Font
- OpenCode CLI
- template catalog
- runtime platform
- host architecture

Confirm missing prerequisites do not crash the app and clearly explain what the user should install or enable next.

## Archive Checks

Verify backup behavior:

- excludes `.env`
- excludes secret files
- excludes `.git`
- excludes `node_modules`
- excludes `bin`
- excludes `obj`
- excludes large rebuildable outputs
- includes `workspace.yaml`
- includes durable history where expected
- includes docs
- includes `mounts/config`
- includes runtime-related durable metadata where expected

## Sign-Off

- Package smoke passed
- First workspace workflow passed
- Recovery workflow passed
- Publish workflow passed
- Prerequisite diagnostics reviewed
- Known limitations recorded
- Ready to tag release candidate

## Execution Record

Release candidate status: `READY`

Metadata:

- Commit hash: `4d8b9cfd365f5feb6377a55010a7c8d20044ab83`
- Validation date: `2026-06-28T12:48:19+02:00`
- Validator: `OpenCode assistant`
- Host OS/version: `Microsoft Windows NT 10.0.19045.0` for packaged Windows validation, `Linux 6.18.33.1-microsoft-standard-WSL2` for local build/test orchestration
- Packaged application version: `1.0.0.0`
- Packaged application build info: `1.0.0+4d8b9cfd365f5feb6377a55010a7c8d20044ab83`
- Package filenames:
  - `opencode-stuff-win-x64.zip`
  - `opencode-stuff-linux-x64.tar.gz`
  - `opencode-stuff-macos-arm64.tar.gz`

Latest Windows package SHA-256:

- `opencode-stuff-win-x64.zip`: `debe7f5ac23c37e3a13b250e344898001477992de281e6252f72bbacb6c25901`

### Windows Package Smoke

Status: `PASS`

Evidence:

- zip extracted into a clean Windows temp folder
- `OpenCode.Workspace.exe` exists in the extracted package
- `catalog/`, `Localization/`, and `docs/` are present
- required platform assemblies are present
- release package verified without `.pdb` files
- extracted app launched successfully from temp folder
- main window title is `opencode stuff`
- startup completed without depending on repository working directory
- existing workspace index load completed successfully
- missing-index startup completed successfully without crashing
- build/version info is discoverable from Windows file version metadata

### Upgrade Compatibility Note

Historical evidence retained from earlier RC validation:

- existing `%LOCALAPPDATA%\OpenCode.Workspace.Manager` data was preserved
- existing `workspaces.json` continued to load
- existing startup log, tutorial-state, and discovery-log files remained present

Final RC sign-off focused on the blocking A-E release checklist items and did not rerun a full packaged upgrade flow.

### Cross-Platform Package Verification

Status: `PASS`

Windows:

- archive extracts
- executable exists
- expected platform files exist
- startup verified

Linux:

- archive extracts
- executable exists
- expected packaged files exist
- GUI startup not exercised in this environment

macOS:

- archive extracts
- executable exists
- expected packaged files exist
- GUI startup not exercised in this environment

### First Workspace Workflow

Status: `PASS`

Workspace under test:

- name: `rc-first-workspace`
- root path: `C:\Users\miha.pirnat\AppData\Local\Temp\opencode-rc-workspaces`

Verified in packaged app:

- `Create Workspace`: `PASS`
- friendly template naming: `PASS`
  - visible template name: `Data Processing`
  - visible template summary: `Data workspace with PostgreSQL and pgAdmin examples enabled.`
- `Start workspace`: `PASS`
  - workspace record updated to `LastOperationName=Start`
  - workspace record updated to `LastOperationResult=Provisioned and started workspace.`
  - Docker containers started:
    - `rc-first-workspace-workspace`
    - `rc-first-workspace-postgres-1`
    - `rc-first-workspace-pgadmin-1`
- `Attach`: `PASS`
  - startup log recorded `Workspace operation 'Attach' completed provider call with message 'Attach launched for 'rc-first-workspace'.'`
  - existing Windows Terminal process remained active with title `OpenCode`
- `Save Point`: `PASS`
  - packaged dialog opened and accepted message `RC save point before packaged backup and publish`
  - startup log recorded `Workspace operation 'Create Save Point' completed provider call with message 'Save Point created.'`
- `Backup`: `PASS`
  - packaged save dialog opened as `Backup workspace`
  - packaged shell reported `Backup created at 'C:\Users\miha.pirnat\OneDrive - Kopa, racunalniski inzeniring d.d\Dokumenti\rc-first-workspace-20260626-124909.zip' with 23 file(s).`
  - manifest path reported as `C:\Users\miha.pirnat\OneDrive - Kopa, racunalniski inzeniring d.d\Dokumenti\rc-first-workspace-20260626-124909-backup-manifest.yaml`
- `Remove from list`: `PASS`
  - packaged shell reported `Removed 'rc-first-workspace' from the workspace list.`
- `Relaunch and verify expected state`: `PASS`
  - after relaunch, `rc-first-workspace` was no longer present in `workspaces.json`
  - workspace root still existed on disk

Notes:

- stable packaged UI automation names/IDs were required to finish this section reliably
- `Start` and `Attach` remain slower than ideal, but both completed successfully in the packaged flow

### Recovery Workflow

Status: `PASS`

Validated in packaged app:

- after Section A removed `rc-first-workspace` from the list, the packaged app successfully re-imported the same workspace root
- packaged `Open Existing Repository` inspection now succeeds and reports:
  - `Branch: workspace/rc-first-workspace-20260626-1030`
  - `Status: Branch workspace/rc-first-workspace-20260626-1030`
  - `Configuration: Found`
  - `Path: workspace.yaml`
- the packaged shell selected the imported workspace and showed `Imported existing Git checkout 'rc-first-workspace'.`
- a preserved user file was created before recovery:
  - path: `C:\Users\miha.pirnat\AppData\Local\Temp\opencode-rc-workspaces\docs\preserve-me.txt`
  - SHA-256 before recovery: `D213FAC2FAB84E18965D8A90D60015572DFE1D46A46461B4B301E60ED6DF919D`
- managed runtime files were damaged before recovery:
  - deleted: `.opencode\local\runtime-state.yaml`
  - deleted: `compose.yaml`
- packaged `Recover` completed and reported:
  - `Workspace operation 'Recover' completed provider call with message 'Workspace 'rc-first-workspace' runtime was repaired.'`

Recorded evidence:

- workspace root still exists: `C:\Users\miha.pirnat\AppData\Local\Temp\opencode-rc-workspaces`
- `.git` exists at that path
- `workspace.yaml` exists at that path
- Windows host `git status --short --branch` at that path succeeds and reports:
  - `## workspace/rc-first-workspace-20260626-1030`
- preserved user file SHA-256 after recovery: `D213FAC2FAB84E18965D8A90D60015572DFE1D46A46461B4B301E60ED6DF919D`
- `compose.yaml` exists after recovery and was regenerated
- `.opencode\local\runtime-state.yaml` exists after recovery and was regenerated
- packaged UI shows runtime-state status `Loaded (linux/amd64)` after recovery

### Publish Workflow

Status: `PASS`

Validated in packaged app:

- local bare remote configured at `C:\Users\miha.pirnat\AppData\Local\Temp\opencode-rc-remote.git`
- packaged Save Point completed before publish
- packaged Publish confirmation dialog reported:
  - Working Copy: `workspace/opencode-rc-workspaces-20260626-1709`
  - Remote backup: `origin (C:\Users\miha.pirnat\AppData\Local\Temp\opencode-rc-remote.git)`
  - Tracking branch: first publish will create upstream tracking.
  - Ahead/behind: `0/0`
  - Working tree is clean.
- remote branch exists after publish:
  - `workspace/opencode-rc-workspaces-20260626-1709`
- remote commit id after publish:
  - `a75d54b17be2a4233b2ca1a8302ba0932730233b`
- no force-push path was involved

### Prerequisites And Diagnostics

Status: `PASS`

Final packaged diagnostics evidence from the extracted Windows package:

- Diagnostics page opened successfully
- `Run Doctor` completed successfully for `rc-first-workspace`
- required prerequisite rows were visible under `Required Prerequisites`
- required UI automation ids resolved successfully:
  - `Diagnostic_Git`
  - `Diagnostic_Docker`
  - `Diagnostic_DockerCompose`
  - `Diagnostic_WindowsTerminal`
  - `Diagnostic_NerdFont`
  - `Diagnostic_OpenCodeCli`
  - `Diagnostic_TemplateCatalog`
  - `Diagnostic_HostArchitecture`
  - `Diagnostic_RuntimePlatform`
- extracted package diagnostics also showed actionable optional-state rows including:
  - `Podman` -> `Fail`
  - `Cascadia Code` -> `Pass`
  - `JetBrains Mono` -> `Pass`
- copied packaged doctor evidence remained valid and included the stable `Diagnostic_*` labels

Historical note:

- an earlier RC pass failed here before the dedicated packaged diagnostics rows and automation ids were added; that failure is superseded by the final extracted-package rerun above

### Archive Checks

Status: `PASS`

Validated:

- release package excludes debug symbol files
- created workspace contains expected durable structure at the chosen root:
  - `workspace.yaml`
  - `docs/`
  - `history/`
  - `mounts/`
  - `artifacts/`
  - `.opencode/`
- packaged backup archive verification passed for the recorded RC backup:
  - excludes `.env`
  - excludes `.git` content
  - excludes `.opencode/local/runtime-state.yaml`
  - includes `workspace.yaml`
  - includes `history/timeline.yaml`
  - includes `docs/`
  - includes `mounts/config/`
  - includes runtime-related durable metadata including `runtimes/default.yaml` and `mounts/config/applied-state.yaml`
- `node_modules`, `bin`, `obj`, and `credentials.json` were not present in the validated workspace snapshot, so there were no matching workspace items to exclude in that archive

### Sign-Off Result

Status: `PASS`

Summary:

- package smoke passed
- cross-platform package integrity passed
- full build/test validation is green on Windows-host `Release`
- packaged GUI workflow checklist execution completed for the release-blocking A-E sections
- final extracted Windows package rerun passed launch, workspace load, diagnostics, and `Create Workspace`
- release candidate is ready to tag as `v0.2.0-avalonia`
