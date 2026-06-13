# Workspace

What is it?

A workspace is the durable body of work.

Why does it exist?

It exists so sources, knowledge, artifacts, history, and recovery information can survive runtime changes, machine changes, and tool upgrades.

How does it relate to the other concepts?

- the workspace is the durable asset
- the runtime is the disposable tool environment
- the session is the temporary live execution attached to the workspace

## Workspace Content Classification

Workspace content is classified into three categories:

### Tracked

Part of the durable workspace.

### Ignored

Rebuildable or temporary content.

### Needs Review

Unknown or ambiguous content.

Unknown hidden folders are reviewed instead of automatically ignored because many dot-prefixed paths are important project assets, while others are only machine-local tool state. The safe default is to preserve work and ask for review when the classification is unclear.
