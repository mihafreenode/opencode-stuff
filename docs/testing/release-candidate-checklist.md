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
