# Catalogs

The packaged `catalog/` directory is the declarative extension surface used to resolve workspace capabilities and templates. YAML remains inspectable in both a source checkout and an extracted release.

## Current Layout

```text
catalog/
  capabilities/
  features/
  knowledge-packs/
  mcp/
  services/
  skills/
  templates/
```

Only top-level `*.yaml` files in each directory are loaded. Files are ordered case-insensitively, deserialized with camel-case names, and cached by catalog provider instance. Unknown properties are ignored during loading.

## Features

`catalog/features/<id>.yaml` can define:

- `id`, `displayName`, `description`
- `category` and `lifecycle`
- `alwaysEnabled`
- `requires` and `recommends`
- selected catalog `knowledgePacks` and `capabilities`
- `dependencies.apt`, `dependencies.npm`, and `dependencies.pip`
- ordered `postInstall` commands

Current categories are `runtime`, `knowledge-pack`, `sample-data-pack`, `documentation-pack`, and `template-pack`. Current lifecycle values are `stable`, `preview`, and `experimental`.

Features are appropriate for declarative package groups and readable post-install steps. New orchestration behavior still belongs in code.

## Skills

`catalog/skills/<id>.yaml` defines identity, display text, and `dependencies.features`. Selecting a skill can therefore pull in required feature manifests.

## Services

`catalog/services/<id>.yaml` describes generated Compose services:

- image and host ports
- environment values and profiles
- restart and healthcheck settings
- volumes, entrypoint, and command
- service dependencies and workspace dependency condition

Use existing manifests such as `catalog/services/postgres.yaml` as examples. Never embed secrets in a catalog manifest.

## MCP Entries

`catalog/mcp/<id>.yaml` currently carries `id`, `displayName`, and `description`. This workspace selection catalog is separate from configuring the packaged local MCP executable for an external client.

## Templates

`catalog/templates/<id>.yaml` provides a friendly starting combination:

- optional `workspaceImage`
- feature, service, skill, and MCP ids
- optional smoke metadata for validation automation

After creation, the resulting workspace configuration is canonical. A template is not a hidden ongoing source of truth.

## Capabilities

`catalog/capabilities/<id>.yaml` contains user and agent-facing descriptions, tools, tasks, examples, related documentation, onboarding links, and learning progression. Capabilities explain what is available; they do not by themselves install a tool.

## Knowledge Packs

`catalog/knowledge-packs/<id>.yaml` describes packaged knowledge sources, onboarding references, skill references, output aliases, and workspace index paths. These catalog manifests differ from provider configurations under `knowledgePacks:` in `workspace.yaml`.

Oracle and APEX knowledge packs may point to Oracle-specific documentation and generated metadata. Their operational workflows belong in the [Oracle documentation](../oracle-lifecycle-workflows.md); the loading and resolution rules remain the same as other catalog content.

## Resolution Rules

The resolver combines workspace selections with always-enabled features, feature dependencies, skill dependencies, services, capabilities, and catalog knowledge packs. Validation should reject missing ids, invalid conventions, and dependency problems before generation.

Keep manifests portable. Avoid host-specific absolute paths, container IDs, desktop UI state, credentials, and assumptions tied to one contributor's machine.
