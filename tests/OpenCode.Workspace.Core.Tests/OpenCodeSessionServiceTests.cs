using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OpenCodeSessionServiceTests
{
    [Fact]
    public void ParseSessionList_ReturnsOrderedSessionIds()
    {
        var service = new OpenCodeSessionService();
        var output = """
Session ID                      Title                                            Updated
────────────────────────────────────────────────────────────────────────────────────────
ses_newest                      Latest work                                       5:32 PM
ses_older                       Older work                                        1:01 PM
""";

        var items = service.ParseSessionList(output);

        Assert.Collection(
            items,
            item => Assert.Equal("ses_newest", item.Id),
            item => Assert.Equal("ses_older", item.Id));
    }

    [Fact]
    public void TryGetSessionDirectory_ReadsExportedDirectory()
    {
        var service = new OpenCodeSessionService();
        var export = """
{
  "info": {
    "id": "ses_demo",
    "directory": "/workspace"
  }
}
""";

        var directory = service.TryGetSessionDirectory(export);

        Assert.Equal("/workspace", directory);
    }

    [Fact]
    public async Task SelectLatestSessionForWorkspaceAsync_ReturnsFirstMatchingWorkspaceSession()
    {
        var service = new OpenCodeSessionService();
        var output = """
Session ID                      Title                                            Updated
────────────────────────────────────────────────────────────────────────────────────────
ses_newest                      Latest work                                       5:32 PM
ses_workspace                   Workspace work                                    1:01 PM
""";

        var sessionId = await service.SelectLatestSessionForWorkspaceAsync(
            output,
            session => Task.FromResult<string?>(session == "ses_workspace" ? "/workspace" : "/other"),
            "/workspace");

        Assert.Equal("ses_workspace", sessionId);
    }
}
