# Vision

OpenCode Stuff is evolving from a runtime launcher into a durable workspace system.

The workspace is the durable asset.

It should preserve:

- sources
- knowledge
- artifacts
- history
- recovery information

The runtime is replaceable.

It provides tools, automation, and AI capabilities, but it should not own the long-term state of the work.

The session is temporary.

It is a running interaction with a runtime attached to a workspace.

Git is used as the default persistence engine so workspaces gain local history, Save Points, Working Copies, Publish, Backup, and Recovery without inventing a proprietary source-control system.

The platform should make advanced work feel approachable without making it mysterious. Good tooling can feel magical, but organizations still need repositories, specifications, documentation, tests, and recovery workflows that make the result understandable, maintainable, and transferable.
