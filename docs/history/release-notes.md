# Release history

This file records what earlier milestones reported. It is historical chronology, not the current architecture, support matrix, package contract, or release checklist.

## Remote browser terminal milestone

- Added an opt-in RemoteBridge path for browser presentation of an existing LocalHost terminal runtime through Cloudflare Access and Tunnel.
- Added exact Host/Origin checks, Cloudflare Access JWT/JWKS validation, a narrow route allowlist, and identity-bound one-time bridge grants.
- Kept LocalHost credentials server-side and RemoteBridge disabled by default.
- Added operator setup, manual acceptance, and troubleshooting documentation.

For the maintained design and setup, see [Cloudflare remote access](../integrations/cloudflare-remote-access.md).

## v0.2.0-avalonia candidate

- Removed the WPF desktop shell and made Avalonia the sole desktop shell.
- Completed the candidate's workspace, Save Point, Timeline, Backup, Publish, removal, recovery, and attach workflows.
- Added shared host capability detection and Windows, Linux, and macOS platform projects.
- Preserved the existing `OpenCode.Workspace.Manager` app-data location and workspace-index compatibility for that candidate.
- Produced Windows, Linux, and macOS CI artifacts and made Windows-only tests skip on non-Windows hosts.
- Recorded a clean Windows Release build and extracted-package smoke at commit `4d8b9cfd365f5feb6377a55010a7c8d20044ab83`.
- Recorded test snapshots of Core `390/390`, Avalonia `104/104`, Windows platform `26/26`, Linux platform `2/2`, macOS platform `2/2`, and CLI `20/20`.

The candidate still depended on manual packaged GUI checks. Its filenames, test totals, architecture wording, and known limitations are historical and must not be used as current release claims. See [v0.2 Avalonia validation](v0.2-avalonia-validation.md) for retained execution evidence.
