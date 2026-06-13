using System.Windows;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Manager;

public partial class QuickTutorialWindow : Window
{
    public QuickTutorialWindow()
    {
        InitializeComponent();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
        => Close();

    private void NextOrFinish_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not QuickTutorialViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsLastStep)
        {
            Close();
            return;
        }

        viewModel.NextCommand.Execute(null);
    }

    private void OpenImage_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not QuickTutorialViewModel viewModel || !viewModel.SelectedStepHasImage)
        {
            return;
        }

        var imageWindow = new QuickTutorialImageWindow
        {
            Owner = this,
            DataContext = new QuickTutorialImageViewModel
            {
                ImageTitle = viewModel.SelectedStep?.Title ?? "Tutorial Image",
                ImagePath = viewModel.SelectedStepImagePath,
            },
        };

        imageWindow.ShowDialog();
    }
}
