# Philosophy

## There Is No Magic. Only Stuff.

If you want the concise overview first, see the [fact sheet](fact-sheet.md).

`opencode stuff` starts from a simple observation: productive work rarely depends on one big idea. It depends on useful things that were discovered, written down, validated, and preserved.

Over time, teams accumulate:

- reproducible environments
- version history
- automation
- documentation
- validation
- searchable knowledge
- repeatable workflows
- shared conventions

The project philosophy is summarized by the phrase:

> There is no magic. Only stuff.

That is not a joke about complexity. It is an engineering reminder that reliable systems come from understandable parts assembled carefully over time.

## The Setup Problem

Many effective working environments depend on a combination of repositories, terminals, scripts, automation, notes, and operational knowledge.

For developers, these systems are often tolerable because the setup steps become familiar. For everyone else, the setup itself becomes the barrier.

Recreating useful environments manually is slow, error-prone, and difficult to share. It encourages drift: one machine has the fix, another has the script, and a third depends on memory that was never written down.

`opencode stuff` exists because repeated setup work is wasteful, and because useful environments should be easier to preserve and reopen than they usually are.

## Durable Workspaces

The project is built around the idea that the work should outlive the tool environment.

Work should survive:

- tool changes
- runtime replacement
- AI model changes
- machine replacement

To make that practical, the system separates three concepts:

- Workspace: the durable body of work
- Runtime: the disposable execution environment
- Session: a temporary running instance attached to a workspace

This separation exists because they change at different speeds.

A workspace may live for months or years. A runtime may be upgraded or replaced many times. A session may last minutes or hours.

Treating them as separate concepts makes recovery, migration, and replacement much easier to reason about.

## Git As A Persistence Engine

Git is used because it already solves several hard problems well: durability, history, branching, synchronization, and recovery.

Most users should not need Git expertise to benefit from that.

Instead, the normal experience is framed in workspace terms:

- Save Point maps to a commit
- Publish maps to a push
- Working Copy usually maps to a local branch
- Restore maps to recovering a prior state as a new copy

Advanced Git remains available when needed.

> Git provides the storage engine. The workspace provides the user experience.

This is an important design constraint. The application should simplify Git for normal use without hiding the underlying repository from an expert who needs to inspect or recover it with standard tools.

## Recoverability Over Convenience

The project prefers recoverability over cleverness.

That means:

- lost work is failure
- destructive actions should be avoided
- restore as copy is preferred over overwrite
- local recovery and off-machine backup are separate concerns
- conflicts should be preserved for review rather than hidden

> Conflict is not failure. Lost work is failure.

This changes the default behavior.

If the system is unsure whether work is protected, it should report that it is not protected. If publication is risky, it should stop and ask for review. If recovery is needed, it should create a new copy by default instead of mutating the current workspace in place.

## Preserve Work, Not Noise

Workspaces should preserve:

- sources
- knowledge
- decisions
- artifacts
- history

Workspaces should avoid preserving:

- caches
- temporary files

Durable assets are the things users expect to keep: repository content, documentation, notes, tests, datasets, reports, and workspace definitions.

Generated assets are replaceable files derived from those durable inputs, such as `compose.yaml`, `.env`, generated onboarding, and generated agent guidance blocks.

Ephemeral assets are disposable runtime state such as containers, terminal sessions, caches, and diagnostics logs.

`Repair Runtime` is for generated or runtime state. Work restoration belongs to Save Points, checkpoints, patches, and backup restore flows.
- rebuildable dependencies
- machine-specific state

> Preserve work. Ignore noise.

Lost work is worse than an oversized repository.

That is why unknown hidden folders are reviewed instead of being silently ignored, and why durable artifacts are preserved even when they make the workspace larger.

## Knowledge Work

The project is not only about software development.

Workspaces are also relevant for:

- analysts
- researchers
- consultants
- technical writers
- engineers

These users produce more than code. They also produce sources, decisions, artifacts, knowledge, and history.

The workspace model is intended to preserve that broader body of work, not just a source tree.

## Packaging Useful Things

The purpose of `opencode stuff` is not to pretend complexity does not exist. Complexity is often real and necessary.

The goal is to package complexity into reusable, inspectable, portable units.

That means keeping the implementation readable enough that contributors can understand how a workspace works, while keeping the day-to-day user experience focused on opening, saving, publishing, and recovering work.

## Open Sorcery

Software should feel approachable and powerful.

Users should be able to accomplish things that once required specialist expertise. Automation, templates, AI, tooling, and reusable workflows can make that experience feel magical, and the goal is not to remove that feeling.

In OpenCode terms, that means a repository can help a user move from a blank machine to a working environment, from a prompt to a specification, or from a question to a validated change faster than older workflows allowed.

That is useful. It is also intentional.

> Software may feel magical. Its operation should never be mysterious.

## There Is No Magic

The apparent magic must always have an engineering explanation behind it.

Every outcome must be explainable.

Every generated artifact must be inspectable.

Every workflow must be reproducible.

Every important asset must be recoverable.

The mechanisms behind the apparent magic are ordinary engineering assets:

- specifications
- source code
- documentation
- configuration
- tests
- version history

Those assets are why the repository is the source of truth, why durable workspaces matter more than any one runtime, and why Save Points, recovery workflows, and documentation-first onboarding are treated as product features instead of secondary details.

> The goal is not to eliminate magic. The goal is to make the magic inspectable.

## Open Sorcery, Not Wizardry

Useful expertise should become transferable assets instead of private rituals.

Knowledge should not be trapped in individuals.

Knowledge should not depend on a specific AI model.

Knowledge should not depend on a vendor platform.

Knowledge should not depend on undocumented tribal knowledge.

Repositories, documentation, specifications, tests, and onboarding materials should make expertise transferable across people, tools, and time.

This is why OpenCode favors repository-owned guidance such as `workspace.yaml`, `AGENTS.md`, onboarding docs, validation scripts, and durable history over instructions that exist only in a chat transcript or in one person's memory.

> Knowledge should live in repositories, specifications, documentation, and tests—not in individuals.

## Spells, Spellbooks, and Evidence

In this philosophy, a spell is a repeatable piece of knowledge that produces a useful outcome.

That could be:

- a specification
- a workflow
- a validation procedure
- a recovery process
- a provisioning script
- a report-generation pipeline
- a reusable onboarding sequence

A spell should be:

- understandable
- inspectable
- repeatable
- teachable

> A spell is reusable knowledge.

A repository is a spellbook.

Not because it contains magic, but because it contains accumulated knowledge that can be reused by others.

Useful spellbook contents include:

- specifications
- source code
- documentation
- tests
- validation rules
- onboarding guides
- architecture decisions
- recovery procedures
- automation

Knowledge becomes more valuable when it is preserved, organized, and transferable.

> A repository is a spellbook.

Every important spell should leave evidence.

That evidence may include:

- specifications
- commits
- tests
- reports
- validation output
- documentation
- Save Points
- change history

If a result matters, there should be evidence explaining how it was produced.

> Every important spell should leave evidence.

From an educational perspective, learning should not stop at invoking a spell.

Students should be encouraged to inspect the spell, understand the spell, modify the spell, and create new spells.

The goal is not consumption of knowledge.

The goal is creation and transfer of knowledge.

> The best spells can be taught.

From a business perspective, organizations should not depend on undocumented spells known only by a few experts. Critical workflows should be documented and reproducible. Institutional knowledge should live in repositories rather than individuals.

> Knowledge becomes durable when it can survive the loss of its creator.

This framing is only useful when it stays grounded in engineering practice. In OpenCode, that means important spells should be preserved in [Philosophy](philosophy.md), [Design Principles](design-principles.md), the [AGENTS.md Guide](agents-guide.md), [Team Onboarding](team-onboarding.md), [Save Points](concepts/save-point.md), and [Repository Workflows](capabilities/repository.md).

## Maps Over Mazes

Complexity is sometimes unavoidable.

Confusion is not.

The purpose of specifications, diagrams, onboarding guides, architecture documents, tests, validation rules, and recovery workflows is to create maps.

A map does not remove complexity.

A map makes complexity navigable.

Examples of useful maps include:

- architecture diagrams
- process flows
- dependency maps
- state diagrams
- specifications
- onboarding guides

> Do not remove complexity. Make it navigable.

> A good diagram is a map.

> A good specification is a map of intent.

> A good test is a map of expectations.

## Knowledge Gravity

Useful knowledge should attract related knowledge.

Repositories should become easier to understand over time.

Solved problems should become reusable assets.

The desired progression looks like this:

```text
Lesson
    ↓
Documentation
    ↓
Validation
    ↓
Automation
    ↓
Capability
```

That means repeated discoveries should be preserved, repeated mistakes should become validation, repeated workflows should become automation, and repeated success should become reusable capability.

> Useful knowledge should attract more knowledge.

> A solved problem should become easier to solve again.

> Repositories should become easier to understand as they grow.

## Portable Understanding

Knowledge should survive:

- team changes
- machine changes
- tool changes
- vendor changes
- AI model changes

A repository should contain enough context to rebuild understanding.

That includes:

- onboarding
- documentation
- specifications
- architecture
- examples
- tests

> Knowledge is portable when understanding is portable.

> The best onboarding is already in the repository.

> A repository should explain itself.

## From Apprentice To Teacher

Learning is not complete when information is consumed.

Learning becomes durable when it can be explained, adapted, and shared.

The progression should look like this:

```text
Apprentice
    ↓
Practitioner
    ↓
Contributor
    ↓
Teacher
```

Understanding is more valuable than memorization.

Contribution demonstrates understanding.

Teaching demonstrates mastery.

Durable knowledge is transferable knowledge.

The goal is not simply producing outputs.

The goal is helping people learn, understand, improve, and eventually teach others.

> The best proof of understanding is explanation.

> The highest form of learning is teaching.

> Knowledge becomes durable when it can be taught.

## Visible Systems

Invisible systems depend on trust.

Visible systems enable understanding.

OpenCode favors visibility through:

- diagrams
- specifications
- tests
- reports
- validation
- timelines
- documentation
- version history

Useful visibility mappings include:

- Intent -> Specification
- Behavior -> Test
- Structure -> Diagram
- Evolution -> Timeline
- Evidence -> Report

Every important capability should have at least one visible representation.

> Invisible knowledge is fragile knowledge.

> What cannot be seen cannot easily be taught.

> Visibility is a prerequisite for understanding.

> If knowledge matters, make it visible.

## Relationship To Existing Philosophy

These ideas extend the same core direction as [Open Sorcery](#open-sorcery), [There Is No Magic](#there-is-no-magic), [Durable Workspaces](#durable-workspaces), repository ownership through [workspace.yaml](workspace-yaml.md), [Save Points](concepts/save-point.md), [Documentation-first onboarding](first-workspace.md), [Specification-driven development](workspace-yaml.md), [Recoverability](architecture/recovery-model.md), and [Agent Transparency](agents-guide.md#agent-transparency).

The purpose of all of these concepts is the same:

- make knowledge visible
- make understanding transferable
- make work durable

## Educational Perspective

AI can make software creation feel magical.

OpenCode should help users inspect how things work.

Students should be able to move from prompt to specification, from specification to code, from code to tests, and from tests to understanding.

The objective is not merely generating software.

The objective is learning, understanding, and ownership.

That is one reason documentation is treated as a first-class asset and why AI is positioned as an accelerator rather than a source of truth.

## Business Perspective

Good tooling should feel magical.

Good engineering should make the magic auditable.

Organizations must be able to understand, maintain, and evolve what they build.

Operational resilience requires transparency.

That is why OpenCode emphasizes recoverability, inspectable generated files, specification-driven development, durable workspaces, and explicit recovery workflows instead of opaque automation that only works when a particular service, vendor, agent, or model behaves as expected.

> Good tooling feels magical. Good engineering makes the magic auditable.

## Principle Links

These ideas are already reflected in the rest of the platform:

- [Repository as source of truth](workspace-yaml.md)
- [Durable workspaces](concepts/workspace.md)
- [Save Points](concepts/save-point.md)
- [Recovery workflows](architecture/recovery-model.md)
- [Documentation-first onboarding](first-workspace.md)
- [Specification-driven development](workspace-yaml.md)
- [Ownership and trust](capabilities/repository.md)

The system should make advanced work more accessible without making its behavior opaque.

> Users should never lose their work because a tool, service, agent, or model failed.

## Preserve Discoveries

Useful discoveries should not remain accidental.

In practice, preservation often follows a progression:

- a lesson becomes documentation
- a repeated lesson becomes automation
- a recurring mistake becomes validation
- a useful checkpoint becomes a Save Point
- a proven workflow becomes reusable workspace structure
- a useful result becomes durable knowledge

This is one of the main ideas behind the repository.

Save Points, validation, reusable workflows, and durable knowledge are all ways of turning one-time effort into assets that can be reused later.

## Why The Satchel?

The project’s visual metaphor is a satchel: a bag of useful things.

It is not a bag of surprises. It is a bag of prepared, reusable, and understandable things that were worth keeping.

That is the relevant part of the metaphor.

`opencode stuff` is interested in carrying the right tools, the right notes, the right automation, and the right conventions so they remain available when needed.

## Closing Thoughts

At its core, `opencode stuff` is an attempt to make useful work easier to preserve, reopen, recover, and reuse.

It assumes that productive environments are built from many small, understandable parts. It also assumes those parts become far more valuable when they are durable, portable, and recoverable instead of temporary and machine-bound.

There is no magic. Only stuff.
