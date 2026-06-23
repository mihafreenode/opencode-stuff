# Release Notes

## Avalonia Primary Windows Path

- Avalonia now supports the full Level A workflow set on Windows:
  - Create Workspace
  - Open Existing Repository
  - Start Workspace
  - Recover Workspace
  - Attach Workspace
- Workspace discovery no longer blocks on slow session inspection.
- Existing workspace imports preserve repository-owned `workspace.yaml` / `workspace.yml`.
- Recovery preserves user-owned files and canonical config.
- Attach uses the shared Windows Terminal launcher and runtime diagnostics path.
- WPF is now fallback/maintenance only.

Validation summary:

- Core: `370/370`
- Avalonia: `55/55`
- Windows attach: `7 passed, 1 skipped`
- Windows clean restore/build: succeeded

Migration note:

- Existing users can launch Avalonia as the default desktop application on Windows.
- Use WPF only if you still require an advanced workflow that has not yet been migrated.
