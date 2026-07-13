using System.Reflection;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexStaticValidationTests
{
    [Fact]
    public void CatalogTemplates_ResolveExpectedOracleIntegrity()
    {
        var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        var templates = provider.LoadTemplates();

        var plsql = templates.Single(template => template.Id == "oracle-plsql-demo");
        var apex = templates.Single(template => template.Id == "oracle-apex-demo");
        var apexlang = templates.Single(template => template.Id == "oracle-apexlang-demo");

        Assert.Equal(OracleWorkspaceKind.PlSql, OracleWorkspaceFamily.Detect(plsql));
        Assert.Equal(OracleWorkspaceKind.Apex, OracleWorkspaceFamily.Detect(apex));
        Assert.Equal(OracleWorkspaceKind.ApexLang, OracleWorkspaceFamily.Detect(apexlang));

        Assert.DoesNotContain("oracle-ords", plsql.Services);
        Assert.DoesNotContain("oracle-apex-demo", plsql.Features);
        Assert.DoesNotContain("oracle-apexlang-demo", plsql.Features);

        Assert.Contains("oracle-demo", apex.Features);
        Assert.Contains("oracle-apex-demo", apex.Features);
        Assert.Contains("oracle-ords", apex.Services);

        Assert.Contains("oracle-demo", apexlang.Features);
        Assert.Contains("oracle-apex-demo", apexlang.Features);
        Assert.Contains("oracle-ords", apexlang.Services);
        Assert.Contains("oracle-apexlang-demo", apexlang.Features);
    }

    [Fact]
    public void OracleTemplates_EncodeApexlangExtendsApexSemantics()
    {
        var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        var templates = provider.LoadTemplates();

        var plsql = templates.Single(template => template.Id == "oracle-plsql-demo");
        var apex = templates.Single(template => template.Id == "oracle-apex-demo");
        var apexlang = templates.Single(template => template.Id == "oracle-apexlang-demo");

        Assert.Equal(["core", "oracle-demo"], plsql.Features);
        Assert.Equal(["oracle-demo"], plsql.Services);

        Assert.Contains("oracle-demo", apex.Features);
        Assert.Contains("oracle-apex-demo", apex.Features);
        Assert.Equal(["oracle-demo", "oracle-ords"], apex.Services);

        Assert.Contains("oracle-demo", apexlang.Features);
        Assert.Contains("oracle-apex-demo", apexlang.Features);
        Assert.Contains("oracle-apexlang-demo", apexlang.Features);
        Assert.Equal(["oracle-demo", "oracle-ords"], apexlang.Services);

        Assert.Contains("runtime", apex.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runtime", apexlang.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OracleApexDemo_GeneratesExpectedFilesWithoutApexApplicationSource()
    {
        var snapshot = CreateWorkspaceFromTemplate("oracle-apex-demo", "oracle-apex-static");

        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-apex-demo.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-samples.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "team-onboarding.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "troubleshooting", "workspace-sessions.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-lifecycle-workflows.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "sharing-oracle-workspaces.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-documentation-strategy.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-documentation-discovery.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-tools", "README.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-tools", "ords.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-knowledge-map.yaml")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-apex-index.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-apex-books.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-apex-api-reference.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-apex-administration.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-apex-installation.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-apex-release-notes.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-apex-version-archives.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-apex-api-map.yaml")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-apex-api-packages.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-ords-index.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-plsql-index.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-database-index.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "skills", "oracle", "apex.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "skills", "oracle", "ords.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "skills", "oracle", "plsql.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "skills", "oracle", "database.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "update-oracle-doc-index.ps1")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "update-oracle-doc-index.sh")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "update-oracle-navigation-index.ps1")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "update-oracle-navigation-index.sh")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "tutorial", "oracle", "init", "03-customers-schema.sql")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "tutorial", "oracle", "init", "04-customers-sample-data.sql")));
        Assert.False(File.Exists(Path.Combine(snapshot.Paths.RootPath, "apex", "application.apx")));

        var schemaSql = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "tutorial", "oracle", "init", "03-customers-schema.sql"));
        Assert.Contains("DEMO_CUSTOMERS", schemaSql.ToUpperInvariant());
        Assert.Contains("DEMO_PRODUCTS", schemaSql.ToUpperInvariant());
        Assert.Contains("DEMO_ORDERS", schemaSql.ToUpperInvariant());
        Assert.Contains("DEMO_ORDER_SUMMARY_V", schemaSql.ToUpperInvariant());
        Assert.Contains("DEMO_CUSTOMER_API", schemaSql.ToUpperInvariant());
    }

    [Fact]
    public void OracleApexLangDemo_GeneratesExpectedFilesAndScripts()
    {
        var snapshot = CreateWorkspaceFromTemplate("oracle-apexlang-demo", "oracle-apexlang-static");

        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-apexlang-demo.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "apexlang-introduction.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-tools", "apexlang.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-tools", "apexlang-hello-world.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "apex", "application.apx")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "export-apex.sh")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "import-apex.sh")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "validate-apex.sh")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "sqlcl.sh")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "apexlang-hello-world.sh")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "apexlang-hello-world.ps1")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "open-sqlcl.ps1")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "sql", "hello-apexlang", "generate-hello-apexlang.sql")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "sql", "hello-apexlang", "validate-hello-apexlang.sql")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "sql", "hello-apexlang", "import-hello-apexlang.sql")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "sql", "hello-apexlang", "export-hello-apexlang.sql")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "team-onboarding.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "troubleshooting", "workspace-sessions.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-lifecycle-workflows.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "sharing-oracle-workspaces.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-documentation-discovery.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-apexlang-index.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-apexlang-navigation.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-knowledge-map.yaml")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-apex-api-map.yaml")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "reference", "oracle-apex-api-packages.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "skills", "oracle", "apexlang.md")));

        AssertScriptLooksValid(Path.Combine(snapshot.Paths.RootPath, "scripts", "export-apex.sh"), "apex export", "scripts/sqlcl.sh");
        AssertScriptLooksValid(Path.Combine(snapshot.Paths.RootPath, "scripts", "import-apex.sh"), "apex import", "scripts/sqlcl.sh");
        AssertScriptLooksValid(Path.Combine(snapshot.Paths.RootPath, "scripts", "validate-apex.sh"), "Validated", null);
        AssertScriptLooksValid(Path.Combine(snapshot.Paths.RootPath, "scripts", "sqlcl.sh"), "/opt/sqlcl/sqlcl", "exec \"${sqlcl_bin}\"");
        AssertScriptLooksValid(Path.Combine(snapshot.Paths.RootPath, "scripts", "apexlang-hello-world.sh"), "Hello APEXlang", "scripts/sqlcl.sh");

        var helloWorldScript = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "scripts", "apexlang-hello-world.sh"));
        Assert.Contains("title: Hello from APEXlang", helloWorldScript, StringComparison.Ordinal);
        Assert.Contains("p_primary_schema => 'TESTSCHEMA'", helloWorldScript, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlCli.class", helloWorldScript, StringComparison.Ordinal);

        var generateSql = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "sql", "hello-apexlang", "generate-hello-apexlang.sql"));
        var importSql = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "sql", "hello-apexlang", "import-hello-apexlang.sql"));
        var exportSql = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "sql", "hello-apexlang", "export-hello-apexlang.sql"));
        Assert.Contains("apex generate -workspace TEST -schema TESTSCHEMA -id 101 -name \"Hello APEXlang\"", generateSql, StringComparison.Ordinal);
        Assert.Contains("apex import -workspace TEST -schema TESTSCHEMA -id 101 -name \"Hello APEXlang\"", importSql, StringComparison.Ordinal);
        Assert.Contains("apex export -applicationid 101 -split -exptype APEXLANG", exportSql, StringComparison.Ordinal);

        var openSqlclScript = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "open-sqlcl.ps1"));
        Assert.Contains("/workspace/scripts/sqlcl.sh", openSqlclScript, StringComparison.Ordinal);
        Assert.DoesNotContain("exec sql '", openSqlclScript, StringComparison.Ordinal);
    }

    [Fact]
    public void OracleApexLangHelloWorldDocs_DoNotContainSecretsOrSessionIds()
    {
        var snapshot = CreateWorkspaceFromTemplate("oracle-apexlang-demo", "oracle-apexlang-hello-docs");
        var docPath = Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-tools", "apexlang-hello-world.md");
        var doc = File.ReadAllText(docPath);

        Assert.Contains("scripts/apexlang-hello-world.sh", doc, StringComparison.Ordinal);
        Assert.Contains("scripts/sqlcl.sh -version", doc, StringComparison.Ordinal);
        Assert.DoesNotContain("demo_password", doc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("change-on-first-demo", doc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SESSION=", doc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("APEX_PUBLIC_USER", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OracleComposeAndEnvironment_WireDatabaseAndOrdsForApexVariants()
    {
        var apexSnapshot = CreateWorkspaceFromTemplate("oracle-apex-demo", "oracle-apex-compose");
        var apexLangSnapshot = CreateWorkspaceFromTemplate("oracle-apexlang-demo", "oracle-apexlang-compose");

        var apexCompose = File.ReadAllText(apexSnapshot.Paths.ComposePath);
        var apexLangCompose = File.ReadAllText(apexLangSnapshot.Paths.ComposePath);
        var apexEnv = File.ReadAllText(apexSnapshot.Paths.EnvironmentFilePath);
        var apexState = new WorkspaceRuntimeStateService().Read(apexSnapshot.Paths.RuntimeStatePath);
        var plsqlSnapshot = CreateWorkspaceFromTemplate("oracle-plsql-demo", "oracle-plsql-compose");
        var plsqlCompose = File.ReadAllText(plsqlSnapshot.Paths.ComposePath);

        Assert.NotNull(apexState);
        var apexDbPort = WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(apexSnapshot.Definition, apexState, WorkspaceRuntimeResourceCatalog.OracleDatabaseResourceId);
        var apexOrdsPort = WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(apexSnapshot.Definition, apexState, WorkspaceRuntimeResourceCatalog.OracleOrdsResourceId);

        Assert.Contains("oracle-demo:", apexCompose);
        Assert.Contains("oracle-ords:", apexCompose);
        Assert.Contains("${ORACLE_HOST_PORT}:1521", apexCompose);
        Assert.Contains("${ORACLE_ORDS_PORT}:8080", apexCompose);
        Assert.Contains("env_file:", apexCompose);
        Assert.Contains("- .env", apexCompose);
        Assert.Contains("ORACLE_ADMIN_USER: \"${ORACLE_ADMIN_USER}\"", apexCompose);
        Assert.Contains("ORACLE_PASSWORD: \"${ORACLE_PASSWORD}\"", apexCompose);
        Assert.Contains("ORACLE_HOST: \"${ORACLE_HOST}\"", apexCompose);
        Assert.Contains("ORACLE_PORT: \"${ORACLE_PORT}\"", apexCompose);
        Assert.Contains("ORACLE_SERVICE_NAME: \"${ORACLE_SERVICE_NAME}\"", apexCompose);
        Assert.Contains("ORACLE_ORDS_PUBLIC_USER: \"${ORACLE_ORDS_PUBLIC_USER}\"", apexCompose);
        Assert.Contains("ORACLE_ORDS_PUBLIC_PASSWORD: \"${ORACLE_ORDS_PUBLIC_PASSWORD}\"", apexCompose);
        Assert.Contains("/mounts/config/ords:/etc/ords/config", apexCompose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("entrypoint:", apexCompose);
        Assert.Contains("- \"bash\"", apexCompose);
        Assert.Contains("- \"-lc\"", apexCompose);
        Assert.Contains("- \"bash /etc/ords/config/init-ords-config.sh\"", apexCompose);
        Assert.Contains("http://localhost:8080/ords/_/landing", apexCompose);
        Assert.DoesNotContain("DBHOST:", apexCompose);
        Assert.DoesNotContain("DBPORT:", apexCompose);
        Assert.DoesNotContain("DBSERVICENAME:", apexCompose);
        Assert.DoesNotContain("ORACLE_PWD:", apexCompose);
        Assert.Contains("oracle-demo:", apexLangCompose);
        Assert.Contains("oracle-ords:", apexLangCompose);
        Assert.Contains("ORACLE_ADMIN_USER: \"${ORACLE_ADMIN_USER}\"", apexLangCompose);
        Assert.Contains("/mounts/config/ords:/etc/ords/config", apexLangCompose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("http://localhost:8080/ords/_/landing", apexLangCompose);
        Assert.Contains($"ORACLE_HOST_PORT={apexDbPort}", apexEnv);
        Assert.Contains("ORACLE_HOST=oracle-demo", apexEnv);
        Assert.Contains("ORACLE_PORT=1521", apexEnv);
        Assert.Contains("ORACLE_SERVICE_NAME=FREEPDB1", apexEnv);
        Assert.Contains("ORACLE_ADMIN_USER=SYS", apexEnv);
        Assert.Contains($"ORACLE_ORDS_PORT={apexOrdsPort}", apexEnv);
        Assert.Contains($"ORACLE_ORDS_BASE_URL=http://localhost:{apexOrdsPort}/ords", apexEnv);
        Assert.Contains("ORACLE_ORDS_INTERNAL_BASE_URL=http://oracle-ords:8080/ords", apexEnv);
        Assert.Contains("ORACLE_ORDS_PUBLIC_USER=ORDS_PUBLIC_USER", apexEnv);
        Assert.Contains($"ORACLE_APEX_LOGIN_URL=http://localhost:{apexOrdsPort}/ords/apex", apexEnv);
        Assert.DoesNotContain("oracle-ords:", plsqlCompose);
        Assert.DoesNotContain("ORACLE_ORDS_PUBLIC_PASSWORD:", plsqlCompose);

        var method = typeof(DockerService).GetMethod("GetComposeProfiles", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var profiles = Assert.IsAssignableFrom<IReadOnlyList<string>>(method!.Invoke(null, new object[] { apexSnapshot.Definition }));
        Assert.Equal(["oracle-apex", "oracle-demo", "oracle-ords"], profiles);
        Assert.Contains("      - oracle-apex", apexCompose);
        Assert.Contains("      - oracle-apex", apexLangCompose);
    }

    [Fact]
    public void OracleApexLangCompose_DependsOnOnlyDefinedServices()
    {
        var snapshot = CreateWorkspaceFromTemplate("oracle-apexlang-demo", "oracle-apexlang-depends-on");
        var compose = File.ReadAllText(snapshot.Paths.ComposePath).Replace("\r\n", "\n", StringComparison.Ordinal);

        var definedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referencedDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? currentService = null;
        var inDependsOn = false;

        foreach (var rawLine in compose.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("  ") && !line.StartsWith("    ") && line.EndsWith(":", StringComparison.Ordinal))
            {
                currentService = line.Trim().TrimEnd(':');
                definedServices.Add(currentService);
                inDependsOn = false;
                continue;
            }

            if (currentService is null)
            {
                continue;
            }

            if (line.Trim() == "depends_on:")
            {
                inDependsOn = true;
                continue;
            }

            if (!inDependsOn)
            {
                continue;
            }

            if (!rawLine.StartsWith("      ", StringComparison.Ordinal))
            {
                inDependsOn = false;
                continue;
            }

            if (rawLine.StartsWith("        ", StringComparison.Ordinal))
            {
                continue;
            }

            var dependency = line.Trim().TrimStart('-').Trim().TrimEnd(':');
            if (!string.IsNullOrWhiteSpace(dependency))
            {
                referencedDependencies.Add(dependency);
            }
        }

        Assert.NotEmpty(referencedDependencies);
        Assert.All(referencedDependencies, dependency => Assert.Contains(dependency, definedServices));
    }

    [Fact]
    public void OracleApexLangGeneratedWorkspace_ContainsManagedOrdsConfigBootstrapScript()
    {
        var snapshot = CreateWorkspaceFromTemplate("oracle-apexlang-demo", "oracle-apexlang-ords-config");
        var bootstrapScriptPath = Path.Combine(snapshot.Paths.RootPath, "mounts", "config", "ords", "init-ords-config.sh");

        Assert.True(File.Exists(bootstrapScriptPath));

        var bootstrapScript = File.ReadAllText(bootstrapScriptPath);
        Assert.Contains("ords --config \"${config_dir}\" install --config-only", bootstrapScript, StringComparison.Ordinal);
        Assert.Contains("--gateway-user \"${ORACLE_APEX_PUBLIC_USER:-APEX_PUBLIC_USER}\"", bootstrapScript, StringComparison.Ordinal);
        Assert.Contains("global/settings.xml", bootstrapScript, StringComparison.Ordinal);
        Assert.Contains("databases/default/pool.xml", bootstrapScript, StringComparison.Ordinal);
    }

    [Fact]
    public void OracleProvisioningScript_ContainsApexStagesOnlyForApexVariants()
    {
        var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
        var expander = new TemplateExpander();
        var generator = new OracleWorkspaceProvisioningScriptGenerator();

        var plsqlScript = generator.Generate(resolver.Resolve(expander.Expand("plsql", provider.LoadTemplates().Single(item => item.Id == "oracle-plsql-demo"))));
        var apexScript = generator.Generate(resolver.Resolve(expander.Expand("apex", provider.LoadTemplates().Single(item => item.Id == "oracle-apex-demo"))));

        Assert.DoesNotContain("oracle_set_stage 'Installing APEX'", plsqlScript);
        Assert.DoesNotContain("oracle_set_stage 'Configuring ORDS'", plsqlScript);

        Assert.Contains("oracle_set_stage 'Provisioning Oracle'", apexScript);
        Assert.Contains("oracle_set_stage 'Installing APEX'", apexScript);
        Assert.Contains("oracle_set_stage 'Configuring ORDS'", apexScript);
        Assert.Contains("oracle_set_stage 'Final Validation'", apexScript);
        Assert.Contains("Workspace provisioning stopped.", apexScript);
        Assert.Contains("Oracle XML Database (XDB) is invalid.", apexScript);
        Assert.DoesNotContain("apex_http_status=$(curl -sS -o /tmp/apex-health-body.txt -w '%{http_code}' \"${oracle_apex_url}\" || true)", apexScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Oracle REST Data Services landing is healthy, but the APEX runtime route is not available.", apexScript, StringComparison.Ordinal);
        Assert.True(apexScript.IndexOf("oracle_set_stage 'Installing APEX'", StringComparison.Ordinal) > apexScript.IndexOf("oracle_set_stage 'Provisioning Oracle'", StringComparison.Ordinal));
        Assert.Contains("Oracle administrator password does not match the running database.", apexScript);
        Assert.Contains("Required pluggable database is not open.", apexScript);
        Assert.Contains("${ORACLE_SERVICE_NAME} open_mode=${pdb_open_mode:-missing}", apexScript);
        Assert.Contains("ORACLE_APEX_MEDIA_DIR", apexScript);
        Assert.DoesNotContain("/ords/apex_admin", apexScript, StringComparison.Ordinal);
        Assert.DoesNotContain(") || true", apexScript, StringComparison.Ordinal);
    }

    [Fact]
    public void OracleDocs_ContainExpectedMessagingAndAvoidUnsupportedClaims()
    {
        var repoRoot = TestPaths.RepositoryRoot;
        var plsqlDoc = File.ReadAllText(Path.Combine(repoRoot, "docs", "oracle-plsql-demo.md"));
        var apexDoc = File.ReadAllText(Path.Combine(repoRoot, "docs", "oracle-apex-demo.md"));
        var apexLangDoc = File.ReadAllText(Path.Combine(repoRoot, "docs", "oracle-apexlang-demo.md"));
        var onboardingArticle = File.ReadAllText(Path.Combine(repoRoot, "docs", "articles", "oracle-onboarding.md"));
        var beyondPlsqlArticle = File.ReadAllText(Path.Combine(repoRoot, "docs", "articles", "beyond-plsql-oracle-apex.md"));
        var lifecycleDoc = File.ReadAllText(Path.Combine(repoRoot, "docs", "oracle-lifecycle-workflows.md"));
        var sharingDoc = File.ReadAllText(Path.Combine(repoRoot, "docs", "sharing-oracle-workspaces.md"));
        var teamOnboardingDoc = File.ReadAllText(Path.Combine(repoRoot, "docs", "team-onboarding.md"));
        var agentsGuideDoc = File.ReadAllText(Path.Combine(repoRoot, "docs", "agents-guide.md"));
        var samplesDoc = File.ReadAllText(Path.Combine(repoRoot, "docs", "oracle-samples.md"));
        var toolsIndexDoc = File.ReadAllText(Path.Combine(repoRoot, "docs", "oracle-tools", "README.md"));

        Assert.Contains("Oracle PL/SQL Demo", plsqlDoc);
        Assert.Contains("Try It Yourself", plsqlDoc);
        Assert.Contains("Oracle APEX Demo", apexDoc);
        Assert.Contains("Oracle APEX Demo extends the Oracle PL/SQL path", apexDoc);
        Assert.Contains(".local/oracle/downloads/apex/", apexDoc);
        Assert.Contains("does not redistribute Oracle APEX ZIP files", apexDoc);
        Assert.Contains("Interactive Report", apexDoc);
        Assert.Contains("Oracle APEXlang Demo", apexLangDoc);
        Assert.Contains(".local/oracle/downloads/apex/", apexLangDoc);
        Assert.Contains("source-controlled Oracle APEX workflow", apexLangDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Open Application Specification Language", apexLangDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Try It Yourself", apexLangDoc);
        Assert.Contains("From Oracle Demo to Oracle Onboarding", onboardingArticle);
        Assert.Contains("Try It Yourself", onboardingArticle);
        Assert.Contains("Beyond PL/SQL", beyondPlsqlArticle);
        Assert.Contains("Export", lifecycleDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Validate", lifecycleDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Git Review", lifecycleDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Import", lifecycleDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source of truth", onboardingArticle, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Run Tutorial", teamOnboardingDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AGENTS.md Guide", agentsGuideDoc);
        Assert.Contains("Workspace Discovered", teamOnboardingDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DEMO_ORDER_SUMMARY_V", samplesDoc);
        Assert.Contains("DEMO_CUSTOMER_API", samplesDoc);
        Assert.Contains("SQLcl", toolsIndexDoc);
        Assert.Contains("Data Pump", toolsIndexDoc);
        Assert.Contains("ORDS", toolsIndexDoc);
        Assert.Contains("APEX Export / Import", toolsIndexDoc);
        Assert.Contains("APEXlang", toolsIndexDoc);
        Assert.Contains("SQL Developer", toolsIndexDoc);
        Assert.Contains("University of Maribor", apexDoc);
        Assert.DoesNotContain("University of Maribor", plsqlDoc);
        Assert.DoesNotContain("University of Maribor", apexLangDoc);
        Assert.Contains("oracle-tools/sqlcl.md", plsqlDoc);
        Assert.Contains("oracle-tools/ords.md", apexDoc);
        Assert.Contains("oracle-tools/apexlang.md", apexLangDoc);

        foreach (var content in new[] { plsqlDoc, apexDoc, apexLangDoc, onboardingArticle, beyondPlsqlArticle, lifecycleDoc, sharingDoc, teamOnboardingDoc, agentsGuideDoc, samplesDoc, toolsIndexDoc })
        {
            Assert.DoesNotContain("already verified", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("runtime validation is complete", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("accepts Oracle license", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("redistribute Oracle binaries", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void OracleToolsDocs_Exist_AndNoReportingTemplateWasAdded()
    {
        var repoRoot = TestPaths.RepositoryRoot;
        var toolDocs = new[]
        {
            Path.Combine(repoRoot, "docs", "oracle-tools", "README.md"),
            Path.Combine(repoRoot, "docs", "oracle-tools", "sqlcl.md"),
            Path.Combine(repoRoot, "docs", "oracle-tools", "data-pump.md"),
            Path.Combine(repoRoot, "docs", "oracle-tools", "ords.md"),
            Path.Combine(repoRoot, "docs", "oracle-tools", "apex-export-import.md"),
            Path.Combine(repoRoot, "docs", "oracle-tools", "apexlang.md"),
            Path.Combine(repoRoot, "docs", "oracle-tools", "sql-developer.md"),
        };

        foreach (var path in toolDocs)
        {
            Assert.True(File.Exists(path), $"Expected tool doc to exist: {path}");
        }

        var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        Assert.DoesNotContain(provider.LoadTemplates(), template => template.Id.Contains("reporting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OracleDocumentationReferences_AndSkills_ExistInRepository()
    {
        var repoRoot = TestPaths.RepositoryRoot;
        var referenceDocs = new[]
        {
            Path.Combine(repoRoot, "docs", "reference", "oracle-apex-index.md"),
            Path.Combine(repoRoot, "docs", "reference", "oracle-apex-books.md"),
            Path.Combine(repoRoot, "docs", "reference", "oracle-apex-api-reference.md"),
            Path.Combine(repoRoot, "docs", "reference", "oracle-apex-administration.md"),
            Path.Combine(repoRoot, "docs", "reference", "oracle-apex-installation.md"),
            Path.Combine(repoRoot, "docs", "reference", "oracle-apex-release-notes.md"),
            Path.Combine(repoRoot, "docs", "reference", "oracle-apex-version-archives.md"),
            Path.Combine(repoRoot, "docs", "reference", "oracle-apexlang-index.md"),
            Path.Combine(repoRoot, "docs", "reference", "oracle-apexlang-navigation.md"),
            Path.Combine(repoRoot, "docs", "reference", "oracle-ords-index.md"),
            Path.Combine(repoRoot, "docs", "reference", "oracle-plsql-index.md"),
            Path.Combine(repoRoot, "docs", "reference", "oracle-database-index.md"),
            Path.Combine(repoRoot, "docs", "reference", "oracle-apex-api-map.yaml"),
            Path.Combine(repoRoot, "docs", "reference", "oracle-apex-api-packages.md"),
            Path.Combine(repoRoot, "docs", "reference", "oracle-knowledge-map.yaml"),
            Path.Combine(repoRoot, "docs", "oracle-documentation-strategy.md"),
            Path.Combine(repoRoot, "docs", "oracle-documentation-discovery.md"),
        };

        var skillDocs = new[]
        {
            Path.Combine(repoRoot, "skills", "oracle", "apex.md"),
            Path.Combine(repoRoot, "skills", "oracle", "apexlang.md"),
            Path.Combine(repoRoot, "skills", "oracle", "ords.md"),
            Path.Combine(repoRoot, "skills", "oracle", "plsql.md"),
            Path.Combine(repoRoot, "skills", "oracle", "database.md"),
        };

        foreach (var path in referenceDocs.Concat(skillDocs))
        {
            Assert.True(File.Exists(path), $"Expected Oracle reference asset to exist: {path}");
        }

        var apexLangIndex = File.ReadAllText(Path.Combine(repoRoot, "docs", "reference", "oracle-apexlang-index.md"));
        var strategy = File.ReadAllText(Path.Combine(repoRoot, "docs", "oracle-documentation-strategy.md"));
        var knowledgeMap = File.ReadAllText(Path.Combine(repoRoot, "docs", "reference", "oracle-knowledge-map.yaml"));
        var apexSkill = File.ReadAllText(Path.Combine(repoRoot, "skills", "oracle", "apex.md"));

        Assert.Contains("https://docs.oracle.com/en/database/oracle/apex/26.1/apxln/", apexLangIndex);
        Assert.Contains("references, not Oracle documentation copies", strategy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id: oracle-knowledge-pack", knowledgeMap);
        Assert.Contains("title: Oracle Knowledge Pack", knowledgeMap);
        Assert.Contains("oracle-apex-api-reference.md", knowledgeMap);
        Assert.Contains("docs/reference/oracle-apex-index.md", apexSkill);
    }

    [Fact]
    public void OracleAgentsGuidance_AndPlaceholderScripts_DescribeReferenceOnlyPolicy()
    {
        var repoRoot = TestPaths.RepositoryRoot;
        var agents = File.ReadAllText(Path.Combine(repoRoot, "AGENTS.md"));
        var ps1 = File.ReadAllText(Path.Combine(repoRoot, "scripts", "update-oracle-doc-index.ps1"));
        var sh = File.ReadAllText(Path.Combine(repoRoot, "scripts", "update-oracle-doc-index.sh"));
        var navPs1 = File.ReadAllText(Path.Combine(repoRoot, "scripts", "update-oracle-navigation-index.ps1"));
        var navSh = File.ReadAllText(Path.Combine(repoRoot, "scripts", "update-oracle-navigation-index.sh"));

        Assert.Contains("## Oracle Documentation Strategy", agents);
        Assert.Contains("### Oracle Documentation Discovery", agents);
        Assert.Contains("docs/reference", agents);
        Assert.Contains("Use official Oracle documentation as the authoritative source.", agents);
        Assert.Contains("docs/reference/oracle-knowledge-map.yaml", agents);
        Assert.Contains("Forbidden: downloading, mirroring, caching, or redistributing Oracle documentation content.", ps1);
        Assert.Contains("Forbidden: downloading, mirroring, caching, or redistributing Oracle documentation content.", sh);
        Assert.Contains("Forbidden: downloading, mirroring, caching, or redistributing Oracle documentation content.", navPs1);
        Assert.Contains("Forbidden: downloading, mirroring, caching, or redistributing Oracle documentation content.", navSh);
    }

    [Fact]
    public void OracleWorkspaces_IncludeReferencesOnly_AndDoNotBundleOracleManualCopies()
    {
        var snapshot = CreateWorkspaceFromTemplate("oracle-apexlang-demo", "oracle-reference-policy");

        var referenceRoot = Path.Combine(snapshot.Paths.RootPath, "docs", "reference");
        var skillRoot = Path.Combine(snapshot.Paths.RootPath, "skills", "oracle");
        var allFiles = Directory.EnumerateFiles(snapshot.Paths.RootPath, "*", SearchOption.AllDirectories).ToList();

        Assert.All(Directory.EnumerateFiles(referenceRoot), path => Assert.Contains(Path.GetExtension(path), new[] { ".md", ".yaml" }));
        Assert.All(Directory.EnumerateFiles(skillRoot), path => Assert.Equal(".md", Path.GetExtension(path)));
        Assert.DoesNotContain(allFiles, path => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(allFiles, path => path.Contains("mirror", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(allFiles, path => path.Contains("manual", StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase));

        var provisioningScript = File.ReadAllText(snapshot.Paths.ProvisionScriptPath);
        Assert.DoesNotContain("docs.oracle.com", provisioningScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("download-oracle-doc", provisioningScript, StringComparison.OrdinalIgnoreCase);

        var knowledgeMap = File.ReadAllText(Path.Combine(referenceRoot, "oracle-knowledge-map.yaml"));
        var apiMap = File.ReadAllText(Path.Combine(referenceRoot, "oracle-apex-api-map.yaml"));
        Assert.Contains("oracle-apex-api-reference.md", knowledgeMap);
        Assert.Contains("APEX_JSON", apiMap);
    }

    [Fact]
    public void Repository_DoesNotTrackOracleApexMedia()
    {
        var repoRoot = TestPaths.RepositoryRoot;
        var localOracleRoot = Path.Combine(repoRoot, ".local", "oracle", "downloads");
        Assert.False(Directory.Exists(localOracleRoot), "Repository should not track .local/oracle/downloads contents.");
    }

    private static WorkspaceSnapshot CreateWorkspaceFromTemplate(string templateId, string workspaceName)
    {
        Assert.True(OracleTemplateTestHelpers.CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = OracleTemplateTestHelpers.CreateTempRoot($"{workspaceName}-root");
        var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
        var template = provider.LoadTemplates().Single(item => item.Id == templateId);
        var definition = new TemplateExpander().Expand(workspaceName, template);
        return OracleTemplateTestHelpers.CreateOrchestrator(tempRoot, resolver).CreateWorkspace(tempRoot, definition);
    }

    private static void AssertScriptLooksValid(string path, string expectedText, string? expectedEnvironmentVariable)
    {
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains(expectedText, content);
        Assert.DoesNotContain("TODO", content, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(expectedEnvironmentVariable))
        {
            Assert.Contains(expectedEnvironmentVariable, content);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var mode = File.GetUnixFileMode(path);
            Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
        }
    }
}
