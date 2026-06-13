using System.IO;
using System.Linq;
using System.Windows.Media;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Manager.Services;

namespace OpenCode.Workspace.Manager.ViewModels;

public sealed class WorkspaceListItemViewModel : ObservableObject
{
    private static readonly Brush RunningBrush = CreateBrush("#FFDCFCE7");
    private static readonly Brush RunningForegroundBrush = CreateBrush("#FF166534");
    private static readonly Brush StoppedBrush = CreateBrush("#FFE5E7EB");
    private static readonly Brush StoppedForegroundBrush = CreateBrush("#FF374151");
    private static readonly Brush NeedsPrepareBrush = CreateBrush("#FFFEF3C7");
    private static readonly Brush NeedsPrepareForegroundBrush = CreateBrush("#FF92400E");
    private static readonly Brush ErrorBrush = CreateBrush("#FFFEE2E2");
    private static readonly Brush ErrorForegroundBrush = CreateBrush("#FFB91C1C");
    private static readonly Brush ProtectedBrush = CreateBrush("#FFDBEAFE");
    private static readonly Brush ProtectedForegroundBrush = CreateBrush("#FF1D4ED8");

    private readonly PoLocalizationService _localization;
    private WorkspaceSnapshot _snapshot;

    public WorkspaceListItemViewModel(WorkspaceSnapshot snapshot, PoLocalizationService localization)
    {
        _snapshot = snapshot;
        _localization = localization;
    }

    public WorkspaceSnapshot Snapshot
    {
        get => _snapshot;
        set
        {
            if (SetProperty(ref _snapshot, value))
            {
                RaiseAllProperties();
            }
        }
    }

    public string Name => Snapshot.Definition.Workspace.Name;
    public string RootPath => Snapshot.Paths.RootPath;
    public string ShortRootPath => ShortenPath(Snapshot.Paths.RootPath);
    public string Image => Snapshot.Definition.Workspace.Image;
    public string FeaturesSummary => string.Join(", ", Snapshot.Definition.Features.DefaultIfEmpty("core"));
    public string ServicesSummary => Snapshot.Definition.Services.Count == 0 ? _localization.Get("workspace.none") : string.Join(", ", Snapshot.Definition.Services);
    public string ServicesStatusSummary => BuildServicesStatusSummary();
    public WorkspaceRuntimeState RuntimeState => Snapshot.RuntimeState;
    public bool IsRunning => Snapshot.RuntimeState == WorkspaceRuntimeState.Running;
    public bool HasUpdateAvailable => Snapshot.UpdateRequired;
    public bool HasError => Snapshot.Record.LastOperationSucceeded == false || Snapshot.RuntimeState == WorkspaceRuntimeState.Unknown;
    public string StatusLabel => HasError
        ? _localization.Get("workspace.status.error")
        : HasUpdateAvailable
            ? _localization.Get("workspace.status.updateAvailable")
            : Snapshot.RuntimeState == WorkspaceRuntimeState.Running
                ? _localization.Get("workspace.status.running")
                : _localization.Get("workspace.status.stopped");
    public string LastOperationResult => string.IsNullOrWhiteSpace(Snapshot.Record.LastOperationResult)
        ? _localization.Get("workspace.lastOperation.none")
        : Snapshot.Record.LastOperationResult!;
    public string LastOperationSummary => ShortenLine(LastOperationResult, 96);
    public string SafetyStatusLabel => Snapshot.Safety.OverallStatus switch
    {
        WorkspaceSafetyLevel.Protected => _localization.Get("safety.status.protected"),
        WorkspaceSafetyLevel.PartiallyProtected => _localization.Get("safety.status.partiallyProtected"),
        WorkspaceSafetyLevel.AtRisk => _localization.Get("safety.status.atRisk"),
        WorkspaceSafetyLevel.NeedsReview => _localization.Get("safety.status.needsReview"),
        _ => Snapshot.Safety.Headline,
    };
    public string SafetyMessage => Snapshot.Safety.Message;
    public string SafetySummary => ShortenLine(Snapshot.Safety.Message, 96);
    public Brush SafetyBrush => Snapshot.Safety.OverallStatus == WorkspaceSafetyLevel.Protected ? ProtectedBrush : StatusBrush;
    public Brush SafetyForegroundBrush => Snapshot.Safety.OverallStatus == WorkspaceSafetyLevel.Protected ? ProtectedForegroundBrush : StatusForegroundBrush;
    public Brush StatusBrush => HasError
        ? ErrorBrush
        : HasUpdateAvailable
            ? NeedsPrepareBrush
            : Snapshot.RuntimeState == WorkspaceRuntimeState.Running
                ? RunningBrush
                : StoppedBrush;
    public Brush StatusForegroundBrush => HasError
        ? ErrorForegroundBrush
        : HasUpdateAvailable
            ? NeedsPrepareForegroundBrush
            : Snapshot.RuntimeState == WorkspaceRuntimeState.Running
                ? RunningForegroundBrush
                : StoppedForegroundBrush;

    private void RaiseAllProperties()
    {
        RaisePropertyChanged(nameof(Name));
        RaisePropertyChanged(nameof(RootPath));
        RaisePropertyChanged(nameof(ShortRootPath));
        RaisePropertyChanged(nameof(Image));
        RaisePropertyChanged(nameof(FeaturesSummary));
        RaisePropertyChanged(nameof(ServicesSummary));
        RaisePropertyChanged(nameof(ServicesStatusSummary));
        RaisePropertyChanged(nameof(RuntimeState));
        RaisePropertyChanged(nameof(IsRunning));
        RaisePropertyChanged(nameof(HasUpdateAvailable));
        RaisePropertyChanged(nameof(HasError));
        RaisePropertyChanged(nameof(StatusLabel));
        RaisePropertyChanged(nameof(LastOperationResult));
        RaisePropertyChanged(nameof(LastOperationSummary));
        RaisePropertyChanged(nameof(SafetyStatusLabel));
        RaisePropertyChanged(nameof(SafetyMessage));
        RaisePropertyChanged(nameof(SafetySummary));
        RaisePropertyChanged(nameof(SafetyBrush));
        RaisePropertyChanged(nameof(SafetyForegroundBrush));
        RaisePropertyChanged(nameof(StatusBrush));
        RaisePropertyChanged(nameof(StatusForegroundBrush));
    }

    private string BuildServicesStatusSummary()
    {
        if (Snapshot.Definition.Services.Count == 0)
        {
            return _localization.Get("workspace.services.noneEnabled");
        }

        var serviceState = Snapshot.RuntimeState switch
        {
            WorkspaceRuntimeState.Running => _localization.Get("workspace.services.state.running"),
            WorkspaceRuntimeState.Stopped => _localization.Get("workspace.services.state.stopped"),
            _ => _localization.Get("workspace.services.state.unknown"),
        };

        return string.Join(", ", Snapshot.Definition.Services.Select(service => $"{service}: {serviceState}"));
    }

    private static string ShortenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length <= 52)
        {
            return path;
        }

        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (parts.Length < 3)
        {
            return ShortenLine(path, 52);
        }

        return $"...{Path.DirectorySeparatorChar}{parts[^2]}{Path.DirectorySeparatorChar}{parts[^1]}";
    }

    private static string ShortenLine(string text, int maxLength)
    {
        var firstLine = text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
        if (firstLine.Length <= maxLength)
        {
            return firstLine;
        }

        return $"{firstLine[..(maxLength - 3)]}...";
    }

    private static Brush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}
