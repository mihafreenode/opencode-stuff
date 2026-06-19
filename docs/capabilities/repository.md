# Repository Workflows

## What It Is

This capability covers the durable repository conventions that make a workspace self-describing, including `workspace.yaml`, generated artifacts, save-point language, and `AGENTS.md` guidance.

## Why Use It

Use it first when you need to understand how to work safely in the repository before changing code, publishing work, or investigating generated files.

For Oracle teams moving from SVN-era workflows, [Practical Git for Oracle Developers](../oracle/practical-git-for-oracle-developers.md) explains the Oracle-specific mindset behind Save Points, repository-first documentation, and low-risk Git adoption.

## Available Tools

### Git

Purpose: Persistent history, working copies, save points, and publish workflows.

Supported workflows: working copy creation, save point and publish flows, reviewable change history.

Common use cases: preserve work before risky edits, review changes before publishing.

### workspace.yaml

Purpose: Canonical workspace intent and feature selection.

Supported workflows: workspace discovery, runtime regeneration, capability catalog generation.

Common use cases: understand enabled features quickly, change portable workspace behavior.

### AGENTS.md

Purpose: Repository-specific guidance for humans and AI agents.

Supported workflows: onboarding, safety guidance, capability discovery entry point.

Common use cases: learn local rules without scanning the repository, resume work after attach, or continue after a `Repair Runtime` action.

## Typical Tasks

- Inspect `workspace.yaml` and the capability catalog before searching the repository.
- Create or review Working Copies and Save Points before making significant changes.
- Use `AGENTS.md` and generated capability guidance to follow local repository rules.

## Examples

- Open `AGENTS.md`, then `docs/capabilities/README.md`, before searching for implementation details.
- Review generated files through their headers and edit `workspace.yaml` or catalog manifests instead of patching runtime artifacts directly.

## Related Documentation

- [Capability Catalog](README.md)
Primary discovery index for enabled capabilities.
- [AGENTS Guidance](../../AGENTS.md)
Repository-specific workflow and safety guidance.
- [Practical Git for Oracle Developers](../oracle/practical-git-for-oracle-developers.md)
Oracle-focused transition guidance that complements the repository mechanics described here.

## Related Capabilities

- [Documentation](documentation.md)
- [Testing](testing.md)
