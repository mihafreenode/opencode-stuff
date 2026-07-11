using System.Reflection;
using System.Text.Json;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexGuidedTourBuilderTests
{
    private static readonly string[] ExpectedLessonIds =
    [
        "understand-workspace",
        "inspect-before-editing",
        "create-or-connect-application",
        "add-equipment-report-page",
        "add-equipment-form-page",
        "add-shared-status-lov",
        "add-validation",
        "validate-and-deploy",
        "builder-to-git-round-trip",
        "agent-enhancement",
        "error-and-repair-exercise",
        "rollback-exercise",
    ];

    [Fact]
    public void BuildFiles_GeneratesMarkdownHtmlAndTutorialMetadata()
    {
        var files = BuildFiles();

        Assert.Contains(Path.Combine("docs", "tutorials", "apexlang-guided-tour.md"), files.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine("docs", "tutorials", "apexlang-guided-tour.html"), files.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(".opencode", "tutorials", "apexlang-guided-tour.json"), files.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Markdown_IncludesAllLessonIdsTracksAndPromptGuidance()
    {
        var markdown = BuildFiles()[Path.Combine("docs", "tutorials", "apexlang-guided-tour.md")];

        Assert.Contains("Beginner track:", markdown, StringComparison.Ordinal);
        Assert.Contains("Experienced APEX developer track:", markdown, StringComparison.Ordinal);
        Assert.Contains("semantic plan", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Validate before import", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Builder-to-Git round trip", markdown, StringComparison.Ordinal);
        Assert.Contains("Error and repair exercise", markdown, StringComparison.Ordinal);
        Assert.Contains("Rollback exercise", markdown, StringComparison.Ordinal);
        Assert.Contains("Use the semantic workflow rather than raw .apx edits.", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Edit the raw .apx file directly", markdown, StringComparison.OrdinalIgnoreCase);
        foreach (var lessonId in ExpectedLessonIds)
        {
            Assert.Contains($"Lesson id: `{lessonId}`", markdown, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Html_IsSelfContainedResponsiveAndStoresProgressInLocalStorage()
    {
        var html = BuildFiles()[Path.Combine("docs", "tutorials", "apexlang-guided-tour.html")];

        Assert.Contains("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("localStorage", html, StringComparison.Ordinal);
        Assert.Contains("copy-button", html, StringComparison.Ordinal);
        Assert.Contains("track-button", html, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 920px)", html, StringComparison.Ordinal);
        Assert.Contains(".layout { display: grid; grid-template-columns: 320px 1fr;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://cdn", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script src=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link rel=\"stylesheet\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://example.test/replace-with-builder-url", html, StringComparison.Ordinal);
        Assert.Contains("https://example.test/replace-with-application-url", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TutorialMetadata_ContainsExpectedCapabilitiesAndLessonIdsOnly()
    {
        var json = BuildFiles()[Path.Combine(".opencode", "tutorials", "apexlang-guided-tour.json")];
        using var document = JsonDocument.Parse(json);

        Assert.Equal("1.0", document.RootElement.GetProperty("tutorialVersion").GetString());
        var lessonIds = document.RootElement.GetProperty("lessonIdentifiers").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.Equal(ExpectedLessonIds.Length, lessonIds.Count);
        foreach (var lessonId in ExpectedLessonIds)
        {
            Assert.Contains(lessonId, lessonIds);
        }

        var serialized = document.RootElement.ToString();
        Assert.DoesNotContain("password", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("semantic editing", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("repair planning", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkspaceGeneratedContent_IncludesGuidedTourArtifacts()
    {
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-apexlang-demo", Image = "ubuntu:24.04" },
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

        Assert.Contains(Path.Combine("docs", "tutorials", "apexlang-guided-tour.md"), files.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine("docs", "tutorials", "apexlang-guided-tour.html"), files.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(".opencode", "tutorials", "apexlang-guided-tour.json"), files.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Equipment Register", files[Path.Combine("docs", "tutorials", "apexlang-guided-tour.md")], StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> BuildFiles()
    {
        var builderType = typeof(WorkspaceDefinition).Assembly.GetType("OpenCode.Workspace.Core.Generation.OracleApexGuidedTourBuilder", throwOnError: true)!;
        var buildFiles = builderType.GetMethod("BuildFiles", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        return (IReadOnlyDictionary<string, string>)buildFiles.Invoke(null, null)!;
    }
}
