using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspaceSummaryViewModel : ObservableObject
{
    private string? _runtimeStatusLabelOverride;
    private string? _lastActivityOverride;
    private string? _failedOperationNameOverride;
    private bool _isSelected;
    private string _headline = string.Empty;
    private string _summary = string.Empty;
    private string _recommendation = string.Empty;
    private ActionItemViewModel? _primaryAction;

    public WorkspaceSummaryViewModel(WorkspaceShellItem item)
    {
        Item = item;
    }

    public WorkspaceShellItem Item { get; }
    public WorkspaceSnapshot? Snapshot => Item.Snapshot;
    public WorkspaceRecord Record => Item.Record;
    public bool IsLoading => Item.IsLoading;
    public bool HasSnapshot => Snapshot is not null;
    public bool HasError => !HasSnapshot && !IsLoading;
    public string ErrorMessage => string.IsNullOrWhiteSpace(Item.ErrorMessage) ? "Workspace could not be loaded." : Item.ErrorMessage;
    public string Name => HasSnapshot ? Snapshot!.Definition.Workspace.Name : DisplayNameFromRecord();
    public string RootPath => HasSnapshot ? Snapshot!.Paths.RootPath : WorkspaceRecordPathResolver.GetWorkspaceRoot(Record);
    public string RepositoryPath => HasSnapshot
        ? string.IsNullOrWhiteSpace(Snapshot!.Record.RepositoryPath) ? Snapshot.Paths.RootPath : Snapshot.Record.RepositoryPath
        : string.IsNullOrWhiteSpace(Record.RepositoryPath) ? Record.RootPath : Record.RepositoryPath;
    public string RuntimeStatusLabel => IsLoading
        ? "Loading..."
        : HasError
        ? _runtimeStatusLabelOverride ?? "Error"
        : !string.IsNullOrWhiteSpace(_runtimeStatusLabelOverride)
        ? _runtimeStatusLabelOverride!
        : Snapshot?.Readiness is not null
        ? WorkspaceReadinessPresentationFormatter.FormatStatusLabel(Snapshot.Readiness, this)
        : FormatHealthStatusLabel(Snapshot!.Health.OverallStatus);
    public string ProtectionLabel => IsLoading
        ? "Loading..."
        : HasError
        ? "Needs Review"
        : Snapshot!.Safety.OverallStatus switch
        {
            WorkspaceSafetyLevel.Protected => "Protected",
            WorkspaceSafetyLevel.PartiallyProtected => "Partially Protected",
            WorkspaceSafetyLevel.AtRisk => "At Risk",
            WorkspaceSafetyLevel.NeedsReview => "Needs Review",
            _ => Snapshot.Safety.Headline,
        };
    public string RuntimeSummary => IsLoading
        ? Item.LoadingStatusMessage
        : !HasSnapshot
        ? "Runtime unavailable"
        : string.IsNullOrWhiteSpace(Snapshot!.ResolvedRuntimePlan?.TargetPlatform)
            ? $"Runtime {Snapshot.RuntimeState}"
            : $"Runtime {Snapshot.ResolvedRuntimePlan.TargetPlatform}";
    public string CurrentBranch => IsLoading
        ? "Loading..."
        : !HasSnapshot || string.IsNullOrWhiteSpace(Snapshot!.Safety.AdvancedGit.CurrentBranch)
            ? Record.SelectedWorkspaceBranch is { Length: > 0 } ? Record.SelectedWorkspaceBranch : "Unknown"
            : Snapshot.Safety.AdvancedGit.CurrentBranch;
    public string Services => IsLoading ? "Loading details..." : !HasSnapshot ? "Unavailable" : Snapshot!.Definition.Services.Count == 0 ? "No services" : string.Join(", ", Snapshot.Definition.Services);
    public string Features => IsLoading ? "Loading details..." : !HasSnapshot ? "Unavailable" : Snapshot!.Definition.Features.Count == 0 ? "No features" : string.Join(", ", Snapshot.Definition.Features);
    public string WorkspaceTypeLabel => IsLoading
        ? "Loading workspace type..."
        : !HasSnapshot
        ? "Workspace"
        : FormatWorkspaceTypeLabel(Snapshot!.Definition);
    public IReadOnlyList<string> ServiceDisplayItems => IsLoading ? [] : BuildServiceDisplayItems();
    public bool HasServiceDisplayItems => ServiceDisplayItems.Count > 0;
    public string LastActivity => IsLoading
        ? string.IsNullOrWhiteSpace(Item.LoadingStatusMessage) ? "Loading details..." : Item.LoadingStatusMessage
        : HasError
        ? _lastActivityOverride ?? ErrorMessage
        : !string.IsNullOrWhiteSpace(_lastActivityOverride)
        ? _lastActivityOverride!
        : Snapshot?.Readiness is not null
        ? Snapshot.Readiness.Summary
        : !string.IsNullOrWhiteSpace(Snapshot!.Health.Summary)
            ? Snapshot.Health.Summary
            : string.IsNullOrWhiteSpace(Snapshot.Record.LastOperationResult)
                ? "No recent activity"
                : Snapshot.Record.LastOperationResult!;
    public string SafetyState => IsLoading ? "Workspace details are still loading." : HasError ? "Workspace record exists but the workspace could not be loaded." : Snapshot!.Safety.Headline;
    public string RepositoryStatus => IsLoading
        ? string.IsNullOrWhiteSpace(Item.LoadingStatusMessage) ? "Loading details..." : Item.LoadingStatusMessage
        : HasError
        ? "Workspace needs review before it can be opened safely."
        : string.IsNullOrWhiteSpace(Snapshot!.Safety.AdvancedGit.StatusSummary)
            ? CurrentBranch
            : Snapshot.Safety.AdvancedGit.StatusSummary;
    public string LocalRuntimeStateStatus => IsLoading
        ? "Loading..."
        : !HasSnapshot
        ? "Unavailable"
        : Snapshot!.LocalRuntimeState is null
        ? "Missing"
        : string.IsNullOrWhiteSpace(Snapshot.LocalRuntimeState.ResolvedPlatform)
            ? "Loaded"
            : $"Loaded ({Snapshot.LocalRuntimeState.ResolvedPlatform})";
    public string RuntimeTarget => IsLoading ? "Loading..." : HasSnapshot ? Snapshot!.ResolvedRuntimePlan?.TargetPlatform ?? "Unknown" : "Unavailable";
    public WorkspaceHealthSnapshot? Health => Snapshot?.Health;
    public WorkspaceReadinessSnapshot? Readiness => Snapshot?.Readiness;
    public string ReadinessPrimaryActionLabel => Readiness is null ? string.Empty : WorkspaceReadinessPresentationFormatter.FormatPrimaryActionLabel(Readiness);
    public string ReadinessActivityLabel => Readiness is null ? string.Empty : WorkspaceReadinessPresentationFormatter.FormatActivityLabel(Readiness.CurrentActivity);
    public bool IsReadinessOperationInProgress => Readiness?.IsOperationInProgress == true;
    public string SafeWorkspaceName => BuildSafeWorkspaceToken(Name);
    public string RowAutomationId => $"WorkspaceRow_{SafeWorkspaceName}";
    public string RowAutomationName => $"WorkspaceRow_{SafeWorkspaceName}";
    public string TitleAutomationId => $"WorkspaceTitle_{SafeWorkspaceName}";
    public string TitleAutomationName => $"WorkspaceTitle_{SafeWorkspaceName}";
    public string SelectedMarkerAutomationId => "WorkspaceRow_Selected";
    public string SelectedMarkerAutomationName => "WorkspaceRow_Selected";
    public string? FailedOperationName => _failedOperationNameOverride ?? Record.LastOperationName;
    public bool HasTransientOperationFailure => !string.IsNullOrWhiteSpace(_failedOperationNameOverride) && !string.IsNullOrWhiteSpace(_lastActivityOverride);
    public string TransientOperationSummary => _lastActivityOverride ?? string.Empty;
    public string Headline
    {
        get => string.IsNullOrWhiteSpace(_headline) ? RuntimeStatusLabel : _headline;
        private set => SetProperty(ref _headline, value);
    }

    public string Summary
    {
        get => string.IsNullOrWhiteSpace(_summary) ? LastActivity : _summary;
        private set => SetProperty(ref _summary, value);
    }

    public string Recommendation
    {
        get => _recommendation;
        private set
        {
            if (SetProperty(ref _recommendation, value))
            {
                RaisePropertyChanged(nameof(HasRecommendation));
            }
        }
    }

    public bool HasRecommendation => !string.IsNullOrWhiteSpace(Recommendation);

    public ActionItemViewModel? PrimaryAction
    {
        get => _primaryAction;
        private set
        {
            if (SetProperty(ref _primaryAction, value))
            {
                RaisePropertyChanged(nameof(HasPrimaryAction));
            }
        }
    }

    public bool HasPrimaryAction => PrimaryAction is not null;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void SetReprovisioningState(string message)
    {
        _runtimeStatusLabelOverride = "Reprovisioning";
        _lastActivityOverride = message;
        RaiseWorkspaceDisplayChanged();
    }

    public void SetOperationFailureState(string message, string? operationName = null)
    {
        _runtimeStatusLabelOverride = "Error";
        _lastActivityOverride = message;
        _failedOperationNameOverride = operationName;
        RaiseWorkspaceDisplayChanged();
    }

    public void ClearTransientOperationState()
    {
        if (_runtimeStatusLabelOverride is null && _lastActivityOverride is null && _failedOperationNameOverride is null)
        {
            return;
        }

        _runtimeStatusLabelOverride = null;
        _lastActivityOverride = null;
        _failedOperationNameOverride = null;
        RaiseWorkspaceDisplayChanged();
    }

    public void ApplyPresentation(WorkspacePresentation presentation)
    {
        Headline = presentation.Headline;
        Summary = presentation.Summary;
        Recommendation = presentation.Recommendation;
        PrimaryAction = presentation.PrimaryAction;
    }

    private string DisplayNameFromRecord()
        => string.IsNullOrWhiteSpace(Record.Name) ? Record.RootPath : Record.Name;

    private static string BuildSafeWorkspaceToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unnamed";
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    private void RaiseWorkspaceDisplayChanged()
    {
        RaisePropertyChanged(nameof(RuntimeStatusLabel));
        RaisePropertyChanged(nameof(LastActivity));
        RaisePropertyChanged(nameof(Readiness));
        RaisePropertyChanged(nameof(ReadinessPrimaryActionLabel));
        RaisePropertyChanged(nameof(ReadinessActivityLabel));
        RaisePropertyChanged(nameof(IsReadinessOperationInProgress));
        RaisePropertyChanged(nameof(Headline));
        RaisePropertyChanged(nameof(Summary));
        RaisePropertyChanged(nameof(Health));
    }

    private static string FormatHealthStatusLabel(WorkspaceHealthStatus status)
        => status switch
        {
            WorkspaceHealthStatus.Healthy => "Healthy",
            WorkspaceHealthStatus.Attention => "Attention",
            WorkspaceHealthStatus.Degraded => "Degraded",
            WorkspaceHealthStatus.Unavailable => "Unavailable",
            WorkspaceHealthStatus.Provisioning => "Provisioning",
            WorkspaceHealthStatus.Investigating => "Investigating",
            _ => "Healthy",
        };

    private static string FormatWorkspaceTypeLabel(WorkspaceDefinition definition)
        => OracleWorkspaceFamily.Detect(definition) switch
        {
            OracleWorkspaceKind.ApexLang => "Oracle APEX Workspace",
            OracleWorkspaceKind.Apex => "Oracle APEX Workspace",
            OracleWorkspaceKind.PlSql => "Oracle Database Workspace",
            _ when definition.Services.Contains("postgres", StringComparer.OrdinalIgnoreCase) => "Postgres Workspace",
            _ when definition.Services.Contains("pgadmin", StringComparer.OrdinalIgnoreCase) => "Postgres Workspace",
            _ when definition.Services.Count > 0 => "Service Workspace",
            _ => "Workspace",
        };

    private IReadOnlyList<string> BuildServiceDisplayItems()
    {
        if (!HasSnapshot)
        {
            return [];
        }

        if (Snapshot!.AvailableServices.Count > 0)
        {
            return Snapshot.AvailableServices
                .Select(service => service.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return Snapshot.Definition.Services
            .Where(service => !string.IsNullOrWhiteSpace(service))
            .Select(FormatFallbackServiceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatFallbackServiceName(string serviceId)
        => serviceId switch
        {
            "oracle-ords" => "REST APIs",
            "oracle-demo" => "Oracle Database",
            "postgres" => "Postgres",
            "pgadmin" => "pgAdmin",
            _ => string.Join(' ', serviceId.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..]))
        };
}
