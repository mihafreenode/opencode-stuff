# Avalonia Shell

The Avalonia shell is the primary Windows desktop shell and the first cross-platform desktop shell for `opencode stuff`.

It exists alongside the legacy Windows WPF application.

Current split:

- `OpenCode.Workspace.Core`: workspace, runtime, generation, diagnostics, catalog, and recovery domain logic
- `OpenCode.Workspace.AppSupport`: small portable shell support such as localization and build-info services
- `OpenCode.Workspace.Manager`: Windows WPF fallback shell and Windows-specific integration
- `OpenCode.Workspace.Avalonia`: cross-platform desktop shell and primary Windows desktop path for Windows, macOS, and Linux

## Goals

- keep the durable workspace model in shared code
- avoid turning the cross-platform shell into a web dashboard
- keep the shared durable-workspace model stable while WPF moves into fallback/maintenance mode
- keep platform-specific terminal behavior behind explicit services

## Level A Scope

Implemented in the Avalonia shell:

- desktop startup and shell composition
- left navigation
- workspaces overview
- create workspace
- open existing repository
- start workspace
- recover workspace
- attach workspace
- diagnostics screen backed by core doctor and validation services
- templates screen
- save points read-only preview
- transcripts/activity preview from workspace timelines
- documentation links
- settings screen with `System`, `Light`, and `Dark` theme modes
- bottom status bar
- view-model-focused tests

Not implemented in this phase:

- full Save Point write flows
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

- Avalonia now uses the shared Windows Terminal handoff and attach integration on Windows
- platform launch remains an explicit service boundary for Linux and macOS later
- WPF remains fallback-only for non-Level-A workflows

## Validation

Portable validation for this phase focuses on:

- Avalonia project build
- view-model tests
- shared core and CLI test suites

Windows-host validation still remains the authoritative build/test path for the primary Windows desktop experience.
