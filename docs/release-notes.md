# Release Notes

## v0.2.0-avalonia Candidate

### Architecture

- the WPF desktop shell was removed
- Avalonia is now the only desktop shell
- shared host capability detection and platform-specific projects are in place for Windows, Linux, and macOS

### Desktop Workflows

- Level A workspace workflows are complete in Avalonia
- Level B durability workflows are complete:
  - Save Point
  - Timeline
  - Backup
  - Publish
  - Workspace Removal
- workspace discovery no longer blocks on slow session inspection
- existing workspace imports preserve repository-owned `workspace.yaml` / `workspace.yml`
- recovery preserves user-owned files and canonical config
- attach uses the shared Windows Terminal launcher and runtime diagnostics path

### Infrastructure

- cross-platform CI now builds and publishes Avalonia desktop artifacts for Windows, Linux, and macOS
- release packaging publishes:
  - `opencode-stuff-win-x64.zip`
  - `opencode-stuff-linux-x64.tar.gz`
  - `opencode-stuff-macos-arm64.tar.gz`
- Windows-only platform tests now skip cleanly on non-Windows hosts instead of failing

### Compatibility

- existing app-data remains under `OpenCode.Workspace.Manager` intentionally to preserve user state until a dedicated migration is implemented
- existing workspace index compatibility is preserved

### Validation Snapshot

- Core: `387/387`
- Avalonia: `97/97`
- Platform.Windows: `19 passed, 7 skipped`
- Platform.Linux: `2/2`
- Platform.MacOS: `2/2`
- CLI: `20/20`
- cross-platform publish verified for `win-x64`, `linux-x64`, and `osx-arm64`

### Known Limitations

- manual packaged GUI release checklist execution is still required before tagging the release
- full packaged desktop workflow automation is not yet implemented
- some deferred convenience UX remains intentionally out of scope for this release candidate
