using System.Text.Json;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

/// <summary>
/// Generates workspace-local onboarding content that is derived from the selected
/// template/features. Durable edits belong in source files, not these generated copies.
/// </summary>
public sealed class WorkspaceContentGenerator
{
    private const string GeneratedCapabilityGuidanceBegin = "<!-- BEGIN GENERATED WORKSPACE CAPABILITY GUIDANCE -->";
    private const string GeneratedCapabilityGuidanceEnd = "<!-- END GENERATED WORKSPACE CAPABILITY GUIDANCE -->";
    private const string GeneratedOnboardingLinksBegin = "<!-- BEGIN GENERATED ONBOARDING LINKS -->";
    private const string GeneratedOnboardingLinksEnd = "<!-- END GENERATED ONBOARDING LINKS -->";

    public IReadOnlyDictionary<string, string> Generate(ResolvedWorkspace workspace)
    {
        var definition = workspace.Definition;
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        files[Path.Combine("docs", "capabilities", "README.md")] = WithGeneratedHeader(BuildCapabilityCatalogIndex(workspace));

        foreach (var capability in workspace.Capabilities)
        {
            files[Path.Combine("docs", "capabilities", capability.Id + ".md")] = WithGeneratedHeader(BuildCapabilityPage(workspace, capability));
        }

        files["AGENTS.md"] = BuildAgentsCapabilityGuidance(workspace);
        files[Path.Combine("docs", "team-onboarding.md")] = WithGeneratedHeader(BuildTeamOnboardingDoc(workspace));
        files[Path.Combine("docs", "troubleshooting", "workspace-sessions.md")] = WithGeneratedHeader(BuildWorkspaceSessionsTroubleshootingDoc(workspace));

        if (IsDocumentationWorkspace(definition))
        {
            files[Path.Combine("docs", "documentation-features.md")] = WithGeneratedHeader(DocumentationFeaturesWorkspaceDoc());
            files["DOCUMENTATION-FEATURES.md"] = WithGeneratedHeader(DocumentationFeaturesQuickGuide());
            files[Path.Combine("samples", "documentation", "report.md")] = WithGeneratedHeader(DocumentSampleMarkdown());
            files[Path.Combine("samples", "documentation", "report.html")] = WithGeneratedHtmlHeader(DocumentSampleHtml());
            files[Path.Combine("samples", "documentation", "architecture.mmd")] = WithGeneratedMermaidHeader(DocumentSampleMermaid());
            files[Path.Combine("scripts", "validate-documentation-tooling.sh")] = DocumentationToolingValidationScript();
            files[Path.Combine("scripts", "demo-documentation-workflows.sh")] = DocumentationWorkflowDemoScript();
        }

        if (!IsOracleDemoWorkspace(definition))
        {
            return files;
        }

        files[Path.Combine("docs", "oracle-demo.md")] = WithGeneratedHeader(OracleDemoWorkspaceDoc());
        files["ORACLE-DEMO.md"] = WithGeneratedHeader(OracleDemoConnectionGuide());
        files[Path.Combine(".opencode", "context", "oracle-demo.json")] = OracleDemoContextJson();
        files[Path.Combine("tutorial", "workspace-tutorial.json")] = BuildOracleTutorialJson();
        files[Path.Combine("tutorial", "oracle", "README.md")] = WithGeneratedHeader(OracleTutorialReadme());
        files[Path.Combine("tutorial", "oracle", "START-HERE-ORACLE.md")] = WithGeneratedHeader(OracleStartHere());
        files[Path.Combine("tutorial", "oracle", "opencode-start.md")] = WithGeneratedHeader(OracleOpenCodeStartPrompt());
        files[Path.Combine("tutorial", "oracle", "init", "01-create-demo-user.sql")] = WithGeneratedSqlHeader(CreateDemoUserSql());
        files[Path.Combine("tutorial", "oracle", "init", "02-demo-schema.sql")] = WithGeneratedSqlHeader(DemoSchemaSql());
        files[Path.Combine("tutorial", "oracle", "scripts", "03-sample-queries.sql")] = WithGeneratedSqlHeader(SampleQueriesSql());
        files[Path.Combine("tutorial", "oracle", "scripts", "tutorial-query.sql")] = WithGeneratedSqlHeader(TutorialQuerySql());
        files[Path.Combine("knowledge", "skills", "oracle-explain-procedure.md")] = ExplainProcedureSkill();
        files[Path.Combine("knowledge", "skills", "oracle-explain-trigger.md")] = ExplainTriggerSkill();
        files[Path.Combine("knowledge", "skills", "oracle-debug-procedure.md")] = DebugProcedureSkill();
        files[Path.Combine("knowledge", "skills", "oracle-refactor-procedure.md")] = RefactorProcedureSkill();
        files[Path.Combine("knowledge", "skills", "oracle-generate-test-cases.md")] = GenerateTestCasesSkill();
        files[Path.Combine(".local", "oracle", "network", "admin", "README.md")] = WithGeneratedHeader(NetworkAdminReadme());
        files[Path.Combine("scripts", "verify-oracle-demo.sh")] = VerifyOracleDemoScript();
        files[Path.Combine("open-sqlcl.ps1")] = OpenSqlclScript();
        files[Path.Combine("test-oracle-connection.ps1")] = TestConnectionScript();
        files[Path.Combine("run-tutorial-query.ps1")] = RunTutorialQueryScript();
        files[Path.Combine("scripts", "start-opencode-oracle-demo.ps1")] = StartOpenCodeOracleDemoScript();

        foreach (var pair in OracleWorkspaceGeneratedContent.Generate(definition, WithGeneratedHeader, WithGeneratedSqlHeader, WithGeneratedScriptHeader))
        {
            files[pair.Key] = pair.Value;
        }

        var oracleSettings = OracleWorkspaceSettings.From(definition);
        foreach (var key in files.Keys.ToList())
        {
            files[key] = ReplaceOracleHostFacingEndpoints(files[key], oracleSettings);
        }

        return files;
    }

    public static string MergeGeneratedCapabilityGuidance(string? existingContent, string generatedBlockBody)
        => MergeGeneratedBlock(existingContent, GeneratedCapabilityGuidanceBegin, GeneratedCapabilityGuidanceEnd, generatedBlockBody);

    public static string MergeGeneratedOnboardingLinks(string? existingContent, string generatedBlockBody)
        => MergeGeneratedBlock(existingContent, GeneratedOnboardingLinksBegin, GeneratedOnboardingLinksEnd, generatedBlockBody);

    private static string MergeGeneratedBlock(string? existingContent, string beginMarker, string endMarker, string generatedBlockBody)
    {
        var generatedBlock = string.Join("\n",
        [
            beginMarker,
            generatedBlockBody.Trim(),
            endMarker,
        ]);

        if (string.IsNullOrWhiteSpace(existingContent))
        {
            return generatedBlock + "\n";
        }

        var beginIndex = existingContent.IndexOf(beginMarker, StringComparison.Ordinal);
        var endIndex = existingContent.IndexOf(endMarker, StringComparison.Ordinal);

        if (beginIndex >= 0 && endIndex >= beginIndex)
        {
            var replacementEnd = endIndex + endMarker.Length;
            return existingContent.Substring(0, beginIndex)
                + generatedBlock
                + existingContent.Substring(replacementEnd);
        }

        var trimmed = existingContent.TrimEnd();
        return trimmed + "\n\n" + generatedBlock + "\n";
    }

    private static bool IsOracleDemoWorkspace(WorkspaceDefinition definition)
        => OracleWorkspaceFamily.IsOracleWorkspace(definition);

    private static bool IsDocumentationWorkspace(WorkspaceDefinition definition)
        => definition.Features.Contains("document-processing", StringComparer.OrdinalIgnoreCase);

    private static string ReplaceOracleHostFacingEndpoints(string content, OracleWorkspaceSettings oracleSettings)
        => content
            .Replace("http://localhost:8181/ords/apex", oracleSettings.ApexLoginUrl, StringComparison.Ordinal)
            .Replace("http://localhost:8181/ords", oracleSettings.OrdsBaseUrl, StringComparison.Ordinal)
            .Replace("//localhost:1521/", $"//localhost:{oracleSettings.HostPort}/", StringComparison.Ordinal);

    private static string BuildCapabilityCatalogIndex(ResolvedWorkspace workspace)
    {
        var lines = new List<string>
        {
            "# Capability Catalog",
            string.Empty,
            "Use this catalog before searching the repository or probing the runtime.",
            string.Empty,
            "## Getting Started",
            string.Empty,
            "If using a shell:",
            string.Empty,
            "```bash",
            "su opencode",
            "cd /workspace",
            "opencode -s resume",
            "```",
            string.Empty,
            "Docker Desktop Exec is a valid way to access a workspace, but the best onboarding experience starts from an OpenCode session rather than a root shell.",
            string.Empty,
            "Then review:",
            string.Empty,
            "- capability catalog",
            "- onboarding materials",
            "- workspace documentation",
            string.Empty,
            "Read more:",
            string.Empty,
            "- [Team Onboarding](../team-onboarding.md)",
            "- [Workspace Sessions Troubleshooting](../troubleshooting/workspace-sessions.md)",
            string.Empty,
            "The capability catalog is intended to answer questions such as `Can I process Excel files?`, `What PDF tools are available?`, `What OCR tools are available?`, `What Oracle tooling exists?`, and `What onboarding materials are available?` without repository-wide searching.",
            string.Empty,
            "Tool guidance:",
            string.Empty,
            "- capability docs describe supported workflows",
            "- capability docs may mention optional tools",
            "- agents should verify installed tools before claiming they are available",
            "- if a documented tool is missing, report that clearly instead of assuming it exists",
            string.Empty,
            "## Enabled Capabilities",
            string.Empty,
        };

        foreach (var capability in workspace.Capabilities)
        {
            lines.Add($"- [x] {capability.DisplayName}");
        }

        foreach (var capability in workspace.Capabilities)
        {
            lines.Add(string.Empty);
            lines.Add($"## {capability.DisplayName}");
            lines.Add(string.Empty);
            lines.Add(capability.Description);
            lines.Add(string.Empty);
            lines.Add($"Onboarding relevance: {capability.OnboardingRelevance}");
            lines.Add(string.Empty);
            lines.Add($"Available tools: {string.Join(", ", capability.AvailableTools.Select(tool => tool.Name))}");
            lines.Add(string.Empty);
            lines.Add($"Read more: [{capability.DisplayName}]({capability.Id}.md)");
        }

        return string.Join("\n", lines);
    }

    private static string BuildCapabilityPage(ResolvedWorkspace workspace, CapabilityManifest capability)
    {
        var enabledCapabilities = workspace.Capabilities.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>
        {
            $"# {capability.DisplayName}",
            string.Empty,
            "## What It Is",
            string.Empty,
            capability.WhatItIs,
            string.Empty,
            "## Why Use It",
            string.Empty,
            capability.WhyUseIt,
            string.Empty,
            "## Available Tools",
            string.Empty,
        };

        foreach (var tool in capability.AvailableTools)
        {
            lines.Add($"### {tool.Name}");
            lines.Add(string.Empty);
            lines.Add($"Purpose: {tool.Purpose}");
            lines.Add(string.Empty);
            lines.Add($"Supported workflows: {string.Join(", ", tool.SupportedWorkflows)}");
            lines.Add(string.Empty);
            lines.Add($"Common use cases: {string.Join(", ", tool.CommonUseCases)}");
            lines.Add(string.Empty);
        }

        lines.Add("## Typical Tasks");
        lines.Add(string.Empty);
        foreach (var task in capability.TypicalTasks)
        {
            lines.Add($"- {task}");
        }

        lines.Add(string.Empty);
        lines.Add("## Examples");
        lines.Add(string.Empty);
        foreach (var example in capability.Examples)
        {
            lines.Add($"- {example}");
        }

        lines.Add(string.Empty);
        lines.Add("## Related Documentation");
        lines.Add(string.Empty);
        foreach (var document in capability.RelatedDocumentation.Where(item => !string.IsNullOrWhiteSpace(item.Path) && IsWorkspaceLinkAvailable(workspace, item.Path)))
        {
            lines.Add($"- [{document.Label}]({document.Path})");
            if (!string.IsNullOrWhiteSpace(document.Description))
            {
                lines.Add(document.Description);
            }
        }

        lines.Add(string.Empty);
        lines.Add("## Related Capabilities");
        lines.Add(string.Empty);
        foreach (var relatedCapabilityId in capability.RelatedCapabilities)
        {
            if (enabledCapabilities.TryGetValue(relatedCapabilityId, out var relatedCapability))
            {
                lines.Add($"- [{relatedCapability.DisplayName}]({relatedCapability.Id}.md)");
            }
        }

        return string.Join("\n", lines);
    }

    private static string BuildAgentsCapabilityGuidance(ResolvedWorkspace workspace)
    {
        var lines = new List<string>
        {
            "## Workspace Capability Discovery",
            string.Empty,
            "Start here:",
            string.Empty,
            "- docs/capabilities/README.md",
            string.Empty,
            "This catalog describes:",
            string.Empty,
            "- enabled capabilities",
            "- available tools",
            "- onboarding materials",
            "- examples",
            "- supported workflows",
            string.Empty,
            "Do not scan the repository first.",
            string.Empty,
            "Use the capability catalog before searching the workspace.",
            string.Empty,
            "If attached to a container shell rather than an OpenCode session, see:",
            string.Empty,
            "- docs/team-onboarding.md",
            "- docs/troubleshooting/workspace-sessions.md",
        };

        foreach (var capability in workspace.Capabilities)
        {
            lines.Add(string.Empty);
            lines.Add($"## {capability.DisplayName} Guidance");
            lines.Add(string.Empty);
            lines.Add("Start here:");
            lines.Add(string.Empty);
            lines.Add($"- docs/capabilities/{capability.Id}.md");

            foreach (var link in capability.AgentStartHere.Where(item => !string.IsNullOrWhiteSpace(item.Path) && IsWorkspaceLinkAvailable(workspace, item.Path)))
            {
                lines.Add($"- {link.Path}");
            }

            if (capability.LearningProgression.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Learning progression:");
                lines.Add(string.Empty);
                lines.Add(string.Join("\n    ↓\n", capability.LearningProgression));
            }
        }

        return string.Join("\n", lines);
    }

    private static string BuildAgentsOnboardingLinks(ResolvedWorkspace workspace)
    {
        var emittedLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>
        {
            "## Enabled Onboarding Materials",
        };

        foreach (var capability in workspace.Capabilities)
        {
            var links = GetEnabledOnboardingLinks(workspace, capability)
                .Where(link => emittedLinks.Add(link))
                .ToList();
            if (links.Count == 0)
            {
                continue;
            }

            lines.Add(string.Empty);
            lines.Add($"{capability.DisplayName}:");
            lines.Add(string.Empty);
            foreach (var link in links)
            {
                lines.Add($"- {link}");
            }
        }

        return string.Join("\n", lines);
    }

    private static IEnumerable<string> GetEnabledOnboardingLinks(ResolvedWorkspace workspace, CapabilityManifest capability)
    {
        var links = new List<string> { $"docs/capabilities/{capability.Id}.md" };

        foreach (var link in capability.AgentStartHere.Where(item => !string.IsNullOrWhiteSpace(item.Path) && IsWorkspaceLinkAvailable(workspace, item.Path)))
        {
            links.Add(link.Path);
        }

        if (string.Equals(capability.Id, "oracle", StringComparison.OrdinalIgnoreCase))
        {
            links.Add("docs/oracle-documentation-strategy.md");
            links.Add("docs/oracle-documentation-discovery.md");
            links.Add("docs/reference/oracle-knowledge-map.yaml");
            links.Add("docs/reference/oracle-plsql-index.md");
            links.Add("docs/reference/oracle-database-index.md");
            links.Add("docs/oracle-plsql-demo.md");

            if (workspace.Definition.Features.Contains("oracle-apex-demo", StringComparer.OrdinalIgnoreCase))
            {
                links.Add("docs/oracle-apex-demo.md");
                links.Add("docs/reference/oracle-apex-index.md");
                links.Add("docs/reference/oracle-apex-books.md");
                links.Add("docs/reference/oracle-apex-api-reference.md");
                links.Add("docs/reference/oracle-apex-administration.md");
                links.Add("docs/reference/oracle-apex-installation.md");
                links.Add("docs/reference/oracle-apex-release-notes.md");
                links.Add("docs/reference/oracle-apex-version-archives.md");
                links.Add("docs/reference/oracle-apex-api-map.yaml");
                links.Add("docs/reference/oracle-apex-api-packages.md");
                links.Add("docs/reference/oracle-ords-index.md");
            }

            if (workspace.Definition.Features.Contains("oracle-apexlang-demo", StringComparer.OrdinalIgnoreCase))
            {
                links.Add("docs/oracle-apexlang-demo.md");
            }
        }

        if (string.Equals(capability.Id, "document-processing", StringComparison.OrdinalIgnoreCase))
        {
            links.Add("docs/documentation-features.md");
        }

        return links
            .Where(link => !string.IsNullOrWhiteSpace(link) && IsWorkspaceLinkAvailable(workspace, link))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public string BuildAgentsDocument(ResolvedWorkspace workspace, string? existingContent)
    {
        var withCapabilityGuidance = MergeGeneratedCapabilityGuidance(existingContent, BuildAgentsCapabilityGuidance(workspace));
        return MergeGeneratedOnboardingLinks(withCapabilityGuidance, BuildAgentsOnboardingLinks(workspace));
    }

    private static bool IsWorkspaceLinkAvailable(ResolvedWorkspace workspace, string path)
    {
        if (string.Equals(path, "README.md", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "../../AGENTS.md", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "docs/capabilities/README.md", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.Contains("documentation-features", StringComparison.OrdinalIgnoreCase)
            || path.Contains("samples/documentation", StringComparison.OrdinalIgnoreCase))
        {
            return IsDocumentationWorkspace(workspace.Definition);
        }

        if (path.Contains("oracle-tools", StringComparison.OrdinalIgnoreCase)
            || path.Contains("oracle-samples", StringComparison.OrdinalIgnoreCase)
            || path.Contains("oracle-plsql-demo", StringComparison.OrdinalIgnoreCase)
            || path.Contains("oracle-apex-demo", StringComparison.OrdinalIgnoreCase)
            || path.Contains("oracle-apexlang-demo", StringComparison.OrdinalIgnoreCase)
            || path.Contains("oracle-documentation-strategy", StringComparison.OrdinalIgnoreCase)
            || path.Contains("oracle-documentation-discovery", StringComparison.OrdinalIgnoreCase)
            || path.Contains("docs/reference/oracle-", StringComparison.OrdinalIgnoreCase)
            || path.Contains("skills/oracle/", StringComparison.OrdinalIgnoreCase))
        {
            return IsOracleDemoWorkspace(workspace.Definition);
        }

        return true;
    }

    private static string BuildTeamOnboardingDoc(ResolvedWorkspace workspace)
    {
        var onboardingLinks = workspace.Capabilities
            .SelectMany(capability => GetEnabledOnboardingLinks(workspace, capability))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(link => link, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = new List<string>
        {
            "# Team Onboarding",
            string.Empty,
            "This workspace is intended to be discovered, opened, and resumed through an OpenCode session rather than a root shell.",
            string.Empty,
            "## Connecting to a Workspace Session",
            string.Empty,
            "Most onboarding exercises assume you are connected to an OpenCode session rather than a root shell.",
            string.Empty,
            "Typical workflow:",
            string.Empty,
            "```bash",
            "su opencode",
            "cd /workspace",
            "opencode -s resume",
            "```",
            string.Empty,
            "Useful commands:",
            string.Empty,
            "```bash",
            "opencode sessions",
            "opencode -s <session-id>",
            "```",
            string.Empty,
            "Suggested first questions:",
            string.Empty,
            "- What capabilities are available?",
            "- What onboarding docs exist?",
            "- What tools are installed?",
            string.Empty,
            "## Using Docker Desktop Exec",
            string.Empty,
            "Docker Desktop Exec is a valid way to access a workspace.",
            string.Empty,
            "You may be attached to:",
            string.Empty,
            "- root shell",
            "- opencode user shell",
            "- OpenCode session",
            string.Empty,
            "OpenCode sessions provide the best onboarding experience.",
            string.Empty,
            "## Start Here",
            string.Empty,
            "- `docs/capabilities/README.md`",
            "- `docs/troubleshooting/workspace-sessions.md`",
        };

        if (onboardingLinks.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Enabled Onboarding Materials");
            lines.Add(string.Empty);
            lines.AddRange(onboardingLinks.Select(link => $"- `{link}`"));
        }

        return string.Join("\n", lines);
    }

    private static string BuildWorkspaceSessionsTroubleshootingDoc(ResolvedWorkspace workspace)
        => """
# Workspace Sessions Troubleshooting

Use this guide when the workspace starts but you are not yet in a usable OpenCode session.

## Using Docker Desktop Exec

Docker Desktop Exec is a valid way to access a workspace.

You may land in:

- root shell
- opencode user shell
- OpenCode session

OpenCode sessions provide the best onboarding experience.

## I only see a root shell

Symptoms:

```text
root@container:/#
```

Resolution:

```bash
su opencode
cd /workspace
opencode -s resume
```

## opencode command not found

Possible causes:

- wrong user
- PATH issue
- provisioning incomplete

Recovery steps:

1. switch to `opencode` with `su opencode`
2. verify provisioning completed
3. reprovision or recover the workspace if `opencode` is still unavailable

## No sessions available

Possible causes:

- workspace never initialized
- session removed
- provisioning failed

Recovery steps:

1. run `opencode sessions`
2. verify the workspace was provisioned successfully
3. recover or reprovision the workspace

## Cannot attach to session

Diagnostics:

```bash
opencode sessions
```

Recovery steps:

1. confirm you are running as `opencode`
2. confirm the current directory is `/workspace`
3. retry `opencode -s resume` or `opencode -s <session-id>`
4. review workspace diagnostics and provisioning logs

## Workspace starts but agent is not running

Investigation steps:

1. verify the workspace is provisioned
2. verify `opencode sessions` shows a restorable session
3. reopen the session with `opencode -s resume`
4. reprovision if the runtime or agent bootstrap was incomplete

## Capability catalog missing

Expected files:

```text
docs/capabilities/README.md
```

Recovery:

- reprovision workspace
- regenerate documentation

## AGENTS.md missing or outdated

Recovery:

- reprovision workspace
- verify generated blocks

## Tool mentioned in docs but not installed

Example symptom:

```text
weasyprint: command not found
```

Resolution:

1. verify the capability catalog
2. verify installed tooling
3. review workspace feature configuration
4. reprovision if required

Agents should not claim a tool exists merely because documentation mentions it.
""";

    private static string WithGeneratedHeader(string body)
        => string.Join("\n",
        [
            "# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES",
            "# Source inputs: workspace.yaml and catalog manifests under catalog/.",
            "# User edits to this file are not preserved. Edit workspace.yaml or catalog manifests instead.",
            body.TrimStart('\r', '\n'),
            string.Empty,
        ]);

    private static string WithGeneratedSqlHeader(string body)
        => string.Join("\n",
        [
            "-- GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES",
            "-- Source inputs: workspace.yaml and catalog manifests under catalog/.",
            "-- User edits to this file are not preserved. Edit workspace.yaml or catalog manifests instead.",
            body.TrimStart('\r', '\n'),
            string.Empty,
        ]);

    private static string WithGeneratedHtmlHeader(string body)
        => string.Join("\n",
        [
            "<!-- GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES -->",
            "<!-- Source inputs: workspace.yaml and catalog manifests under catalog/. -->",
            "<!-- User edits to this file are not preserved. Edit workspace.yaml or catalog manifests instead. -->",
            body.TrimStart('\r', '\n'),
            string.Empty,
        ]);

    private static string WithGeneratedMermaidHeader(string body)
        => string.Join("\n",
        [
            "%% GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES",
            "%% Source inputs: workspace.yaml and catalog manifests under catalog/.",
            "%% User edits to this file are not preserved. Edit workspace.yaml or catalog manifests instead.",
            body.TrimStart('\r', '\n'),
            string.Empty,
        ]);

    private static string WithGeneratedScriptHeader(string body)
        => string.Join("\n",
        [
            "#!/usr/bin/env bash",
            "# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES",
            "# Source inputs: workspace.yaml and catalog manifests under catalog/.",
            "# User edits to this file are not preserved. Edit workspace.yaml or catalog manifests instead.",
            body.TrimStart('\r', '\n'),
            string.Empty,
        ]);

    private static string BuildOracleTutorialJson()
    {
        var document = new
        {
            title = "Oracle PL/SQL Workspace",
            subtitle = "Short first-run guide for the Oracle PL/SQL demo.",
            steps = new object[]
            {
                new
                {
                    id = "intro",
                    title = "Welcome to Oracle PL/SQL Demo",
                    summary = "This workspace gives you a local Oracle demo environment for onboarding and live explanation.",
                    highlights = new[]
                    {
                        "Local Oracle database",
                        "SQLcl",
                        "Sample schema",
                        "Guided tutorial",
                        "Oracle AI skills",
                    },
                    presenterFlow = new[]
                    {
                        "Create Oracle Workspace",
                        "Start Oracle",
                        "Open Tutorial",
                        "Connect using SQL Developer (optional)",
                        "Run sample query",
                        "Explain procedure",
                        "Explain trigger",
                        "Complete",
                    },
                    estimatedTime = "15-20 minutes",
                    note = "No customer infrastructure required. Staging access is optional.",
                    bullets = new[]
                    {
                        "Use the Oracle Demo Database panel on the right side of the workspace details.",
                        "Everything in this tutorial uses the known local demo connection. Inside the workspace runtime and OpenCode, use demo_user/demo_password@//oracle-demo:1521/FREEPDB1.",
                        "SQL Developer is optional. If it is not installed, continue with SQLcl or SQL*Plus.",
                        "Do not inspect .env, TNS_ADMIN, ORACLE_HOME, tnsnames.ora, or other configuration files during normal demo verification.",
                    },
                },
                new
                {
                    id = "verify",
                    title = "1. Verify The Environment",
                    summary = "Start Oracle and wait for the local demo database to become ready.",
                    bullets = new[]
                    {
                        "In the Oracle Demo Database panel, choose Start.",
                        "The first provisioning run downloads SQLcl and needs internet access. Later tutorial work stays local.",
                        "If the Oracle Demo Database panel is not Running and Ready yet, stop and start Oracle first.",
                        "When the panel shows Running and Ready, run scripts/verify-oracle-demo.sh.",
                        "Use the known local demo connection. Do not ask for credentials or perform connection discovery for the local demo workspace.",
                    },
                },
                new
                {
                    id = "hello-world",
                    title = "2. Hello World PL/SQL",
                    summary = "Use the known local demo connection and run one small PL/SQL block to confirm the database is ready.",
                    bullets = new[]
                    {
                        "Connect with sqlplus demo_user/demo_password@//oracle-demo:1521/FREEPDB1 or use the helper actions that already know the demo connection.",
                        "Run SET SERVEROUTPUT ON and then BEGIN DBMS_OUTPUT.PUT_LINE('Hello from Oracle PL/SQL Workspace'); END; /",
                        "Do not inspect .env, environment variables, or tnsnames.ora during normal verification.",
                    },
                },
                new
                {
                    id = "schema",
                    title = "3. Demo Schema",
                    summary = "Inspect the local demo schema that was created automatically for you.",
                    bullets = new[]
                    {
                        "The schema is installed automatically on first database start.",
                        "Focus first on demo_customers, demo_products, demo_orders, demo_show_customer, and demo_orders_biu_trigger.",
                        "If you want to inspect the generated setup later, the initialization SQL lives under tutorial/oracle/init/.",
                    },
                },
                new
                {
                    id = "data",
                    title = "4. Sample Data",
                    summary = "Query the seeded data and verify the sample relationships.",
                    bullets = new[]
                    {
                        "Run the read-only verification queries against demo_user/demo_password@//oracle-demo:1521/FREEPDB1.",
                        "The generated helper script scripts/verify-oracle-demo.sh runs the standard smoke test for you.",
                        "Use the Run Tutorial Query quick action if you want a single-click check.",
                        "Notice that the data is local and requires no customer environment.",
                    },
                },
                new
                {
                    id = "procedure",
                    title = "5. Procedure Walkthrough",
                    summary = "Explain the demo procedure with AI before changing anything.",
                    bullets = new[]
                    {
                        "Open knowledge/skills/oracle-explain-procedure.md.",
                        "Ask AI to explain demo_show_customer step-by-step, including inputs, outputs, tables, and side effects.",
                        "Keep the focus on understanding existing code rather than generating replacement code.",
                    },
                },
                new
                {
                    id = "trigger",
                    title = "6. Trigger Walkthrough",
                    summary = "Use AI to understand when the trigger runs and what it enforces.",
                    bullets = new[]
                    {
                        "Open knowledge/skills/oracle-explain-trigger.md.",
                        "Ask AI to explain demo_orders_biu_trigger, including validations and field mutations.",
                        "Verify the explanation by inserting or updating one row in the local demo schema.",
                    },
                },
                new
                {
                    id = "troubleshoot",
                    title = "7. Trigger Troubleshooting",
                    summary = "Practice debugging against the local demo database.",
                    bullets = new[]
                    {
                        "Try an invalid order quantity and observe the raised application error.",
                        "Use the oracle-debug-procedure skill pattern for the smallest safe correction mindset.",
                        "Confirm the behavior again with SQLcl after the explanation.",
                    },
                },
                new
                {
                    id = "refactor",
                    title = "8. Refactoring Example",
                    summary = "Review readability improvements without changing behavior.",
                    bullets = new[]
                    {
                        "Use oracle-refactor-procedure.md as the prompt for AI assistance.",
                        "Prefer naming, formatting, and structure improvements over logic changes.",
                        "Run the generated test cases against the local demo objects.",
                    },
                },
                new
                {
                    id = "scenario",
                    title = "9. Mini Real-World Scenario",
                    summary = "Rehearse the Monday demo sequence end to end.",
                    bullets = new[]
                    {
                        "Create workspace, start Oracle, wait for Running and Ready, run scripts/verify-oracle-demo.sh, explain a procedure, and explain a trigger.",
                        "Treat staging as optional follow-up configuration only.",
                        "The main story is reproducible onboarding, not privileged access to customer infrastructure.",
                    },
                },
            },
        };

        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string DocumentationFeaturesWorkspaceDoc() => """
## Documentation Features Workspace

This workspace is prepared for modern documentation and reporting workflows on Ubuntu while staying close to how documents render on Windows systems.

Read more:

- `docs/capabilities/documentation.md`
- `docs/capabilities/document-processing.md`
- `docs/capabilities/analytics.md`
- `docs/capabilities/reporting.md`
- `docs/capabilities/testing.md`

### Included Capabilities

- Markdown to PDF with `pandoc` and `typst`
- HTML to PDF with `weasyprint`
- Diagram rendering with Mermaid, Graphviz, and PlantUML
- Office document conversion with LibreOffice
- PDF inspection with `pdfinfo`, `pypdf`, and `pymupdf`
- Report generation with `reportlab`
- Broad Windows-compatible font coverage including Carlito, Caladea, Liberation, Noto, Inter, Roboto, JetBrains Mono, and Fira Code

### Runtime Baseline

New workspaces use Node.js 22 LTS by default so Playwright, Mermaid, and modern npm packages run against a current ecosystem baseline.

### First Commands To Run

1. `scripts/validate-documentation-tooling.sh`
2. `scripts/demo-documentation-workflows.sh`

### Validation Output

The validation script writes reports under `artifacts/documentation-validation/`:

- `fc-list.txt` for the full installed font catalog
- `font-match.txt` for practical Windows-compatible font mapping checks
- `tool-versions.txt` for CLI availability and versions

New workspaces use Node.js 22 LTS as the default runtime baseline so Mermaid, Playwright, and modern npm packages run without older engine mismatches.

### Demo Output

The demo script writes generated outputs under `artifacts/documentation-demo/`:

- `markdown-report.pdf`
- `html-report.pdf`
- `reportlab-report.pdf`
- `architecture.svg`
- `architecture.png`
- PDF metadata and inspection reports

### Font Compatibility Focus

- Arial-compatible: `Arial` or `Liberation Sans`
- Calibri-compatible: `Carlito`
- Cambria-compatible: `Caladea`
- Unicode and multilingual coverage: `Noto Sans`, `Noto Serif`, and Noto CJK families
- Emoji coverage: `Noto Color Emoji`
- Developer snippets: `JetBrains Mono` and `Fira Code`

This makes the workspace suitable for business reports, architecture manuals, tutorials, multilingual content, diagrams, and analytical reporting.
""";

    private static string DocumentationFeaturesQuickGuide() => """
# Documentation Features Workspace

Use this workspace when you need reliable PDF generation, report authoring, diagrams, and document validation without hand-assembling extra tools.

## Quick Start

Run the validation pass:

```bash
scripts/validate-documentation-tooling.sh
```

Run the end-to-end demo:

```bash
scripts/demo-documentation-workflows.sh
```

## What Gets Verified

- `pandoc`
- `typst`
- `node` and `npm`
- `playwright`
- `chromium`
- `mmdc`
- `weasyprint`
- Python PDF libraries: `pypdf`, `pymupdf`, `reportlab`, `markdown-it-py`
- `dot`
- `plantuml`
- installed font catalog and Windows-compatible font matches

## Sample Inputs

- `samples/documentation/report.md`
- `samples/documentation/report.html`
- `samples/documentation/architecture.mmd`

## Read More

- `docs/capabilities/README.md`
- `docs/capabilities/documentation.md`
- `docs/capabilities/document-processing.md`
- `docs/capabilities/ocr.md` when OCR is enabled
- `docs/capabilities/spell-checking.md` when spell checking is enabled
""";

    private static string DocumentSampleMarkdown() => """
# Analytical Report Draft

This sample exercises the Markdown to PDF toolchain with multilingual text, developer formatting, and Unicode coverage.

## Executive Summary

- Report title: Documentation Features Workspace
- Compatibility goal: Windows-like PDF output on Ubuntu
- Validation target: business reports, manuals, tutorials, and architecture documentation

## International Content

- Slovenian: Dokumentacija mora ostati berljiva in ponovljiva.
- Croatian: Izvjestaj treba imati pouzdano generiranje PDF-a.
- Chinese: 多语言内容和图表需要稳定输出。
- Emoji: PDF smoke test 😀

## Code Sample

```text
SELECT report_name, generated_at_utc
FROM generated_reports
ORDER BY generated_at_utc DESC;
```
""";

    private static string DocumentSampleHtml() => """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <title>Documentation Features HTML Sample</title>
  <style>
    body {
      font-family: "Carlito", "Liberation Sans", Arial, "Noto Sans", sans-serif;
      margin: 32px;
      color: #172033;
      line-height: 1.5;
    }

    h1, h2 {
      font-family: "Caladea", Cambria, "Noto Serif", serif;
      margin-bottom: 0.3rem;
    }

    code, pre {
      font-family: "JetBrains Mono", "Fira Code", "Noto Sans Mono", monospace;
      background: #f3f5f8;
    }

    pre {
      padding: 12px;
      border-radius: 8px;
      overflow: auto;
    }
  </style>
</head>
<body>
  <h1>Documentation Features Workspace</h1>
  <p>This HTML sample verifies business-document fonts, multilingual rendering, and code-block output.</p>

  <h2>Compatibility Notes</h2>
  <p>Arial-compatible text should resolve through Liberation Sans or Arial when Microsoft core fonts are available.</p>
  <p>Calibri-compatible text should resolve through Carlito. Cambria-compatible text should resolve through Caladea.</p>
  <p>Unicode coverage: Slovenian, Croatian, Chinese, and emoji 😀.</p>

  <pre><code>graph TD
  Workspace --&gt; PDF
  Workspace --&gt; Diagrams
  Workspace --&gt; Reports
  </code></pre>
</body>
</html>
""";

    private static string DocumentSampleMermaid() => """
flowchart LR
    Author[Authoring Sources] --> Markdown[Markdown and Typst]
    Author --> Html[HTML and CSS]
    Author --> Diagram[Mermaid, Graphviz, PlantUML]
    Markdown --> Pdf[Professional PDF Output]
    Html --> Pdf
    Diagram --> Pdf
""";

    private static string DocumentationToolingValidationScript() => WithGeneratedScriptHeader("""
set -euo pipefail

workspace_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
report_dir="${workspace_root}/artifacts/documentation-validation"
mkdir -p "${report_dir}"

require_command() {
  local command_name="$1"
  command -v "${command_name}" >/dev/null 2>&1 || {
    printf 'Missing required command: %s\n' "${command_name}" >&2
    exit 1
  }
}

require_command pandoc
require_command typst
require_command node
require_command npm
require_command playwright
require_command chromium
require_command mmdc
require_command weasyprint
require_command dot
require_command plantuml
require_command fc-list
require_command fc-match
require_command pdfinfo

python3 - <<'PY'
import importlib

required = [
    ("pypdf", "pypdf"),
    ("fitz", "pymupdf"),
    ("reportlab", "reportlab"),
    ("markdown_it", "markdown-it-py"),
]

missing = []
for module_name, package_name in required:
    try:
        importlib.import_module(module_name)
    except Exception:
        missing.append(package_name)

if missing:
    raise SystemExit("Missing required Python packages: " + ", ".join(missing))
PY

{
  printf 'pandoc: %s\n' "$(pandoc --version | sed -n '1p')"
  printf 'typst: %s\n' "$(typst --version)"
  printf 'node: %s\n' "$(node --version)"
  printf 'node-eval: %s\n' "$(node -e "console.log(process.version)")"
  printf 'npm: %s\n' "$(npm --version)"
  printf 'playwright: %s\n' "$(playwright --version)"
  printf 'chromium: %s\n' "$(chromium --version)"
  printf 'mmdc: %s\n' "$(mmdc --version)"
  printf 'weasyprint: %s\n' "$(weasyprint --version)"
  printf 'dot: %s\n' "$(dot -V 2>&1)"
  printf 'plantuml: %s\n' "$(plantuml -version | sed -n '1p')"
  printf 'python-pdf-libs: ok\n'
} | tee "${report_dir}/tool-versions.txt"

{
  printf 'Arial -> %s\n' "$(fc-match Arial)"
  printf 'Calibri -> %s\n' "$(fc-match Calibri)"
  printf 'Cambria -> %s\n' "$(fc-match Cambria)"
  printf 'Noto Sans -> %s\n' "$(fc-match 'Noto Sans')"
  printf 'Noto Sans CJK SC -> %s\n' "$(fc-match 'Noto Sans CJK SC')"
  printf 'Noto Color Emoji -> %s\n' "$(fc-match 'Noto Color Emoji')"
  printf 'JetBrains Mono -> %s\n' "$(fc-match 'JetBrains Mono')"
  printf 'Fira Code -> %s\n' "$(fc-match 'Fira Code')"
} | tee "${report_dir}/font-match.txt"

fc-list | sort > "${report_dir}/fc-list.txt"

printf 'Validation reports written to %s\n' "${report_dir}"
""");

    private static string DocumentationWorkflowDemoScript() => WithGeneratedScriptHeader("""
set -euo pipefail

workspace_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
output_dir="${workspace_root}/artifacts/documentation-demo"
mkdir -p "${output_dir}"

"${workspace_root}/scripts/validate-documentation-tooling.sh"

cat > "${output_dir}/mermaid-puppeteer.json" <<'EOF'
{
  "args": ["--no-sandbox", "--disable-setuid-sandbox"]
}
EOF

pandoc "${workspace_root}/samples/documentation/report.md" \
  --pdf-engine=typst \
  -V mainfont="Carlito" \
  -V monofont="JetBrains Mono" \
  -o "${output_dir}/markdown-report.pdf"

weasyprint "${workspace_root}/samples/documentation/report.html" "${output_dir}/html-report.pdf"

PUPPETEER_EXECUTABLE_PATH="$(command -v chromium)" \
  mmdc -p "${output_dir}/mermaid-puppeteer.json" -i "${workspace_root}/samples/documentation/architecture.mmd" -o "${output_dir}/architecture.svg"

PUPPETEER_EXECUTABLE_PATH="$(command -v chromium)" \
  mmdc -p "${output_dir}/mermaid-puppeteer.json" -i "${workspace_root}/samples/documentation/architecture.mmd" -o "${output_dir}/architecture.png"

OUTPUT_DIR="${output_dir}" python3 - <<'PY'
import os
from pathlib import Path
from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas

output_path = Path(os.environ["OUTPUT_DIR"]) / "reportlab-report.pdf"
c = canvas.Canvas(str(output_path), pagesize=A4)
c.setTitle("Documentation Features ReportLab Sample")
c.setFont("Helvetica", 14)
c.drawString(72, 800, "Documentation Features Workspace")
c.setFont("Helvetica", 11)
c.drawString(72, 778, "ReportLab generated PDF smoke test for analytical reporting output.")
c.drawString(72, 756, "This verifies Python-side report generation is ready out of the box.")
c.save()
PY

pdfinfo "${output_dir}/markdown-report.pdf" | tee "${output_dir}/markdown-report.pdfinfo.txt"

OUTPUT_DIR="${output_dir}" python3 - <<'PY' | tee "${output_dir}/pdf-metadata.txt"
import os
from pathlib import Path
from pypdf import PdfReader
import fitz

output_dir = Path(os.environ["OUTPUT_DIR"])
for file_name in ["markdown-report.pdf", "html-report.pdf", "reportlab-report.pdf"]:
    pdf_path = output_dir / file_name
    reader = PdfReader(str(pdf_path))
    document = fitz.open(pdf_path)
    print(f"{file_name}: pages={len(reader.pages)} metadata={reader.metadata}")
    print(f"{file_name}: pymupdf-metadata={document.metadata}")
    document.close()
PY

printf 'Generated documentation demo outputs in %s\n' "${output_dir}"
""");

    private static string OracleDemoWorkspaceDoc() => """
## Oracle PL/SQL Workspace

This workspace packages Oracle Free, SQLcl, SQL*Plus, tutorial content, sample PL/SQL, AI prompts, and safety guidance into one portable demo environment.

The Oracle demo database is local to your machine. Staging setup is optional and is not part of the first tutorial.

### Connection Details

- Inside workspace runtime and OpenCode: `demo_user/demo_password@//oracle-demo:1521/FREEPDB1`
- Windows host tools: `demo_user/demo_password@//localhost:1521/FREEPDB1`

- Host: `localhost`
- Port: `1521`
- Service: `FREEPDB1`
- Demo username: `demo_user`
- Demo password: `demo_password`

### First Steps

1. Start the Oracle demo database from the dashboard.
2. Open the tutorial from the dashboard or `Help > Quick Tutorial`.
3. Open SQLcl with `./open-sqlcl.ps1`.
4. Run `scripts/verify-oracle-demo.sh` inside the workspace runtime after Oracle shows Running and Ready.
5. Ask AI to explain `demo_show_customer` and `demo_orders_biu_trigger` using the prompts under `knowledge/skills/`.

### Demo Verification

Before opening OpenCode or running demo verification, start Oracle first and wait until the Oracle Demo Database panel shows `Running` and `Ready`.

Use the known local demo connection for normal verification inside the workspace runtime:

```text
demo_user/demo_password@//oracle-demo:1521/FREEPDB1
```

Do not read `.env`.

Do not inspect secrets.

Do not inspect `tnsnames.ora`.

Do not inspect `TNS_ADMIN` or `ORACLE_HOME`.

Use the known local demo connection. Do not ask for credentials.

Do not perform connection discovery.

Do not modify files.

Do not install anything.

Run:

```bash
scripts/verify-oracle-demo.sh
```

The helper script runs:

```sql
SELECT 'Connection OK' AS status FROM dual;

SELECT customer_id, customer_name
FROM demo_customers
ORDER BY customer_id;

SET SERVEROUTPUT ON
EXEC demo_show_customer(1);
```

Report results only.

### Staging Later

When a customer staging environment is added later, place `tnsnames.ora`, `sqlnet.ora`, and wallet files under `.local/oracle/network/admin` and keep sessions read-only whenever possible.
""";

    private static string OracleDemoConnectionGuide() => """
# Oracle Demo Connection

This workspace includes a local Oracle demo database.

Inside the workspace runtime, use:

```text
sqlplus -S demo_user/demo_password@//oracle-demo:1521/FREEPDB1
```

From Windows / SQL Developer, use:

- Host: localhost
- Port: 1521
- Service: FREEPDB1
- Username: demo_user
- Password: demo_password

Run:

```bash
scripts/verify-oracle-demo.sh
```

Do not inspect `.env` for normal demo verification.
`.env` contains local runtime settings and generated secrets.
""";

    private static string OracleTutorialReadme() => """
## Oracle Tutorial Files

Use these files with SQLcl, SQL*Plus, or SQL Developer:

- `01-create-demo-user.sql`: creates the demo user and grants local demo privileges
- `02-demo-schema.sql`: creates tables, sample procedures, and trigger logic
- `03-sample-queries.sql`: tutorial queries and procedure examples
- `tutorial-query.sql`: quick one-command smoke test
- `START-HERE-ORACLE.md`: demo-safe verification guidance for OpenCode and terminal checks
- `scripts/verify-oracle-demo.sh`: standard Oracle demo readiness and data smoke test

The demo schema is installed automatically on first Oracle container initialization.

For routine demo verification inside the workspace runtime, use the known local connection `demo_user/demo_password@//oracle-demo:1521/FREEPDB1`.

If Oracle is not running yet, start Oracle first and wait for the app to show `Running` and `Ready` before opening OpenCode or running the verification script.

Do not read `.env`, inspect secrets, inspect `tnsnames.ora`, or perform connection discovery unless you are explicitly troubleshooting workspace configuration.
""";

    private static string OracleStartHere() => """
If Oracle is not running yet, start Oracle first from the app and wait until the Oracle Demo Database panel shows `Running` and `Ready`.

Before asking for connection details, read `ORACLE-DEMO.md` or `.opencode/context/oracle-demo.json`.

Connection string inside workspace runtime:

```text
demo_user/demo_password@//oracle-demo:1521/FREEPDB1
```

Do not read `.env`.

Do not inspect secrets.

Do not inspect `tnsnames.ora`.

Do not inspect `TNS_ADMIN` or `ORACLE_HOME`.

Use the known local demo connection. Do not ask for credentials.

Do not perform connection discovery.

Do not modify files.

Do not install anything.

Run:

```bash
scripts/verify-oracle-demo.sh
```

The helper script runs:

```sql
SELECT 'Connection OK' AS status FROM dual;

SELECT customer_id, customer_name
FROM demo_customers
ORDER BY customer_id;

SET SERVEROUTPUT ON
EXEC demo_show_customer(1);
```

Report results only.
""";

    private static string OracleOpenCodeStartPrompt() => """
Review this Oracle PL/SQL demo workspace.

If Oracle is not running yet, say `Start Oracle first` and stop. Do not imply verification will work until the Oracle Demo Database panel shows `Running` and `Ready`.

Before asking for connection details, read `ORACLE-DEMO.md` or `.opencode/context/oracle-demo.json`.

Do not inspect .env.

Do not inspect secrets.

Do not inspect tnsnames.ora.

Do not inspect TNS_ADMIN or ORACLE_HOME.

Use the known local demo connection. Do not ask for credentials.

Do not perform connection discovery.

Connection string inside workspace runtime:

demo_user/demo_password@//oracle-demo:1521/FREEPDB1

Do not modify files.

Do not install anything.

Run scripts/verify-oracle-demo.sh

That helper script uses:

SELECT 'Connection OK' AS status FROM dual;

SELECT customer_id, customer_name
FROM demo_customers
ORDER BY customer_id;

SET SERVEROUTPUT ON
EXEC demo_show_customer(1);

Report results only.

Start by explaining what `demo_show_customer` does.

Then explain `demo_orders_biu_trigger`.

Suggest read-only verification commands I should run against the local demo database.

Identify useful edge cases and test scenarios for the demo PL/SQL.
""";

    private static string CreateDemoUserSql() => """
ALTER SESSION SET CONTAINER = FREEPDB1;

DECLARE
    l_exists NUMBER := 0;
BEGIN
    SELECT COUNT(*) INTO l_exists FROM dba_users WHERE username = 'DEMO_USER';
    IF l_exists = 0 THEN
        EXECUTE IMMEDIATE 'CREATE USER demo_user IDENTIFIED BY "demo_password" QUOTA UNLIMITED ON USERS';
    END IF;
END;
/

GRANT CREATE SESSION TO demo_user;
GRANT CREATE TABLE TO demo_user;
GRANT CREATE VIEW TO demo_user;
GRANT CREATE PROCEDURE TO demo_user;
GRANT CREATE TRIGGER TO demo_user;
GRANT CREATE SEQUENCE TO demo_user;
GRANT UNLIMITED TABLESPACE TO demo_user;
""";

    private static string DemoSchemaSql() => """
ALTER SESSION SET CONTAINER = FREEPDB1;
ALTER SESSION SET CURRENT_SCHEMA = demo_user;

CREATE TABLE demo_customers (
    customer_id NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    customer_name VARCHAR2(120) NOT NULL,
    email_address VARCHAR2(200) NOT NULL,
    created_at TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL
);

CREATE TABLE demo_products (
    product_id NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    product_name VARCHAR2(120) NOT NULL,
    unit_price NUMBER(10,2) NOT NULL,
    active_flag CHAR(1) DEFAULT 'Y' NOT NULL CHECK (active_flag IN ('Y', 'N'))
);

CREATE TABLE demo_orders (
    order_id NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    customer_id NUMBER NOT NULL REFERENCES demo_customers(customer_id),
    product_id NUMBER NOT NULL REFERENCES demo_products(product_id),
    quantity NUMBER(10) NOT NULL,
    order_total NUMBER(12,2) NOT NULL,
    status_code VARCHAR2(30) DEFAULT 'NEW' NOT NULL,
    created_at TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,
    updated_at TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL
);

MERGE INTO demo_customers target
USING (
    SELECT 'Ada Lovelace' customer_name, 'ada@example.local' email_address FROM dual UNION ALL
    SELECT 'Grace Hopper', 'grace@example.local' FROM dual UNION ALL
    SELECT 'Alan Turing', 'alan@example.local' FROM dual
) source
ON (target.email_address = source.email_address)
WHEN NOT MATCHED THEN
    INSERT (customer_name, email_address)
    VALUES (source.customer_name, source.email_address);

MERGE INTO demo_products target
USING (
    SELECT 'Starter Subscription' product_name, 49.00 unit_price, 'Y' active_flag FROM dual UNION ALL
    SELECT 'Refactoring Workshop', 299.00, 'Y' FROM dual UNION ALL
    SELECT 'Legacy Support Pack', 149.00, 'Y' FROM dual
) source
ON (target.product_name = source.product_name)
WHEN NOT MATCHED THEN
    INSERT (product_name, unit_price, active_flag)
    VALUES (source.product_name, source.unit_price, source.active_flag);

MERGE INTO demo_orders target
USING (
    SELECT 1 customer_id, 1 product_id, 2 quantity, 98.00 order_total, 'NEW' status_code FROM dual UNION ALL
    SELECT 2, 2, 1, 299.00, 'PAID' FROM dual
) source
ON (target.customer_id = source.customer_id AND target.product_id = source.product_id AND target.order_total = source.order_total)
WHEN NOT MATCHED THEN
    INSERT (customer_id, product_id, quantity, order_total, status_code)
    VALUES (source.customer_id, source.product_id, source.quantity, source.order_total, source.status_code);

CREATE OR REPLACE PROCEDURE demo_show_customer (
    p_customer_id IN demo_customers.customer_id%TYPE
) AS
    l_customer_name demo_customers.customer_name%TYPE;
    l_email_address demo_customers.email_address%TYPE;
    l_order_count NUMBER;
BEGIN
    SELECT customer_name,
           email_address
      INTO l_customer_name,
           l_email_address
      FROM demo_customers
     WHERE customer_id = p_customer_id;

    SELECT COUNT(*)
      INTO l_order_count
      FROM demo_orders
     WHERE customer_id = p_customer_id;

    DBMS_OUTPUT.PUT_LINE('Customer: ' || l_customer_name);
    DBMS_OUTPUT.PUT_LINE('Email: ' || l_email_address);
    DBMS_OUTPUT.PUT_LINE('Orders: ' || l_order_count);
EXCEPTION
    WHEN NO_DATA_FOUND THEN
        DBMS_OUTPUT.PUT_LINE('Customer ' || p_customer_id || ' was not found.');
END demo_show_customer;
/

CREATE OR REPLACE TRIGGER demo_orders_biu_trigger
BEFORE INSERT OR UPDATE ON demo_orders
FOR EACH ROW
DECLARE
    l_unit_price demo_products.unit_price%TYPE;
    l_active_flag demo_products.active_flag%TYPE;
BEGIN
    IF :NEW.quantity IS NULL OR :NEW.quantity <= 0 THEN
        RAISE_APPLICATION_ERROR(-20001, 'Quantity must be greater than zero.');
    END IF;

    SELECT unit_price,
           active_flag
      INTO l_unit_price,
           l_active_flag
      FROM demo_products
     WHERE product_id = :NEW.product_id;

    IF l_active_flag <> 'Y' THEN
        RAISE_APPLICATION_ERROR(-20002, 'Orders may only reference active products.');
    END IF;

    :NEW.order_total := ROUND(l_unit_price * :NEW.quantity, 2);
    :NEW.updated_at := SYSTIMESTAMP;

    IF INSERTING AND :NEW.created_at IS NULL THEN
        :NEW.created_at := SYSTIMESTAMP;
    END IF;
END demo_orders_biu_trigger;
/
""";

    private static string SampleQueriesSql() => """
SET SERVEROUTPUT ON

PROMPT === Customers ===
SELECT customer_id, customer_name, email_address
  FROM demo_customers
 ORDER BY customer_id;

PROMPT === Products ===
SELECT product_id, product_name, unit_price, active_flag
  FROM demo_products
 ORDER BY product_id;

PROMPT === Orders ===
SELECT order_id, customer_id, product_id, quantity, order_total, status_code
  FROM demo_orders
 ORDER BY order_id;

PROMPT === Procedure Demo ===
EXEC demo_show_customer(1)

PROMPT === Trigger Demo ===
INSERT INTO demo_orders (customer_id, product_id, quantity, status_code)
VALUES (1, 1, 3, 'NEW');

SELECT order_id, quantity, order_total, updated_at
  FROM demo_orders
 ORDER BY order_id DESC FETCH FIRST 1 ROWS ONLY;

ROLLBACK;
""";

    private static string TutorialQuerySql() => """
SET SERVEROUTPUT ON
SELECT COUNT(*) AS customer_count FROM demo_customers;
EXEC demo_show_customer(1)
""";

    private static string ExplainProcedureSkill() => """
# Oracle Skill: Explain Procedure

Explain this procedure step-by-step.
Describe inputs, outputs, tables used, business logic, exceptions, and possible side effects.
""";

    private static string ExplainTriggerSkill() => """
# Oracle Skill: Explain Trigger

Explain when this trigger executes, what validations it performs, what fields it modifies, and what business rules it enforces.
""";

    private static string DebugProcedureSkill() => """
# Oracle Skill: Debug Procedure

Identify syntax issues, logic issues, possible runtime exceptions, and provide the smallest safe correction.
""";

    private static string RefactorProcedureSkill() => """
# Oracle Skill: Refactor Procedure

Improve readability and maintainability without changing behavior.
""";

    private static string GenerateTestCasesSkill() => """
# Oracle Skill: Generate Test Cases

Generate valid, invalid, and edge-case SQL test scenarios.
""";

    private static string NetworkAdminReadme() => """
## Oracle Network Configuration

Place customer-provided Oracle network files here when staging access is needed later:

- `tnsnames.ora`
- `sqlnet.ora`
- wallet files

The workspace shell exports `TNS_ADMIN=/workspace/.local/oracle/network/admin` when this directory exists.

Treat staging as read-only. Prefer a dedicated read-only database account and avoid any DDL, DML, or PL/SQL execution against staging.
""";

    private static string OracleDemoContextJson()
    {
        var document = new
        {
            kind = "oracle-demo-connection",
            insideWorkspace = new
            {
                host = "oracle-demo",
                port = 1521,
                service = "FREEPDB1",
                username = "demo_user",
                password = "demo_password",
                connectString = "demo_user/demo_password@//oracle-demo:1521/FREEPDB1",
            },
            windowsSqlDeveloper = new
            {
                host = "localhost",
                port = 1521,
                service = "FREEPDB1",
                username = "demo_user",
                password = "demo_password",
            },
            verifyScript = "scripts/verify-oracle-demo.sh",
        };

        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string VerifyOracleDemoScript() => """
#!/usr/bin/env bash
# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES
# Source inputs: workspace.yaml and catalog manifests under catalog/.
# User edits to this file are not preserved. Edit workspace.yaml or catalog manifests instead.

set -euo pipefail

shell_init_path=/opt/opencode-workspace/config/opencode-shell-init.sh
if [ -f "${shell_init_path}" ]; then
  # Load Oracle client and PATH setup when the script is run outside the attach shell.
  . "${shell_init_path}" >/dev/null 2>&1 || true
fi

if ! command -v sqlplus >/dev/null 2>&1; then
  printf 'sqlplus is not available yet. Start Oracle first and wait for provisioning to finish.\n' >&2
  exit 1
fi

sqlplus -S demo_user/demo_password@//oracle-demo:1521/FREEPDB1 <<'EOF'
SELECT 'Connection OK' AS status FROM dual;
SELECT customer_id, customer_name FROM demo_customers ORDER BY customer_id;
SET SERVEROUTPUT ON
EXEC demo_show_customer(1);
EXIT;
EOF
""";

    private static string OpenSqlclScript() => """
$ErrorActionPreference = 'Stop'

function Pause-OnFailure {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Yellow
    Read-Host 'Press Enter to close'
    exit 1
}

$workspaceRoot = $PSScriptRoot
$workspaceName = Split-Path -Leaf $workspaceRoot
$containerName = ($workspaceName.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-') + '-workspace'
$connectionTarget = '//oracle-demo:1521/FREEPDB1'
$connectionString = "demo_user/demo_password@$connectionTarget"
$shellInitPath = '/opt/opencode-workspace/config/opencode-shell-init.sh'

Write-Host "SQLcl target: $connectionTarget"

docker ps --format '{{.Names}}' | Select-String -SimpleMatch $containerName | Out-Null
if ($LASTEXITCODE -ne 0) {
    Pause-OnFailure "The Oracle demo workspace is not running. Start Oracle from the Oracle Demo Database panel first."
}

docker exec --user opencode -w /workspace $containerName sh -lc ". $shellInitPath >/dev/null 2>&1; command -v sql >/dev/null 2>&1"
if ($LASTEXITCODE -ne 0) {
    Pause-OnFailure "SQLcl is not ready yet. Start Oracle with internet access so provisioning can finish downloading SQLcl."
}

docker exec --user opencode -w /workspace $containerName sh -lc ". $shellInitPath >/dev/null 2>&1; sql -v"
if ($LASTEXITCODE -ne 0) {
    Pause-OnFailure "SQLcl version check failed inside the workspace container."
}

docker exec -it --user opencode -w /workspace $containerName sh -lc ". $shellInitPath >/dev/null 2>&1; exec sql '$connectionString'"
if ($LASTEXITCODE -ne 0) {
    Pause-OnFailure "SQLcl could not open the Oracle demo connection."
}
""";

    private static string TestConnectionScript() => """
$ErrorActionPreference = 'Stop'

function Pause-OnFailure {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Yellow
    Read-Host 'Press Enter to close'
    exit 1
}

$workspaceRoot = $PSScriptRoot
$workspaceName = Split-Path -Leaf $workspaceRoot
$containerName = ($workspaceName.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-') + '-workspace'
$connectionTarget = '//oracle-demo:1521/FREEPDB1'
$connectionString = "demo_user/demo_password@$connectionTarget"
$sqlclScriptPath = '/workspace/tutorial/oracle/scripts/tutorial-query.sql'
$sqlplusScriptPath = '/tmp/sqlplus-demo-check.sql'
$shellInitPath = '/opt/opencode-workspace/config/opencode-shell-init.sh'

Write-Host "Oracle target: $connectionTarget"

docker ps --format '{{.Names}}' | Select-String -SimpleMatch $containerName | Out-Null
if ($LASTEXITCODE -ne 0) {
    Pause-OnFailure "The Oracle demo workspace is not running. Start Oracle from the Oracle Demo Database panel first."
}

docker exec --user opencode -w /workspace $containerName sh -lc ". $shellInitPath >/dev/null 2>&1; command -v sqlplus >/dev/null 2>&1"
if ($LASTEXITCODE -eq 0) {
    docker exec --user opencode -w /workspace $containerName sh -lc ". $shellInitPath >/dev/null 2>&1; sqlplus -v"
    if ($LASTEXITCODE -ne 0) {
        Pause-OnFailure "SQL*Plus is present but the version check failed inside the workspace container."
    }

    docker exec --user opencode -w /workspace $containerName sh -lc "cat > $sqlplusScriptPath <<'SQL'
SELECT 'Connection OK' AS status FROM dual;
SELECT customer_id, customer_name FROM demo_customers ORDER BY customer_id;
SET SERVEROUTPUT ON
EXEC demo_show_customer(1);
EXIT;
SQL"

    docker exec --user opencode -w /workspace $containerName sh -lc ". $shellInitPath >/dev/null 2>&1; sqlplus -S '$connectionString' @$sqlplusScriptPath"
    if ($LASTEXITCODE -ne 0) {
        Pause-OnFailure "SQL*Plus failed while running the demo verification query."
    }

    Write-Host 'SQL*Plus demo verification completed successfully.'
    exit 0
}

docker exec --user opencode -w /workspace $containerName sh -lc ". $shellInitPath >/dev/null 2>&1; command -v sql >/dev/null 2>&1"
if ($LASTEXITCODE -ne 0) {
    Pause-OnFailure "Neither SQL*Plus nor SQLcl is ready yet. Start Oracle with internet access so provisioning can finish."
}

docker exec --user opencode -w /workspace $containerName sh -lc ". $shellInitPath >/dev/null 2>&1; sql -v"
if ($LASTEXITCODE -ne 0) {
    Pause-OnFailure "SQLcl version check failed inside the workspace container."
}

docker exec --user opencode -w /workspace $containerName sh -lc ". $shellInitPath >/dev/null 2>&1; sql -S '$connectionString' @$sqlclScriptPath"
if ($LASTEXITCODE -ne 0) {
    Pause-OnFailure "SQLcl failed while running the tutorial query script."
}

Write-Host 'SQLcl tutorial query completed successfully.'
""";

    private static string RunTutorialQueryScript() => TestConnectionScript();

    private static string StartOpenCodeOracleDemoScript() => """
$ErrorActionPreference = 'Stop'

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$attachScript = Join-Path $workspaceRoot 'attach-workspace.ps1'

if (-not (Test-Path $attachScript)) {
    throw "Expected attach script was not found at $attachScript"
}

& $attachScript
exit $LASTEXITCODE
""";
}
