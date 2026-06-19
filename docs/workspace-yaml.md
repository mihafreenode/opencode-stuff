# workspace.yaml

`workspace.yaml` is the durable definition of a workspace.

If you want to change lasting workspace behavior, this is the file to edit.

Read this after the README and first-workspace guide if you want to understand how a workspace is described on disk.

## Purpose

`workspace.yaml` is the canonical definition of a workspace.

It is user-owned, durable, and intended to stay portable between the local-first OpenCode Stuff environment and future hosted workspace environments.

## Repository Onboarding

Templates are used once.

After a repository contains workspace configuration, that repository becomes the source of truth for onboarding and workspace setup.

OpenCode Workspace Manager discovers these supported configuration paths when opening an existing repository:

- `workspace.yaml`
- `workspace.yml`
- `.opencode/profile.yaml`
- `.opencode/profile.yml`

The application keeps using the discovered path. It does not auto-migrate or silently normalize repository layout.

In practice, that means the repository can carry:

- source code
- workspace configuration
- onboarding docs
- AGENTS guidance
- scripts and troubleshooting notes

Together these form a reusable project knowledge base.

## Ownership Rules

- edit `workspace.yaml` to change lasting workspace behavior
- do not edit generated `compose.yaml` for durable changes
- do not rely on container IDs or Windows-specific paths as portable configuration
- do not store machine-local runtime choices in `workspace.yaml`; `.opencode/local/` is regenerated per machine

In practice:

- the workspace is the durable asset
- the runtime can be replaced
- generated runtime files can be recreated

## Current MVP Shape

```yaml
workspace:
  id: documentation-analysis
  name: documentation-analysis
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
  - pgadmin

mcp: []

agent:
  profile: opencode-default

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
```

## Field Guide

### `workspace`

Top-level workspace identity and runtime defaults.

- `id`: stable workspace identifier
- `name`: user-facing and filesystem-friendly workspace name
- `image`: base container image, `ubuntu:24.04` by default in the MVP

### `provider`

Workspace persistence provider settings.

- `type`: `git` by default
- `url`: optional remote backup location

**WSL vs Docker**

Use a WSL workspace if your workflow depends on Windows tools such as PowerShell, Visual Studio, MSBuild, WPF, or MSIX packaging.

Use a Docker workspace when portability, reproducibility, and Linux-based development or automation are the primary goals.

### `runtime`

Runtime selection settings.

- `default`: default runtime definition name under `runtimes/`
- `node`: requested Node.js major version for the workspace runtime, `22` by default for new workspaces

If a workspace omits `runtime.node`, OpenCode Workspace Manager currently normalizes it to Node.js 22 LTS during generated runtime updates and new provisioning runs.

Examples:

```yaml
runtime:
  default: default
  node: 20
```

```yaml
runtime:
  default: default
  node: 22
```

```yaml
runtime:
  default: default
  node: 24
```

### `features`

Human-friendly capability selections. Features expand to provisioning actions such as apt packages or npm packages.

### `skills`

Reserved for reusable OpenCode capability manifests.

### `services`

Additional Compose services such as PostgreSQL or pgAdmin.

### `mcp`

Optional integrations that remain manifest-driven for future expansion.

### `terminal`

Terminal preferences describe the recommended shell experience for the workspace.

- `font`: preferred Windows Terminal Nerd Font for the OpenCode Stuff managed profile
- `prompt`: prompt provider such as `starship` or `default-bash`
- `installIfMissing`: whether recommended terminal tools should be installed automatically during provisioning
- `utilities`: optional helpers such as `zoxide` and `fzf`

### `agent`

Agent preferences keep new workspaces usable immediately.

- `profile`: recommended way to reference a reusable agent profile
- `provider`, `connection`, `model`: advanced overrides when a user intentionally wants to bypass the default profile behavior

If no explicit agent settings exist, OpenCode Stuff resolves the built-in default profile:

- profile: `opencode-default`
- provider: `opencode`
- connection: `zen`
- model: `big-pickle`

## Generated Companions

The application generates these runtime artifacts beside `workspace.yaml`:

- `compose.yaml`
- `.env`
- provisioning scripts under `mounts/config/`

Those files include generated headers that point contributors back to `workspace.yaml` and the catalog manifests.

## Documentation Features Template

The built-in `documentation-analysis` template is presented in the UI as `Documentation Features`.

It is intended for report-heavy work such as manuals, architecture documentation, tutorials, multilingual PDFs, and data analysis and reporting.

Its generated workspace content includes:

- `DOCUMENTATION-FEATURES.md`
- `docs/documentation-features.md`
- `scripts/validate-documentation-tooling.sh`
- `scripts/demo-documentation-workflows.sh`
- sample Markdown, HTML, and Mermaid inputs under `samples/documentation/`

The provisioning plan behind that template includes:

- Markdown to PDF tooling with `pandoc` and `typst`
- HTML to PDF tooling with `weasyprint`
- diagram tooling with Mermaid, Graphviz, and PlantUML
- PDF inspection tooling with `poppler-utils`, `pypdf`, and `pymupdf`
- report-generation tooling with `reportlab`
- Windows-friendly font coverage with Liberation, Carlito, Caladea, Noto, Inter, Roboto, JetBrains Mono, Fira Code, and optional Microsoft core fonts when the Ubuntu image provides `ttf-mscorefonts-installer`

## v0.1 Default Example

When the UI creates a new workspace in the MVP, it starts from:

- `image: ubuntu:24.04`
- the always-on `core` feature
- `runtime.node: 22`
- any additional features or services selected in the create form
