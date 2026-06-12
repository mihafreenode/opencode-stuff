# Architecture

## Goal

OpenCode Workspace Manager creates and operates local OpenCode workspaces on Windows by generating runtime artifacts from a canonical YAML definition.

The design optimizes for:

- readability over cleverness
- official sources over custom images
- user-owned YAML over opaque application state
- portable definitions over machine-specific runtime details

## High-Level Design

The MVP is split into two projects:

- `OpenCode.Workspace.Core`: portable domain and generation logic
- `OpenCode.Workspace.Manager`: WPF shell plus Windows-only runtime behavior

This separation exists so the core logic can stay useful if future Workspace Stuff services consume the same portable artifacts.

## Canonical Vs Generated Artifacts

### Canonical and portable

- `workspace.yaml`
- built-in catalog manifests under `catalog/`
- default agent profile resolution rules

### Generated implementation details

- `compose.yaml`
- `.env`
- provisioning scripts
- helper files under `mounts/config/`

### Disposable runtime details

- Docker container IDs
- current container process state
- Windows-specific UI state

## Workspace Lifecycle

1. The user creates or opens a workspace.
2. The application reads or writes `workspace.yaml`.
3. Built-in manifests expand selected features and services into packages and service containers.
4. Agent configuration resolves from workspace settings, user preferences, future catalog defaults, or the built-in OpenCode default profile.
5. The application generates `compose.yaml`, `.env`, and provisioning scripts.
6. Docker Compose starts the workspace and optional service containers.
7. Provisioning installs core tools and OpenCode inside the Ubuntu container and creates the `opencode` Linux user.
8. Windows Terminal attaches to the running workspace container as the `opencode` user.
9. A generated workspace shell helper recreates or reattaches the `opencode` screen session.
10. Inside that loop, OpenCode is restored with `opencode -s` and restarted if it exits.

## Why Provisioning Is Script-Based

The project uses official Ubuntu LTS images instead of maintaining a custom development image pipeline.

That keeps the build chain inspectable and contributor-friendly:

- contributors can see exactly what gets installed
- generated scripts document the provisioning plan
- the workspace definition stays more portable than a custom image

## Why Docker Exec Is Used For Attach

The attach flow uses `docker exec` against the exact running workspace container.

That is more reliable than `docker compose exec` for a Windows desktop app because it avoids:

- compose working-directory requirements
- WSL path translation issues
- ambiguity about which workspace container should receive the attach session

## Current v0.1 Runtime Artifacts

For each workspace the MVP generates:

- `workspace.yaml`
- `compose.yaml`
- `.env`
- `mounts/config/provision.sh`

Each generated file starts with a header comment that points contributors back to the canonical inputs.
