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
curl -fsSL http://localhost:8181/ords/_/landing
curl -fsSL http://localhost:8181/ords/apex
```

## Endpoint Discovery

ORDS has two different address styles in this project:

- internal Docker service address
- host published address

Choose the address based on where the command is running.

### Inside The Workspace Container

- `localhost` is the workspace container itself, not ORDS
- use the internal Docker service name and internal ORDS port from `compose.yaml`

Example:

```text
http://oracle-ords:8080/ords/_/landing
http://oracle-ords:8080/ords/apex
```

### On The Host

- use the published host port from `compose.yaml`
- do not assume the published port is always `8181`

Example:

```text
http://localhost:<published-port>/ords/_/landing
http://localhost:<published-port>/ords/apex
```

## Recommended Verification Flow

1. determine whether you are on the host or inside the workspace container
2. locate `compose.yaml`
3. determine the ORDS endpoint
4. run `GET /ords/_/landing`
5. run `GET /ords/apex`
6. if ORDS is unreachable, report the exact endpoint that was tested

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
