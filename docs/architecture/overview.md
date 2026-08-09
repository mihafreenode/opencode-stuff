# Architecture Overview

This is the authoritative overview of the current OpenCode Stuff architecture. Detailed ownership rules are linked below; accepted decisions are indexed in [ADRs](../adr/README.md).

OpenCode Stuff separates durable work from replaceable execution:

- a workspace is the durable body of work
- a runtime is a replaceable tool environment
- a session is a bounded interaction with that environment

`workspace.yaml` and repository content are durable inputs. Generated runtime files, containers, terminal processes, and machine-local caches are replaceable.

## System Shape

```text
Presentation clients and controllers
               |
               v
           LocalHost
               |
               v
              Core
```

Presentation clients include the Avalonia desktop, the LocalHost browser terminal, the Windows terminal helper, and the remote browser through RemoteBridge. Controllers include desktop application services and local MCP clients. LocalHost is the loopback control plane and runtime owner. Core supplies portable models, generation, diagnostics, persistence, and orchestration; `WorkspaceOrchestrator` runs inside LocalHost.

This is an ownership boundary, not a claim that every client operation requires LocalHost:

- Avalonia workspace and Runtime Resources shared reads and mutations use LocalHost
- platform presentation actions such as opening a service URL remain local to the desktop
- CLI diagnostic and smoke commands may execute Core locally when shared mutable state is not required

See [LocalHost](local-host.md), [Desktop](desktop.md), and [ADR 0001](../adr/0001-localhost-control-plane.md).

## Current Architecture Invariants

- New shared workspace state and mutations go through LocalHost.
- New Avalonia features must not add direct desktop-to-Core orchestration.
- LocalHost is loopback-only and must never be exposed directly to a LAN, tunnel, or public network.
- Remote access reaches the terminal presentation surface only through RemoteBridge; RemoteBridge is not a general LocalHost proxy.
- LocalHost owns canonical operations, workspace instances, interactive sessions, terminal runtimes, attachments, transcripts, and provider process lifetime.
- Presentation helpers render and relay terminal I/O; a helper never owns or supervises the provider.
- Detaching a presentation does not stop its terminal runtime or provider session.
- MCP is a local control protocol. PTY bytes never travel through MCP, and MCP is not exposed remotely.
- `ProviderSessionId` is provider identity and remains separate from OpenCode Stuff controller, interactive-session, runtime, and attachment identities.
- `workspace.yaml` remains canonical and portable; generated files and `.opencode/local/` state are not durable workspace identity.
- Git operations preserve work by default: Publish is explicit, conflicts stop automation, and restore defaults to a copy.

## Components

- `OpenCode.Workspace.Core`: portable domain models, catalog resolution, generation, diagnostics, Git persistence, and workspace orchestration
- `OpenCode.Workspace.Api` source project, packaged as `OpenCode.Workspace.LocalHost`: LocalHost application services, canonical operation state, workspace instances, sessions, terminal runtimes, and loopback HTTP/streaming contracts
- `OpenCode.Workspace.LocalClient`: LocalHost discovery and typed client contracts
- `OpenCode.Workspace.Avalonia`: Windows desktop presentation and application-service adapters
- `OpenCode.Workspace.Platform*`: explicit host integrations
- `OpenCode.Workspace.RemoteBridge`: optional, narrow remote terminal presentation adapter behind Cloudflare Access and Tunnel
- `OpenCode.Workspace.Cli`: local diagnostics, smoke tooling, and LocalHost clients where shared state is involved
- `OpenCode.Workspace.Mcp`: local stdio controller backed by LocalHost; never a terminal transport

## State Classes

### Durable and portable

- `workspace.yaml`
- repository content and Git history
- catalog manifests and localization sources
- Save Points, checkpoints, and durable recovery metadata

### Generated or machine-local

- `compose.yaml`, `.env`, and provisioning/configuration scripts
- `mounts/config/applied-state.yaml`
- `.opencode/local/` discovery and runtime cache
- LocalHost operation/session records used for local continuity and diagnostics

Generated files must identify their source inputs and edit policy. Durable changes belong in canonical inputs, not generated output.

### Ephemeral

- containers and process IDs
- ConPTY handles and terminal attachments
- in-memory replay buffers and connection state
- presentation windows and WebSockets

## Workspace Lifecycle

LocalHost invokes Core orchestration to load the workspace, resolve catalogs and platform details, generate artifacts, provision, start, stop, validate, and remove runtime resources. Successful provisioning persists applied state so update detection compares desired and applied plans rather than timestamps.

Interactive startup is a separate concern. LocalHost creates or reuses an [interactive session and terminal runtime](interactive-sessions.md), starts the provider under the runtime, and grants an exclusive presentation attachment. The current desktop `Open Workspace` action prepares and starts the workspace; terminal attachment is a separate action.

## Terminal And Remote Boundaries

LocalHost owns ConPTY/provider lifetime and canonical terminal sequencing. Windows helpers, the local browser, and RemoteBridge are presentations of that same runtime. See [Terminal Runtime](terminal-runtime.md).

Remote access is deliberately narrower than LocalHost:

```text
remote browser -> Cloudflare Access -> Tunnel -> loopback RemoteBridge -> loopback LocalHost
```

See [Remote Access](remote-access.md) for trust boundaries and the [Cloudflare remote access integration guide](../integrations/cloudflare-remote-access.md) for deployment.

## Related Authorities

- [LocalHost](local-host.md)
- [Interactive Sessions](interactive-sessions.md)
- [Terminal Runtime](terminal-runtime.md)
- [Remote Access](remote-access.md)
- [Desktop](desktop.md)
- [Git Workspace Provider](git-workspace-provider.md)
- [Recovery Model](recovery-model.md)
- [Platform Resolution](platform-resolution.md)
