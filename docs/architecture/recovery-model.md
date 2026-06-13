# Recovery Model

The recovery model prefers preservation over convenience.

## Local Recovery

Local recovery comes from:

- Save Points
- checkpoints
- timeline events
- exported patches

Save Points protect committed workspace state. Checkpoints can preserve additional local state, including captured untracked files.

## Off-Machine Backup

Off-machine backup is separate from local recovery.

It requires:

- a configured remote
- intentional Publish

This distinction matters. A workspace can be locally recoverable without being backed up against machine loss.

In user-facing terms, Publish is the explicit backup and synchronization action.

## Restore Strategy

Default restore behavior should create a new workspace copy instead of overwriting the current one.

That reduces the chance of turning one mistake into two.

## Unknown Content Handling

When the system is uncertain about content, it should:

- preserve work
- request review
- avoid silent decisions

This supports recovery by preventing accidental loss of important files that might otherwise be hidden by over-broad ignore rules or silently omitted from Save Points.

That includes nested secrets, nested unknown hidden folders, and dangerous ignore rules that hide durable workspace content. The system should surface these before Save Point creation instead of making a silent decision.

## Advanced Recovery

Git experts should be able to inspect and recover workspace state using standard tools such as:

- `git status`
- `git branch`
- `git log`
- `git diff`
- `git show`
- `git restore`

The workspace abstraction should simplify common operations without hiding the underlying repository.
