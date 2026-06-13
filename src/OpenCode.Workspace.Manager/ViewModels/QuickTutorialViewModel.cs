using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using OpenCode.Workspace.Manager.Models;

namespace OpenCode.Workspace.Manager.ViewModels;

public sealed class QuickTutorialViewModel : ObservableObject
{
    private readonly QuickTutorialDocument _document;
    private QuickTutorialStep? _selectedStep;

    public QuickTutorialViewModel(QuickTutorialDocument document)
    {
        _document = document;
        Steps = new ObservableCollection<QuickTutorialStep>(document.Steps);
        _selectedStep = Steps.FirstOrDefault();
        PreviousCommand = new RelayCommand(SelectPreviousStep, () => SelectedStepIndex > 0);
        NextCommand = new RelayCommand(SelectNextStep, () => SelectedStepIndex < Steps.Count - 1);
    }

    public ObservableCollection<QuickTutorialStep> Steps { get; }

    public RelayCommand PreviousCommand { get; }

    public RelayCommand NextCommand { get; }

    public string WindowTitle => _document.Title;

    public string CloseLabel => "Close";

    public string PreviousLabel => "Back";

    public string NextLabel => "Next";

    public string FinishLabel => "Finish";

    public string NextOrFinishLabel => IsLastStep ? FinishLabel : NextLabel;

    public string StepListLabel => "Steps";

    public string StepCounter => Steps.Count == 0 ? "0 of 0" : $"{SelectedStepIndex + 1} of {Steps.Count}";

    public string SelectedStepImagePath => SelectedStep is null ? string.Empty : ResolveImagePath(SelectedStep.ImagePath);

    public bool SelectedStepHasImage => !string.IsNullOrWhiteSpace(SelectedStepImagePath);

    public string SelectedStepImageCaption => SelectedStep?.Image.Caption ?? string.Empty;

    public QuickTutorialStep? SelectedStep
    {
        get => _selectedStep;
        set
        {
            if (SetProperty(ref _selectedStep, value))
            {
                RaisePropertyChanged(nameof(SelectedStepIndex));
                RaisePropertyChanged(nameof(StepCounter));
                RaisePropertyChanged(nameof(IsLastStep));
                RaisePropertyChanged(nameof(NextOrFinishLabel));
                RaisePropertyChanged(nameof(SelectedStepImagePath));
                RaisePropertyChanged(nameof(SelectedStepHasImage));
                RaisePropertyChanged(nameof(SelectedStepImageCaption));
                PreviousCommand.RaiseCanExecuteChanged();
                NextCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int SelectedStepIndex
        => SelectedStep is null ? -1 : Steps.IndexOf(SelectedStep);

    public bool IsLastStep => SelectedStepIndex >= Steps.Count - 1;

    private void SelectPreviousStep()
    {
        if (SelectedStepIndex <= 0)
        {
            return;
        }

        SelectedStep = Steps[SelectedStepIndex - 1];
    }

    private void SelectNextStep()
    {
        if (SelectedStepIndex < 0 || SelectedStepIndex >= Steps.Count - 1)
        {
            return;
        }

        SelectedStep = Steps[SelectedStepIndex + 1];
    }

    private static string ResolveImagePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return string.Empty;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, imagePath));
    }
}
