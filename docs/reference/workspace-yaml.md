# Workspace YAML

`workspace.yaml` is the portable source of truth for lasting workspace intent. It is user-owned and should normally be committed with the repository.

## Discovery And Ownership

Existing repository discovery recognizes:

- `workspace.yaml`
- `workspace.yml`
- `.opencode/profile.yaml`
- `.opencode/profile.yml`

The app preserves the discovered path. It does not silently migrate repository layout or replace invalid repository-owned configuration with template defaults.

Change durable behavior here, not in generated `compose.yaml`, `.env`, or `mounts/config/*`. Keep machine-local runtime choices under `.opencode/local/`, which is regenerated and ignored by Git.

## Current Core Shape

```yaml
workspace:
  id: documentation-analysis
  name: Documentation Analysis
  image: ubuntu:24.04

provider:
  type: git
  url: null

runtime:
  default: default
  node: 22

features:
  - core
  - document-processing
skills: []
services:
  - postgres
mcp: []
knowledgePacks: []

terminal:
  font:
    provider: nerd-fonts
    family: JetBrainsMono Nerd Font
  prompt:
    provider: starship
  installIfMissing: true
  utilities:
    zoxide: false
    fzf: false

agent:
  profile: opencode-default

analytics:
  marimoPort: 2718
```

Unknown YAML properties are currently ignored when reading. When the app updates an existing mapping, it updates known top-level fields while preserving unrelated top-level content where possible. Do not depend on unknown properties for runtime behavior.

## Fields

### `workspace`

- `id`: stable slug; blank values are generated from `name`.
- `name`: display name.
- `image`: base workspace image; defaults to `ubuntu:24.04`.

### `provider`

- `type`: defaults to `git`.
- `url`: optional remote URL used by persistence/publish workflows.

### `runtime`

- `default`: runtime definition name, default `default`.
- `node`: requested Node.js major version; omitted or non-positive values normalize to `22`.

### Catalog Selections

- `features`: feature manifest ids; dependencies and always-enabled features are resolved from the catalog.
- `skills`: skill manifest ids; skills can add feature dependencies.
- `services`: service manifest ids used for generated Compose services.
- `mcp`: MCP manifest ids.

Blank entries are removed and duplicate ids are collapsed case-insensitively during normalization. See [Catalogs](catalogs.md).

### `knowledgePacks`

Host-side provider configurations with:

- `provider`: required provider id.
- `enabled`: defaults to `true`.
- `mode`: `optional` by default or `required`.
- `settings`: provider-owned nested YAML.

These differ from catalog `KnowledgePackManifest` entries selected by features. Generated provider output normally lives under `.opencode/knowledge/`.

### `terminal`

- `font.provider`: defaults to `nerd-fonts`.
- `font.family`: defaults to `JetBrainsMono Nerd Font`.
- `prompt.provider`: defaults to `starship`.
- `installIfMissing`: provisioning preference.
- `utilities.zoxide` and `utilities.fzf`: optional shell helpers.

Terminal preferences influence managed workspace artifacts and the app's own Windows Terminal profile. They do not authorize changes to unrelated terminal profiles.

### `agent`

- `profile`: defaults to `opencode-default`.
- `provider`, `connection`, `model`: optional direct overrides.

Prefer a profile unless a workspace intentionally requires explicit provider settings.

### `analytics`

- `marimoPort`: optional positive host port; the default behavior uses `2718` where the analytics runtime applies it.

### `oracle`

The core schema supports optional `databaseImage`, positive `hostPort`, positive `ordsPort`, and `apex` environment identity fields. Never store Oracle passwords or tokens in this file.

For operational meaning, image/volume reset rules, SQLcl profiles, APEX synchronization, and deployment profiles, use the [Oracle Lifecycle Workflows](../oracle-lifecycle-workflows.md) and related Oracle guides rather than treating this general schema reference as an Oracle runbook.

## Generated Companions

Generation produces inspectable files including `compose.yaml`, `.env`, provisioning and attach scripts under `mounts/config/`, `.opencode/local/runtime-state.yaml`, and `mounts/config/applied-state.yaml`. Generated headers identify source inputs and edit ownership.

Successful prepare/update operations persist applied state. Stop/start alone should not mark a workspace as needing update.

## Secret Safety

Do not store credentials, API keys, private keys, tokens, password-bearing connection strings, machine identifiers, or transient runtime state in workspace YAML. Save Point validation can block suspicious changed or untracked content, including nested files.
