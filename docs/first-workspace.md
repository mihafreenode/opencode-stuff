# First Workspace Guide

This guide is for first-time use.

If WSL, Docker Desktop, SSH access, and Git credentials are already working, you should be able to create a workspace, open it, start the runtime, and begin working without extra setup.

## First 5 Minutes

1. Open OpenCode Stuff.
2. Create a workspace, or open an existing one.
3. Open the workspace.
4. Wait for the runtime to become ready.
5. Start working in the terminal session.

If anything blocks that flow, use [Troubleshooting](troubleshooting.md).

## Before You Start

Confirm that you have:

- WSL installed
- Docker Desktop installed and running
- Docker Desktop WSL integration enabled for your Ubuntu distribution
- Git credentials configured
- SSH access configured for your Git provider
- Windows Terminal installed
- .NET 10 Desktop Runtime installed

If you still need setup help, use [Windows Setup](windows-prerequisites.md), especially the `Configure SSH Access` and `Docker Desktop And WSL Integration` sections.

## Create A Workspace

1. Open OpenCode Stuff.
2. Select `Create Workspace`.
3. Enter a workspace name.
4. Keep the suggested workspace folder unless you have a reason to change it.
5. Choose the features or services you want.
6. Create the workspace.

The app will:

- create the workspace folder
- initialize local recovery
- create the initial Save Point
- prepare the runtime files

If you already have a workspace folder, you can open it instead. The app will initialize local recovery for plain folders automatically.

## Open The Workspace

1. Select the workspace in the list.
2. Choose `Open Workspace`, or double-click the workspace.

The app will:

- start the runtime if needed
- validate that it is ready
- open the terminal session

## Start Working

Once the terminal opens, you are working inside the runtime attached to the workspace.

The repository should remain the durable source of onboarding knowledge:

```text
Repository
    ↓
Workspace Discovery
    ↓
Provision Environment
    ↓
Read Documentation
    ↓
Start Working
```

Featured learning paths in this repository include:

- [Oracle Family](../README.md#oracle-family)
- [Analytics & Reporting Workspace](analytics-workspace.md)
- [Education & STEM Workspace](education-stem-workspace.md)
- [Philosophy](philosophy.md)
- [Design Principles](design-principles.md)

Those philosophy and design guides explain why onboarding, specifications, examples, tests, and recovery guidance should act as visible maps of the workspace rather than hidden setup knowledge.

They also point from first use toward deeper understanding through the [Capability Catalog](capabilities/README.md), [Save Points](concepts/save-point.md), and [AGENTS.md Guide](agents-guide.md).

Use the workspace details panel to understand:

- current workspace status
- current safety status
- current Working Copy
- latest Save Point
- whether remote backup is configured

You do not need to understand Docker or Git internals to use these parts of the UI.

## Protect Your Work

Use these actions regularly:

- `Create Save Point` to record local progress
- `Create Checkpoint` when you want extra local recovery
- `Publish` when you want off-machine backup

You do not need to publish every change. Local-only workspaces are valid.

Recommended habit:

1. Create a Save Point before a big change.
2. Create another Save Point when the change is in a good state.
3. Publish when you want off-machine backup.

## If Something Looks Wrong

Start with:

- the workspace safety card in the app
- [Troubleshooting](troubleshooting.md)

If the app says local work is protected but remote backup is not configured, you can continue working safely and configure backup later.

If the app says `Needs Review`, your local work is still safe. It means publishing needs review before anything is sent to the remote backup.
