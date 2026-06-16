using System.Text.RegularExpressions;

namespace OpenCode.Workspace.Core.Tests;

public sealed class CapabilityDocumentationTests
{
    [Fact]
    public void CapabilityDocs_Exist_LinkInternally_AndCoverExpectedContent()
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

        foreach (var fileName in capabilityFiles)
        {
            Assert.True(File.Exists(Path.Combine(capabilityRoot, fileName)), $"Expected capability doc to exist: {fileName}");
        }

        var catalog = File.ReadAllText(Path.Combine(capabilityRoot, "README.md"));
        var documentProcessing = File.ReadAllText(Path.Combine(capabilityRoot, "document-processing.md"));
        var ocr = File.ReadAllText(Path.Combine(capabilityRoot, "ocr.md"));
        var spellChecking = File.ReadAllText(Path.Combine(capabilityRoot, "spell-checking.md"));
        var analytics = File.ReadAllText(Path.Combine(capabilityRoot, "analytics.md"));
        var testing = File.ReadAllText(Path.Combine(capabilityRoot, "testing.md"));
        var oracle = File.ReadAllText(Path.Combine(capabilityRoot, "oracle.md"));
        var onboarding = File.ReadAllText(Path.Combine(root, "docs", "team-onboarding.md"));
        var documentationFeatures = File.ReadAllText(Path.Combine(root, "docs", "documentation-features.md"));

        Assert.Contains("Repository Workflows", catalog);
        Assert.Contains("Documentation", catalog);
        Assert.Contains("Oracle", catalog);

        Assert.Contains("PDF", documentProcessing);
        Assert.Contains("Office", documentProcessing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("conversion", documentProcessing, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("scanned", ocr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("text extraction", ocr, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("English", spellChecking);
        Assert.Contains("Slovenian", spellChecking);

        Assert.Contains("spreadsheet", analytics, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("report", analytics, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("regression", testing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Playwright", testing);

        Assert.Contains("SQLcl", oracle);
        Assert.Contains("ORDS", oracle);
        Assert.Contains("APEX", oracle);
        Assert.Contains("APEXlang", oracle);

        Assert.Contains("docs/capabilities/oracle.md", onboarding);
        Assert.Contains("docs/capabilities/documentation.md", documentationFeatures);

        foreach (var fileName in capabilityFiles)
        {
            var filePath = Path.Combine(capabilityRoot, fileName);
            var content = File.ReadAllText(filePath);
            foreach (Match match in Regex.Matches(content, @"\[[^\]]+\]\(([^)]+)\)"))
            {
                var link = match.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(link)
                    || link.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    || link.StartsWith('#'))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, link.Replace('/', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(fullPath), $"Expected documentation link target to exist: {fileName} -> {link}");
            }
        }
    }
}
