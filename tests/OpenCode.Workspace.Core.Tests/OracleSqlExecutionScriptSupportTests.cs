using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleSqlExecutionScriptSupportTests
{
    [Fact]
    public void NormalizeSingleStatementText_RemovesBomCrLfAndTrailingTerminator()
    {
        var normalized = OracleSqlExecutionScriptSupport.NormalizeSingleStatementText("\uFEFFSELECT status FROM dual;\r\n");

        Assert.Equal("SELECT status FROM dual\n", normalized);
    }

    [Fact]
    public void NormalizeScriptText_RemovesBomAndCrLfButPreservesScriptTerminators()
    {
        var normalized = OracleSqlExecutionScriptSupport.NormalizeScriptText("\uFEFFBEGIN\r\n  NULL;\r\nEND;\r\n/\r\n");

        Assert.Equal("BEGIN\n  NULL;\nEND;\n/\n", normalized);
    }

    [Fact]
    public void BuildDiagnosticPreview_SkipsGeneratedHeaders()
    {
        var preview = OracleSqlExecutionScriptSupport.BuildDiagnosticPreview("""
            -- GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES
            -- Source inputs: workspace.yaml and catalog manifests under catalog/.
            -- User edits to this file are not preserved. Edit workspace.yaml or catalog manifests instead.
            apex validate -workspace TEST -input /workspace/exports/apexlang/hello-apexlang
            exit
            """);

        Assert.Equal("apex validate -workspace TEST -input /workspace/exports/apexlang/hello-apexlang exit", preview);
    }

    [Fact]
    public void ShellLibrary_ContainsSanitizedDiagnosticsAndSqlErrorHandling()
    {
        var script = OracleSqlExecutionScriptSupport.BuildShellLibrary();

        Assert.Contains("WHENEVER SQLERROR EXIT SQL.SQLCODE", script, StringComparison.Ordinal);
        Assert.Contains("[oracle-sql] phase=", script, StringComparison.Ordinal);
        Assert.Contains("[oracle-sql] source=", script, StringComparison.Ordinal);
        Assert.Contains("[oracle-sql] statement=", script, StringComparison.Ordinal);
        Assert.Contains("oracle_sql_sanitize_output", script, StringComparison.Ordinal);
        Assert.Contains("single-sql-statement", script, StringComparison.Ordinal);
        Assert.Contains("sqlcl-command-script", script, StringComparison.Ordinal);
    }

    [Fact]
    public void OracleProvisioningAndApexlangScripts_UseSharedSqlExecutionMechanism()
    {
        var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
        var expander = new TemplateExpander();
        var resolved = resolver.Resolve(expander.Expand("oracle-apexlang", provider.LoadTemplates().Single(item => item.Id == "oracle-apexlang-demo")));

        var provisionScript = new OracleWorkspaceProvisioningScriptGenerator().Generate(resolved);
        var generatedFiles = new WorkspaceContentGenerator().Generate(resolved);
        var helloWorldScript = generatedFiles[Path.Combine("scripts", "apexlang-hello-world.sh")];

        Assert.Contains("oracle_sql_run_file", provisionScript, StringComparison.Ordinal);
        Assert.Contains("'query_apex_registry'", provisionScript, StringComparison.Ordinal);
        Assert.Contains("'query_database_open_mode'", provisionScript, StringComparison.Ordinal);
        Assert.Contains("'apexins.sql'", provisionScript, StringComparison.Ordinal);

        Assert.Contains("oracle_sql_run_file 'Creating Sample Application' sqlcl /nolog sqlcl-command-script 'sql/hello-apexlang/generate-hello-apexlang.sql'", helloWorldScript, StringComparison.Ordinal);
        Assert.Contains("sql/hello-apexlang/validate-hello-apexlang.sql", helloWorldScript, StringComparison.Ordinal);
        Assert.Contains("sql/hello-apexlang/import-hello-apexlang.sql", helloWorldScript, StringComparison.Ordinal);
        Assert.Contains("sql/hello-apexlang/export-hello-apexlang.sql", helloWorldScript, StringComparison.Ordinal);
        Assert.Contains("[oracle-sql] output_begin", helloWorldScript, StringComparison.Ordinal);
    }
}
