using System.Reflection;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexSyntaxReferenceBuilderTests
{
    [Fact]
    public void BuildFiles_GeneratesCompactSkillReferencesAndPreservesSyntaxExamples()
    {
        var builderType = typeof(WorkspaceDefinition).Assembly.GetType("OpenCode.Workspace.Core.Generation.OracleApexSyntaxReferenceBuilder", throwOnError: true)!;
        var buildFiles = builderType.GetMethod("BuildFiles", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var html = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Core.Tests", "Fixtures", "ApexDevelopersCompanion", "reading-apexlang-syntax.html"));

        var files = (IReadOnlyDictionary<string, string>)buildFiles.Invoke(null, [html, "https://docs.oracle.com/en/database/oracle/apex/26.1/apxdc/reading-apexlang-syntax.html", "26.1"])!;

        Assert.Contains(Path.Combine(".opencode", "skills", "apexlang", "references", "syntax-basics.md"), files.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(".opencode", "skills", "apexlang", "references", "component-references.md"), files.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(".opencode", "skills", "apexlang", "references", "embedded-languages.md"), files.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("@/", files[Path.Combine(".opencode", "skills", "apexlang", "references", "component-references.md")], StringComparison.Ordinal);
        Assert.Contains("@", files[Path.Combine(".opencode", "skills", "apexlang", "references", "component-references.md")], StringComparison.Ordinal);
        Assert.Contains("```sql", files[Path.Combine(".opencode", "skills", "apexlang", "references", "embedded-languages.md")], StringComparison.Ordinal);
        Assert.Contains("```plsql", files[Path.Combine(".opencode", "skills", "apexlang", "references", "embedded-languages.md")], StringComparison.Ordinal);
        Assert.Contains("javascript-browser", files[Path.Combine(".opencode", "skills", "apexlang", "references", "embedded-languages.md")], StringComparison.Ordinal);
        Assert.Contains("javascript-mle", files[Path.Combine(".opencode", "skills", "apexlang", "references", "embedded-languages.md")], StringComparison.Ordinal);
    }

    [Fact]
    public void OracleWorkspaceGeneratedContent_IncludesApexlangReferenceFiles()
    {
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-apexlang", Image = "ubuntu:24.04" },
            Features = ["oracle-apexlang-demo"],
            Services = [],
        };
        var generatedType = typeof(WorkspaceDefinition).Assembly.GetType("OpenCode.Workspace.Core.Generation.OracleWorkspaceGeneratedContent", throwOnError: true)!;
        var generate = generatedType.GetMethod("Generate", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var files = (IReadOnlyDictionary<string, string>)generate.Invoke(null,
        [
            definition,
            null,
            (Func<string, string>)(content => content),
            (Func<string, string>)(content => content),
            (Func<string, string>)(content => content),
        ])!;

        var skill = files[Path.Combine("skills", "oracle", "apexlang.md")];
        Assert.Contains(".opencode/skills/apexlang/references/syntax-basics.md", skill, StringComparison.Ordinal);
        Assert.Contains(".opencode/knowledge/apex-developers-companion/prompts/compact-context.md", skill, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(".opencode", "skills", "apexlang", "references", "identifiers-and-scopes.md"), files.Keys, StringComparer.OrdinalIgnoreCase);
    }
}
