# Oracle APEXlang Skill

Purpose:
Help humans and agents navigate Oracle APEXlang before editing or reviewing application specification files.

When to use:
- working with `apex/application.apx`
- reviewing source-controlled Oracle APEX application definitions
- mapping Builder concepts to APEXlang sections

Recommended documentation indexes:
- `docs/reference/oracle-apexlang-index.md`
- `docs/reference/oracle-apexlang-navigation.md`
- `docs/reference/oracle-apex-index.md` for Builder concepts outside the specification format

Common workflows:
- identify the affected APEXlang section first
- confirm the official section name and structure in Oracle docs
- compare the requested change with existing exported application patterns

Documentation discovery workflow:
- start at `docs/reference/oracle-knowledge-map.yaml`
- open `docs/reference/oracle-apexlang-index.md`
- use `docs/reference/oracle-apexlang-navigation.md` for fast section discovery

Package lookup workflow:
- if the change also touches runtime PL/SQL packages, switch to `docs/reference/oracle-apex-api-reference.md`
- use `docs/reference/oracle-apex-api-map.yaml` to classify package families before editing helper code

Version compatibility guidance:
- use version-matched APEXlang and APEX docs whenever the runtime version is known
- check `docs/reference/oracle-apex-version-archives.md` if exported structures differ from the latest examples

Troubleshooting guidance:
- if a concept is hard to place, map it back to Builder terminology with `docs/reference/oracle-apex-index.md`
- use release notes when structure or naming appears version-specific

Official documentation:
- https://docs.oracle.com/en/database/oracle/apex/26.1/apxln/
