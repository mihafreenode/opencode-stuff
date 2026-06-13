# Checkpoint

A checkpoint complements Save Points.

Save Points capture committed workspace state in Git. Checkpoints exist to preserve additional local recovery information when needed, especially for work that may not yet be part of a Save Point.

A checkpoint can include:

- workspace manifest snapshot
- current working copy branch
- current commit SHA
- patch of tracked changes
- captured untracked files
- artifact index snapshot
- runtime metadata

Checkpoints are intentionally conservative. If the system cannot prove that local files were captured, it should not report them as protected.
