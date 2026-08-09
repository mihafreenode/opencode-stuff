# Oracle Team Onboarding

Oracle workspaces are designed to be shared as repositories rather than recreated as machine-specific setups. The repository carries source code, `workspace.yaml`, onboarding notes, `AGENTS.md` guidance, and reviewable automation; local credentials, wallets, Oracle media, and generated runtime state stay local.

For the product capability and maturity map, start with [Oracle and Oracle APEX Integration](../integrations/oracle-apex.md).

## Primary Flow

```text
Clone Repository
    -> Open Existing Repository
    -> Review Discovered Workspace
    -> Prepare Workspace
    -> Open Workspace
    -> Follow Repository Tutorial
    -> Create Safe Working Copy and Save Points
```

On Windows, use OpenCode Workspace Manager as the onboarding and workspace-management shell. Prepare the workspace when the app indicates that provisioning or generated content is required, then use `Open Workspace` to open its managed terminal session as the `opencode` user. This is the normal path; users should not need to enter a root shell, run `su`, or manually reconstruct an OpenCode session.

Inside the managed terminal, start by asking:

- What capabilities are available?
- What Oracle onboarding docs exist?
- What tools and runtime services are available?
- What generated files are implementation details rather than source?

## Session Diagnostics

Normal onboarding does not require session administration. When debugging session state, run these commands inside the workspace as the `opencode` user:

```bash
opencode session list
opencode session delete <session-id>
```

Opening the workspace uses the managed LocalHost attach flow and restores the provider conversation when possible. Do not substitute older manual provider-session syntax for the normal attach workflow.

## Docker Exec Fallback

Docker Desktop Exec is an advanced fallback for diagnosis when the managed terminal cannot attach. It may open a root shell and therefore does not represent the normal permissions or session environment.

To inspect sessions from the host, use the exact workspace container name and preserve the attach user's home directory:

```powershell
docker exec --user opencode -w /workspace <workspace-container> env HOME=/home/opencode opencode session list
docker exec --user opencode -w /workspace <workspace-container> env HOME=/home/opencode opencode session delete <session-id>
```

Run list and delete as separate commands. Prefer returning to `Open Workspace` after diagnosis rather than continuing development in Docker Exec.

Historical Docker Desktop Exec screenshots may exist in repository test artifacts, but they are not part of the packaged documentation or the recommended current entry path.

## Sharing A Workspace

Before sharing or publishing an Oracle workspace:

1. Keep `workspace.yaml`, source, migration scripts, APEX exports, tests, documentation, and `AGENTS.md` guidance in the repository.
2. Keep `.local/`, credentials, SQLcl profiles, wallets, TNS secrets, downloaded Oracle media, database volumes, and generated runtime state out of commits.
3. Explain required user-supplied Oracle downloads and the exact version expected.
4. Export database and APEX changes into reviewable source before relying on a shared environment as the only copy.
5. Create a Safe Working Copy and Save Points; Publish remains explicit and must not target protected or mainline work accidentally.
6. Verify a teammate can clone, discover, prepare, and open the workspace from repository-owned instructions.

For a practical Git explanation, see [Practical Git for Oracle Developers](practical-git-for-oracle-developers.md).

## Recommended Reading

1. [Oracle and Oracle APEX Integration](../integrations/oracle-apex.md)
2. [Practical Git for Oracle Developers](practical-git-for-oracle-developers.md)
3. [From Oracle Demo to Oracle Onboarding](../articles/oracle-onboarding.md)
4. [Oracle PL/SQL Demo](../oracle-plsql-demo.md)
5. [Oracle APEX Demo](../oracle-apex-demo.md)
6. [Oracle APEXlang Demo](../oracle-apexlang-demo.md)
7. [Oracle Lifecycle Workflows](../oracle-lifecycle-workflows.md)
8. [Oracle Tools](../oracle-tools/README.md)

## First-Time Oracle Developer Flow

Start with the Oracle PL/SQL Demo. Review the discovered `workspace.yaml` and local `AGENTS.md`, prepare and open the workspace, then use the generated tutorial and verification scripts. Continue to APEX and APEXlang only after the database foundation and the distinction between shipped schema assets and tutorial application placeholders are clear.
