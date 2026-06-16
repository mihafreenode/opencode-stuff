# AGENTS.md Guide

`AGENTS.md` is a repository-owned guide for contributors and AI coding agents.

It helps a repository carry not only source code, but also the practical rules for working with that code.

## Why It Exists

OpenCode Workspace Manager supports repository discovery.

That means a cloned repository can bring its own:

- `workspace.yaml`
- `README.md`
- `docs/`
- scripts
- `AGENTS.md`

`workspace.yaml` explains the environment.

`AGENTS.md` explains how work should be done inside that environment.

## Relationship To Workspace Onboarding

Typical onboarding flow:

```text
Clone Repository
    ↓
Open Repository
    ↓
Workspace Discovered
    ↓
Provision Environment
    ↓
Read Documentation
    ↓
Start Working
```

`AGENTS.md` belongs in the `Read Documentation` step.

It gives a new developer or coding agent the local rules that are easy to miss if they exist only in chat history or tribal knowledge.

## Relationship To AI Agents

AI coding agents use `AGENTS.md` as repository-specific working guidance.

That usually includes:

- product intent
- naming expectations
- UX language
- safety rules
- testing expectations
- important workflows
- files or folders that matter most

Good `AGENTS.md` guidance reduces repeated explanation and helps agents make smaller, safer changes.

## What Belongs In AGENTS.md

Keep it practical.

Good examples:

- how the product should be described
- what terminology the UI should use
- what files are canonical inputs
- what generated files should not be edited directly
- what tests are expected before merging
- what risky operations should be avoided
- what onboarding or recovery rules must not regress

## What Does Not Belong In AGENTS.md

Avoid using it for:

- secrets or credentials
- private tokens
- machine-specific temporary notes
- long release notes
- duplicate copies of full architecture docs when a link is enough

If information is durable product or user documentation, prefer `docs/`.

If information is environment configuration, prefer `workspace.yaml` or catalog manifests.

## Editing Expectations

`AGENTS.md` is version-controlled repository guidance.

Update it when local working rules change in ways that future contributors or AI agents need to know.

Keep it readable, reviewable, and specific to the repository.

## Simple Example

Examples of useful `AGENTS.md` rules:

- call the product a durable workspace manager, not a Docker console
- keep `workspace.yaml` as the source of truth
- do not hide important onboarding behavior in generated files only
- prefer official Oracle commands and formats over custom wrappers

That kind of guidance helps both humans and AI agents work consistently after repository discovery.
