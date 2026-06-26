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
- The legacy WPF desktop shell was removed.
- Release packaging now targets the Avalonia desktop shell for Windows, Linux, and macOS.
- Existing app-data remains under `OpenCode.Workspace.Manager` for compatibility until a dedicated migration is implemented.

Validation summary:

- Core: `370/370`
- Avalonia: `55/55`
- Windows attach: `7 passed, 1 skipped`
- Windows clean restore/build: succeeded

Migration note:

- Existing users can launch Avalonia as the default desktop application on Windows.
- Use Avalonia as the Windows desktop application.
