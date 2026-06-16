# From Oracle Demo to Oracle Onboarding

Templates create workspaces, but repositories teach teams how to use them.

This Oracle workspace family treats the repository as executable onboarding knowledge:

- `workspace.yaml` preserves the environment intent
- repository discovery keeps existing repositories as the source of truth
- `AGENTS.md` captures local working guidance
- onboarding docs explain expected learning and delivery flows
- safe local workspaces reduce the need for risky shared-environment access

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
