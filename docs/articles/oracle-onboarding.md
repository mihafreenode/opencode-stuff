# From Oracle Demo to Oracle Onboarding

> Historical context: this article explains the repository-first onboarding rationale. For current product behavior and capability status, use [Oracle and Oracle APEX Integration](../integrations/oracle-apex.md) and [Oracle Team Onboarding](../oracle/team-onboarding.md).

Templates create workspaces, but repositories teach teams how to use them.

This Oracle workspace family treats the repository as executable onboarding knowledge:

- `workspace.yaml` preserves the environment intent
- repository discovery keeps existing repositories as the source of truth
- `AGENTS.md` captures local working guidance
- onboarding docs explain expected learning and delivery flows
- safe local workspaces reduce the need for risky shared-environment access

## Recommended First Reading

Many Oracle developers are already experienced in enterprise systems but may be less familiar with Git-based workflows. Before continuing, review the practical Git guidance and the repository safety model:

- [Practical Git for Oracle Developers](../oracle/practical-git-for-oracle-developers.md)
- [Backup And Publish](../user/backup-and-publish.md)
- [Repository Workflows](../capabilities/repository.md)

Typical onboarding flow:

```text
Clone Repository
    ↓
Open Repository
    ↓
Workspace Discovered
    ↓
Provision Environment
    ↓
Read Docs
    ↓
Run Tutorial
```

The repository contains not only source code but also the knowledge required to work with that source code.

Moving from an individual demo environment to team-based development?

Read [Practical Git for Oracle Developers](../oracle/practical-git-for-oracle-developers.md).

## Try It Yourself

1. clone a repository that contains Oracle onboarding assets
2. open it in OpenCode Workspace Manager
3. let workspace discovery load the repository-owned configuration
4. provision the environment
5. run the sample tutorial for the shared `Customers / Products / Orders` domain
6. continue into APEX export, validation, and review only after the database basics are understood

## Related Topics

- [Oracle PL/SQL Demo](../oracle-plsql-demo.md)
- [Oracle APEX Demo](../oracle-apex-demo.md)
- [Oracle APEXlang Demo](../oracle-apexlang-demo.md)
- [Oracle Tools Index](../oracle-tools/README.md)
- [Oracle Samples](../oracle-samples.md)
