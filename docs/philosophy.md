# Philosophy

## There Is No Magic. Only Stuff.

OpenCode Stuff makes useful work easier to preserve, reopen, recover, and reuse. Powerful tooling may feel magical, but its operation must remain inspectable:

> Software may feel magical. Its operation should never be mysterious.

Reliable work comes from understandable assets: specifications, source, configuration, documentation, tests, validation, automation, and history. Important knowledge belongs in repositories rather than private setup rituals, one model, one vendor, or one chat transcript.

## Durable Workspaces

The workspace is the durable asset. It preserves sources, knowledge, artifacts, history, and recovery information. The runtime is replaceable and supplies tools, automation, and AI capabilities. A session is temporary interaction with that runtime.

Work should survive tool, runtime, model, machine, and team changes. Containers, PTYs, sessions, caches, and generated wiring serve the workspace; they do not own it.

## Git As Persistence

Git provides durability, history, synchronization, and recovery without a proprietary storage system. Normal product language remains workspace-oriented:

- Save Point maps to a commit
- Working Copy usually maps to a local branch
- Publish maps to an intentional push
- Restore recovers prior work, preferably as a new copy

Experts retain access to standard Git tools. The product simplifies routine work without hiding its storage engine.

## Recoverability Over Convenience

> Conflict is not failure. Lost work is failure.

Destructive actions stop when safety is uncertain. Publish is explicit, conflicts are preserved for review, local recovery and off-machine backup remain distinct, and restore-as-copy is preferred to overwrite.

Preserve durable sources, decisions, reports, documentation, datasets, and deliverables. Ignore known caches and rebuildable output. Unknown hidden content is reviewed rather than silently discarded or committed. Secrets are never durable workspace content.

## Visible And Transferable Knowledge

Complexity can be packaged, but not concealed. Generated artifacts identify their inputs, important behavior has tests or validation, and documentation provides maps from intent to implementation and recovery.

Repeated discoveries should become documentation; recurring mistakes should become validation; recurring workflows should become automation; proven results should become reusable capability. A repository should become easier to understand over time and preserve the path from use to inspection, modification, contribution, and teaching.

AI accelerates this process but does not own the result. The source of truth remains repository-owned evidence.

## Product Direction

OpenCode Stuff is a durable workspace manager, not a Docker console or a terminal launcher. The primary user intent is to open a workspace and continue useful work without needing to understand disposable runtime details. The platform should make advanced work approachable while retaining explicit ownership, readable control flow, portable definitions, and honest recovery boundaries.

See [Design Principles](design-principles.md), [Architecture Overview](architecture/overview.md), and [Recovery Model](architecture/recovery-model.md).
