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
        var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
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

        Assert.Contains("oracle-ords", apex.Services);
        Assert.Contains("oracle-ords", apexlang.Services);
        Assert.Contains("oracle-apexlang-demo", apexlang.Features);
    }

    [Fact]
    public void OracleApexDemo_GeneratesExpectedFilesWithoutApexApplicationSource()
    {
        var snapshot = CreateWorkspaceFromTemplate("oracle-apex-demo", "oracle-apex-static");

        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-apex-demo.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "team-onboarding.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-lifecycle-workflows.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "sharing-oracle-workspaces.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "tutorial", "oracle", "init", "03-customers-schema.sql")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "tutorial", "oracle", "init", "04-customers-sample-data.sql")));
        Assert.False(File.Exists(Path.Combine(snapshot.Paths.RootPath, "apex", "application.apx")));
    }

    [Fact]
    public void OracleApexLangDemo_GeneratesExpectedFilesAndScripts()
    {
        var snapshot = CreateWorkspaceFromTemplate("oracle-apexlang-demo", "oracle-apexlang-static");

        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-apexlang-demo.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "apexlang-introduction.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "apex", "application.apx")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "export-apex.sh")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "import-apex.sh")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "validate-apex.sh")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "team-onboarding.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-lifecycle-workflows.md")));
        Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "sharing-oracle-workspaces.md")));

        AssertScriptLooksValid(Path.Combine(snapshot.Paths.RootPath, "scripts", "export-apex.sh"), "apex export", "ORACLE_DEMO_CONNECTION");
        AssertScriptLooksValid(Path.Combine(snapshot.Paths.RootPath, "scripts", "import-apex.sh"), "sql -S", "ORACLE_DEMO_CONNECTION");
        AssertScriptLooksValid(Path.Combine(snapshot.Paths.RootPath, "scripts", "validate-apex.sh"), "Validated", null);
    }

    [Fact]
    public void OracleComposeAndEnvironment_WireDatabaseAndOrdsForApexVariants()
    {
        var apexSnapshot = CreateWorkspaceFromTemplate("oracle-apex-demo", "oracle-apex-compose");
        var apexLangSnapshot = CreateWorkspaceFromTemplate("oracle-apexlang-demo", "oracle-apexlang-compose");

        var apexCompose = File.ReadAllText(apexSnapshot.Paths.ComposePath);
        var apexLangCompose = File.ReadAllText(apexLangSnapshot.Paths.ComposePath);
        var apexEnv = File.ReadAllText(apexSnapshot.Paths.EnvironmentFilePath);

        Assert.Contains("oracle-demo:", apexCompose);
        Assert.Contains("oracle-ords:", apexCompose);
        Assert.Contains("8181:8181", apexCompose);
        Assert.Contains("oracle-demo:", apexLangCompose);
        Assert.Contains("oracle-ords:", apexLangCompose);
        Assert.Contains("ORACLE_ORDS_BASE_URL=http://localhost:8181/ords", apexEnv);
        Assert.Contains("ORACLE_APEX_LOGIN_URL=http://localhost:8181/ords/apex", apexEnv);

        var method = typeof(DockerService).GetMethod("GetComposeProfiles", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var profiles = Assert.IsAssignableFrom<IReadOnlyList<string>>(method!.Invoke(null, new object[] { apexSnapshot.Definition }));
        Assert.Equal(["oracle-demo", "oracle-ords"], profiles);
    }

    [Fact]
    public void OracleProvisioningScript_ContainsApexStagesOnlyForApexVariants()
    {
        var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices());
        var expander = new TemplateExpander();
        var generator = new ProvisioningScriptGenerator();

        var plsqlScript = generator.Generate(resolver.Resolve(expander.Expand("plsql", provider.LoadTemplates().Single(item => item.Id == "oracle-plsql-demo"))));
        var apexScript = generator.Generate(resolver.Resolve(expander.Expand("apex", provider.LoadTemplates().Single(item => item.Id == "oracle-apex-demo"))));

        Assert.DoesNotContain("Stage: Installing ORDS", plsqlScript);
        Assert.DoesNotContain("Stage: Installing APEX", plsqlScript);

        Assert.Contains("Stage: Preparing Workspace", apexScript);
        Assert.Contains("Stage: Downloading Dependencies", apexScript);
        Assert.Contains("Stage: Starting Oracle Database", apexScript);
        Assert.Contains("Stage: Waiting for Database Readiness", apexScript);
        Assert.Contains("Stage: Installing ORDS", apexScript);
        Assert.Contains("Stage: Installing APEX", apexScript);
        Assert.Contains("Stage: Configuring Workspace", apexScript);
        Assert.Contains("Stage: Creating Sample Application", apexScript);
        Assert.Contains("Stage: Running Validation", apexScript);
        Assert.Contains("Stage: Ready", apexScript);
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

        Assert.Contains("Oracle PL/SQL Demo", plsqlDoc);
        Assert.Contains("Oracle APEX Demo", apexDoc);
        Assert.Contains("Oracle APEXlang Demo", apexLangDoc);
        Assert.Contains("From Oracle Demo to Oracle Onboarding", onboardingArticle);
        Assert.Contains("Beyond PL/SQL", beyondPlsqlArticle);
        Assert.Contains("workflow", lifecycleDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source of truth", onboardingArticle, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("University of Maribor", apexDoc);
        Assert.DoesNotContain("University of Maribor", plsqlDoc);
        Assert.DoesNotContain("University of Maribor", apexLangDoc);

        foreach (var content in new[] { plsqlDoc, apexDoc, apexLangDoc, onboardingArticle, beyondPlsqlArticle, lifecycleDoc, sharingDoc })
        {
            Assert.DoesNotContain("already verified", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("accepts Oracle license", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("redistribute Oracle binaries", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static WorkspaceSnapshot CreateWorkspaceFromTemplate(string templateId, string workspaceName)
    {
        Assert.True(OracleTemplateTestHelpers.CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = OracleTemplateTestHelpers.CreateTempRoot($"{workspaceName}-root");
        var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices());
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
