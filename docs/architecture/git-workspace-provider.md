# Git Workspace Provider

Git is the default workspace provider.

## Why

Git already provides:

- local durability
- history
- Working Copies
- synchronization
- recovery

Using Git avoids inventing a proprietary persistence mechanism while preserving compatibility with standard tools.

## User Experience

Normal users work with:

- Save Point
- Working Copy
- Publish
- Backup
- Restore

Advanced users can still inspect:

- branch state
- tracking remote
- ahead/behind counts
- commit SHA
- raw diffs and patches

## Provider Responsibilities

The Git workspace provider is responsible for:

- initializing Git for plain local folders
- creating the initial Save Point
- maintaining a safe Working Copy as a local branch
- reporting exact Git state for safety evaluation
- handling Publish intentionally, never automatically
- exporting patches for manual recovery or review

## Working Copy Convention

A Safe Working Copy maps internally to a local Git branch.

Naming convention:

```text
users/{user}/{title}-{yyyyMMdd-HHmm}
```

Examples:

- `users/miha/workspace-safety-20260613-1542`
- `users/ana/customer-analysis-20260613-1605`

Normal UI should call this a Working Copy, not a branch.

Protected or mainline branches such as `main`, `master`, `staging`, `production`, and `release/*` are advanced Git operations outside the normal workspace flow.

## Safety Rules

- local work can be safe even without a remote backup
- Off-Machine Backup requires a configured remote and intentional Publish
- if the remote changed before Publish, publishing is blocked and review is required
- if the system cannot prove that files are protected, it should report them as not protected

## Safe Publish Behavior

Publish is always explicit.

Before Publish:

1. fetch remote state
2. compare local Working Copy with the tracked remote branch
3. stop if anything is unclear or unsafe

If the remote has not changed, the Working Copy can be published normally.

If the remote changed, the provider may attempt a safe update only when the Working Copy is clean and the operation completes without conflicts.

If that safe update succeeds, the app must stop and ask for confirmation before publishing.

If a conflict or uncertainty appears, the app must stop immediately, keep local work safe, and set the workspace to `Needs Review`.

Needs Review message:

> Your local work is safe. The remote workspace changed and needs review before publishing.

## Advanced Recovery

Advanced Git View is diagnostic-only.

It can expose:

- branch name
- remote name
- remote branch
- ahead/behind counts
- latest commit SHA
- status summary
- conflicting files if known
- patch export path or availability

This keeps normal UI simple while preserving a standard Git recovery path for advanced users.

## Workspace Ignore Policy

The provider classifies content into:

- `Tracked`
- `Ignored`
- `Needs Review`

This policy exists to preserve durable work without silently committing secrets, caches, or machine-local state.

The provider performs:

- secret detection before Save Point creation
- hidden-folder review for unknown dot-prefixed directories
- Save Point validation before Git commit
- artifact-aware handling so durable outputs are preserved and disposable caches are ignored

Save Point validation inspects changed and untracked content first. When Git status is unavailable, the provider falls back to recursive scanning while skipping known disposable cache locations.

Unknown hidden content anywhere in the workspace may require review.

Dangerous ignore rules that hide durable workspace content also trigger review.

Blanket dot-folder ignore rules are forbidden.

Bad example:

```gitignore
.*
```

Reason:

It may hide important workspace content such as `.opencode/`, `.github/`, or project-level configuration that should be preserved.

Shared Git mutations are canonical LocalHost operations; presentations must not maintain an independent Git state model. See [Architecture Overview](overview.md), [LocalHost](local-host.md), and [Recovery Model](recovery-model.md).
