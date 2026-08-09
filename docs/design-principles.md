# Design Principles

These principles turn the [project philosophy](philosophy.md) into engineering rules.

## Workspace Before Runtime

Repository content and `workspace.yaml` are durable. Generated files, containers, terminal runtimes, and caches are replaceable and must not become the only source of truth.

## One Canonical Owner

Shared state and mutations use LocalHost. Presentations and controllers request work; they do not duplicate backend or provider ownership. Known migration exceptions are documented, not copied.

## Inspectable Automation

Prefer explicit models and readable control flow. Generated artifacts identify their inputs and edit policy. Important outcomes leave evidence in specifications, tests, validation, documentation, or history.

## Recoverability First

Preserve work when uncertain. Publish is explicit, force-push and automatic conflict resolution are forbidden by default, restore prefers a copy, and destructive choices remain distinct.

## Narrow Trust Boundaries

LocalHost stays loopback-only. Remote access uses the narrow RemoteBridge presentation boundary. MCP is local control, never a PTY or remote transport. Credentials and secrets do not enter durable workspace history.

## Portable Understanding

Official sources, repository-owned guidance, and canonical manifests should let understanding survive changes in people, machines, tools, vendors, and AI models. Simplification must retain a visible path to inspect, modify, validate, and recover the system.

## Validate The Owning Environment

Portable Core behavior is tested cross-platform. Windows desktop, ConPTY, Windows Terminal, and Docker Desktop behavior is validated on the Windows host. A successful build is not a substitute for end-to-end behavior in the environment that owns it.
