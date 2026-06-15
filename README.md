# OpenCode Workspace Manager

OpenCode Workspace Manager is a Windows WPF application for durable AI workspaces with disposable runtimes, local recovery, and safe Git-based working flows.

<img src="docs/walkthrough/images/main-window.png" alt="OpenCode Workspace Manager main window" width="900" />

*Current main window showing workspace status, branch safety information, recovery state, and workspace management tools.*

OpenCode Workspace Manager lets you work with AI agents in isolated, reproducible workspaces without risking your main branch or development environment.

Import an existing Git checkout, create a temporary workspace branch, work safely, and recover progress using Save Points and Checkpoints.

## Inside The Workspace

OpenCode Workspace Manager creates and protects durable workspaces.

Once a workspace is opened, work happens inside an attached OpenCode session.

<img src="docs/walkthrough/images/opencode-terminal-attached.png" alt="OpenCode terminal attached to workspace" width="900" />

The runtime is disposable. The work is durable.

<img src="docs/walkthrough/images/opencode-terminal-working.png" alt="OpenCode actively working inside a workspace" width="900" />

**New here?**
Start with the visual walkthrough:

- [Quick Start Walkthrough](docs/walkthrough/quick-start.md)
- [Existing Git Checkout Workflow](docs/walkthrough/existing-git-checkout.md)

## Most Common Use Case

Already have a Git repository?

Import the checkout, create an isolated workspace branch, open a disposable runtime, and work without touching `main` or `release` branches.

-> [Existing Git Checkout Workflow](docs/walkthrough/existing-git-checkout.md)

## Featured Workspace: Oracle PL/SQL Demo

Not every workspace starts with an existing Git repository.

The Oracle PL/SQL Demo workspace is a concrete example of what Workspace Manager enables: a safe local environment for learning Oracle, understanding existing PL/SQL, experimenting freely, onboarding new developers, and validating ideas before touching shared environments.

Many Oracle teams rely heavily on shared development or staging databases.

The Oracle PL/SQL Demo workspace provides a local environment where developers can learn, experiment, troubleshoot, and understand PL/SQL without affecting shared systems.

It can be created in minutes, reset when needed, and reused as a repeatable onboarding and practice environment.

Supporting tools and content include:

- Oracle Database Free
- SQLcl
- SQL*Plus
- SQL Developer integration
- sample schema and data
- PL/SQL procedures and triggers
- guided tutorials
- AI-assisted PL/SQL explanation

Developers can:

- learn Oracle tooling
- understand existing PL/SQL
- experiment with queries and schema changes
- prototype ideas safely
- validate concepts before touching shared environments

The environment can be reset and recreated at any time.

**Learn more:** [Oracle PL/SQL Demo Workspace](docs/oracle-demo.md)

## Featured Workspace: Documentation Features

The Documentation Features workspace is a ready-to-run report and publishing environment for teams that need reliable PDF output, diagrams, validation, and broad Windows-compatible font coverage inside Ubuntu workspaces.

Included tooling covers:

- Markdown to PDF with `pandoc` and `typst`
- HTML to PDF with `weasyprint`
- Mermaid, Graphviz, and PlantUML diagrams
- PDF inspection with `pdfinfo`, `pypdf`, and `pymupdf`
- Report generation with `reportlab`
- multilingual and business-document fonts including Carlito, Caladea, Liberation, Noto, Inter, Roboto, JetBrains Mono, Fira Code, and optional Microsoft-compatible core fonts

Each generated workspace also includes validation and demo scripts so the toolchain can be checked immediately after provisioning.

**Learn more:** [Documentation Features Workspace](docs/documentation-features.md)

## Who Is This For?

- developers using AI coding agents
- teams experimenting on existing repositories
- projects requiring reproducible environments
- users who want local-first recovery before cloud backup

## What It Does

OpenCode Stuff packages tools, documentation, automation, and AI into reusable workspaces that can be opened on another machine with minimal setup.

A workspace is the durable body of work. A runtime is the replaceable tool environment. A session is the temporary execution where work happens.

Recommended for existing projects: use the [Existing Git Checkout Workflow](docs/walkthrough/existing-git-checkout.md) to create an isolated workspace branch instead of working directly on `main`.

## Features

- durable workspaces with disposable runtimes
- safe local recovery with Save Points and Checkpoints
- explicit Publish to remote backup flow
- existing Git checkout workflow with isolated workspace branches
- safety status that shows branch risk, local changes, and backup state

## Installation

1. Install prerequisites.
2. Configure Git credentials.
3. Create or open a workspace.
4. Start the runtime.
5. Start working.

Detailed setup:

- [Windows Setup](docs/windows-prerequisites.md)
- [First Workspace Guide](docs/first-workspace.md)

## Configuration

1. Open OpenCode Workspace Manager.
2. If the quick tutorial prompt appears, start it for a short walkthrough.
3. Create a workspace with a clear name and keep your files inside that workspace folder.
4. Open the workspace from the list or by double-clicking it.
5. Open Workspace resumes the latest OpenCode session when one exists. If no OpenCode session exists, a new one is started.
6. Create a Save Point before a big change, create another after a good state, and Publish to remote backup only when you want off-machine backup.

## Typical Workflow

1. Import an existing Git repository.
2. Create an isolated workspace branch.
3. Open a disposable runtime.
4. Work with AI agents.
5. Create Save Points during important milestones.
6. Merge, publish, archive, or discard changes when finished.

## Workspace / Runtime / Session

- workspace: durable body of work
- runtime: disposable tool environment
- session: temporary execution inside the runtime

## Development

Development is centered on durable workspaces, disposable runtimes, and repeatable setup.

Node.js 22 LTS is the default runtime baseline for new workspaces.

Why: recent documentation, analytics, visualization, browser automation, diagram generation, MCP tooling, and AI-adjacent npm packages increasingly target Node 20+ and Node 22 LTS. Using Node 22 reduces engine warnings and compatibility drift across workspace templates.

## Safety & Recovery

- Save Point protects local progress.
- Publish is explicit.
- Backup is optional but recommended.
- Restore favors recovery over overwrite.
- Timeline records key recovery and publish events.
- Working Copy is the normal place to make local changes safely.

Git is the persistence engine underneath.

The workspace provides the experience.

### Workspace Safety

- Save Points protect work.
- Backups protect against machine loss.
- Secrets are protected.
- Disposable files are ignored.
- Important work is preserved.

Your work is protected locally. Remote backup is optional but recommended.

## Architecture

Git is the persistence engine underneath. The workspace provides the experience.

Read more:

- [Architecture](docs/architecture.md)
- [Philosophy](docs/philosophy.md)
- [Workspace Guide](docs/workspace-yaml.md)
- [Runtime Guide](docs/concepts/runtime.md)

## Documentation

### Getting Started

- [Quick Start Walkthrough](docs/walkthrough/quick-start.md)
- [Existing Git Checkout Workflow](docs/walkthrough/existing-git-checkout.md)
- [Windows Setup](docs/windows-prerequisites.md)
- [First Workspace Guide](docs/first-workspace.md)

### Concepts

- [Workspace](docs/concepts/workspace.md)
- [Runtime](docs/concepts/runtime.md)
- [Session](docs/concepts/session.md)
- [Save Points](docs/concepts/save-point.md)

### Reference

- [Architecture](docs/architecture.md)
- [Workspace YAML](docs/workspace-yaml.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Fact Sheet](docs/fact-sheet.md)
- [Oracle PL/SQL Demo Workspace](docs/oracle-demo.md)

## Learn More

For the reasoning behind the project and the Durable Workspace model, see [docs/philosophy.md](docs/philosophy.md).

- Philosophy: [docs/philosophy.md](docs/philosophy.md)
- Workspace Concepts: [docs/concepts/workspace.md](docs/concepts/workspace.md)
- Runtime Concepts: [docs/concepts/runtime.md](docs/concepts/runtime.md)
- Session Concepts: [docs/concepts/session.md](docs/concepts/session.md)
- Save Points: [docs/concepts/save-point.md](docs/concepts/save-point.md)
- Content Classification: [docs/concepts/content-classification.md](docs/concepts/content-classification.md)
- Fact Sheet: [docs/fact-sheet.md](docs/fact-sheet.md)

## Philosophy

> There is no magic. Only stuff.
