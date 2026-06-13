# Save Point

What is it?

A Save Point is the normal user-facing way to record workspace progress.

Why does it exist?

It provides fast, local, offline recovery without forcing users to think in raw Git terms.

How does it relate to the other concepts?

- a Save Point usually maps to a local Git commit
- it protects local workspace progress
- it is separate from Publish, which backs work up to a remote location

Implementation detail:

OpenCode Stuff stores Save Points using Git, but the normal experience stays focused on progress, backup, and recovery rather than low-level source-control commands.

## What gets saved?

Save Points are intended to capture meaningful work.

Included examples:

- source documents
- notes
- reports
- configurations
- workspace definitions

Excluded examples:

- dependency caches
- build outputs
- temporary files

Hidden-folder review policy:

Unknown hidden folders are reviewed before Save Point creation instead of being automatically ignored or automatically included. This protects important workspace content without silently committing machine-local tool state.

Save Point validation inspects changed and untracked content before saving. Nested secrets or unknown hidden folders anywhere in the workspace may require review before the Save Point can be created.
