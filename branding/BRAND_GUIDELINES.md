# opencode stuff Brand Guidelines

## Core Idea

`opencode stuff` is practical developer tooling built around a simple philosophy:

`There is no magic. Only stuff.`

The brand should feel credible for serious engineering work first. The Nakor reference is an intellectual easter egg, not a fantasy theme.

## Personality

- practical
- clever
- reliable
- slightly playful
- engineering-focused
- open source
- minimalist

## Symbol

Primary symbol: a satchel holding useful developer artifacts.

Canonical raster assets:

- documentation and branding artwork: `docs/images/opencode-stuff-satchel-transparent.png`
- application icon source: `docs/images/opencode-stuff-satchel-icon.png`
- source header artwork: `docs/images/opencode-stuff-header-brand.png`
- UI-ready Avalonia header asset: `docs/images/opencode-stuff-header-brand-ui.png`

The icon source is canonical for Avalonia, Windows, Linux, macOS, installer, taskbar, dock, and favicon generation.

Do not redesign, redraw, regenerate, or otherwise reinterpret the application icon. Generate derived sizes from the canonical icon source only.

Contained artifacts:

- terminal prompt
- notebook
- gear
- wrench
- small orange

Rules:

- the satchel is always the primary form
- the orange is always a small accent, never the hero
- avoid fantasy props, magic effects, mascots, or character illustration

## Palette

Primary accent:

- Engineering Orange: `#F28C28`

Neutrals:

- Charcoal: `#1F2933`
- Slate: `#52606D`
- Soft Slate: `#7B8794`
- Off-White: `#F7F8FA`
- Line Gray: `#CBD2D9`

Usage:

- orange is reserved for emphasis and recognition
- the product UI should stay mostly neutral
- avoid additional accent colors in core branding

## Logo System

Primary lockup:

- satchel mark on the left
- `opencode stuff` wordmark on the right
- tagline optionally below in hero contexts

Icon-only mark:

- satchel silhouette with minimal internal artifacts
- usable for avatar, favicon, and app icon
- application icon variants must be generated from `opencode-stuff-satchel-icon.png`

## Clear Space

Minimum clear space around the mark or lockup:

- use the width of the satchel buckle as the minimum margin on all sides

## Minimum Sizes

- master logo: `160px` wide minimum
- icon-only mark: `24px` minimum
- favicon and application icons: generate from `opencode-stuff-satchel-icon.png`

## Asset Roles

`opencode-stuff-satchel-transparent.png`

- README
- documentation landing pages
- onboarding guides
- release notes
- splash and about surfaces
- presentations and branding surfaces

`opencode-stuff-satchel-icon.png`

- Avalonia application icon
- Windows executable and installer icon generation
- Linux desktop icon
- macOS application icon generation
- taskbar and dock icon assets
- favicon generation

Do not use the full branding artwork as the primary application icon because small sizes lose detail.

`opencode-stuff-header-brand.png`

- source artwork for the wide lockup header
- preserve the original exported source for future derivation work

`opencode-stuff-header-brand-ui.png`

- Avalonia header banner asset
- derived from `opencode-stuff-header-brand.png` with the ImageMagick trim pipeline
- do not manually redraw it to adjust padding

## Icon Generation Policy

One source image. Many generated sizes. No artistic drift.

Generate resized assets from `opencode-stuff-satchel-icon.png` with ImageMagick:

```bash
magick opencode-stuff-satchel-icon.png -resize 16x16 opencode-stuff-satchel-icon-16.png
magick opencode-stuff-satchel-icon.png -resize 32x32 opencode-stuff-satchel-icon-32.png
magick opencode-stuff-satchel-icon.png -resize 48x48 opencode-stuff-satchel-icon-48.png
magick opencode-stuff-satchel-icon.png -resize 64x64 opencode-stuff-satchel-icon-64.png
magick opencode-stuff-satchel-icon.png -resize 128x128 opencode-stuff-satchel-icon-128.png
magick opencode-stuff-satchel-icon.png -resize 256x256 opencode-stuff-satchel-icon-256.png
magick opencode-stuff-satchel-icon.png -define icon:auto-resize=16,24,32,48,64,128,256 opencode-stuff-satchel-icon.ico
```

Generate the Avalonia header asset from the source artwork with the ImageMagick trim pipeline:

```bash
magick docs/images/opencode-stuff-header-brand.png -fuzz 8% -trim +repage -alpha set -fuzz 12% -transparent "rgb(16,19,19)" -shave 9x5 -trim +repage -bordercolor none -border 4x2 docs/images/opencode-stuff-header-brand-ui.png
```

## Typography

Recommended families:

- UI / docs: `Inter`, `Segoe UI`, `system-ui`, `sans-serif`
- code / terminal: `JetBrains Mono`, `Cascadia Mono`, `Consolas`, `monospace`

Tone:

- plain, direct, technical
- avoid whimsical display fonts

## Light Mode

- off-white or white background
- charcoal wordmark
- charcoal satchel outline and slate structural details
- orange only on the orange artifact and selected highlights

## Dark Mode

- charcoal or near-black background
- off-white wordmark
- off-white satchel outline with muted slate internal details
- orange remains the single bright accent

## Status / Accent Guidance For WPF UI

Orange should be used only for:

- active workspace emphasis
- selected tab or active section
- progress indicators
- primary action buttons
- important status highlights

Keep the rest of the UI neutral and high-contrast.

## Workspace Manager Theme

The satchel metaphor extends into the product:

- workspace = bag of stuff
- repositories, containers, terminals, skills, MCPs, and docs are useful things stored together
- the UI should feel organized, inspectable, and tool-oriented rather than magical or animated

## Terminal Branding

Primary line:

- `opencode stuff`

Supporting line:

- `There is no magic.`
- `Only stuff.`

Terminal artwork should stay mostly typographic and ANSI-friendly.

## Taglines

Primary:

- `There is no magic. Only stuff.`

Alternatives:

- `Useful things for developers.`
- `Tools, not magic.`
- `Just useful stuff.`

## Do Not

- do not draw Nakor directly
- do not use fantasy, wizard, or RPG visual language
- do not turn the orange into a mascot
- do not use rainbow, neon, or heavy gradients
- do not overload small icons with tiny internal detail
