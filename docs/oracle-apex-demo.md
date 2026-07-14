# Oracle APEX Demo

Oracle APEX Demo extends the Oracle PL/SQL path with browser-based Oracle application development.

It uses Oracle Database Free, Oracle REST Data Services (ORDS), Oracle APEX, and SQLcl to provide a reproducible local environment for learning and onboarding.

Oracle APEX media is user-provided. The repository does not redistribute Oracle APEX ZIP files.

Place the official Oracle APEX ZIP under `.local/oracle/downloads/apex/` as `apex.zip` or an official filename such as `apex_24.2_en.zip`, then run `Prepare Workspace`.

## Oracle Image Compatibility

The default Oracle image for APEX-capable templates is:

- `gvenzl/oracle-free:23`

This is the default because fresh volumes created from `gvenzl/oracle-free:23-slim-faststart` expose `XDB` in the registry as `INVALID` even when `XMLType` and `DBMS_XDB` work. Oracle APEX 26.1.0 still rejects that database in `apxprereq.sql`, so the faststart image is not the generated default for APEX workspaces.

PL/SQL-only workspaces may have different compatibility requirements because they do not run the APEX installer prerequisite gate.

You can override the database image in `workspace.yaml`:

```yaml
oracle:
  databaseImage: gvenzl/oracle-free:23
```

After changing `oracle.databaseImage`, reset the runtime so Oracle creates a fresh data volume for the new image.

The supported setup does not rely on directly calling `DBMS_REGISTRY.VALID('XDB')`.

## ORDS Runtime Layout

The generated ORDS setup separates immutable workspace content from mutable runtime state.

- generated ORDS scripts are bind-mounted read-only from the workspace to `/opt/opencode-workspace/ords`
- mutable ORDS config, logs, and runtime state live in a named Docker volume mounted at `/etc/ords/config`
- the ORDS container runs as non-root `oracle`
- observed effective UID/GID: `54321:54321`

Do not bind-mount a host-owned directory over `/etc/ords/config` in the generated setup. Replacing that image-owned writable directory with a host path can make ORDS unable to create its log and config state.

## What This Demo Is For

This workspace focuses on the traditional Oracle APEX Builder workflow.

The goal is to let a new teammate provision a reproducible local Oracle environment, open the ORDS landing URL, open the APEX runtime URL, and continue learning from there without manually rebuilding Oracle setup details. When running on the host, read `compose.yaml` for the published ORDS port and use `http://localhost:<published-port>/ords/_/landing` plus `http://localhost:<published-port>/ords/apex`.

## Progression

```text
Oracle PL/SQL Demo
    ↓
Oracle APEX Demo
    ↓
Oracle APEXlang Demo
```

Oracle APEX Demo is the middle step in that progression. It keeps PL/SQL foundations visible while adding browser-based application development on top.

Capability discovery starts with `docs/capabilities/README.md` and `docs/capabilities/oracle.md`.

Moving from an individual demo environment to team-based development?

Read [Practical Git for Oracle Developers](oracle/practical-git-for-oracle-developers.md).

## Customer Orders Demo

Use the shared sample domain to build a small `Customer Orders Demo` application around:

- Interactive Report for customers and orders
- Interactive Grid for product maintenance
- simple chart for order activity
- dashboard-style KPIs for customers, products, orders, and total sales

## Try It Yourself

1. view customers and orders in an Interactive Report
2. edit products in an Interactive Grid
3. add a simple order chart or dashboard card
4. export report data where supported

## Related Topics

- [Oracle Samples](oracle-samples.md)
- [ORDS](oracle-tools/ords.md)
- [APEX Export / Import](oracle-tools/apex-export-import.md)
- [SQL Developer](oracle-tools/sql-developer.md)
- [Oracle Lifecycle Workflows](oracle-lifecycle-workflows.md)

## Additional Learning Resources

For users who want a structured Slovenian introduction to traditional Oracle APEX development, see:

"Malokodno programiranje z APEX-om: Prirocnik s prakticnimi primeri"
University of Maribor

This open-access book covers practical Oracle APEX development using the traditional APEX Builder workflow and complements this workspace.

This workspace provides the reproducible environment and automation, while the book provides a guided learning path with practical examples.
