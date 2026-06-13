# Quick Start Walkthrough

## First 5 Minutes

1. Open OpenCode Workspace Manager.
2. Create a new workspace or use an existing Git checkout.
3. Open the workspace from the list.
4. Wait for the runtime to validate and attach.
5. Create a Save Point before a big change.

![OpenCode Workspace Manager main window](images/main-window.png)

### Runtime Attached

Once the workspace opens, the app attaches a Windows Terminal session to the runtime prepared for that workspace.

Open Workspace resumes the latest OpenCode session when one exists. If no OpenCode session exists, a new one is started.

![OpenCode terminal attached to workspace](images/opencode-terminal-attached.png)

### Working With OpenCode

This is where real work happens: repository analysis, implementation steps, edits, tests, and other agent-assisted tasks.

![OpenCode actively working inside a workspace](images/opencode-terminal-working.png)

## Quick Tutorial In The App

The app can show a short first-run tutorial.

- Start it from the first-run prompt.
- Reopen it later from `Help > Quick Tutorial`.

## Existing Projects

If you already have a Git checkout, use the existing-checkout flow instead of working directly on `main`.

- [Use with an existing Git checkout](existing-git-checkout.md)
