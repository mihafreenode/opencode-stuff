using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceProvisioningException : InvalidOperationException
{
    public WorkspaceProvisioningException(WorkspaceProvisioningHealthRecord healthRecord, string rawOutput)
        : base(BuildMessage(healthRecord))
    {
        HealthRecord = healthRecord;
        RawOutput = rawOutput;
    }

    public WorkspaceProvisioningHealthRecord HealthRecord { get; }

    public string RawOutput { get; }

    private static string BuildMessage(WorkspaceProvisioningHealthRecord healthRecord)
    {
        var parts = new List<string>
        {
            healthRecord.Summary,
            $"Stage: {healthRecord.Stage}",
            $"Reason: {healthRecord.Reason}",
        };

        if (!string.IsNullOrWhiteSpace(healthRecord.Evidence))
        {
            parts.Add($"Evidence: {healthRecord.Evidence}");
        }

        if (!string.IsNullOrWhiteSpace(healthRecord.RecommendedAction))
        {
            parts.Add($"Recommended action: {healthRecord.RecommendedAction}");
        }

        if (!string.IsNullOrWhiteSpace(healthRecord.Confidence))
        {
            parts.Add($"Confidence: {healthRecord.Confidence}");
        }

        return string.Join(Environment.NewLine, parts);
    }
}
