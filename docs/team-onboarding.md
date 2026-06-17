# Team Onboarding

Oracle workspaces in this repository are intended to be cloned, discovered, reviewed, and provisioned without manual recreation of Oracle-specific settings.

Expected flow:

```text
Clone Repository
    ↓
Open Existing Repository
    ↓
Workspace Discovered
    ↓
Review Configuration
    ↓
Provision Environment
    ↓
Read Docs
    ↓
Run Tutorial
    ↓
Start Learning
```

## Connecting to a Workspace Session

Most onboarding exercises assume the user is connected to an OpenCode session rather than a root shell.

Typical workflow:

```bash
su opencode
cd /workspace
opencode -s resume
```

Useful commands:

```bash
opencode sessions
opencode -s <session-id>
```

Suggested first questions:

- What capabilities are available?
- What onboarding docs exist?
- What tools are installed?

## Using Docker Desktop Exec

Docker Desktop Exec is a valid way to access a workspace.

Users may be attached to:

- root shell
- opencode user shell
- OpenCode session

OpenCode sessions provide the best onboarding experience.

## Recommended Reading Order

1. `README.md`
2. `docs/capabilities/README.md`
3. `docs/capabilities/oracle.md`
4. `docs/oracle-plsql-demo.md`
5. `docs/oracle-apex-demo.md` when moving beyond PL/SQL
6. `docs/oracle-apexlang-demo.md` when the team wants source-controlled APEX workflows
7. `docs/oracle-lifecycle-workflows.md`
8. `docs/agents-guide.md`

## First-Time Oracle Developer Flow

Use Oracle PL/SQL Demo as the first entry point.

After the repository is discovered:

1. review the loaded `workspace.yaml`
2. review local guidance in `AGENTS.md` when present
3. provision the environment
4. open the workspace
5. start with the generated tutorial or verification scripts
6. continue into Oracle APEX and then Oracle APEXlang only when the PL/SQL foundation is understood

## Capability Discovery

Start here before searching the repository:

- `docs/capabilities/README.md`
- `docs/capabilities/oracle.md`
- `docs/oracle-plsql-demo.md`
- `docs/oracle-apex-demo.md`
- `docs/oracle-apexlang-demo.md`
- `docs/oracle-tools/README.md`
- `docs/oracle-samples.md`
- `docs/troubleshooting/workspace-sessions.md`
