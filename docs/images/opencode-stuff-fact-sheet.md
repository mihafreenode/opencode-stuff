# opencode stuff

> There is no magic. Only stuff.

OpenCode Stuff helps package tools, documentation, automation, and AI into reusable workspaces that can be opened on another machine with minimal setup.

---

## The Goal

### Install prerequisites.
### Open a workspace.
### Start working.

Your workspace carries the complexity so you can focus on your work.

---

## Key Concepts

### Workspace

A durable body of work.

Contains:

- Sources
- Knowledge
- Work
- Artifacts
- History

A workspace survives:

- Runtime replacement
- Tool upgrades
- Machine changes
- AI model changes

---

### Runtime

A disposable tool environment.

Provides:

- Tools
- Automation
- AI capabilities
- MCP integrations

Runtimes are replaceable.

---

### Session

A temporary execution of a runtime attached to a workspace.

You can:

- Start
- Stop
- Replace
- Restart

without losing the workspace.

---

## Safety & Recovery

### Save Point

Capture a meaningful milestone.

Internally backed by Git.

Examples:

- Before source import
- Before report generation
- After review
- Before publishing

---

### Publish

Back up your Save Points to a remote repository.

Publishing is always explicit.

Nothing is published automatically.

---

### Backup

Protect work against machine loss.

Remote backup is recommended but optional.

---

### Restore

Restore work from:

- Save Points
- Checkpoints
- Published versions

Default behavior:

> Restore as a new copy.

Your work is never overwritten automatically.

---

## Why Workspaces?

Workspaces help you:

- Avoid repeated setup
- Onboard others faster
- Preserve knowledge
- Protect work
- Stay productive
- Reproduce environments

Instead of rebuilding tools, documentation, automation, and workflows on every machine, package them once and reuse them.

---

## Git Provides Durability. You Get the Experience.

Git is the persistence engine underneath.

The workspace provides the user experience.

Most users work with:

- Save Points
- Publish
- Backup
- Restore

Advanced Git tools remain available when needed.

---

## First-Time Setup

1. Install WSL
2. Install Docker Desktop
3. Configure Git credentials
4. Create or open a workspace
5. Start a runtime
6. Start working

---

## What's Included

### Tools

Curated and configured tooling.

### Automation

Scripts, tasks, and workflows.

### AI Capabilities

Assistants, models, and integrations.

### Knowledge

Documentation, notes, and guides.

### Artifacts

Generated outputs and reports.

### History

Save Points, checkpoints, and decisions.

---

## Workspace Safety

The application continuously evaluates:

### Local Recovery

- Save Points
- Checkpoints
- Working Copy protection

### Off-Machine Backup

- Remote repository configured
- Published Save Points
- Backup status

Possible states:

- Protected
- Partially Protected
- At Risk
- Needs Review

Principle:

> Conflict is not failure. Lost work is failure.

---

## Documentation

- Philosophy
- Workspace Guide
- Runtime Guide
- Architecture
- Troubleshooting

Read more:

**docs/philosophy.md**

---

## Platform Support

### Host

- Windows (primary)

### Runtime

- Ubuntu
- Docker
- WSL2

Future runtime providers may include:

- Remote Linux hosts
- Shared development servers
- Cloud workspaces

without changing the workspace model.

---

## Philosophy

Useful discoveries should not be repeated.

Over time:

- A lesson becomes documentation.
- A repeated lesson becomes automation.
- A recurring mistake becomes validation.
- A proven workflow becomes a reusable workspace.

OpenCode Stuff exists to preserve those discoveries and make them portable.

> There is no magic. Only stuff.