# Third-Party Notices

OpenCode Workspace bundles third-party runtime and NuGet dependencies.

- The authoritative package and license metadata for source builds is declared in `Directory.Packages.props` and project files under `src/` and `tests/`.
- Release archives also include each published application's `.deps.json` manifest so the exact packaged dependency graph remains inspectable.
- The local browser terminal embeds xterm.js through XtermBlazor. Both projects are distributed under the MIT License; their source and license metadata are available from `https://github.com/xtermjs/xterm.js` and `https://github.com/BattlefieldDuck/XtermBlazor`.
- Oracle software used by optional Oracle/APEX workspace scenarios is subject to Oracle's own license terms and is not redistributed by default in the local release archive.
