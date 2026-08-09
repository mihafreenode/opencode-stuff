# Backup And Publish

These actions protect different risks. None is a substitute for understanding what it captures.

## Action Matrix

| Action | Captures | Location | Typical use | Important limit |
| --- | --- | --- | --- | --- |
| Save Point | Meaningful tracked and approved untracked workspace content, backed by a Git commit | Local repository | Milestones and fast local recovery | Does not provide off-machine protection |
| Checkpoint | Manifest, branch and commit identity, tracked patch, approved untracked files, artifact index, and runtime metadata when available | Local `history/checkpoints/` | Stronger recovery around unfinished work | Only claims files that were successfully captured |
| Backup | Portable archive plus `backup-manifest.yaml` classification | User-selected archive path | Copy to another disk or backup system | Archive safety depends on where you store it |
| Publish | Current Working Copy after remote fetch and safety assessment | Configured Git remote | Explicit off-machine collaboration or backup | Stops on conflict, uncertainty, or protected-branch risk |
| Restore / recovery | Selected Save Point, Checkpoint, patch, or backup material | Existing or safe recovery location | Recover lost or damaged work | Review target and overwrite implications first |
| Remove from list only | App registration only | App-data index | Hide an entry without cleanup | Files and Docker resources remain |
| Remove Docker resources | Managed containers and runtime resources | Docker | Reclaim/recreate runtime resources | Workspace files remain |
| Delete workspace files | Not implemented by the current desktop workflow | Local machine | Manual cleanup after backup | The UI disables this choice; remove registration/resources separately, then delete files manually if intentional |

## Save Point Safety

Before creating a Save Point, the app inspects changed and untracked content, including nested paths.

- `Tracked`: durable source, documents, reports, presentations, generated deliverables, configuration, and other intentional work.
- `Ignored`: caches, previews, dependency folders, temporary files, and rebuildable outputs.
- `Needs Review`: unknown hidden content, suspicious secret candidates, and ambiguous generated material.

Unknown hidden folders are neither silently ignored nor silently committed. Dangerous ignore rules that hide durable content also require review.

Never record credentials, API keys, private keys, token files, password-bearing configuration, or other secrets. If validation blocks a Save Point, remove the secret from workspace history and use an appropriate secret store or machine-local configuration.

## Publish Safety

Publish is explicit and never automatic. The app fetches remote state before publishing. If the remote changed, it may perform only a clean, conflict-free safe update and then stops for confirmation. Any conflict or uncertainty stops the flow with local work preserved.

The app does not auto-resolve conflicts and does not force-push by default. Protected and mainline branches are outside the normal publish workflow; create a Safe Working Copy instead.

## Restore Before Removal

Before manual file deletion:

1. Create a Save Point if the working tree can be safely recorded.
2. Create a Checkpoint if unfinished local changes need extra capture.
3. Export a Backup and inspect its `backup-manifest.yaml`.
4. Store a copy outside the workspace folder.
5. Remove registration and managed resources at the intended level.
6. Delete the local folder manually only after confirming the backup and exact path.

If file deletion fails, the safe behavior is to keep registration and report actionable paths. Do not assume a partial deletion completed successfully.
