namespace OpenCode.Workspace.Core.Models;

/// <summary>
/// The repository keeps a small Windows-local index of known workspaces so the
/// app can reopen them quickly. The durable workspace behavior still lives in the
/// user-owned workspace.yaml file inside each workspace folder.
/// </summary>
public sealed class WorkspaceRecord
{
    public string Name { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset LastOpenedUtc { get; init; }
    public DateTimeOffset? LastPreparedUtc { get; init; }
    public string? LastOperationName { get; init; }
    public string? LastOperationResult { get; init; }
    public bool? LastOperationSucceeded { get; init; }
    public DateTimeOffset? LastOperationUtc { get; init; }
}

public sealed class WorkspacePaths
{
    public required string RootPath { get; init; }
    public required string GitIgnorePath { get; init; }
    public required string WorkspaceYamlPath { get; init; }
    public required string ComposePath { get; init; }
    public required string EnvironmentFilePath { get; init; }
    public required string MountsRootPath { get; init; }
    public required string InboxPath { get; init; }
    public required string WorkspacePath { get; init; }
    public required string UserPath { get; init; }
    public required string HomePath { get; init; }
    public required string ConfigPath { get; init; }
    public required string ProvisionScriptPath { get; init; }
    public required string StarshipConfigPath { get; init; }
    public required string ShellInitScriptPath { get; init; }
    public required string OpencodeWorkspaceShellPath { get; init; }
    public required string ScreenConfigPath { get; init; }
    public required string AttachWrapperScriptPath { get; init; }
    public required string TerminalDiagnosticsScriptPath { get; init; }
    public required string AppliedStatePath { get; init; }
    public required string HistoryPath { get; init; }
    public required string CheckpointsPath { get; init; }
    public required string CheckpointIndexPath { get; init; }
    public required string TimelinePath { get; init; }
    public required string RuntimesPath { get; init; }
    public required string DefaultRuntimePath { get; init; }
    public required string ArtifactsPath { get; init; }
    public required string ArtifactRunsPath { get; init; }
    public required string ArtifactIndexPath { get; init; }
}

public sealed class WorkspaceAppliedState
{
    public string DesiredStateHash { get; init; } = string.Empty;
    public string WorkspaceDefinitionHash { get; init; } = string.Empty;
    public DateTimeOffset AppliedUtc { get; init; }
    public string? AppVersion { get; init; }
}

public enum WorkspaceRuntimeState
{
    Unknown,
    Stopped,
    Running,
}

public sealed class WorkspaceSnapshot
{
    public required WorkspaceRecord Record { get; init; }
    public required WorkspaceDefinition Definition { get; init; }
    public required WorkspacePaths Paths { get; init; }
    public required WorkspaceRuntimeState RuntimeState { get; init; }
    public required WorkspaceSafetySnapshot Safety { get; init; }
    public WorkspaceAppliedState? AppliedState { get; init; }
    public bool UpdateRequired { get; init; }
}
