# Troubleshooting

Start here if the first-use flow gets stuck.

Normal path:

1. create or open a workspace
2. open the workspace
3. wait for the runtime to start
4. begin working in the terminal session

If one of those steps fails, match it to the section below.

## I Cannot Start The Runtime

Symptoms:

- health checks show the runtime engine is unavailable
- start or provision fails before the runtime launches

Action:

1. Start Docker Desktop.
2. Wait for the engine to finish starting.
3. Run the health checks again.

If Docker still does not respond, go back to [Windows Setup](windows-prerequisites.md) and verify the install commands and checks.

## I Cannot Open The Terminal Session

Symptoms:

- attach fails immediately
- health checks say Windows Terminal is unavailable

Action:

1. Install Windows Terminal.
2. Ensure the App Execution Alias for `wt.exe` is enabled.
3. Try attach again.

If the workspace itself is running, this is usually a terminal setup issue, not a workspace data-loss issue.

If Windows executables cannot be launched from Ubuntu/WSL at all, see [Debugging WSL Windows Interop](troubleshooting/wsl-windows-interop.md).

## Selected Nerd Font Is Missing

Symptoms:

- terminal health checks warn that the selected Nerd Font is not installed
- the managed OpenCode Stuff Windows Terminal profile falls back to a different font

Action:

1. Use the `Install Selected Font` action in the Terminal Settings section, or install the font manually.
2. Refresh health checks.
3. Reopen the OpenCode Stuff terminal tab if it was already open.

The app only manages fonts chosen for its own terminal experience. It does not rewrite unrelated Windows Terminal profiles.

## OpenCode Stuff Terminal Profile Missing

Symptoms:

- health checks warn that the OpenCode Stuff terminal profile does not exist

Action:

1. Create or reopen a workspace from the app.
2. The app will regenerate its managed Windows Terminal fragment.
3. Refresh health checks.

OpenCode Stuff writes only its own fragment under the Windows Terminal fragments directory and does not edit unrelated user profiles.

## The Workspace Opens But Setup Fails

Most setup failures come from:

- missing internet access
- temporary package repository failures
- upstream package name changes

What to do next:

1. Try again in case it was a temporary network or package issue.
2. Confirm internet access is working.
3. If it keeps failing, inspect the generated setup script below.

Check the generated provisioning script under:

```text
mounts/config/provision.sh
```

That file is generated intentionally so contributors can inspect the exact installation plan.

## The Runtime Stopped Unexpectedly

During the smoke test, the workspace, PostgreSQL, and pgAdmin containers all exited together with code `255` after a Docker Desktop interruption.

Interpret that as a runtime interruption first, not automatically as a workspace failure.

Recommended recovery path:

1. confirm Docker Desktop is reachable again
2. restart the workspace from the app
3. re-run any validation that depends on the runtime still being alive

This usually means the tool environment stopped. It does not automatically mean your workspace was lost.

## The Session Does Not Restore

If the workspace opens but the terminal session does not restore correctly, the most common fix is:

1. stop the workspace
2. open it again
3. try attach again

If that still fails, use the details below.

The attach flow runs:

```bash
opencode session list
opencode --session <session-id>
```

That means:

1. look for an existing OpenCode session for the workspace
2. resume the most recent matching session when one exists
3. start a new OpenCode session when none exist

If attach still fails, confirm the workspace was provisioned successfully and that `opencode` exists inside the container.

## Attach Validation Note

This section is mainly for contributors and advanced troubleshooting.

The app can verify the container state and can request attach through Windows Terminal, but some parts of the final terminal interaction are inherently interactive.

For automated smoke testing, prefer validating:

1. the workspace container is running
2. `screen` state inside the container
3. the app emitted the attach request without throwing a Windows Terminal error

Manual confirmation is still the most reliable way to judge the final terminal user experience.

For `v0.1`, a clean manual terminal screenshot is acceptable if automated window capture remains flaky after attach itself is already working correctly.

## Terminal Diagnostics

Normal `Attach` should open OpenCode directly without extra diagnostic output.

Most users should not need this. Use it only if the terminal session keeps failing after the normal recovery steps above.

If you need terminal-specific troubleshooting, run the generated diagnostics wrapper for a workspace manually:

```text
terminal-diagnostics.ps1
```

That script is generated in the workspace root and is intended for troubleshooting profile, font, locale, and UTF-8 rendering issues without changing the normal user attach flow.
