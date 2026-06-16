# SQL Developer

## What It Is

SQL Developer is Oracle's desktop IDE for browsing schema objects, running queries, and executing PL/SQL visually.

## Why It Exists

Use it when you want to:

- browse tables and views visually
- inspect package specs and bodies
- run ad hoc SQL queries
- help first-time Oracle users who prefer a GUI before SQLcl

## How It Fits The Demo

- Oracle PL/SQL Demo: optional visual path for exploring sample objects
- Oracle APEX Demo: useful for checking schema objects behind the browser app

## Example Connection

```text
Host: localhost
Port: 1521
Service: FREEPDB1
Username: demo_user
Password: demo_password
```

## Relationship To Other Tools

- SQL Developer is optional and GUI-oriented
- SQLcl is the scripted and automation-friendly path
- APEX Builder handles browser application design

## Licensing / Prerequisite Notes

- documentation-only in this demo family
- not a provisioning dependency
- requires a separate local install by the developer

## Beginner Exercise

1. connect using the generated demo connection details
2. browse `DEMO_CUSTOMERS`, `DEMO_PRODUCTS`, and `DEMO_ORDERS`
3. query `DEMO_ORDER_SUMMARY_V`
4. inspect `DEMO_CUSTOMER_API`
