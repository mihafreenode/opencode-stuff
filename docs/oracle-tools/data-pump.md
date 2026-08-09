# Data Pump

## What It Is

Data Pump (`expdp` / `impdp`) is Oracle's standard schema and data export/import mechanism.

## Why It Exists

Use Data Pump when you want to:

- create onboarding snapshots
- refresh local test environments
- move schema/data between Oracle environments
- create repeatable backup and restore exercises

## How It Fits The Demo

- Oracle PL/SQL Demo: schema backup and restore example
- Oracle APEX Demo: move schema/data separately from the application definition
- Oracle APEXlang Demo: keep schema/data migration separate from source-controlled app export

## Current Workspace Support

The generated `scripts/export-datapump.sh` and `scripts/import-datapump.sh` files are conceptual scaffolding. They print recommended `expdp` or `impdp` command lines; they do not connect to Oracle, create a dump, copy dump files, execute an import, or verify the result.

Treat the commands below as starting points that a DBA or workspace owner must adapt for directory objects, container execution, credentials, file ownership, storage, and the target environment. Do not describe the generated helpers as a working backup or restore workflow.

## Example Commands

```bash
expdp demo_user/demo_password@FREEPDB1 schemas=DEMO_USER directory=DATA_PUMP_DIR dumpfile=demo_user.dmp logfile=demo_user-exp.log
impdp demo_user/demo_password@FREEPDB1 schemas=DEMO_USER directory=DATA_PUMP_DIR dumpfile=demo_user.dmp logfile=demo_user-imp.log
```

## Relationship To Other Tools

- Data Pump exports schema/data
- APEX Export / Import exports the application
- APEXlang keeps the application definition reviewable and automatable

## Licensing / Prerequisite Notes

- included with Oracle Database tooling concepts used here
- no additional Oracle product assumed in this onboarding path

## Beginner Exercise

1. review the generated `export-datapump` and `import-datapump` scripts
2. confirm that they only print conceptual commands
3. design a disposable test that supplies the required Oracle directory and file-transfer behavior
4. run reviewed Data Pump commands directly in that test environment
5. verify dump creation, import logs, expected objects, and row counts before calling the workflow complete
