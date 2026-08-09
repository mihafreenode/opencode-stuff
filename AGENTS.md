# AGENTS.md

## Purpose

This repository contains OpenCode Workspace Manager, a Windows-first desktop application for durable, Git-backed workspaces with replaceable Ubuntu runtimes. Linux and macOS packages exist for evaluation, while Windows owns the current terminal and release-validation path. Contributions are Apache-2.0; keep the root `LICENSE` and do not add full license banners to every file.

Read [Architecture Overview](docs/architecture/overview.md), [Philosophy](docs/philosophy.md), and [Design Principles](docs/design-principles.md) before structural changes.

## Product And Code Direction

- Describe the product as a durable workspace manager, not a Docker console or terminal launcher.
- Prefer explicit models, readable orchestration, official sources, and small changes over hidden conventions or clever frameworks.
- Keep the UI focused on workspaces, Save Points, Working Copies, Publish, Backup, and Restore. Put Docker and raw Git details in diagnostics or advanced views.
- Treat conflicts as reviewable state: conflict is not failure; lost work is failure.

## Architecture Safety

- Use LocalHost for new shared state and mutations. `WorkspaceOrchestrator` runs inside LocalHost.
- Do not add new direct Avalonia-to-Core orchestration.
- The existing Avalonia **Runtime Resources** in-process bypass is a known migration exception, not precedent.
- LocalHost owns operations, workspace instances, interactive sessions, terminal runtimes, attachments, transcripts, and provider processes.
- A terminal helper is presentation only and never owns or supervises the provider.
- Detach is not stop. Keep `ProviderSessionId` separate from controller, interactive-session, runtime, and attachment identities.
- Never carry PTY data through MCP. MCP is local control only.
- Never expose LocalHost publicly or on a LAN. Remote access is only through the narrow, loopback RemoteBridge behind Cloudflare Access and Tunnel.
- RemoteBridge is not a general proxy, remote MCP endpoint, workspace administration API, or runtime owner.

Authorities: [LocalHost](docs/architecture/local-host.md), [Interactive Sessions](docs/architecture/interactive-sessions.md), [Terminal Runtime](docs/architecture/terminal-runtime.md), [Remote Access](docs/architecture/remote-access.md), and [ADRs](docs/adr/README.md).

## Canonical And Generated State

Canonical inputs are:

- `workspace.yaml`
- `catalog/features/*.yaml`
- `catalog/skills/*.yaml`
- `catalog/services/*.yaml`
- `catalog/mcp/*.yaml`
- `catalog/templates/*.yaml`
- `Localization/*.po`

Generated runtime files include `compose.yaml`, `.env`, scripts under `mounts/config/`, and `mounts/config/applied-state.yaml`. Every generated file identifies that it is generated, its source inputs, and whether edits survive. Durable edits belong in canonical inputs. Successful provisioning/update persists applied state; restart alone does not imply an update.

## Git, Content, And Secrets

- Safe Working Copies map to `users/{user}/{title}-{yyyyMMdd-HHmm}` after safe lowercase Git-name sanitization. User-facing UI says Working Copy, not branch.
- Never auto-publish, force-push by default, auto-resolve conflicts, or publish to protected/mainline branches.
- Fetch before Publish. If remote state changed, stop on conflict or uncertainty; after a clean safe update, ask again before Publish.
- Classify content as `Tracked`, `Ignored`, or `Needs Review`. Do not silently ignore or commit unknown hidden content.
- Track durable reports, documents, presentations, and deliverables; ignore known caches, dependencies, previews, and rebuildable output.
- Never commit credentials, API keys, private keys, tokens, or secret configuration. Inspect changed and untracked content recursively before Save Points; dangerous ignore rules require review.

See [Git Workspace Provider](docs/architecture/git-workspace-provider.md) and [Recovery Model](docs/architecture/recovery-model.md).

## Removal Safety

Keep supported choices explicit: remove registration only, or remove owned Docker resources while retaining workspace files. Local workspace-file deletion is not implemented in the current desktop workflow. Never imply that registration/resource removal deleted files; manual deletion requires a verified backup and an explicit user action outside the app.

## Oracle Sources

For Oracle work, start with `docs/reference/oracle-knowledge-map.yaml` and use version-matched official Oracle documentation as authority. Prefer APEXlang for application definitions, APEX API docs for PL/SQL packages, ORDS docs for REST/deployment, and Oracle SQL/PLSQL docs for database behavior. Do not substitute blogs or forums when official documentation exists.

## Validation And Host Safety

- Validate portable Core behavior cross-platform, but validate the Windows desktop, ConPTY, Windows Terminal, Docker Desktop, packaging, and screenshots on the Windows host.
- From WSL, a Linux `dotnet` or Docker result is not a substitute for Windows-host validation. Use checked-in scripts under `scripts/windows-debug/` and `scripts/testing/` rather than large ad-hoc commands.
- External-process tests require cancellation, timeouts, process-tree cleanup, disposal, and deterministic shutdown. Docker tests skip early when prerequisites are absent and clean resources.
- Never kill Windows Terminal, the current host shell, broad host processes, or an unrelated running app during automation. Real terminal focus/interaction remains manual validation.
- Prefer exception and diagnostics capture before adding UI click retries. Do not block UI startup with sync-over-async.
- Validate in order: static tests, Windows solution tests, smoke dry run, live runtime smoke, manual validation.

Detailed procedures belong under `docs/development/`, `docs/testing/`, and `docs/troubleshooting/`; link them instead of growing this file.

## Change Discipline

- Update architecture docs and ADRs when ownership or trust boundaries change.
- Keep platform-specific behavior explicit and outside portable Core where possible.
- Do not modify unrelated terminal profiles or shell configuration; only manage clearly marked OpenCode Stuff-owned sections and profiles.
- Do not package or release behavior that has not passed the appropriate runtime validation.
