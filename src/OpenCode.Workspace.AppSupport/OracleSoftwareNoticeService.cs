using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.AppSupport;

public sealed class OracleSoftwareNoticeService
{
    private readonly WorkspaceRepository _workspaceRepository;

    public OracleSoftwareNoticeService(WorkspaceRepository workspaceRepository)
    {
        _workspaceRepository = workspaceRepository;
    }

    public bool RequiresAcknowledgement(TemplateManifest template)
        => OracleWorkspaceFamily.IsOracleWorkspace(template);

    public bool RequiresAcknowledgement(WorkspaceSnapshot snapshot)
        => OracleWorkspaceFamily.IsOracleWorkspace(snapshot.Definition) && !snapshot.Record.OracleSoftwareNoticeShown;

    public OracleSoftwareNoticePrompt BuildPrompt(TemplateManifest template, string workspaceName)
    {
        return BuildPrompt(workspaceName, OracleWorkspaceFamily.Detect(template) is OracleWorkspaceKind.Apex or OracleWorkspaceKind.ApexLang);
    }

    public OracleSoftwareNoticePrompt BuildPrompt(WorkspaceSnapshot snapshot)
    {
        return BuildPrompt(snapshot.Definition.Workspace.Name, OracleWorkspaceFamily.HasApex(snapshot.Definition));
    }

    public WorkspaceRecord Acknowledge(WorkspaceRecord record)
    {
        var updated = new WorkspaceRecord
        {
            Name = record.Name,
            RootPath = record.RootPath,
            RepositoryPath = record.RepositoryPath,
            ConfigurationPath = record.ConfigurationPath,
            SourceType = record.SourceType,
            ImportedFromExistingCheckout = record.ImportedFromExistingCheckout,
            OriginalDefaultBranch = record.OriginalDefaultBranch,
            SelectedWorkspaceBranch = record.SelectedWorkspaceBranch,
            RemoteOriginUrl = record.RemoteOriginUrl,
            CreatedUtc = record.CreatedUtc,
            LastOpenedUtc = record.LastOpenedUtc,
            LastPreparedUtc = record.LastPreparedUtc,
            OracleSoftwareNoticeShown = true,
            LastOperationName = record.LastOperationName,
            LastOperationResult = record.LastOperationResult,
            LastOperationSucceeded = record.LastOperationSucceeded,
            LastOperationUtc = record.LastOperationUtc,
        };

        _workspaceRepository.Save(updated);
        return updated;
    }

    private static OracleSoftwareNoticePrompt BuildPrompt(string workspaceName, bool includesApex)
    {
        var facts = new List<string>
        {
            includesApex
                ? "This workspace provisions Oracle software and may install Oracle APEX and ORDS from Oracle-provided sources."
                : "This workspace provisions Oracle software from Oracle-provided sources.",
            "Oracle software is subject to Oracle licensing terms.",
            includesApex
                ? "OpenCode Stuff does not redistribute Oracle software. Continue only if you are allowed to use these Oracle components."
                : "Please review applicable Oracle licensing information before continuing.",
            "Oracle Database Free: https://www.oracle.com/database/free/",
            "Oracle APEX: https://apex.oracle.com/",
            "Oracle ORDS: https://www.oracle.com/database/technologies/appdev/rest.html",
            "Oracle Licensing Information: https://www.oracle.com/corporate/license/",
        };

        return new OracleSoftwareNoticePrompt
        {
            Title = "Oracle Software Notice",
            SubjectName = workspaceName,
            Summary = "Review the Oracle software reminder before continuing with this Oracle workspace.",
            Facts = facts,
            AcknowledgementLabel = "I understand this workspace uses Oracle-provided software and I must review the applicable Oracle licensing terms.",
            ConfirmLabel = "Continue",
            CancelLabel = "Cancel",
        };
    }
}

public sealed class OracleSoftwareNoticePrompt
{
    public required string Title { get; init; }
    public required string SubjectName { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Facts { get; init; }
    public required string AcknowledgementLabel { get; init; }
    public required string ConfirmLabel { get; init; }
    public required string CancelLabel { get; init; }
}
