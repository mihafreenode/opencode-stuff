# Oracle APEX Demo

Oracle APEX Demo extends the Oracle PL/SQL path with browser-based Oracle application development.

It uses Oracle Database Free, Oracle REST Data Services (ORDS), Oracle APEX, and SQLcl to provide a reproducible local environment for learning and onboarding.

## What This Demo Is For

This workspace focuses on the traditional Oracle APEX Builder workflow.

The goal is to let a new teammate provision a reproducible local Oracle environment, open `http://localhost:8181/ords`, and continue learning from there without manually rebuilding Oracle setup details.

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
