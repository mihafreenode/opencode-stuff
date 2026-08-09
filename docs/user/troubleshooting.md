# Troubleshooting

Match the visible symptom first. Workspace files are usually unaffected by runtime or presentation failures.

## The App Does Not Start

- Confirm the full Windows ZIP was extracted; do not run a lone copied `OpenCode.Workspace.exe`.
- Keep `bin`, `catalog`, `config`, `docs`, and `Localization` beside the executable.
- Check `%LOCALAPPDATA%\OpenCode.Workspace.Manager\avalonia-startup.log`.
- The official Windows ZIP is self-contained; a .NET Desktop Runtime is only relevant to framework-dependent builds.

## Docker Is Unavailable

1. Start Docker Desktop and wait for the engine.
2. Run `docker version` and `docker compose version` in PowerShell.
3. Run `wsl --status` and `wsl --update` if WSL is stale.
4. Confirm Docker Desktop uses its WSL2 backend.

An unavailable Docker socket inside an unrelated WSL shell does not prove Docker Desktop is unavailable to the Windows app.

## Create Or Import Fails

- Confirm the chosen folder is writable.
- For import, confirm it is the intended local Git checkout.
- Review local changes and branch status; the app will not discard them.
- Fix invalid repository-owned `workspace.yaml` instead of expecting template fallback.
- Confirm Git identity and remote credentials when the failing step uses Git.

## Open Workspace Fails During Setup

- Read the operation transcript and the exact failing command.
- Confirm internet access to Ubuntu, npm, or other configured package sources.
- Inspect `mounts/config/provision.sh` for the generated plan.
- Change durable intent in `workspace.yaml`, then use `Prepare Workspace` or the recommended update action.
- Use `Repair Runtime` for generated/runtime wiring, not to restore deleted user files.

Do not treat stderr alone as failure. The command exit code and post-command readiness checks determine success.

## Workspace Is Running But No Terminal Opens

This is expected after `Open Workspace`: readiness and session attachment are separate.

1. Open the interactive sessions area.
2. Create or select a session.
3. Attach it.
4. Confirm Windows Terminal is installed and `wt --version` works.

Desktop terminal attachment is Windows-only. Linux and macOS packages do not currently provide it.

## Session Is Already Attached

Reconnect a detached session normally. If another presentation is active, request takeover only when you intend to transfer control away from it. If the provider process exited or failed, restart the session after confirming the workspace remains ready.

## Windows Terminal Attach Fails

- Verify `wt.exe` is available.
- Review the exact Windows Terminal and PowerShell fallback commands in the operation transcript.
- Try the logged PowerShell fallback first to distinguish launcher integration from LocalHost/session failure.
- Check the managed profile and selected font in Settings.
- Run the workspace's generated `terminal-diagnostics.ps1` for profile, locale, and rendering checks.

The app manages only its own Windows Terminal fragment and does not rewrite unrelated profiles.

## Selected Font Shows Boxes Or Diamonds

Install the selected Nerd Font, refresh diagnostics, and open a new terminal presentation. Verify the actual Windows Terminal font face matches a registered Windows font family.

## Runtime Stopped Unexpectedly

Confirm Docker Desktop is healthy, then use `Open Workspace` again. A container interruption does not automatically mean durable workspace files were lost. Re-run validation that depended on the old runtime process.

## Save Point Is Blocked

Review every reported path. Remove secrets, decide whether unknown hidden content is durable, and correct dangerous ignore rules. Do not bypass review by broadly ignoring dot folders.

## Publish Is Blocked

- Confirm a remote is configured and credentials work.
- Move normal work off protected/mainline branches into a Safe Working Copy.
- Fetch and review remote changes.
- Resolve conflicts manually; the app does not auto-resolve or force-push.

## Removal Or Deletion Fails

Confirm which removal option was selected. `Remove from list only` never deletes files. Docker cleanup leaves workspace files. Destructive deletion may be unavailable and can stop if Linux-created file permissions cannot be normalized. Keep the registration, note the failed paths, and use repair-and-retry rather than manually assuming partial cleanup succeeded.

## CLI Diagnostics

From the extracted Windows package:

```powershell
bin\cli\OpenCode.Workspace.Cli.exe doctor --workspace C:\path\to\workspace
bin\cli\OpenCode.Workspace.Cli.exe validate-platform --workspace C:\path\to\workspace --target linux/amd64
bin\cli\OpenCode.Workspace.Cli.exe mcp doctor --install-root . --json
```

See [CLI Reference](../reference/cli.md) and [Paths And State](../reference/paths-and-state.md).
