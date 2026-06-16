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

## Recommended Reading Order

1. `README.md`
2. `docs/oracle-plsql-demo.md`
3. `docs/oracle-apex-demo.md` when moving beyond PL/SQL
4. `docs/oracle-apexlang-demo.md` when the team wants source-controlled APEX workflows
5. `docs/oracle-lifecycle-workflows.md`
6. `docs/agents-guide.md`

## First-Time Oracle Developer Flow

Use Oracle PL/SQL Demo as the first entry point.

After the repository is discovered:

1. review the loaded `workspace.yaml`
2. review local guidance in `AGENTS.md` when present
3. provision the environment
4. open the workspace
5. start with the generated tutorial or verification scripts
6. continue into Oracle APEX and then Oracle APEXlang only when the PL/SQL foundation is understood
