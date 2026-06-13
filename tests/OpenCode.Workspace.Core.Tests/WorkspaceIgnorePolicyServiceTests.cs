using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceIgnorePolicyServiceTests
{
    private readonly WorkspaceIgnorePolicyService _service = new();

    [Fact]
    public void ReviewWorkspace_GeneratedRootEnvironmentFile_DoesNotRequireReview()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"ignore-policy-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(rootPath);
            File.WriteAllText(
                Path.Combine(rootPath, ".env"),
                string.Join(
                    Environment.NewLine,
                    [
                        "# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES",
                        "# Source inputs: workspace.yaml and catalog manifests under catalog/.",
                        "WORKSPACE_NAME=demo",
                    ]));

            var review = _service.ReviewWorkspace(rootPath);

            Assert.False(review.HasSecretCandidates);
            Assert.False(review.HasReviewRequired);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                TestFileSystem.DeleteDirectoryIfExists(rootPath);
            }
        }
    }

    [Theory]
    [InlineData(".opencode/")]
    [InlineData(".github/")]
    [InlineData(".devcontainer/")]
    [InlineData(".editorconfig")]
    [InlineData(".gitattributes")]
    [InlineData(".gitignore")]
    public void Classify_DefaultTrackedHiddenContent_IsTracked(string path)
    {
        var classification = _service.Classify(path, path.EndsWith('/'));

        Assert.Equal(WorkspaceContentDisposition.Tracked, classification.Disposition);
    }

    [Theory]
    [InlineData(".cache/")]
    [InlineData(".pytest_cache/")]
    [InlineData(".mypy_cache/")]
    [InlineData(".npm/")]
    [InlineData(".pnpm-store/")]
    public void Classify_DefaultIgnoredHiddenCaches_AreIgnored(string path)
    {
        var classification = _service.Classify(path, isDirectory: true);

        Assert.Equal(WorkspaceContentDisposition.Ignored, classification.Disposition);
    }

    [Theory]
    [InlineData(".foo/")]
    [InlineData(".bar/")]
    [InlineData(".custom-tool/")]
    public void Classify_UnknownHiddenFolders_RequireReview(string path)
    {
        var classification = _service.Classify(path, isDirectory: true);

        Assert.Equal(WorkspaceContentDisposition.NeedsReview, classification.Disposition);
    }

    [Fact]
    public void Classify_NestedUnknownHiddenFolder_RequiresReview()
    {
        var classification = _service.Classify("src/.custom-tool/", isDirectory: true);

        Assert.Equal(WorkspaceContentDisposition.NeedsReview, classification.Disposition);
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".env.local")]
    [InlineData("secrets/api-key.txt")]
    [InlineData("private.key")]
    [InlineData("certificate.pfx")]
    [InlineData("id_rsa")]
    public void Classify_SecretCandidates_RequireReview(string path)
    {
        var classification = _service.Classify(path, isDirectory: false);

        Assert.Equal(WorkspaceContentDisposition.NeedsReview, classification.Disposition);
        Assert.Contains("secret", classification.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("sources/")]
    [InlineData("knowledge/")]
    [InlineData("work/")]
    [InlineData("artifacts/")]
    [InlineData("docs/")]
    [InlineData("runtimes/")]
    [InlineData("history/timeline.yaml")]
    [InlineData("workspace.yaml")]
    public void Classify_DurableWorkspaceContent_IsTracked(string path)
    {
        var classification = _service.Classify(path, path.EndsWith('/'));

        Assert.Equal(WorkspaceContentDisposition.Tracked, classification.Disposition);
    }

    [Theory]
    [InlineData("node_modules/")]
    [InlineData("bin/")]
    [InlineData("obj/")]
    [InlineData(".venv/")]
    [InlineData("venv/")]
    [InlineData("__pycache__/")]
    [InlineData("tmp/")]
    [InlineData("temp/")]
    [InlineData(".artifact-cache/")]
    public void Classify_RebuildableContent_IsIgnored(string path)
    {
        var classification = _service.Classify(path, isDirectory: true);

        Assert.Equal(WorkspaceContentDisposition.Ignored, classification.Disposition);
    }

    [Theory]
    [InlineData("artifacts/report-v001.md", WorkspaceContentDisposition.Tracked)]
    [InlineData("artifacts/final-summary.docx", WorkspaceContentDisposition.Tracked)]
    [InlineData(".artifact-cache/previews/report.html", WorkspaceContentDisposition.Ignored)]
    [InlineData("build/generated-report.pdf", WorkspaceContentDisposition.Ignored)]
    public void Classify_ArtifactPolicy_MatchesExpectedDisposition(string path, WorkspaceContentDisposition expected)
    {
        var classification = _service.Classify(path, isDirectory: false);

        Assert.Equal(expected, classification.Disposition);
    }

    [Fact]
    public void Review_UnknownHiddenFolders_AreNotSilentlyIgnored()
    {
        var review = _service.Review(new[] { _service.Classify(".foo/", isDirectory: true) });

        Assert.True(review.HasReviewRequired);
        Assert.True(review.HasUnknownHiddenFolders);
        Assert.DoesNotContain(review.Classifications, item => item.RelativePath == ".foo/" && item.Disposition == WorkspaceContentDisposition.Ignored);
    }

    [Fact]
    public void Review_BlanketDotIgnoreRule_IsRejected()
    {
        var review = _service.Review(Array.Empty<WorkspaceContentClassification>(), new[] { ".*" });

        Assert.True(review.HasDurableIgnoreConflicts);
        Assert.Contains(review.Findings, item => item.RelativePath == ".*");
    }

    [Fact]
    public void Review_IgnoringOpencodeFolder_RequiresReview()
    {
        var review = _service.Review(
            new[] { _service.Classify(".opencode/", isDirectory: true) },
            new[] { ".opencode/" });

        Assert.True(review.HasDurableIgnoreConflicts);
        Assert.Contains(review.Findings, item => item.RelativePath == ".opencode/");
    }

    [Fact]
    public void Review_IgnoringTimelineFile_RequiresReview()
    {
        var review = _service.Review(
            new[] { _service.Classify("history/timeline.yaml", isDirectory: false) },
            new[] { "history/*.yaml" });

        Assert.True(review.HasDurableIgnoreConflicts);
        Assert.Contains(review.Findings, item => item.RelativePath == "history/timeline.yaml");
    }

    [Fact]
    public void Review_IgnoringArtifactsWithGlob_RequiresReview()
    {
        var review = _service.Review(
            new[] { _service.Classify("artifacts/report-v001.md", isDirectory: false) },
            new[] { "artifacts/**" });

        Assert.True(review.HasDurableIgnoreConflicts);
        Assert.Contains(review.Findings, item => item.RelativePath == "artifacts/");
    }

    [Fact]
    public void Review_IgnoringOpencodeWithGlob_RequiresReview()
    {
        var review = _service.Review(
            new[] { _service.Classify(".opencode/", isDirectory: true) },
            new[] { ".opencode/**" });

        Assert.True(review.HasDurableIgnoreConflicts);
        Assert.Contains(review.Findings, item => item.RelativePath == ".opencode/");
    }
}
