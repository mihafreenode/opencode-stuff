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

    [Fact]
    public void Compare_ReportsVersionDiffsAndProvenance()
    {
        var importer = new OracleApexLanguageReferenceImporter();
        var previous = importer.Import(ReadFixture("apexlang-reference-v25.2.md"), ReadFixture("apexlang-reference-v25.2.ebnf"), new OracleApexLanguageReferenceProvenance
        {
            SourceKind = "fixture",
            SourceLocation = "https://docs.oracle.com/en/database/oracle/apex/25.2/apxln/index.html",
            GrammarLocation = "https://docs.oracle.com/en/database/oracle/apex/25.2/apxln/apexlang.ebnf",
            ApexVersion = "25.2",
            ImportedUtc = DateTimeOffset.UnixEpoch,
        });
        var current = importer.Import(ReadFixture("apexlang-reference-v26.1.md"), ReadFixture("apexlang-reference-v26.1.ebnf"), new OracleApexLanguageReferenceProvenance
        {
            SourceKind = "fixture",
            SourceLocation = "https://docs.oracle.com/en/database/oracle/apex/26.1/apxln/index.html",
            GrammarLocation = "https://docs.oracle.com/en/database/oracle/apex/26.1/apxln/apexlang.ebnf",
            ApexVersion = "26.1",
            ImportedUtc = DateTimeOffset.UnixEpoch,
        });

        var comparer = new OracleApexLanguageReferenceCatalogComparer();
        var diff = comparer.Compare(previous, current, OracleApexComponentCatalog.AtlasSeed.CompareWithReference(previous), OracleApexComponentCatalog.AtlasSeed.CompareWithReference(current));

        Assert.Contains(diff.Differences, item => item.Kind == "component-added" && item.ComponentName == "deployment");
        Assert.Contains(diff.Differences, item => item.Kind == "component-removed" && item.ComponentName == "legacybanner");
        Assert.Contains(diff.Differences, item => item.Kind == "property-removed" && item.ComponentName == "application" && item.PropertyPath == "theme");
        Assert.Contains(diff.Differences, item => item.Kind == "property-required-changed" && item.ComponentName == "application" && item.PropertyPath == "alias" && item.BeforeValue == "optional" && item.AfterValue == "required");
        Assert.Contains(diff.Differences, item => item.Kind == "property-default-changed" && item.ComponentName == "application" && item.PropertyPath == "name" && item.BeforeValue.Contains("Legacy Demo", StringComparison.OrdinalIgnoreCase) && item.AfterValue.Contains("Demo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(diff.Differences, item => item.Kind == "property-enum-changed" && item.RemovedValues.Contains("TRACE", StringComparer.OrdinalIgnoreCase));
        Assert.Contains(diff.Differences, item => item.Kind == "property-applicability-or-constraint-changed" && item.ComponentName == "application" && item.PropertyPath == "name");
        Assert.Contains(diff.Differences, item => item.Kind == "documentation-anchor-changed" && item.ComponentName == "page");
        Assert.Contains(diff.Differences, item => item.Kind == "canonical-example-changed" && item.ComponentName == "application");
        Assert.All(diff.Differences, item =>
        {
            Assert.Equal("25.2", item.Provenance.FromCatalog.ApexVersion);
            Assert.Equal("26.1", item.Provenance.ToCatalog.ApexVersion);
            Assert.NotEmpty(item.Provenance.FromDocumentationReference);
            Assert.NotEmpty(item.Provenance.ToDocumentationReference);
        });
        Assert.True(diff.AtlasCompatibility.AddedWarnings.Count > 0 || diff.AtlasCompatibility.RemovedWarnings.Count > 0);
    }

    private static string ReadFixture(string fileName)
        => File.ReadAllText(Path.Combine(GetRepositoryRoot(), "tests", "OpenCode.Workspace.Core.Tests", "Fixtures", fileName));

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
