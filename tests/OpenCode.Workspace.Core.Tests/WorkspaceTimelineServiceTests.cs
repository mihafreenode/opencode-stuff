using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceTimelineServiceTests
{
    [Fact]
    public void Append_RecordsSavePointAndPublishEvents()
    {
        var service = new WorkspaceTimelineService();
        var filePath = Path.Combine(Path.GetTempPath(), $"timeline-{Guid.NewGuid():N}.yaml");

        try
        {
            service.Append(filePath, "save-point", "Created Save Point", "Captured local progress.");
            service.Append(filePath, "publish-succeeded", "Published workspace", "Working Copy published successfully.");
            service.Append(filePath, "publish-blocked", "Publish needs review", "Remote workspace changed.");

            var timeline = service.Load(filePath);

            Assert.Contains(timeline.Events, item => item.Type == "save-point");
            Assert.Contains(timeline.Events, item => item.Type == "publish-succeeded");
            Assert.Contains(timeline.Events, item => item.Type == "publish-blocked");
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
