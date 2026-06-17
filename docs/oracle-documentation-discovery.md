# Oracle Documentation Discovery

This repository uses lightweight navigation indexes to make Oracle workspaces easier for humans and AI agents without redistributing Oracle documentation.

## Why These Indexes Exist

- Oracle documentation is broad and split across product areas
- onboarding improves when local guidance points directly to the right official source
- AI agents work better when package families, version archives, and topic maps are explicit

## Why Oracle Documentation Is Not Mirrored

- official Oracle documentation remains authoritative
- mirrored copies drift over time
- the repository should stay lightweight and licensing-safe
- durable workspaces benefit more from stable metadata than from stale offline manuals

## How Agents Should Use The Indexes

1. Start with `docs/reference/oracle-knowledge-map.yaml`.
2. Choose the relevant path such as APEX specification, APEX runtime API, ORDS deployment, or database SQL.
3. Open the repository-owned index for that topic.
4. Follow the official Oracle links from that index.
5. Prefer version-matched documentation when the runtime version is known.

## Version-Specific Documentation

Use `docs/reference/oracle-apex-version-archives.md` when the local runtime is not on the newest release.

Version-matched documentation matters for:

- APEX Builder features
- PL/SQL package availability
- release-note behavior changes
- installation and upgrade steps

## Package Discovery

The package map and package catalog accelerate Oracle development by answering two fast questions:

- which package family is likely relevant?
- where is the official Oracle package page?

Start with:

- `docs/reference/oracle-apex-api-map.yaml`
- `docs/reference/oracle-apex-api-packages.md`

Then open the official Oracle API page for the package you need.
