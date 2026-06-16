# ORDS

## What It Is

ORDS is Oracle REST Data Services, the HTTP layer that serves Oracle APEX and Oracle REST endpoints.

## Why It Exists

Use ORDS when you want to:

- reach the APEX login page
- verify browser-based Oracle development is available
- troubleshoot application URLs
- confirm local onboarding environment health

## How It Fits The Demo

- Oracle APEX Demo: required for the APEX Builder experience
- Oracle APEXlang Demo: same APEX runtime while export/import stays source-controlled

## Example Commands

```bash
curl -fsSL http://localhost:8181/ords
curl -fsSL http://localhost:8181/ords/apex
```

## Relationship To Other Tools

- ORDS serves Oracle APEX over HTTP
- SQLcl handles command-line database automation
- APEX Export / Import moves the application definition behind that runtime

## Licensing / Prerequisite Notes

- used with Oracle Database Free and Oracle APEX in this demo family
- no separate cloud-only product assumed

## Beginner Exercise

1. run the ORDS health-check script
2. open the ORDS base URL
3. open the APEX login URL
4. compare the generated URLs with the workspace `.env`
