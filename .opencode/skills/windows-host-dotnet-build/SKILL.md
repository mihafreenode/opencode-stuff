# Windows Host .NET Build

Use this skill when a .NET project depends on Windows-only SDK/runtime support such as WPF, WinForms, or a `-windows` target framework while the repo is being edited from WSL.

## Goal

Build or create the project with the Windows-hosted `dotnet` installation instead of the Linux one.

## When this applies

- The project targets `net*-windows`
- The desktop shell no longer depends on WPF.
- The project sets `<UseWindowsForms>true</UseWindowsForms>`
- The user asks for a WPF or WinForms app
- A Linux `dotnet` command would fail because desktop targets or `Microsoft.WindowsDesktop.App` are unavailable

## Workflow

1. Inspect the repo for an existing `*.slnx`, `*.sln`, or `*.csproj`.
2. If no desktop project exists, create it with Windows `dotnet`, not Linux `dotnet`.
3. If working from WSL, convert the repo path with `wslpath -w`.
4. Run restore and build on the Windows host with `cmd.exe /c` or PowerShell.
5. If repeated builds are likely, add a small wrapper script in the repo root.
6. Verify the build output exists under `bin/Debug/<tfm>/` or the requested configuration.

## Command patterns

Create a WPF app from WSL:

```bash
workspace_dir="$(pwd)"
windows_dir="$(wslpath -w "$workspace_dir")"
cmd.exe /c "cd /d \"$windows_dir\" && dotnet new wpf -n MyApp -f net10.0"
```

Build a solution from WSL:

```bash
workspace_dir="$(pwd)"
windows_dir="$(wslpath -w "$workspace_dir")"
cmd.exe /c "dotnet build \"$windows_dir\\MySolution.slnx\""
```

Build a project from WSL:

```bash
workspace_dir="$(pwd)"
windows_dir="$(wslpath -w "$workspace_dir")"
cmd.exe /c "dotnet build \"$windows_dir\\MyApp\\MyApp.csproj\""
```

## Notes

- `dotnet new wpf -f net10.0` generates a project that targets `net10.0-windows`.
- If the user does not want a solution file, building the `*.csproj` directly is fine.
- Keep changes minimal; do not add extra infrastructure unless it helps future Windows-hosted builds.
