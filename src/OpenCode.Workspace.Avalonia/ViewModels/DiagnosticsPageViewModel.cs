using System.Collections.ObjectModel;
using System.Globalization;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Platform;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class DiagnosticsPageViewModel : PageViewModel
{
    private readonly IDiagnosticsShellService _diagnosticsShellService;
    private readonly Func<WorkspaceLoadReport> _workspaceLoadReportProvider;
    private IClipboardService? _clipboardService;
    private WorkspaceReference? _selectedWorkspaceTarget;
    private string _statusMessage;
    private DiagnosticItemViewModel? _selectedDoctorItem;
    private DiagnosticItemViewModel? _selectedValidationItem;
    private string _latestDoctorSummary = "Doctor has not been run yet.";
    private string _latestValidationSummary = "No platform validation has been run yet.";
    private string _latestValidationContext = string.Empty;
    private string _latestWorkspaceLoadSummary = "Workspace loading has not completed yet.";

    public DiagnosticsPageViewModel(IDiagnosticsShellService diagnosticsShellService, IEnumerable<WorkspaceReference> workspaceTargets, Func<WorkspaceLoadReport>? workspaceLoadReportProvider = null)
        : base("Diagnostics", "Checklist-style host and workspace diagnostics.")
    {
        _diagnosticsShellService = diagnosticsShellService;
        _workspaceLoadReportProvider = workspaceLoadReportProvider ?? (() => new WorkspaceLoadReport());
        _statusMessage = "Choose a workspace target and run a doctor or validation command.";
        WorkspaceTargets = new ObservableCollection<WorkspaceReference>(workspaceTargets);
        _selectedWorkspaceTarget = WorkspaceTargets.FirstOrDefault();
        RunDoctorCommand = new AsyncRelayCommand(RunDoctorAsync, () => SelectedWorkspaceTarget is not null);
        ValidateAmd64Command = new AsyncRelayCommand(() => ValidateAsync("linux/amd64"), () => SelectedWorkspaceTarget is not null);
        ValidateArm64Command = new AsyncRelayCommand(() => ValidateAsync("linux/arm64"), () => SelectedWorkspaceTarget is not null);
        UpdateDetail("Diagnostics", _statusMessage);
    }

    public ObservableCollection<WorkspaceReference> WorkspaceTargets { get; }
    public ObservableCollection<DiagnosticItemViewModel> RequiredDoctorItems { get; } = [];
    public ObservableCollection<DiagnosticItemViewModel> DoctorItems { get; } = [];
    public ObservableCollection<DiagnosticItemViewModel> ValidationItems { get; } = [];
    public AsyncRelayCommand RunDoctorCommand { get; }
    public AsyncRelayCommand ValidateAmd64Command { get; }
    public AsyncRelayCommand ValidateArm64Command { get; }
    public bool HasDoctorResults => DoctorItems.Count > 0;
    public bool HasValidationResults => ValidationItems.Count > 0;

    public string LatestDoctorSummary
    {
        get => _latestDoctorSummary;
        private set => SetProperty(ref _latestDoctorSummary, value);
    }

    public string LatestValidationSummary
    {
        get => _latestValidationSummary;
        private set => SetProperty(ref _latestValidationSummary, value);
    }

    public string LatestValidationContext
    {
        get => _latestValidationContext;
        private set => SetProperty(ref _latestValidationContext, value);
    }

    public string LatestWorkspaceLoadSummary
    {
        get => _latestWorkspaceLoadSummary;
        private set => SetProperty(ref _latestWorkspaceLoadSummary, value);
    }

    public WorkspaceReference? SelectedWorkspaceTarget
    {
        get => _selectedWorkspaceTarget;
        set
        {
            if (SetProperty(ref _selectedWorkspaceTarget, value))
            {
                RunDoctorCommand.RaiseCanExecuteChanged();
                ValidateAmd64Command.RaiseCanExecuteChanged();
                ValidateArm64Command.RaiseCanExecuteChanged();
                if (SelectedDoctorItem is null && SelectedValidationItem is null)
                {
                    UpdateDetail("Diagnostics", _statusMessage);
                }
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public DiagnosticItemViewModel? SelectedDoctorItem
    {
        get => _selectedDoctorItem;
        set
        {
            if (SetProperty(ref _selectedDoctorItem, value) && value is not null)
            {
                ShowDiagnosticDetail(value, "Doctor check", LatestDoctorSummary);
            }
        }
    }

    public DiagnosticItemViewModel? SelectedValidationItem
    {
        get => _selectedValidationItem;
        set
        {
            if (SetProperty(ref _selectedValidationItem, value) && value is not null)
            {
                ShowDiagnosticDetail(value, "Validation check", LatestValidationSummary);
            }
        }
    }

    public async Task RunDoctorAsync()
    {
        RefreshWorkspaceLoadSummary();
        if (SelectedWorkspaceTarget is null)
        {
            return;
        }

        var result = await _diagnosticsShellService.RunDoctorAsync(SelectedWorkspaceTarget.RootPath);
        var hostCapabilities = await _diagnosticsShellService.DetectHostCapabilitiesAsync();
        var templateCatalog = _diagnosticsShellService.GetTemplateCatalogStatus();
        RequiredDoctorItems.Clear();
        DoctorItems.Clear();

        AddRequiredHostDiagnostic("Diagnostic_Git", "Git", hostCapabilities.FindEntry("tool.git"), "Install Git before using Save Points, Publish, and Recovery.");
        AddRequiredHostDiagnostic("Diagnostic_Docker", hostCapabilities.FindEntry("container.docker")?.DisplayName ?? "Docker", hostCapabilities.FindEntry("container.docker"), "Install Docker Desktop or Docker Engine and ensure docker is available.");
        AddRequiredHostDiagnostic("Diagnostic_DockerCompose", "Docker Compose", hostCapabilities.FindEntry("container.docker-compose"), "Install or enable docker compose before running packaged workspaces.");
        AddRequiredHostDiagnostic("Diagnostic_WindowsTerminal", "Windows Terminal", hostCapabilities.FindEntry("terminal.windows-terminal"), "Install Windows Terminal or enable its App Execution Alias before attach validation.");
        AddRequiredHostDiagnostic("Diagnostic_NerdFont", "Nerd Font", hostCapabilities.FindEntry("font.nerd-fonts"), "Install a supported Nerd Font such as JetBrainsMono Nerd Font.");
        AddRequiredHostDiagnostic("Diagnostic_OpenCodeCli", "OpenCode CLI", hostCapabilities.FindEntry("tool.opencode-cli"), "Install the OpenCode CLI and ensure opencode is available on PATH.");
        AddRequiredDoctorItem(new DiagnosticItemViewModel(
            "Template catalog",
            ToStatus(templateCatalog.IsAvailable),
            templateCatalog.IsAvailable ? $"Loaded {templateCatalog.TemplateCount} packaged template manifest(s)." : "The packaged template catalog did not load any templates.",
            ResultGuidance(templateCatalog.IsAvailable, "Verify the packaged catalog folder exists and includes template manifests."),
            $"Catalog root: {templateCatalog.CatalogRootPath}{Environment.NewLine}{templateCatalog.Detail}",
            "Diagnostic_TemplateCatalog"));
        AddRequiredDoctorItem(new DiagnosticItemViewModel("Host architecture", "Pass", hostCapabilities.Architecture, string.Empty, hostCapabilities.Platform.ToString(), "Diagnostic_HostArchitecture"));
        AddRequiredDoctorItem(new DiagnosticItemViewModel(
            "Runtime platform",
            ToStatus(!string.IsNullOrWhiteSpace(result.RuntimeState?.ResolvedPlatform ?? result.HostPlatform?.NativeContainerPlatform), string.IsNullOrWhiteSpace(result.RuntimeState?.ResolvedPlatform ?? result.HostPlatform?.NativeContainerPlatform)),
            result.RuntimeState?.ResolvedPlatform ?? result.HostPlatform?.NativeContainerPlatform ?? "Unavailable",
            ResultGuidance(!string.IsNullOrWhiteSpace(result.RuntimeState?.ResolvedPlatform ?? result.HostPlatform?.NativeContainerPlatform), "Run the workspace once or regenerate runtime state so the runtime platform can be recorded."),
            $"Requested native target: {result.HostPlatform?.NativeContainerPlatform ?? "unknown"}",
            "Diagnostic_RuntimePlatform"));

        foreach (var section in hostCapabilities.Sections)
        {
            foreach (var entry in section.Entries)
            {
                if (IsDedicatedDiagnostic(entry.Id))
                {
                    continue;
                }

                DoctorItems.Add(new DiagnosticItemViewModel(entry.DisplayName, ToStatus(entry.Status), entry.Summary, ResultGuidance(entry.Status == HostCapabilityStatus.Available, $"Review {section.DisplayName.ToLowerInvariant()} support on this host."), entry.Details));
            }
        }

        var host = result.HostPlatform;
        var docker = host?.Docker;
        DoctorItems.Add(new DiagnosticItemViewModel("Host platform", "Pass", hostCapabilities.Platform.ToString(), string.Empty, hostCapabilities.Architecture));
        DoctorItems.Add(new DiagnosticItemViewModel("Host OS", "Pass", host?.OperatingSystem.ToString() ?? "Unknown", string.Empty, host?.HostDescription));
        DoctorItems.Add(new DiagnosticItemViewModel("Docker CLI", ToStatus(docker?.CliAvailable == true), docker?.CliAvailable == true ? "Docker CLI is available." : "Docker CLI is not available.", ResultGuidance(docker?.CliAvailable == true, "Install Docker Desktop or Docker Engine and ensure docker is on PATH."), docker?.DiagnosticSummary));
        DoctorItems.Add(new DiagnosticItemViewModel("Docker Engine", ToStatus(docker?.EngineReachable == true), docker?.EngineReachable == true ? "Docker engine is reachable." : "Docker engine is not reachable.", ResultGuidance(docker?.EngineReachable == true, "Start Docker Desktop or the Docker daemon."), docker?.DiagnosticSummary));
        DoctorItems.Add(new DiagnosticItemViewModel("Buildx support", ToStatus(docker?.BuildxAvailable == true), docker?.BuildxAvailable == true ? BuildSupportedPlatformsSummary(docker!.SupportedPlatforms) : "Docker Buildx support is unavailable.", ResultGuidance(docker?.BuildxAvailable == true, "Enable Docker Buildx before validating multi-platform runtime targets."), docker?.DiagnosticSummary));
        DoctorItems.Add(new DiagnosticItemViewModel("ARM64 execution support", ToStatus(result.Arm64ExecutionSupportStatus != Arm64ExecutionSupportStatus.Unavailable, result.Arm64ExecutionSupportStatus == Arm64ExecutionSupportStatus.Unknown), result.Arm64ExecutionSupportDetails ?? result.Arm64ExecutionSupportStatus.ToString(), ResultGuidance(result.Arm64ExecutionSupportStatus != Arm64ExecutionSupportStatus.Unavailable, "Enable buildx or ARM64 execution support before validating linux/arm64.")));
        DoctorItems.Add(new DiagnosticItemViewModel("Workspace configuration", ToStatus(result.WorkspaceConfigurationStatus == WorkspaceConfigurationStatus.Found), result.WorkspaceConfigurationPath ?? "workspace configuration missing.", ResultGuidance(result.WorkspaceConfigurationStatus == WorkspaceConfigurationStatus.Found, "Open or create a valid workspace configuration file."), result.WorkspaceConfigurationError));
        DoctorItems.Add(new DiagnosticItemViewModel("Runtime-state status", ToStatus(result.RuntimeStateStatus == WorkspaceRuntimeStateReadStatus.Loaded, result.RuntimeStateStatus == WorkspaceRuntimeStateReadStatus.Missing), result.RuntimeStateStatus switch { WorkspaceRuntimeStateReadStatus.Loaded => "Runtime state loaded.", WorkspaceRuntimeStateReadStatus.Missing => "Runtime state file is missing.", WorkspaceRuntimeStateReadStatus.Corrupted => "Runtime state file is corrupted.", _ => result.RuntimeStateStatus.ToString() }, ResultGuidance(result.RuntimeStateStatus != WorkspaceRuntimeStateReadStatus.Corrupted, "Delete or regenerate .opencode/local/runtime-state.yaml."), result.RuntimeState?.ResolvedPlatform));
        RaisePropertyChanged(nameof(HasDoctorResults));
        LatestDoctorSummary = result.Recommendation;
        StatusMessage = result.Recommendation;
        SelectedDoctorItem = DoctorItems.FirstOrDefault(item => item.StatusLabel != "Pass") ?? DoctorItems.FirstOrDefault();
    }

    public async Task ValidateAsync(string targetPlatform)
    {
        RefreshWorkspaceLoadSummary();
        if (SelectedWorkspaceTarget is null)
        {
            return;
        }

        var report = await _diagnosticsShellService.ValidateAsync(SelectedWorkspaceTarget.RootPath, targetPlatform);
        ValidationItems.Clear();
        LatestValidationContext = $"Requested Target: {report.TargetPlatform}\nResolved Platform: {report.ResolvedPlatform ?? "Unavailable"}\nCompatibility: {report.CompatibilityDisplay ?? (report.ValidatedWithFallback ? "fallback" : "native")}";
        foreach (var check in report.Checks)
        {
            ValidationItems.Add(new DiagnosticItemViewModel(check.Name, ToStatus(check.Severity != DiagnosticSeverity.Error, check.Severity == DiagnosticSeverity.Warning), check.Message, check.Severity == DiagnosticSeverity.Error ? "Resolve this check and run validation again." : string.Empty, LatestValidationContext));
        }

        RaisePropertyChanged(nameof(HasValidationResults));
        LatestValidationSummary = report.Summary;
        StatusMessage = report.Summary;
        SelectedValidationItem = ValidationItems.FirstOrDefault(item => item.StatusLabel != "Pass") ?? ValidationItems.FirstOrDefault();
    }

    private void UpdateDetail(string title, string summary)
    {
        DetailTitle = title;
        DetailSummary = summary;
        DetailItems.Clear();
        if (SelectedWorkspaceTarget is not null)
        {
            DetailItems.Add(new DetailItemViewModel("Workspace", SelectedWorkspaceTarget.DisplayName));
            DetailItems.Add(new DetailItemViewModel("Root path", SelectedWorkspaceTarget.RootPath));
        }

        UpdateActionPanel();
    }

    private void ShowDiagnosticDetail(DiagnosticItemViewModel item, string category, string summary)
    {
        DetailTitle = item.Title;
        DetailSummary = summary;
        DetailItems.Clear();
        if (SelectedWorkspaceTarget is not null)
        {
            DetailItems.Add(new DetailItemViewModel("Workspace", SelectedWorkspaceTarget.DisplayName));
        }

        DetailItems.Add(new DetailItemViewModel("Category", category));
        DetailItems.Add(new DetailItemViewModel("Status", item.StatusLabel));
        DetailItems.Add(new DetailItemViewModel("Explanation", item.Description));
        if (item.HasContext)
        {
            DetailItems.Add(new DetailItemViewModel("Context", item.Context));
        }

        if (item.HasSuggestedAction)
        {
            DetailItems.Add(new DetailItemViewModel("Suggested action", item.SuggestedAction));
        }

        UpdateActionPanel();
    }

    private void UpdateActionPanel()
    {
        DetailActions.Clear();
        DetailActions.Add(new ActionItemViewModel("Run Doctor", "Refresh the current workspace doctor summary.", SelectedWorkspaceTarget is not null, string.Empty, RunDoctorCommand));
        DetailActions.Add(new ActionItemViewModel("Validate linux/amd64", "Validate direct or fallback amd64 runtime readiness.", SelectedWorkspaceTarget is not null, string.Empty, ValidateAmd64Command));
        DetailActions.Add(new ActionItemViewModel("Validate linux/arm64", "Validate ARM64 build and execution readiness.", SelectedWorkspaceTarget is not null, string.Empty, ValidateArm64Command));
        DetailActions.Add(new ActionItemViewModel("Copy Doctor Evidence", "Copy evidence-friendly packaged doctor output.", HasDoctorResults && _clipboardService is not null, BuildDoctorCopyDisabledReason(), new AsyncRelayCommand(CopyDoctorEvidenceAsync, () => HasDoctorResults && _clipboardService is not null)));
    }

    public void SetClipboardService(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
        UpdateActionPanel();
    }

    public string GetDoctorEvidenceText()
        => string.Join(Environment.NewLine + Environment.NewLine, DoctorItems.Select(item => item.EvidenceText));

    public void RefreshWorkspaceLoadSummary()
    {
        var report = _workspaceLoadReportProvider();
        if (report.RawRecordCount == 0 && report.TotalDuration == TimeSpan.Zero)
        {
            LatestWorkspaceLoadSummary = "Workspace loading has not completed yet.";
            return;
        }

        var slowest = report.SlowestTiming;
        LatestWorkspaceLoadSummary = slowest is null
            ? $"Last workspace load: {report.RawRecordCount} workspaces in {FormatDuration(report.TotalDuration)}."
            : $"Last workspace load: {report.RawRecordCount} workspaces in {FormatDuration(report.TotalDuration)}. Slowest stage: {slowest.StageLabel} for {slowest.WorkspaceName} in {FormatDuration(slowest.Duration)}.";
    }

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalMilliseconds >= 1000
            ? $"{duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s"
            : $"{Math.Max(1, duration.TotalMilliseconds).ToString("F0", CultureInfo.InvariantCulture)} ms";

    private static string ToStatus(bool success, bool warning = false)
        => warning ? "Warning" : success ? "Pass" : "Fail";

    private void AddRequiredHostDiagnostic(string automationId, string title, HostCapabilityEntry? entry, string nextStep)
    {
        if (entry is null)
        {
            AddRequiredDoctorItem(new DiagnosticItemViewModel(title, "Unknown", $"{title} status was not reported on this host.", nextStep, "The current host capability provider did not return this diagnostic row.", automationId));
            return;
        }

        AddRequiredDoctorItem(new DiagnosticItemViewModel(title, ToStatus(entry.Status), entry.Summary, ResultGuidance(entry.Status == HostCapabilityStatus.Available, nextStep), entry.Details, automationId));
    }

    private void AddRequiredDoctorItem(DiagnosticItemViewModel item)
    {
        RequiredDoctorItems.Add(item);
        DoctorItems.Add(item);
    }

    private static bool IsDedicatedDiagnostic(string entryId)
        => entryId is "tool.git"
            or "container.docker"
            or "container.docker-compose"
            or "terminal.windows-terminal"
            or "font.nerd-fonts"
            or "tool.opencode-cli";

    private static string ToStatus(HostCapabilityStatus status)
        => status switch
        {
            HostCapabilityStatus.Available => "Pass",
            HostCapabilityStatus.Warning => "Warning",
            HostCapabilityStatus.Unknown => "Unknown",
            _ => "Fail",
        };

    private static string ResultGuidance(bool success, string nextStep)
        => success ? string.Empty : nextStep;

    private static string BuildSupportedPlatformsSummary(IReadOnlyList<string> supportedPlatforms)
        => supportedPlatforms.Count == 0
            ? "Docker Buildx is available."
            : $"Docker Buildx supports {string.Join(", ", supportedPlatforms)}.";

    private string BuildDoctorCopyDisabledReason()
        => !HasDoctorResults
            ? "Run Doctor first to capture packaged diagnostics evidence."
            : _clipboardService is null
                ? "Clipboard is unavailable."
                : string.Empty;

    private Task CopyDoctorEvidenceAsync()
        => _clipboardService is null ? Task.CompletedTask : _clipboardService.SetTextAsync(GetDoctorEvidenceText());
}
