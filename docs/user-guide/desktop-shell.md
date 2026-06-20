# Desktop Shell

`opencode stuff` now has two desktop shells:

- `OpenCode Workspace Manager`: the current Windows WPF shell
- `OpenCode Workspace Avalonia`: the new cross-platform preview shell

## Current Recommendation

Use the WPF shell on Windows when you need the full current workflow, especially attach and Windows-specific integration.

Use the Avalonia shell when you want a portable, inspectable desktop shell for:

- browsing workspaces
- checking diagnostics
- reviewing templates
- viewing recent timeline activity
- reviewing save point and checkpoint history

## Avalonia Preview Features

- calm native desktop shell layout
- left navigation
- workspaces page
- diagnostics page
- templates page
- transcripts preview
- save points preview
- documentation links
- settings and theme selection

Theme modes:

- `System`
- `Light`
- `Dark`

## Preview Limitations

Some actions are intentionally visible but disabled.

Examples:

- `Attach`
- `Recover`
- `Save Point`

The preview shell explains why those actions are unavailable instead of hiding them or faking behavior.

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
