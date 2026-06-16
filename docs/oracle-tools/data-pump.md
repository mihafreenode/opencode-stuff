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
2. export the demo schema
3. simulate a disposable restore scenario
4. re-import and verify the sample tables are present
