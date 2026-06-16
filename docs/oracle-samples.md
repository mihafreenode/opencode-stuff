# Oracle Samples

All Oracle demos use one shared sample domain:

```text
Customers
    ↓
Orders
    ↓
Products
```

This keeps onboarding coherent across:

- Oracle PL/SQL Demo
- Oracle APEX Demo
- Oracle APEXlang Demo

## Sample Objects

- `DEMO_CUSTOMERS`
- `DEMO_PRODUCTS`
- `DEMO_ORDERS`
- `DEMO_ORDER_SUMMARY_V`
- `DEMO_CUSTOMER_API`

## Learning Goals

- understand basic tables and relationships
- query a view instead of joining everything manually every time
- run packaged logic from SQLcl or SQL Developer
- build a small browser application on top of the same schema
- export, validate, review, and re-import the same application definition

## Progression

- Oracle PL/SQL Demo teaches the schema, view, package, procedure calls, and verification workflow.
- Oracle APEX Demo uses the same domain for a small `Customer Orders Demo` application with reports, grids, charts, and dashboard-style KPIs.
- Oracle APEXlang Demo keeps the same application but adds export, validate, review, and import workflow in Git.
