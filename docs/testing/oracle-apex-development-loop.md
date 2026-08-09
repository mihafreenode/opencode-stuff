# Oracle APEX Development Loop

The Oracle APEX development-loop wrapper is a local configuration and integration-test gate. It does not execute the complete development workflow shown below.

- configuration stays on your machine
- credentials are never committed
- repositories remain portable
- SQLcl profiles are referenced by name, not stored in the repository

## Target Manual Workflow

Configure once

↓

Run Doctor

↓

Run configuration/test wrapper

↓

Prompt OpenCode

↓

Review semantic plan

↓

Validate

↓

Import

↓

Preview

↓

Rollback if required

After validating all required environment variables, `scripts/testing/oracle-apex-development-loop.ps1` runs the `OracleApexAssistantIntegrationTests` filter. It does not run Doctor, prompt OpenCode, create or execute an Assistant plan, validate with SQLcl, import an application, open a preview, detect a Builder change, or roll back source. Perform and verify those steps separately through the product and the target Oracle environment.

## Required variables

All variables are local-only environment variables. The wrapper checks that the required values are present; it does not use them to connect, validate, import, export, or open either URL.

- `OPENCODE_APEX_DEVLOOP_ENABLED`
  Why: enables the local smoke workflow intentionally.
- `OPENCODE_APEX_DEVLOOP_WORKSPACE_ROOT`
  Why: points to the workspace root that contains `workspace.yaml`.
- `OPENCODE_APEX_DEVLOOP_ENVIRONMENT`
  Why: selects the Oracle APEX environment from `workspace.yaml`.
- `OPENCODE_APEX_DEVLOOP_SQLCL_PROFILE`
  Why: references the local SQLcl profile used by validate/import/export.
- `OPENCODE_APEX_DEVLOOP_APPLICATION_ID`
  Why: identifies the development application.
- `OPENCODE_APEX_DEVLOOP_SOURCE_PATH`
  Why: points to the exported APEXlang source tree.
- `OPENCODE_APEX_DEVLOOP_DEPLOYMENT_PROFILE`
  Why: chooses the deployment profile under `src/apex/deployments`.
- `OPENCODE_APEX_DEVLOOP_BUILDER_URL`
  Why: opens the APEX Builder for the target application.
- `OPENCODE_APEX_DEVLOOP_APPLICATION_URL`
  Why: verifies the running application preview.

Optional:

- `OPENCODE_APEX_DEVLOOP_EXPECTS_BUILDER_CHANGE`
  Why: enables the reverse Builder-to-Git smoke scenario after you make a controlled Builder-side change.

## SQLcl profile requirements

The named SQLcl profile must already exist on the local machine.

Expected profile properties:

- connects to a development-only Oracle APEX workspace
- resolves without interactive prompts during the smoke run
- has access to validate, import, and export the target application
- is safe to use for temporary reversible changes

Do not store profile secrets in the repository.

## Expected workspace layout

The development loop expects:

- `workspace.yaml`
- `src/apex/application.apx`
- `src/apex/deployments/<profile>.apx`
- `.opencode/knowledge/apexlang-atlas/state.json` after Atlas knowledge is built

## Template file

Use the generated example file as the starting point:

`/.opencode/local/oracle-apex-development-loop.env.example`

Copy the values into your local shell session, shell profile, or another local-only env file that is not committed.

## PowerShell example

```powershell
$env:OPENCODE_APEX_DEVLOOP_ENABLED = "1"
$env:OPENCODE_APEX_DEVLOOP_WORKSPACE_ROOT = "C:\Users\your.name\source\repos\your-apex-workspace"
$env:OPENCODE_APEX_DEVLOOP_ENVIRONMENT = "dev"
$env:OPENCODE_APEX_DEVLOOP_SQLCL_PROFILE = "local-apex-dev"
$env:OPENCODE_APEX_DEVLOOP_APPLICATION_ID = "100"
$env:OPENCODE_APEX_DEVLOOP_SOURCE_PATH = "src/apex"
$env:OPENCODE_APEX_DEVLOOP_DEPLOYMENT_PROFILE = "development"
$env:OPENCODE_APEX_DEVLOOP_BUILDER_URL = "https://example.test/ords/r/apex/app-builder/home?session=LOCAL"
$env:OPENCODE_APEX_DEVLOOP_APPLICATION_URL = "https://example.test/ords/r/demo/home"
```

Run the relevant tests and the wrapper:

```powershell
dotnet test tests/OpenCode.Workspace.Core.Tests/OpenCode.Workspace.Core.Tests.csproj --filter "OracleApexEnvironmentDoctorServiceTests|OracleApexAssistantIntegrationTests"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/testing/oracle-apex-development-loop.ps1
```

## cmd example

```cmd
set OPENCODE_APEX_DEVLOOP_ENABLED=1
set OPENCODE_APEX_DEVLOOP_WORKSPACE_ROOT=C:\Users\your.name\source\repos\your-apex-workspace
set OPENCODE_APEX_DEVLOOP_ENVIRONMENT=dev
set OPENCODE_APEX_DEVLOOP_SQLCL_PROFILE=local-apex-dev
set OPENCODE_APEX_DEVLOOP_APPLICATION_ID=100
set OPENCODE_APEX_DEVLOOP_SOURCE_PATH=src/apex
set OPENCODE_APEX_DEVLOOP_DEPLOYMENT_PROFILE=development
set OPENCODE_APEX_DEVLOOP_BUILDER_URL=https://example.test/ords/r/apex/app-builder/home?session=LOCAL
set OPENCODE_APEX_DEVLOOP_APPLICATION_URL=https://example.test/ords/r/demo/home
```

Run the wrapper:

```cmd
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\testing\oracle-apex-development-loop.ps1
```

## Expected First Manual Run

1. Set the local-only environment variables.
2. Run Oracle APEX Doctor separately.
3. Fix any missing configuration or deployment-profile issues.
4. Run the wrapper to validate environment-variable presence and the Assistant integration-test filter.
5. Open the product and prompt OpenCode for a small reversible semantic change.
6. Review the semantic plan.
7. Validate and import.
8. Preview the application.
9. Roll back the generated change if you want to restore the original source.

Completion of the wrapper proves only that its configuration gate and selected integration tests passed. It is not evidence that the target Oracle profile connected or that the application was validated, imported, previewed, or rolled back.
