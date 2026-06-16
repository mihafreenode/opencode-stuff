# SQLcl

## What It Is

SQLcl is Oracle's command-line SQL and scripting client for database work, script execution, and APEX-related automation.

## Why It Exists

Use SQLcl when you want to:

- connect to the demo database quickly
- run setup or tutorial scripts
- execute package procedures
- automate export/import workflows
- keep onboarding steps scriptable and reviewable

## How It Fits The Demo

- Oracle PL/SQL Demo: first interactive tool for the sample schema and package
- Oracle APEX Demo: supports schema verification behind the browser application
- Oracle APEXlang Demo: supports export/import automation around the same application

## Example Commands

```bash
sql demo_user/demo_password@//oracle-demo:1521/FREEPDB1
@tutorial/oracle/scripts/03-sample-queries.sql
exec demo_customer_api.get_customer(1)
```

## Relationship To Other Tools

- SQLcl executes SQL and automation scripts
- Data Pump moves schema/data in larger units
- APEX Export / Import moves application definitions
- APEXlang makes application exports more reviewable in Git

## Licensing / Prerequisite Notes

- public Oracle tooling
- used with Oracle Database Free in these demos
- requires Java at runtime, which the workspace provisioning handles

## Beginner Exercise

1. connect with SQLcl
2. query `DEMO_CUSTOMERS`
3. query `DEMO_ORDER_SUMMARY_V`
4. execute `demo_customer_api.get_customer(1)`
5. execute `demo_customer_api.get_order_total(1)`
