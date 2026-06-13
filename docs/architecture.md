# Architecture

This document explains how OpenCode Stuff implements the Durable Workspace model.

Read this after the README and concept docs if you want the implementation view.

Core idea:

- Workspace: durable body of work
- Runtime: replaceable tool environment
- Session: temporary execution

Git provides the persistence engine underneath. Generated runtime artifacts and Docker-backed execution remain implementation details.

## Goal

OpenCode Workspace Manager creates and operates durable local workspaces on Windows by combining a canonical YAML definition, Git-backed persistence, and generated runtime artifacts.

The design optimizes for:

- readability over cleverness
- official sources over custom images
- user-owned YAML over opaque application state
- portable definitions over machine-specific runtime details
- recoverability over convenience

## High-Level Design

The MVP is split into two projects:

- `OpenCode.Workspace.Core`: portable domain and generation logic
- `OpenCode.Workspace.Manager`: WPF shell plus Windows-only runtime behavior

This separation exists so the durable workspace logic stays portable while the Windows app handles host-specific runtime behavior.

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

### Durable persistence details

- Git repository state inside the workspace
- Save Points and timeline events
- checkpoint metadata under `history/`

## Workspace Lifecycle

1. The user creates or opens a workspace.
2. The application reads or writes `workspace.yaml`.
3. Git provides local durability, working-copy history, and optional remote backup.
4. Built-in manifests expand selected features and services into packages and service containers.
5. Agent configuration resolves from workspace settings, user preferences, future catalog defaults, or the built-in OpenCode default profile.
6. The application generates `compose.yaml`, `.env`, and provisioning scripts.
7. Docker Compose starts the workspace and optional service containers.
8. Provisioning installs core tools and OpenCode inside the Ubuntu container and creates the `opencode` Linux user.
9. Windows Terminal attaches to the running workspace container as the `opencode` user.
10. A generated workspace shell helper recreates or reattaches the `opencode` screen session.
11. Inside that loop, OpenCode is restored with `opencode -s` and restarted if it exits.

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
