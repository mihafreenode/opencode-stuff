# Oracle Lifecycle Workflows

This repository favors official Oracle commands, official Oracle formats, and readable scripts.

## End-To-End Flow

```text
Local Development
    ↓
APEX Builder
    ↓
Export
    ↓
Validate
    ↓
Git Review
    ↓
Import
    ↓
Deploy
```

This flow is intentionally practical.

Local onboarding happens in a safe local environment first. Shared staging or team deployment should happen after export, validation, and review rather than as the first place someone learns how the workspace works.

## Database Workflows

- Schema export and import keep table, procedure, and package changes portable.
- Data Pump remains Oracle's standard mechanism for larger schema or data transfer scenarios, but the generated workspace helpers only print conceptual commands and do not execute the transfer.
- SQLcl is the local command-line entry point for repeatable database checks and script execution.

## APEX Workflows

- Oracle APEX Demo focuses on browser-based APEX Builder development.
- Oracle APEXlang Demo adds export and import scaffolding plus a standalone shape check around that same Builder workflow. Authoritative validation still requires version-matched SQLcl and target-environment checks.
- Exported artifacts can be reviewed in Git before they are re-imported or deployed.

## Practical Lifecycle Areas

- schema export and import
- Data Pump export and import
- APEX export and import
- APEXlang validation
- Git review of exported artifacts
- local onboarding before shared staging

The synchronization modes named `watch-safe` and `watch-live` are policies used when an explicit synchronization or Oracle Assistant operation runs. They do not implement autonomous background watching or continuous synchronization.

## Why Official Oracle Commands And Formats

Official Oracle commands and formats reduce surprise for Oracle teams, keep knowledge transferable, and make the generated scripts easier to understand during onboarding.

Static generation and workflow scaffolding are covered in tests. Live Oracle APEX behavior, Data Pump execution, semantic APEXlang validation, and complete application behavior must be confirmed separately with the checked-in smoke tooling and environment-specific manual validation.
