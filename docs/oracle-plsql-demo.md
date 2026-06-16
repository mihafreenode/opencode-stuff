# Oracle PL/SQL Demo

The Oracle PL/SQL Demo is the first Oracle onboarding template in this repository.

It focuses on:

- local Oracle Database Free setup
- SQLcl-based onboarding
- sample PL/SQL objects and data
- repeatable workspace generation
- one shared `Customers / Products / Orders` sample domain

## First-Time Onboarding

Use this as the first Oracle walkthrough:

```text
Clone Repository
    ↓
Open Repository
    ↓
Workspace Discovered
    ↓
Provision Environment
    ↓
Read Docs
    ↓
Run Tutorial
```

Recommended order:

1. read `README.md`
2. review `docs/team-onboarding.md`
3. review `docs/agents-guide.md` when the repository includes `AGENTS.md`
4. provision the workspace
5. open the workspace
6. run the generated Oracle tutorial or verification scripts before moving to shared environments

Capability discovery starts with `docs/capabilities/README.md` and `docs/capabilities/oracle.md`.

Progression:

```text
Oracle PL/SQL Demo
    ↓
Oracle APEX Demo
    ↓
Oracle APEXlang Demo
```

Use Oracle APEX Demo only after this PL/SQL foundation is comfortable.

## Try It Yourself

1. query `DEMO_CUSTOMERS`
2. query `DEMO_PRODUCTS`
3. query `DEMO_ORDER_SUMMARY_V`
4. execute `demo_customer_api.get_customer(1)`
5. inspect the view definition for `DEMO_ORDER_SUMMARY_V`

## Related Topics

- [Oracle Samples](oracle-samples.md)
- [SQLcl](oracle-tools/sqlcl.md)
- [Data Pump](oracle-tools/data-pump.md)
- [SQL Developer](oracle-tools/sql-developer.md)
- [Oracle Lifecycle Workflows](oracle-lifecycle-workflows.md)
