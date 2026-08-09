# ADR 0001: LocalHost Control Plane

- Status: Accepted
- Date: 2026-08-08

## Context

Desktop, MCP, CLI, and browser clients need consistent operation identity, progress, cancellation, workspace state, and interactive-session ownership. In-process orchestration in each client creates conflicting owners and makes client lifetime accidentally control work.

## Decision

LocalHost is the machine-local, loopback-only control plane. `WorkspaceOrchestrator` and canonical shared mutations run inside LocalHost. Controllers register sessions and invoke typed operations; LocalHost persists operation attribution and state independently of controller lifetime.

New shared state and mutations use LocalHost. New Avalonia features must not add direct desktop-to-Core orchestration.

## Current Boundaries

- Avalonia workspace and Runtime Resources shared reads and mutations use LocalHost; native presentation actions remain local to the desktop.
- CLI diagnostic and smoke commands may execute Core locally when they do not require canonical shared mutable state.

These boundaries prevent an inaccurate claim that every client operation requires LocalHost.

## Consequences

LocalHost is a backend and owner, not an optional API sidecar. Clients must discover it through loopback descriptors and tolerate reconnects. LocalHost is never exposed publicly; remote presentation uses RemoteBridge. See [LocalHost](../architecture/local-host.md).
