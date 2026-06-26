# Release Candidate Checklist

Use this checklist before tagging a post-WPF Avalonia release candidate.

This checklist is intentionally manual for now. The goal is to validate the packaged desktop app exactly as a user would receive it without blocking the release on full GUI automation.

## Package Under Test

- Windows package: `opencode-stuff-win-x64.zip`
- Linux package: `opencode-stuff-linux-x64.tar.gz`
- macOS package: `opencode-stuff-macos-arm64.tar.gz`

Record:

- commit hash
- package filename
- validation date
- validator
- host OS/version

## Windows Package Smoke

- Extract `opencode-stuff-win-x64.zip` into a clean temp folder.
- Verify the extracted folder contains `OpenCode.Workspace.Avalonia.exe`.
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

Release candidate status: `NOT READY`

Metadata:

- Commit hash: `13811ec8f2f7f6e582af0f836b82c68b65153af9`
- Validation date: `2026-06-26T10:51:17+02:00`
- Validator: `OpenCode assistant`
- Host OS/version: `Microsoft Windows NT 10.0.19045.0` for packaged Windows validation, `Linux 6.18.33.1-microsoft-standard-WSL2` for local build/test orchestration
- Packaged application version: `1.0.0.0`
- Packaged application build info: `1.0.0+13811ec8f2f7f6e582af0f836b82c68b65153af9`
- Package filenames:
  - `opencode-stuff-win-x64.zip`
  - `opencode-stuff-linux-x64.tar.gz`
  - `opencode-stuff-macos-arm64.tar.gz`

SHA-256:

- `opencode-stuff-win-x64.zip`: `e7b65645bb0ccd24fced962335d1cb56fdea4ede50cea112c14144805fe48763`
- `opencode-stuff-linux-x64.tar.gz`: `9bec255654a3c54eb44e5ceae35a8572787547c25682353662bdff1252043533`
- `opencode-stuff-macos-arm64.tar.gz`: `12aa11ac2bdd617f89877629b4e65c44d06f33b8945090835ec225c4bd865874`

### Windows Package Smoke

Status: `PASS`

Evidence:

- zip extracted into a clean Windows temp folder
- `OpenCode.Workspace.Avalonia.exe` exists in the extracted package
- `catalog/`, `Localization/`, and `docs/` are present
- required platform assemblies are present
- release package verified without `.pdb` files
- extracted app launched successfully from temp folder
- main window title is `opencode stuff`
- startup completed without depending on repository working directory
- existing workspace index load completed successfully
- missing-index startup completed successfully without crashing
- build/version info is discoverable from Windows file version metadata

### Upgrade Compatibility

Status: `FAIL`

Evidence:

- existing `%LOCALAPPDATA%\OpenCode.Workspace.Manager` is preserved
- existing `workspaces.json` continues to load
- existing startup log and tutorial-state files remain present
- existing discovery log remains present
- existing settings and history consumption were not fully exercised through the packaged GUI in this pass

Blocking note:

- no existing user data appears orphaned, but upgrade compatibility is not fully signed off until packaged GUI workflow validation is completed manually

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

Status: `FAIL`

Failure point:

- after Section A removed `rc-first-workspace` from the list, the packaged app was used to re-import the same disposable workspace root for recovery testing
- packaged `Open Existing Repository` inspection reported:
  - `The selected folder is not a Git checkout.`
  - `Repository inspection failed.`

Recorded evidence:

- workspace root still exists: `C:\Users\miha.pirnat\AppData\Local\Temp\opencode-rc-workspaces`
- `.git` exists at that path
- `workspace.yaml` exists at that path
- Windows host `git status --short --branch` at that path succeeds and reports:
  - `## workspace/rc-first-workspace-20260626-1030`
- packaged import dialog UIA dump shows:
  - `The selected folder is not a Git checkout.`
  - `Repository inspection failed.`

Smallest likely remaining blocker:

- packaged existing-checkout inspection is falsely classifying a valid packaged-created workspace root as not being a Git checkout after remove-from-list/relaunch

Further recovery checks were not run after this failure:

- damage managed runtime files
- packaged Recover
- preserved user file hash verification
- regenerated runtime file verification

### Publish Workflow

Status: `FAIL`

Not completed in this pass:

- local bare remote setup
- packaged Save Point and Publish flow
- remote branch verification from packaged GUI

### Prerequisites And Diagnostics

Status: `FAIL`

Validated:

- packaged app does not crash when the workspace index is missing
- shared doctor/discovery backend returns actionable output
- existing Windows app-data path remains in use
- packaged UI shows diagnostics navigation and doctor/validation actions

Not fully validated from the packaged desktop surface:

- Git
- Docker
- Docker Compose
- Windows Terminal
- fonts / Nerd Font
- OpenCode CLI
- template catalog
- runtime platform
- host architecture

Blocking note:

- prerequisite handling is likely correct in shared services, but the packaged GUI checklist still needs manual execution and recording

### Archive Checks

Status: `FAIL`

Validated:

- release package excludes debug symbol files
- created workspace contains expected durable structure at the chosen root:
  - `workspace.yaml`
  - `docs/`
  - `history/`
  - `mounts/`
  - `artifacts/`
  - `.opencode/`

Not completed in this pass:

- packaged backup archive content verification for `.env`, secrets, `.git`, `node_modules`, `bin`, `obj`, large files, `workspace.yaml`, history, docs, `mounts/config`, and runtime-related durable metadata

### Sign-Off Result

Status: `FAIL`

Summary:

- package smoke passed
- cross-platform package integrity passed
- full build/test validation is green
- packaged GUI workflow checklist execution is still pending
- release tag must wait for manual checklist completion and recorded results
