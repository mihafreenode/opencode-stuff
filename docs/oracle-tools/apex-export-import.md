# APEX Export / Import

## What It Is

APEX Export / Import is Oracle's official workflow for moving an application definition between environments.

## Why It Exists

Use it when you want to:

- back up the current application definition
- move an application between local environments
- validate changes before deployment
- hand the application to another teammate or environment

## How It Fits The Demo

- Oracle APEX Demo: introduces official application movement after Builder basics
- Oracle APEXlang Demo: forms the operational path around the source-controlled workflow

## Example Commands

```bash
scripts/export-apex.sh
scripts/validate-apex.sh
scripts/import-apex.sh
```

## Relationship To Other Tools

- APEX Export / Import moves the application definition
- Data Pump moves schema/data
- APEXlang adds Git-oriented review and validation workflow around exports

## Licensing / Prerequisite Notes

- part of Oracle APEX workflow guidance
- scripted here through SQLcl-oriented helpers
- local runtime provisioning requires a user-provided official Oracle APEX ZIP under `.local/oracle/downloads/`

## Beginner Exercise

1. export the `Customer Orders Demo` application
2. validate the exported artifact
3. review the artifact in Git
4. re-import it into the same local environment
