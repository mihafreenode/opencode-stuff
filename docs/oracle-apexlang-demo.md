# Oracle APEXlang Demo

The Oracle APEXlang Demo is a source-controlled Oracle APEX workflow.

It extends the Oracle APEX Demo rather than replacing it.

Oracle now uses `APEXlang` as the name for the Open Application Specification Language introduced in APEX 26.1.

## What This Demo Adds

- export scripts for APEX application definitions
- import scripts for repeatable local re-apply flows
- a lightweight shape-check script for exported artifacts
- repository assets for Git review and team onboarding

APEXlang is not a replacement for APEX Builder.

It complements the Builder workflow by making Oracle APEX application definitions reviewable and automatable while preserving the normal browser-based development experience.

New to Git-based Oracle development?

See [Practical Git for Oracle Developers](oracle/practical-git-for-oracle-developers.md).

## Progression

```text
Oracle PL/SQL Demo
    ↓
Oracle APEX Demo
    ↓
Oracle APEXlang Demo
```

In practical terms, APEXlang extends APEX, and APEX extends the PL/SQL path.

Capability discovery starts with `docs/capabilities/README.md` and `docs/capabilities/oracle.md`.

Oracle APEX media is user-provided for local runtime provisioning. Place the official Oracle APEX ZIP under `.local/oracle/downloads/apex/` before running `Prepare Workspace` for this workspace.

## Try It Yourself

1. export the sample application with `scripts/export-apex.sh`
2. use `scripts/validate-apex.sh` only as a file-presence and shape check
3. review the change in Git
4. run version-matched SQLcl/APEXlang validation against the target environment
5. import the application back into the local environment

The standalone `scripts/validate-apex.sh` helper is not authoritative semantic validation. It checks that the file exists and contains expected APEX-like text; it does not run SQLcl `apex validate`, compile application code, or prove that Oracle APEX can import the artifact.

## Related Topics

- [Oracle Samples](oracle-samples.md)
- [APEXlang](oracle-tools/apexlang.md)
- [APEX Export / Import](oracle-tools/apex-export-import.md)
- [SQLcl](oracle-tools/sqlcl.md)
- [Oracle Lifecycle Workflows](oracle-lifecycle-workflows.md)
