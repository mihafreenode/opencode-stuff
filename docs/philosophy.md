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
