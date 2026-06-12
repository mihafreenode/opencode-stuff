# Catalogs

## Purpose

Catalogs are the contributor-friendly extension surface for the MVP.

The rule of thumb is simple:

- if a capability can be described declaratively, prefer a YAML manifest
- only change C# when the platform needs new orchestration behavior

## Layout

```text
catalog/
  features/
  skills/
  services/
  mcp/
  templates/
```

## Add A Feature

Create `catalog/features/<id>.yaml`.

Use `catalog/features/core.yaml` as the reference example.

Feature manifests are best for:

- apt package groups
- npm package groups
- pip package groups
- post-install commands such as `npx playwright install`

## Add A Skill

Create `catalog/skills/<id>.yaml`.

The MVP skill model is intentionally simple so the repository already communicates where reusable OpenCode capability manifests belong, even before the runtime consumes every skill field.

## Add A Service

Create `catalog/services/<id>.yaml`.

Use `catalog/services/postgres.yaml` or `catalog/services/pgadmin.yaml` as examples.

Service manifests are best for:

- container image selection
- host port exposure
- environment variables
- service dependencies
- named volumes

## Add A Template

Create `catalog/templates/<id>.yaml`.

Templates select a friendly starting combination of features and services, but the durable workspace behavior still ends up in `workspace.yaml`.

## Portability Rule

Catalog content should stay portable enough for future hosted Workspace Stuff use.

Avoid encoding:

- Windows-only paths
- local machine assumptions
- Docker container IDs
- WPF UI state
