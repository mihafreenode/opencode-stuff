# Content Classification

## Tracked

Tracked content is part of the durable workspace.

Examples:

- sources
- knowledge
- work
- artifacts
- docs
- runtimes
- `.opencode/`
- `.github/`
- `workspace.yaml`
- `history/timeline.yaml`

## Ignored

Ignored content is rebuildable, temporary, or machine-local.

Examples:

- caches
- previews
- dependency folders
- build outputs
- temporary conversion output

## Needs Review

Needs Review is used for unknown or ambiguous content.

Examples:

- unknown hidden folders
- unusual generated content
- content that might contain secrets

Unknown hidden folders are reviewed because a blanket dot-folder ignore rule could hide important workspace assets, while auto-tracking could silently include machine-local state.

Secrets are blocked because recovery is not worth exposing credentials.

Artifacts are treated differently from caches because reports, deliverables, and exported results are often the work itself, not disposable noise.

Changed and untracked content is validated before Save Point creation. That review can surface nested secrets, nested unknown hidden folders, and dangerous ignore rules that would otherwise hide durable workspace content.
