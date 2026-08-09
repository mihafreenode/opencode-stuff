namespace OpenCode.Workspace.Core.Tests;

public sealed class CapabilityDocumentationTests
{
    [Fact]
    public void CapabilityCatalog_ContainsCurrentCapabilityFilesAndOnboardingLinks()
    {
        var root = TestPaths.RepositoryRoot;
        var capabilityRoot = Path.Combine(root, "docs", "capabilities");
        var capabilityFiles = new[]
        {
            "README.md",
            "repository.md",
            "documentation.md",
            "document-processing.md",
            "ocr.md",
            "spell-checking.md",
            "analytics.md",
            "reporting.md",
            "testing.md",
            "localization.md",
            "oracle.md",
        };

        Assert.All(capabilityFiles, fileName =>
            Assert.True(File.Exists(Path.Combine(capabilityRoot, fileName)), $"Expected capability doc to exist: {fileName}"));

        var catalog = File.ReadAllText(Path.Combine(capabilityRoot, "README.md"));
        foreach (var link in new[]
        {
            "[Getting Started](../getting-started.md)",
            "[Sessions](../user/sessions.md)",
            "[Troubleshooting](../user/troubleshooting.md)",
            "[Oracle Team Onboarding](../oracle/team-onboarding.md)",
        })
        {
            Assert.Contains(link, catalog, StringComparison.Ordinal);
        }

        foreach (var capability in new[]
        {
            "Repository Workflows",
            "Documentation",
            "Document Processing",
            "OCR",
            "Spell Checking",
            "Analytics",
            "Reporting",
            "Testing",
            "Localization",
            "Oracle",
        })
        {
            Assert.Contains(capability, catalog, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CapabilityDocs_DescribeCurrentDomainCoverage()
    {
        var capabilityRoot = Path.Combine(TestPaths.RepositoryRoot, "docs", "capabilities");
        var expectations = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["document-processing.md"] = ["PDF", "Office", "conversion"],
            ["ocr.md"] = ["scanned", "text extraction"],
            ["spell-checking.md"] = ["English", "Slovenian"],
            ["analytics.md"] = ["spreadsheet", "report"],
            ["testing.md"] = ["regression", "Playwright"],
            ["oracle.md"] = ["SQLcl", "ORDS", "APEX", "APEXlang"],
        };

        foreach (var (fileName, terms) in expectations)
        {
            var content = File.ReadAllText(Path.Combine(capabilityRoot, fileName));
            Assert.All(terms, term => Assert.Contains(term, content, StringComparison.OrdinalIgnoreCase));
        }
    }
}
