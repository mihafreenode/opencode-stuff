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

- Core: `390/390`
- Avalonia: `104/104`
- Platform.Windows: `26/26`
- Platform.Linux: `2/2`
- Platform.MacOS: `2/2`
- CLI: `20/20`
- clean Windows-host `Release` build verified on commit `4d8b9cfd365f5feb6377a55010a7c8d20044ab83`
- fresh extracted Windows package verified from `opencode-stuff-win-x64.zip`
- packaged smoke verified:
  - launch
  - existing workspace list load
  - Diagnostics page
  - required `Diagnostic_*` rows
  - `Create Workspace` dialog

### Known Limitations

- full packaged desktop workflow automation is not yet implemented; release sign-off still depends on manual packaged GUI checks
- some deferred convenience UX remains intentionally out of scope for this release candidate
