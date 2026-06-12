# Troubleshooting

## Docker Desktop Is Not Running

Symptoms:

- health checks show Docker Engine unavailable
- start or provision fails before the container launches

Action:

1. Start Docker Desktop.
2. Wait for the engine to finish starting.
3. Run the health checks again.

## Windows Terminal Is Not Available

Symptoms:

- attach fails immediately
- health checks say Windows Terminal is unavailable

Action:

1. Install Windows Terminal.
2. Ensure the App Execution Alias for `wt.exe` is enabled.
3. Try attach again.

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

## Workspace Starts But Provisioning Fails

Most provisioning failures come from:

- missing internet access
- temporary package repository failures
- upstream package name changes

Check the generated provisioning script under:

```text
mounts/config/provision.sh
```

That file is generated intentionally so contributors can inspect the exact installation plan.

## Docker Desktop Restarted And Containers Disappeared

During the smoke test, the workspace, PostgreSQL, and pgAdmin containers all exited together with code `255` after a Docker Desktop interruption.

Interpret that as a container lifecycle event first, not automatically as an application attach failure.

Recommended recovery path:

1. confirm Docker Desktop is reachable again
2. restart the workspace from the app
3. re-run any validation that depends on the containers still being alive

## Attach Restores Nothing

The attach flow runs:

```bash
screen -D -r opencode || exec screen -S opencode opencode -s
```

That means:

1. try to restore an existing `screen` session named `opencode`
2. if none exists, create a new one and start `opencode -s`

If attach still fails, confirm the workspace was provisioned successfully and that `screen` and `opencode` exist inside the container.

## Attach Validation Note

The app can verify the container state and can request attach through Windows Terminal, but some parts of the final terminal interaction are inherently interactive.

For automated smoke testing, prefer validating:

1. the workspace container is running
2. `screen` state inside the container
3. the app emitted the attach request without throwing a Windows Terminal error

Manual confirmation is still the most reliable way to judge the final terminal user experience.

For `v0.1`, a clean manual terminal screenshot is acceptable if automated window capture remains flaky after attach itself is already working correctly.

## Terminal Diagnostics

Normal `Attach` should open OpenCode directly without extra diagnostic output.

If you need terminal-specific troubleshooting, run the generated diagnostics wrapper for a workspace manually:

```text
terminal-diagnostics.ps1
```

That script is generated in the workspace root and is intended for troubleshooting profile, font, locale, and UTF-8 rendering issues without changing the normal user attach flow.
