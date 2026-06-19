# Design Principles

This page turns the project philosophy in [Philosophy](philosophy.md) into practical engineering rules.

## Open Sorcery, With Receipts

OpenCode should help users do powerful things quickly, including work that once depended on specialist setup knowledge.

The implementation should still leave a clear trail:

- specifications explain intent
- repositories preserve durable assets
- generated files include ownership headers
- tests verify expected behavior
- documentation explains workflows
- version history records change over time

If a workflow feels magical but cannot be inspected, explained, or recovered, it is incomplete.

## Repository Before Runtime

The repository is the durable source of truth.

Runtimes, containers, sessions, and generated wiring exist to serve the repository-owned work rather than replace it.

Primary durable assets include:

- `workspace.yaml`
- source code
- documentation
- specifications
- tests
- reports and deliverables
- version history

## Generated Artifacts Must Stay Inspectable

Generated artifacts are allowed to reduce setup effort and make advanced workflows approachable.

They still need to remain:

- readable
- attributable to their inputs
- reproducible
- replaceable

That is why generated files should point back to their source inputs and why durable edits belong in canonical manifests and repository content rather than in runtime byproducts.

## AI Assists, But Does Not Own

AI can accelerate understanding, scaffolding, refactoring, and documentation work.

AI must not become the only place where reasoning lives.

Important knowledge should end up in:

- specifications
- source code
- tests
- documentation
- onboarding materials

This keeps the work maintainable when models change, vendors change, or a prior conversation is unavailable.

## Recovery Is A Core Design Constraint

Good engineering does not only optimize the happy path.

OpenCode favors Save Points, backup-aware workflows, restore-as-copy behavior, and explicit recovery guidance because durable work matters more than transient convenience.

See also:

- [Save Points](concepts/save-point.md)
- [Recovery Model](architecture/recovery-model.md)

## Documentation Is Product Surface

Documentation is part of the working system, not an afterthought.

Repository-owned docs should help a user move from onboarding to productive work without depending on tribal knowledge or a specific assistant session.

That includes:

- onboarding guidance
- examples
- troubleshooting
- validation instructions
- capability discovery

## Maps, Visibility, and Portable Understanding

When complexity cannot be removed, the system should still provide maps.

In practice, that means important capabilities should expose visible representations such as:

- specifications for intent
- tests for behavior
- diagrams for structure
- reports for evidence
- timelines and history for evolution

Repository-owned materials should help understanding survive team changes, machine changes, tool changes, vendor changes, and AI model changes.

## Knowledge Transfer As A Design Requirement

Important workflows should become easier to understand and repeat over time.

The preferred progression is:

1. preserve the lesson in documentation
2. turn recurring mistakes into validation
3. turn recurring workflows into automation
4. turn proven success into reusable capability

This keeps knowledge visible, teachable, and less dependent on individuals or one-off assistant sessions.

## Transferable Expertise

OpenCode should reduce dependence on individual operators, one-off setup rituals, and vendor-specific memory.

The preferred path is:

1. capture intent in a specification
2. implement the change in code or configuration
3. validate it with tests or checks
4. explain it in documentation
5. preserve it in version history

That is the practical form of Open Sorcery, Not Wizardry.

See also:

- [Philosophy](philosophy.md)
- [AGENTS.md Guide](agents-guide.md)
- [Team Onboarding](team-onboarding.md)
