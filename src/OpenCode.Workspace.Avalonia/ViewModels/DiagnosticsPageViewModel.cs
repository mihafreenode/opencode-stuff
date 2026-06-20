using System.Collections.ObjectModel;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class DiagnosticsPageViewModel : PageViewModel
{
    private readonly IDiagnosticsShellService _diagnosticsShellService;
    private WorkspaceReference? _selectedWorkspaceTarget;
    private string _statusMessage;

    public DiagnosticsPageViewModel(IDiagnosticsShellService diagnosticsShellService, IEnumerable<WorkspaceReference> workspaceTargets)
        : base("Diagnostics", "Checklist-style host and workspace diagnostics.")
    {
        _diagnosticsShellService = diagnosticsShellService;
        _statusMessage = "Choose a workspace target and run a doctor or validation command.";
        WorkspaceTargets = new ObservableCollection<WorkspaceReference>(workspaceTargets);
        _selectedWorkspaceTarget = WorkspaceTargets.FirstOrDefault();
        RunDoctorCommand = new AsyncRelayCommand(RunDoctorAsync, () => SelectedWorkspaceTarget is not null);
        ValidateAmd64Command = new AsyncRelayCommand(() => ValidateAsync("linux/amd64"), () => SelectedWorkspaceTarget is not null);
        ValidateArm64Command = new AsyncRelayCommand(() => ValidateAsync("linux/arm64"), () => SelectedWorkspaceTarget is not null);
        UpdateDetail("Diagnostics", _statusMessage);
    }

    public ObservableCollection<WorkspaceReference> WorkspaceTargets { get; }
    public ObservableCollection<DiagnosticItemViewModel> DoctorItems { get; } = [];
    public ObservableCollection<DiagnosticItemViewModel> ValidationItems { get; } = [];
    public AsyncRelayCommand RunDoctorCommand { get; }
    public AsyncRelayCommand ValidateAmd64Command { get; }
    public AsyncRelayCommand ValidateArm64Command { get; }

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
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task RunDoctorAsync()
    {
        if (SelectedWorkspaceTarget is null)
        {
            return;
        }

        var result = await _diagnosticsShellService.RunDoctorAsync(SelectedWorkspaceTarget.RootPath);
        DoctorItems.Clear();
        DoctorItems.Add(new DiagnosticItemViewModel("Host OS", "Pass", result.HostPlatform?.HostDescription ?? "Unknown", string.Empty));
        DoctorItems.Add(new DiagnosticItemViewModel("Workspace config", ToStatus(result.WorkspaceConfigurationStatus == WorkspaceConfigurationStatus.Found), result.WorkspaceConfigurationPath ?? "workspace.yaml missing", ResultGuidance(result.WorkspaceConfigurationStatus == WorkspaceConfigurationStatus.Found, "Open or create a valid workspace.yaml.")));
        DoctorItems.Add(new DiagnosticItemViewModel("Runtime-state status", ToStatus(result.RuntimeStateStatus == WorkspaceRuntimeStateReadStatus.Loaded || result.RuntimeStateStatus == WorkspaceRuntimeStateReadStatus.Missing), result.RuntimeStateStatus.ToString(), ResultGuidance(result.RuntimeStateStatus != WorkspaceRuntimeStateReadStatus.Corrupted, "Delete or regenerate .opencode/local/runtime-state.yaml.")));
        DoctorItems.Add(new DiagnosticItemViewModel("Resolved platform", ToStatus(result.CanRun), result.ResolvedRuntimePlan?.TargetPlatform ?? "Unavailable", ResultGuidance(result.CanRun, result.Recommendation)));
        DoctorItems.Add(new DiagnosticItemViewModel("ARM64 execution support", ToStatus(result.Arm64ExecutionSupportStatus != Arm64ExecutionSupportStatus.Unavailable), result.Arm64ExecutionSupportDetails ?? result.Arm64ExecutionSupportStatus.ToString(), ResultGuidance(result.Arm64ExecutionSupportStatus != Arm64ExecutionSupportStatus.Unavailable, "Enable buildx or ARM64 execution support before validating linux/arm64.")));
        StatusMessage = result.Recommendation;
        UpdateDetail("Doctor results", result.Recommendation);
    }

    public async Task ValidateAsync(string targetPlatform)
    {
        if (SelectedWorkspaceTarget is null)
        {
            return;
        }

        var report = await _diagnosticsShellService.ValidateAsync(SelectedWorkspaceTarget.RootPath, targetPlatform);
        ValidationItems.Clear();
        foreach (var check in report.Checks)
        {
            ValidationItems.Add(new DiagnosticItemViewModel(check.Name, ToStatus(check.Severity != DiagnosticSeverity.Error, check.Severity == DiagnosticSeverity.Warning), check.Message, check.Severity == DiagnosticSeverity.Error ? "Resolve this check and run validation again." : string.Empty));
        }

        StatusMessage = report.Summary;
        UpdateDetail($"Validation {targetPlatform}", report.Summary);
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

        DetailActions.Clear();
        DetailActions.Add(new ActionItemViewModel("Run Doctor", "Refresh the current workspace doctor summary.", SelectedWorkspaceTarget is not null, string.Empty, RunDoctorCommand));
        DetailActions.Add(new ActionItemViewModel("Validate linux/amd64", "Validate direct or fallback amd64 runtime readiness.", SelectedWorkspaceTarget is not null, string.Empty, ValidateAmd64Command));
        DetailActions.Add(new ActionItemViewModel("Validate linux/arm64", "Validate ARM64 build and execution readiness.", SelectedWorkspaceTarget is not null, string.Empty, ValidateArm64Command));
    }

    private static string ToStatus(bool success, bool warning = false)
        => warning ? "Warning" : success ? "Pass" : "Fail";

    private static string ResultGuidance(bool success, string nextStep)
        => success ? string.Empty : nextStep;
}
