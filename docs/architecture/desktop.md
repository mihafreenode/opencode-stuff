# Desktop

`OpenCode.Workspace.Avalonia` is the desktop presentation, currently delivered and validated as a Windows application. It uses page-oriented MVVM and keeps platform behavior behind explicit services.

## Current Boundary

The primary workspace flow uses typed LocalHost application services for shared state and mutations. This includes workspace operations and interactive-session management. The desktop may discover LocalHost or start its process, but LocalHost remains the backend and runtime owner.

The **Runtime Resources** auxiliary page is a known discrepancy: resource release and orphan cleanup still call `IDesktopWorkspaceService` and orchestrate Core in process. This bypass must be migrated, and must not be copied for new functionality.

## Open Workspace And Attach

`Open Workspace` currently prepares, repairs when safe, provisions when needed, and starts the workspace through LocalHost. It does not itself attach a terminal. Creating/selecting an interactive session and attaching its terminal presentation are separate actions.

Some existing UI text and compatibility services may still describe opening the terminal as part of `Open Workspace`. That language reflects the prior direct-attach flow and should not be treated as the architecture contract.

## Terminal Presentation

Desktop terminal presentation is Windows-only. The desktop launches a Windows helper that connects to the LocalHost-owned ConPTY runtime. The helper is a presentation client: it never owns the provider process and detaching it does not stop the runtime.

The local browser and RemoteBridge are alternate presentations of the same runtime. See [Terminal Runtime](terminal-runtime.md).

## Presentation Responsibilities

- navigation, selection, commands, progress, and diagnostics display
- LocalHost discovery and readiness presentation
- mapping LocalHost contracts into desktop view models
- Windows-specific window, tray, notification, and terminal-launch integration
- keeping the UI responsive while LocalHost operations continue independently

Business orchestration and shared mutable state do not belong in view models or new in-process desktop services.
