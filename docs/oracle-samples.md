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
- build a small browser application on top of the same schema after APEX runtime is installed
- export, validate, review, and re-import the same application definition

## Progression

- Oracle PL/SQL Demo teaches the schema, view, package, procedure calls, and verification workflow.
- Oracle APEX Demo supplies the schema and placeholder from which the tutorial builds a `Customer Orders Demo` with reports, grids, charts, and dashboard-style KPIs; that complete application is not shipped by the template.
- Oracle APEXlang Demo targets the same tutorial application and adds export, shape check, version-matched validation, review, and import workflow in Git.
