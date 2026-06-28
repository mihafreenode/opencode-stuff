namespace OpenCode.Workspace.Core.Models;

public sealed class WorkspaceLaunchPlan
{
    public bool NeedsProvision { get; init; }
    public bool NeedsStart { get; init; }
    public bool CanAttach { get; init; }
    public bool NeedsRecover { get; init; }
    public bool NeedsDiagnostics { get; init; }
    public bool TerminalUnavailable { get; init; }
    public string PrimaryServiceName { get; init; } = "workspace";
    public string Summary { get; init; } = string.Empty;
}
