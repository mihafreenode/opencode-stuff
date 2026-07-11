using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexLanguageReferenceImporterTests
{
    [Fact]
    public void Import_RepresentsDocumentedComponentsAndRelationships()
    {
        var importer = new OracleApexLanguageReferenceImporter();

        var catalog = importer.Import(ReadFixture("apexlang-reference-sample.md"), ReadFixture("apexlang-reference-sample.ebnf"), new OracleApexLanguageReferenceProvenance
        {
            SourceKind = "fixture",
            SourceLocation = "https://docs.oracle.com/en/database/oracle/apex/26.1/apxln/index.html",
            GrammarLocation = "https://docs.oracle.com/en/database/oracle/apex/26.1/apxln/apexlang.ebnf",
            ApexVersion = "26.1",
            ImportedUtc = DateTimeOffset.UtcNow,
        });

        Assert.Contains("application", catalog.Components.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("page", catalog.Components.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("region", catalog.Components.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("item", catalog.Components.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("validation", catalog.Components.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("page", catalog.Components["application"].ChildComponents, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("region", catalog.Components["page"].ChildComponents, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_RecognizesRequiredPropertiesEnumsAndExamples()
    {
        var importer = new OracleApexLanguageReferenceImporter();

        var catalog = importer.Import(ReadFixture("apexlang-reference-sample.md"), ReadFixture("apexlang-reference-sample.ebnf"), new OracleApexLanguageReferenceProvenance
        {
            SourceKind = "fixture",
            SourceLocation = "official",
            GrammarLocation = "grammar",
            ApexVersion = "26.1",
            ImportedUtc = DateTimeOffset.UtcNow,
        });

        var app = catalog.Components["application"];
        Assert.Contains(app.DirectProperties, property => property.PropertyPath == "name" && property.Required);
        Assert.NotEmpty(app.CanonicalExamples);
    }

    [Fact]
    public void MergeWithReference_EnrichesCatalogAndReportsCompatibilityWarnings()
    {
        var importer = new OracleApexLanguageReferenceImporter();
        var reference = importer.Import(ReadFixture("apexlang-reference-sample.md"), ReadFixture("apexlang-reference-sample.ebnf"), new OracleApexLanguageReferenceProvenance
        {
            SourceKind = "fixture",
            SourceLocation = "official",
            GrammarLocation = "grammar",
            ApexVersion = "26.1",
            ImportedUtc = DateTimeOffset.UtcNow,
        });
        var merged = OracleApexComponentCatalog.AtlasSeed.MergeWithReference(reference, null);
        var compatibility = OracleApexComponentCatalog.AtlasSeed.CompareWithReference(reference);

        Assert.Contains(merged.GetComponent("application").Properties, property => property.Name.Contains("listposition", StringComparison.OrdinalIgnoreCase));
        Assert.True(compatibility.HasWarnings);
    }

    private static string ReadFixture(string fileName)
        => File.ReadAllText(Path.Combine(GetRepositoryRoot(), "tests", "OpenCode.Workspace.Core.Tests", "Fixtures", fileName));

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
