using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceServiceCatalogContext
{
    public required WorkspaceDefinition Definition { get; init; }
    public WorkspaceRuntimeStateRecord? RuntimeState { get; init; }
    public string WorkspaceRootPath { get; init; } = string.Empty;
}

public interface IWorkspaceServiceInfoProvider
{
    bool CanHandle(WorkspaceServiceCatalogContext context);
    IReadOnlyList<WorkspaceServiceInfo> Build(WorkspaceServiceCatalogContext context);
}

public static class WorkspaceServiceCatalog
{
    public const string ServicesGuideRelativePath = "docs/workspace-services.md";
    public const string OracleApexWorkflowGuideRelativePath = "docs/oracle-apex-workflow.md";
    public const string OracleApexDiagnosticsRelativePath = "docs/diagnostics/oracle-apex.md";

    private static readonly IWorkspaceServiceInfoProvider[] Providers =
    [
        new GenericWorkspaceServiceInfoProvider(),
        new OracleWorkspaceServiceInfoProvider(),
        new PostgreSqlWorkspaceServiceInfoProvider(),
        new AnalyticsWorkspaceServiceInfoProvider(),
    ];

    public static IReadOnlyList<WorkspaceServiceInfo> Build(WorkspaceSnapshot snapshot)
        => Build(new WorkspaceServiceCatalogContext
        {
            Definition = snapshot.Definition,
            RuntimeState = snapshot.LocalRuntimeState,
            WorkspaceRootPath = snapshot.Paths.RootPath,
        });

    public static IReadOnlyList<WorkspaceServiceInfo> Build(WorkspaceDefinition definition, WorkspaceRuntimeStateRecord? runtimeState = null, string? workspaceRootPath = null)
        => Build(new WorkspaceServiceCatalogContext
        {
            Definition = definition,
            RuntimeState = runtimeState,
            WorkspaceRootPath = workspaceRootPath ?? string.Empty,
        });

    public static string BuildMarkdown(WorkspaceDefinition definition, WorkspaceRuntimeStateRecord? runtimeState = null, string? workspaceRootPath = null)
    {
        var services = Build(definition, runtimeState, workspaceRootPath);
        var lines = new List<string>
        {
            "# Available Services",
            string.Empty,
            "What can you use now from this workspace.",
            string.Empty,
            "| Service | Open / Command | Credentials |",
            "| --- | --- | --- |",
        };

        foreach (var service in services)
        {
            lines.Add($"| {EscapeTable(service.Name)} | {EscapeTable(BuildOpenOrCommandValue(service))} | {EscapeTable(BuildCredentialsValue(service))} |");
        }

        lines.Add(string.Empty);
        foreach (var service in services)
        {
            lines.Add($"## {service.Name}");
            lines.Add(string.IsNullOrWhiteSpace(service.Description) ? service.Category : service.Description);
            lines.Add(string.Empty);

            if (!string.IsNullOrWhiteSpace(service.HostUrl))
            {
                lines.Add($"- Host: `{service.HostUrl}`");
            }

            if (!string.IsNullOrWhiteSpace(service.InternalUrl))
            {
                lines.Add($"- Internal: `{service.InternalUrl}`");
            }

            foreach (var command in service.Commands)
            {
                lines.Add($"- {command.Label}: `{command.Command}`");
            }

            if (!string.IsNullOrWhiteSpace(service.Credentials))
            {
                lines.Add($"- Credentials: {service.Credentials}");
            }

            lines.Add(string.Empty);
        }

        return string.Join("\n", lines);
    }

    private static IReadOnlyList<WorkspaceServiceInfo> Build(WorkspaceServiceCatalogContext context)
        => Providers
            .Where(provider => provider.CanHandle(context))
            .SelectMany(provider => provider.Build(context))
            .GroupBy(service => service.ServiceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string BuildOpenOrCommandValue(WorkspaceServiceInfo service)
    {
        if (!string.IsNullOrWhiteSpace(service.HostUrl))
        {
            return service.HostUrl;
        }

        var command = service.Commands.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Command));
        return command?.Command ?? "Open Workspace";
    }

    private static string BuildCredentialsValue(WorkspaceServiceInfo service)
        => string.IsNullOrWhiteSpace(service.Credentials) ? "-" : service.Credentials.Replace("\r\n", "; ", StringComparison.Ordinal).Replace("\n", "; ", StringComparison.Ordinal);

    private static string EscapeTable(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r\n", "<br />", StringComparison.Ordinal).Replace("\n", "<br />", StringComparison.Ordinal);

    private sealed class GenericWorkspaceServiceInfoProvider : IWorkspaceServiceInfoProvider
    {
        public bool CanHandle(WorkspaceServiceCatalogContext context) => true;

        public IReadOnlyList<WorkspaceServiceInfo> Build(WorkspaceServiceCatalogContext context)
        {
            var folderPath = string.IsNullOrWhiteSpace(context.WorkspaceRootPath) ? "." : context.WorkspaceRootPath;
            return
            [
                new WorkspaceServiceInfo
                {
                    ServiceId = "development-shell",
                    Name = "Development Shell",
                    Category = "Terminal",
                    Description = "Open the managed workspace terminal.",
                    InternalUrl = "/opt/opencode-workspace/config/opencode-workspace-shell.sh",
                    DocsPath = ServicesGuideRelativePath,
                    Actions = ["open-service", "open-docs"],
                },
                new WorkspaceServiceInfo
                {
                    ServiceId = "repo-folder",
                    Name = "Repository Folder",
                    Category = "Folder",
                    Description = "Open the durable workspace files on the host.",
                    HostUrl = folderPath,
                    DocsPath = ServicesGuideRelativePath,
                    Actions = ["open-service", "copy-url", "open-docs"],
                },
                new WorkspaceServiceInfo
                {
                    ServiceId = "opencode-cli",
                    Name = "OpenCode CLI",
                    Category = "CLI",
                    Description = "Use OpenCode from the workspace shell.",
                    DocsPath = ServicesGuideRelativePath,
                    Commands =
                    [
                        new WorkspaceServiceCommandInfo
                        {
                            Label = "CLI",
                            Command = "opencode",
                            Description = "Launch OpenCode inside the workspace shell.",
                        },
                    ],
                    Actions = ["copy-command", "open-docs"],
                },
            ];
        }
    }

    private sealed class OracleWorkspaceServiceInfoProvider : IWorkspaceServiceInfoProvider
    {
        public bool CanHandle(WorkspaceServiceCatalogContext context)
            => OracleWorkspaceFamily.IsOracleWorkspace(context.Definition);

        public IReadOnlyList<WorkspaceServiceInfo> Build(WorkspaceServiceCatalogContext context)
        {
            var definition = context.Definition;
            var services = new List<WorkspaceServiceInfo>
            {
                new()
                {
                    ServiceId = "oracle-database",
                    Name = "Oracle Database",
                    Category = "Database",
                    Description = "Primary Oracle listener for SQLcl and SQL*Plus.",
                    HostUrl = WorkspaceRuntimeResourceCatalog.ResolveServiceEndpoint(definition, context.RuntimeState, "oracle-database"),
                    InternalUrl = "tcp://oracle-demo:1521",
                    Credentials = "Demo user: demo_user / demo_password\nSYS: sys / change-on-first-demo as sysdba",
                    DocsPath = ServicesGuideRelativePath,
                    Commands =
                    [
                        new WorkspaceServiceCommandInfo
                        {
                            Label = "SQLcl",
                            Command = "sql demo_user/demo_password@//oracle-demo:1521/FREEPDB1",
                            Description = "Connect with the demo user from the workspace shell.",
                        },
                    ],
                    Actions = ["copy-url", "copy-credentials", "copy-command", "open-docs"],
                },
                new()
                {
                    ServiceId = "sqlcl",
                    Name = "SQLcl",
                    Category = "SQL Client",
                    Description = "Command-line Oracle client inside the workspace runtime.",
                    Credentials = "demo_user / demo_password",
                    DocsPath = ServicesGuideRelativePath,
                    Commands =
                    [
                        new WorkspaceServiceCommandInfo
                        {
                            Label = "Host helper",
                            Command = "./open-sqlcl.ps1",
                            Description = "Open SQLcl from the host for this workspace.",
                        },
                        new WorkspaceServiceCommandInfo
                        {
                            Label = "Shell command",
                            Command = "sql demo_user/demo_password@//oracle-demo:1521/FREEPDB1",
                            Description = "Connect from the workspace shell.",
                        },
                    ],
                    Actions = ["copy-credentials", "copy-command", "open-docs"],
                },
            };

            if (OracleWorkspaceFamily.HasApex(definition))
            {
                var ordsPort = WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(definition, context.RuntimeState, WorkspaceRuntimeResourceCatalog.OracleOrdsResourceId);
                var defaultEnvironmentName = string.IsNullOrWhiteSpace(definition.Oracle.Apex.DefaultEnvironment)
                    ? definition.Oracle.Apex.Environments.Keys.FirstOrDefault()
                    : definition.Oracle.Apex.DefaultEnvironment;
                definition.Oracle.Apex.Environments.TryGetValue(defaultEnvironmentName ?? string.Empty, out var defaultEnvironment);
                services.AddRange(
                [
                    new WorkspaceServiceInfo
                    {
                        ServiceId = "apex-builder",
                        Name = "APEX Builder",
                        Category = "Admin UI",
                        Description = "Oracle APEX application builder.",
                        HostUrl = $"http://localhost:{ordsPort}/ords/apex",
                        InternalUrl = "http://oracle-ords:8080/ords/apex",
                        Credentials = "Workspace: INTERNAL\nUsername: ADMIN\nPassword: change-on-first-demo",
                        DocsPath = ServicesGuideRelativePath,
                        Actions = ["open-service", "copy-url", "copy-credentials", "open-docs"],
                    },
                    new WorkspaceServiceInfo
                    {
                        ServiceId = "ords-landing",
                        Name = "ORDS Landing",
                        Category = "Web App",
                        Description = "Oracle REST Data Services landing page.",
                        HostUrl = $"http://localhost:{ordsPort}/ords/_/landing",
                        InternalUrl = "http://oracle-ords:8080/ords/_/landing",
                        DocsPath = ServicesGuideRelativePath,
                        Actions = ["open-service", "copy-url", "open-docs"],
                    },
                ]);

                if (defaultEnvironment?.ApplicationId is > 0)
                {
                    services.Add(new WorkspaceServiceInfo
                    {
                        ServiceId = "apex-app-home",
                        Name = "App Home",
                        Category = "Application",
                        Description = "Open the configured Oracle APEX preview application.",
                        HostUrl = $"http://localhost:{ordsPort}/ords/f?p={defaultEnvironment.ApplicationId.Value}",
                        InternalUrl = $"http://oracle-ords:8080/ords/f?p={defaultEnvironment.ApplicationId.Value}",
                        DocsPath = OracleApexWorkflowGuideRelativePath,
                        Actions = ["open-service", "copy-url", "open-docs"],
                    });
                }

                services.AddRange(
                [
                    new WorkspaceServiceInfo
                    {
                        ServiceId = "apex-sql-workshop",
                        Name = "SQL Workshop",
                        Category = "Admin UI",
                        Description = "Open Oracle APEX SQL Workshop.",
                        HostUrl = $"http://localhost:{ordsPort}/ords/apex/sql-workshop",
                        InternalUrl = "http://oracle-ords:8080/ords/apex/sql-workshop",
                        DocsPath = OracleApexWorkflowGuideRelativePath,
                        Actions = ["open-service", "copy-url", "open-docs"],
                    },
                    new WorkspaceServiceInfo
                    {
                        ServiceId = "apex-rest-workshop",
                        Name = "REST Workshop",
                        Category = "Admin UI",
                        Description = "Open Oracle APEX REST Workshop when ORDS workspace services are available.",
                        HostUrl = $"http://localhost:{ordsPort}/ords/apex/workspace-developer/restful-services",
                        InternalUrl = "http://oracle-ords:8080/ords/apex/workspace-developer/restful-services",
                        DocsPath = OracleApexWorkflowGuideRelativePath,
                        Actions = ["open-service", "copy-url", "open-docs"],
                    },
                    new WorkspaceServiceInfo
                    {
                        ServiceId = "sqlcl-terminal",
                        Name = "SQLcl Terminal",
                        Category = "Terminal",
                        Description = "Open SQLcl from the host using the workspace helper.",
                        DocsPath = OracleApexWorkflowGuideRelativePath,
                        Commands =
                        [
                            new WorkspaceServiceCommandInfo
                            {
                                Label = "Host helper",
                                Command = "./open-sqlcl.ps1",
                                Description = "Open SQLcl from the host for this workspace.",
                            },
                        ],
                        Actions = ["copy-command", "open-docs"],
                    },
                    new WorkspaceServiceInfo
                    {
                        ServiceId = "oracle-apex-diagnostics",
                        Name = "Oracle Diagnostics",
                        Category = "Documentation",
                        Description = "Open the generated Oracle APEX diagnostics report for this workspace.",
                        DocsPath = OracleApexDiagnosticsRelativePath,
                        Actions = ["open-docs"],
                    },
                ]);

                if (definition.Oracle.Apex.Environments.Count > 0)
                {
                    services.Add(new WorkspaceServiceInfo
                    {
                        ServiceId = "apex-synchronization",
                        Name = "APEX Synchronization",
                        Category = "Application",
                        Description = "Validate, import, export, pull, and push Oracle APEX workspace state.",
                        DocsPath = OracleApexWorkflowGuideRelativePath,
                        Commands =
                        [
                            new WorkspaceServiceCommandInfo
                            {
                                Label = "Source",
                                Command = defaultEnvironment?.SourcePath ?? "src/apex",
                                Description = "Repository path used for Oracle APEX source synchronization.",
                            },
                        ],
                        Actions = ["copy-command", "open-docs"],
                    });
                }
            }

            return services;
        }
    }

    private sealed class PostgreSqlWorkspaceServiceInfoProvider : IWorkspaceServiceInfoProvider
    {
        public bool CanHandle(WorkspaceServiceCatalogContext context)
            => context.Definition.Services.Contains("postgres", StringComparer.OrdinalIgnoreCase)
                || context.Definition.Services.Contains("pgadmin", StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<WorkspaceServiceInfo> Build(WorkspaceServiceCatalogContext context)
        {
            var definition = context.Definition;
            var services = new List<WorkspaceServiceInfo>();
            if (definition.Services.Contains("postgres", StringComparer.OrdinalIgnoreCase))
            {
                services.Add(new WorkspaceServiceInfo
                {
                    ServiceId = "postgres",
                    Name = "PostgreSQL",
                    Category = "Database",
                    Description = "PostgreSQL database endpoint.",
                    HostUrl = WorkspaceRuntimeResourceCatalog.ResolveServiceEndpoint(definition, context.RuntimeState, "postgres"),
                    InternalUrl = "tcp://postgres:5432",
                    DocsPath = ServicesGuideRelativePath,
                    Actions = ["copy-url", "open-docs"],
                });
            }

            if (definition.Services.Contains("pgadmin", StringComparer.OrdinalIgnoreCase))
            {
                services.Add(new WorkspaceServiceInfo
                {
                    ServiceId = "pgadmin",
                    Name = "pgAdmin",
                    Category = "Admin UI",
                    Description = "Browser-based PostgreSQL administration.",
                    HostUrl = WorkspaceRuntimeResourceCatalog.ResolveServiceOpenUrl(definition, context.RuntimeState, "pgadmin"),
                    InternalUrl = "http://pgadmin:80/",
                    DocsPath = ServicesGuideRelativePath,
                    Actions = ["open-service", "copy-url", "open-docs"],
                });
            }

            return services;
        }
    }

    private sealed class AnalyticsWorkspaceServiceInfoProvider : IWorkspaceServiceInfoProvider
    {
        public bool CanHandle(WorkspaceServiceCatalogContext context)
            => context.Definition.Features.Contains("analytics-reporting", StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<WorkspaceServiceInfo> Build(WorkspaceServiceCatalogContext context)
        {
            var reportsPath = string.IsNullOrWhiteSpace(context.WorkspaceRootPath)
                ? Path.Combine("reports")
                : Path.Combine(context.WorkspaceRootPath, "reports");
            return
            [
                new WorkspaceServiceInfo
                {
                    ServiceId = "marimo",
                    Name = "Marimo",
                    Category = "Notebook",
                    Description = "Interactive analytics notebook app.",
                    HostUrl = WorkspaceRuntimeResourceCatalog.ResolveServiceOpenUrl(context.Definition, context.RuntimeState, "marimo"),
                    InternalUrl = $"http://workspace:{AnalyticsWorkspaceSettings.ContainerMarimoPort}/",
                    DocsPath = ServicesGuideRelativePath,
                    Commands =
                    [
                        new WorkspaceServiceCommandInfo
                        {
                            Label = "Notebook",
                            Command = "marimo edit examples/analytics/analysis.py --host 0.0.0.0 --port ${MARIMO_PORT}",
                            Description = "Start the generated analytics notebook manually.",
                        },
                    ],
                    Actions = ["open-service", "copy-url", "copy-command", "open-docs"],
                },
                new WorkspaceServiceInfo
                {
                    ServiceId = "reports-folder",
                    Name = "Reports Folder",
                    Category = "Folder",
                    Description = "Generated analytics and publishing outputs.",
                    HostUrl = reportsPath,
                    DocsPath = ServicesGuideRelativePath,
                    Actions = ["open-service", "copy-url", "open-docs"],
                },
            ];
        }
    }
}
