# Desktop Shell

`opencode stuff` uses the Avalonia desktop shell on Windows, macOS, and Linux.

## Packaged Run

Extract the published archive and run the desktop executable from the extracted folder:

- Windows: `OpenCode.Workspace.Avalonia.exe`
- Linux: `OpenCode.Workspace.Avalonia`
- macOS: `OpenCode.Workspace.Avalonia`

The packaged app does not need the repository source tree to start. It expects bundled `catalog/`, `Localization/`, `docs/`, and the normal user app-data directory.

## Current Recommendation

Use the Avalonia shell on Windows as the desktop application path.

Avalonia now covers the full Level A workflow set as well as the portable desktop shell for:

- browsing workspaces
- checking diagnostics
- reviewing templates
- viewing recent timeline activity
- reviewing save point and checkpoint history
- creating a workspace
- opening an existing repository
- starting a workspace
- recovering a workspace
- attaching to a workspace session

## Avalonia Features

- calm native desktop shell layout
- left navigation
- workspaces page with placeholder-first discovery and background enrichment
- diagnostics page
- templates page
- transcripts preview
- save points preview
- documentation links
- settings and theme selection
- immediate transcript feedback for long-running workspace actions

Theme modes:

- `System`
- `Light`
- `Dark`

Deferred convenience UX should be implemented natively in Avalonia or documented explicitly.

## Diagnostics

The Diagnostics page uses shared core services where possible.

It can:

- run workspace doctor checks
- validate `linux/amd64`
- validate `linux/arm64`

The output is presented as readable checklist items with status and next steps.

Expected prerequisite checks include:

- Git
- Docker
- Docker Compose
- Windows Terminal when running on Windows
- platform/runtime detection
- host architecture
- bundled catalog availability

Missing prerequisites should produce actionable messages instead of crashes.

## Runtime-State Note

`.opencode/local/` is machine-local and ignored by Git.

It stores local runtime resolution state and can be regenerated.

## Automation Surface

CLI diagnostics remain the primary automation and scripting surface.

The desktop shell is for inspection, interactive workflow, and troubleshooting.

## Compatibility Note

The Avalonia shell still reads and writes the historical app-data folder `OpenCode.Workspace.Manager` so existing local workspace indexes and user state remain intact.
