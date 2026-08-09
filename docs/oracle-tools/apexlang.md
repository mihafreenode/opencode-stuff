# APEXlang

## What It Is

APEXlang is Oracle's Open Application Specification Language for Oracle APEX applications.

## Why It Exists

Use it when you want to:

- review application changes in Git
- make exported application definitions available for review and authoritative validation
- make onboarding repositories more self-describing
- support AI-assisted work with readable application definitions

New to Git-based Oracle development?

See [Practical Git for Oracle Developers](../oracle/practical-git-for-oracle-developers.md).

## How It Fits The Demo

- Oracle APEXlang Demo: advanced stage after PL/SQL and APEX basics
- targets the same `Customer Orders Demo` tutorial goal as the Builder-based APEX workflow; the generated application source is a placeholder, not a complete shipped app

## Example Commands

```bash
scripts/export-apexlang.sh
scripts/validate-apex.sh apex/application.apx
scripts/import-apex.sh apex/application.apx
```

The generated `scripts/validate-apex.sh` command is only a file-presence and text-shape check. Use version-matched SQLcl `apex validate` and target-environment import or runtime checks for authoritative semantic validation.

## Relationship To Other Tools

- APEXlang complements APEX Builder
- APEX Export / Import is still the official movement path
- SQLcl remains the automation entry point

## Licensing / Prerequisite Notes

- terminology and workflow guidance tied to Oracle APEX
- no separate licensing claim made here beyond the APEX environment itself

## Beginner Exercise

1. add a field or label change in the sample APEX application
2. export the application definition
3. run the shape check, then validate through version-matched SQLcl and the target environment
4. review the diff in Git
5. import the application definition back locally
