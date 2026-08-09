# Workspaces

A workspace is the durable body of work: repository files, `workspace.yaml`, documentation, scripts, reports, and recovery history. Its Ubuntu runtime and containers are replaceable.

## Create A Workspace

`Create Workspace` starts from a packaged template. Choose a name, location, and template. The created `workspace.yaml` becomes the lasting configuration; the template is only the starting point. Additional catalog selections are edited in canonical workspace configuration rather than selected in the current creation dialog.

Creation can initialize Git and local recovery state. Review the resulting folder before adding credentials or private data.

## Open An Existing Repository

`Open Existing Repository` registers an existing local Git checkout. Discovery checks these paths in order:

- `workspace.yaml`
- `workspace.yml`
- `.opencode/profile.yaml`
- `.opencode/profile.yml`

The discovered file remains in place and is not silently migrated. Invalid repository-owned configuration is reported rather than replaced with defaults. Import does not discard local Git changes or silently switch branches.

## Statuses

The UI derives a workspace status from configuration, applied state, runtime inspection, and recent operation results.

| Status meaning | What to do |
| --- | --- |
| Running / working | The runtime passed readiness checks; open or attach a session separately. |
| Stopped | Use `Open Workspace` to start and validate it. |
| Update available / required | Apply the requested update or preparation before relying on the runtime. |
| Error / recovery needed | Read the operation transcript and diagnostics, then use the recommended repair action. |

Status text can vary with localization and detailed recovery states. The colored indicator and recommendation are the authoritative current presentation.

## Lifecycle

- `Open Workspace` generates or refreshes managed artifacts when needed, provisions or starts the runtime, and validates readiness.
- `Stop` stops managed runtime resources without deleting durable workspace files.
- `Prepare Workspace` reapplies generated setup and provisioning.
- Recovery recommendations repair generated/runtime wiring; they do not restore deleted durable files.
- The current `Rebuild Runtime` action removes managed runtime resources. Run `Open Workspace` afterward to provision and start them again. UI wording that implies reset and reprovision are one operation is a known discrepancy.
- Interactive session creation and terminal attachment happen after readiness and are separate lifecycle actions.

Do not hand-edit `compose.yaml`, `.env`, or `mounts/config/*` as a lasting fix. Change `workspace.yaml` or a catalog manifest and regenerate.

## Safe Working Copies

A Safe Working Copy is a local Git branch used to keep normal work away from protected or mainline branches. New names follow `users/{user}/{title}-{yyyyMMdd-HHmm}` after unsafe characters are sanitized.

Use a Working Copy when the current branch is `main`, `master`, `staging`, `production`, `release/*`, or otherwise protected. The app does not auto-publish to protected branches and does not replace code review, merging, or pull requests.

## Known Boundaries

- Windows is the primary desktop platform.
- Current `linux-x64` and `osx-arm64` evaluation packages are self-contained.
- Desktop interactive terminal attachment is Windows-only.
- `Open Workspace` does not automatically open or attach Windows Terminal.
- Existing-repository import operates on a local checkout; it is not a hosted clone-and-merge service.
- Working Copy creation does not merge completed work back to a protected branch.
- Runtime repair and rebuild do not restore user files that were deleted outside a recovery or backup workflow.
- `Remove from list only` leaves files and Docker resources untouched.
- `Remove Docker resources` leaves workspace files untouched.
- Local workspace-file deletion is disabled in the current desktop workflow. Back up first, remove registration/resources explicitly, then delete files manually only when that is intentional.

See [Backup And Publish](backup-and-publish.md) before destructive removal.
