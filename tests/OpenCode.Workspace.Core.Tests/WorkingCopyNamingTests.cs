using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkingCopyNamingTests
{
    [Fact]
    public void Create_SanitizesUserAndTitleAndIncludesTimestamp()
    {
        var branchName = WorkingCopyNaming.Create("Miha Pirnat", "Workspace Safety! @ Demo", new DateTimeOffset(2026, 6, 13, 15, 42, 0, TimeSpan.Zero));

        Assert.Equal("users/miha-pirnat/workspace-safety-demo-20260613-1542", branchName);
    }

    [Fact]
    public void SanitizeSegment_LowercasesAndRemovesUnsafeCharacters()
    {
        var sanitized = WorkingCopyNaming.SanitizeSegment(" Customer / Analysis : Final? ", "workspace");

        Assert.Equal("customer-analysis-final", sanitized);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("master")]
    [InlineData("staging")]
    [InlineData("production")]
    [InlineData("release/2026-q2")]
    [InlineData("protected/finance")]
    public void IsProtectedBranch_ReturnsTrueForProtectedNames(string branchName)
    {
        Assert.True(WorkingCopyNaming.IsProtectedBranch(branchName));
    }

    [Fact]
    public void IsSafeWorkingCopy_ReturnsTrueForUsersPrefix()
    {
        Assert.True(WorkingCopyNaming.IsSafeWorkingCopy("users/miha/workspace-safety-20260613-1542"));
    }

    [Fact]
    public void CreateImportedWorkspace_SanitizesTitleAndIncludesTimestamp()
    {
        var branchName = WorkingCopyNaming.CreateImportedWorkspace("My Project", new DateTimeOffset(2026, 6, 13, 14, 30, 0, TimeSpan.Zero));

        Assert.Equal("workspace/my-project-20260613-1430", branchName);
    }

    [Fact]
    public void IsWorkspaceBranch_ReturnsTrueForWorkspacePrefix()
    {
        Assert.True(WorkingCopyNaming.IsWorkspaceBranch("workspace/my-project-20260613-1430"));
    }
}
