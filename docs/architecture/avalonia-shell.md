# Avalonia Shell

The Avalonia shell is the first cross-platform desktop shell for `opencode stuff`.

It exists alongside the existing Windows WPF application.

Current split:

- `OpenCode.Workspace.Core`: workspace, runtime, generation, diagnostics, catalog, and recovery domain logic
- `OpenCode.Workspace.AppSupport`: small portable shell support such as localization and build-info services
- `OpenCode.Workspace.Manager`: Windows WPF shell and Windows-specific integration
- `OpenCode.Workspace.Avalonia`: cross-platform desktop shell preview for Windows, macOS, and Linux

## Goals

- keep the durable workspace model in shared code
- avoid turning the cross-platform shell into a web dashboard
- preserve the current WPF product while the Avalonia shell grows
- keep platform-specific terminal behavior behind explicit services

## Phase 1 Scope

Implemented in the Avalonia shell:

- desktop startup and shell composition
- left navigation
- workspaces overview
- diagnostics screen backed by core doctor and validation services
- templates screen
- save points read-only preview
- transcripts/activity preview from workspace timelines
- documentation links
- settings screen with `System`, `Light`, and `Dark` theme modes
- bottom status bar
- view-model-focused tests

Not implemented in this phase:

- full attach or Windows Terminal parity
- full Save Point write flows
- full recovery UI parity
- remote SSH-backed targets
- packaging work

Unavailable actions remain visible with explicit reason text instead of being hidden.

## Shared Service Boundary

`OpenCode.Workspace.AppSupport` is intentionally small.

It currently contains only framework-neutral shell support:

- `PoLocalizationService`
- `AppBuildInfoService`
- `AppBuildInfo`

This keeps `Core` focused on workspace/runtime/domain behavior while avoiding duplicated shell-neutral code.

## Avalonia Shell Composition

The shell uses page-oriented MVVM.

Primary pages:

- Workspaces
- Remote Targets
- Templates
- Save Points
- Transcripts
- Diagnostics
- Documentation
- Settings

The right-side panel is contextual and shows selected workspace details, diagnostic summary, or screen-specific metadata.

## Platform-Specific Behavior

Attach remains intentionally platform-aware.

- WPF keeps the Windows Terminal handoff and attach integration
- Avalonia preview does not pretend to provide full terminal parity
- preview actions explain when the user should use WPF or CLI instead

## Validation

Portable validation for this phase focuses on:

- Avalonia project build
- view-model tests
- shared core and CLI test suites

Windows-host validation still remains the authoritative build/test path for the WPF shell.
