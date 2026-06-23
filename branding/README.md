# opencode stuff Branding

This folder contains the source branding package for `opencode stuff`.

Canonical exported raster assets live in `docs/images/`:

- `opencode-stuff-satchel-transparent.png`: documentation and branding artwork for medium and large surfaces
- `opencode-stuff-satchel-icon.png`: canonical application icon source for all generated icon sizes and package assets
- `opencode-stuff-header-brand.png`: source header artwork
- `opencode-stuff-header-brand-ui.png`: UI-ready Avalonia header asset derived from the source with ImageMagick trim

Contents:

- `logo/`: master logo and icon-only marks
- `icons/`: organization avatar, app icon source, favicon variants
- `docs/`: README header, docs header, social preview
- `terminal/`: startup banners
- `BRAND_GUIDELINES.md`: usage rules and design system

Application icon variants must be generated from `docs/images/opencode-stuff-satchel-icon.png`.

The Avalonia header banner must use `docs/images/opencode-stuff-header-brand-ui.png`, generated from `docs/images/opencode-stuff-header-brand.png` with the ImageMagick trim pipeline rather than manual redraw.

Do not redraw, reinterpret, manually simplify, vectorize, or create AI-generated icon variants for packaging work.
