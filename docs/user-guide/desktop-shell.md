# Desktop Shell

`opencode stuff` now has two desktop shells:

- `OpenCode Workspace Avalonia`: the primary Windows desktop shell and the cross-platform desktop shell
- `OpenCode Workspace Manager`: the legacy Windows WPF fallback shell

## Current Recommendation

Use the Avalonia shell on Windows as the default desktop application path.

Use the WPF shell only when you still need an advanced workflow that has not yet been migrated.

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

## Remaining Gaps

The primary remaining Windows desktop work is now Level B rather than Level A parity:

- Save Point write flow
- timeline actions
- backup/export
- publish
- advanced Git recovery
- Oracle/demo workflows

Unavailable or unfinished actions should still explain why they are unavailable instead of being hidden or faked.

## Diagnostics

The Diagnostics page uses shared core services where possible.

It can:

- run workspace doctor checks
- validate `linux/amd64`
- validate `linux/arm64`

The output is presented as readable checklist items with status and next steps.

## Runtime-State Note

`.opencode/local/` is machine-local and ignored by Git.

It stores local runtime resolution state and can be regenerated.

## Automation Surface

CLI diagnostics remain the primary automation and scripting surface.

The desktop shells are for inspection, interactive workflow, and troubleshooting.
