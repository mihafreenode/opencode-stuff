# workspace.yaml

## Purpose

`workspace.yaml` is the canonical definition of a workspace.

It is user-owned, durable, and intended to stay portable between the local-first OpenCode Stuff environment and future hosted Workspace Stuff environments.

## Ownership Rules

- edit `workspace.yaml` to change lasting workspace behavior
- do not edit generated `compose.yaml` for durable changes
- do not rely on container IDs or Windows-specific paths as portable configuration

## Current MVP Shape

```yaml
workspace:
  name: documentation-analysis
  image: ubuntu:24.04

features:
  - core
  - document-processing

skills: []

services:
  - postgres
  - pgadmin

mcp: []

agent:
  profile: opencode-default

terminal:
  font:
    provider: nerd-fonts
    family: JetBrainsMono Nerd Font

  prompt:
    provider: starship

  installIfMissing: true

  utilities:
    zoxide: false
    fzf: false
```

## Field Guide

### `workspace`

Top-level workspace identity and runtime defaults.

- `name`: user-facing and filesystem-friendly workspace name
- `image`: base container image, `ubuntu:24.04` by default in the MVP

### `features`

Human-friendly capability selections. Features expand to provisioning actions such as apt packages or npm packages.

### `skills`

Reserved for reusable OpenCode capability manifests.

### `services`

Additional Compose services such as PostgreSQL or pgAdmin.

### `mcp`

Optional integrations that remain manifest-driven for future expansion.

### `terminal`

Terminal preferences describe the recommended shell experience for the workspace.

- `font`: preferred Windows Terminal Nerd Font for the OpenCode Stuff managed profile
- `prompt`: prompt provider such as `starship` or `default-bash`
- `installIfMissing`: whether recommended terminal tools should be installed automatically during provisioning
- `utilities`: optional helpers such as `zoxide` and `fzf`

### `agent`

Agent preferences keep new workspaces usable immediately.

- `profile`: recommended way to reference a reusable agent profile
- `provider`, `connection`, `model`: advanced overrides when a user intentionally wants to bypass the default profile behavior

If no explicit agent settings exist, OpenCode Stuff resolves the built-in default profile:

- profile: `opencode-default`
- provider: `opencode`
- connection: `zen`
- model: `big-pickle`

## Generated Companions

The application generates these runtime artifacts beside `workspace.yaml`:

- `compose.yaml`
- `.env`
- provisioning scripts under `mounts/config/`

Those files include generated headers that point contributors back to `workspace.yaml` and the catalog manifests.

## v0.1 Default Example

When the UI creates a new workspace in the MVP, it starts from:

- `image: ubuntu:24.04`
- the always-on `core` feature
- any additional features or services selected in the create form
