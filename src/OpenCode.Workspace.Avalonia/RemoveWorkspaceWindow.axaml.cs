using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia;

public partial class RemoveWorkspaceWindow : Window
{
    private WorkspaceRemovalChoice _selectedChoice;
    private bool _confirmed;
    private readonly Border _registrationOnlyBorder;
    private readonly Border _dockerResourcesBorder;
    private readonly Border _deleteFilesBorder;
    private readonly RadioButton _registrationOnlyRadioButton;
    private readonly RadioButton _dockerResourcesRadioButton;
    private readonly RadioButton _deleteFilesRadioButton;
    private readonly TextBlock _deleteFilesUnavailableTextBlock;
    private readonly TextBlock _workspaceNameTextBlock;
    private readonly TextBlock _workspaceRootTextBlock;

    public RemoveWorkspaceWindow()
        : this(new WorkspaceRemovalPrompt { WorkspaceName = string.Empty, WorkspaceRoot = string.Empty })
    {
    }

    public RemoveWorkspaceWindow(WorkspaceRemovalPrompt prompt)
    {
        InitializeComponent();
        AppWindowIcons.Apply(this);
        _registrationOnlyBorder = this.FindControl<Border>("RegistrationOnlyBorder") ?? throw new InvalidOperationException("RegistrationOnlyBorder was not found.");
        _dockerResourcesBorder = this.FindControl<Border>("DockerResourcesBorder") ?? throw new InvalidOperationException("DockerResourcesBorder was not found.");
        _deleteFilesBorder = this.FindControl<Border>("DeleteFilesBorder") ?? throw new InvalidOperationException("DeleteFilesBorder was not found.");
        _registrationOnlyRadioButton = this.FindControl<RadioButton>("RegistrationOnlyRadioButton") ?? throw new InvalidOperationException("RegistrationOnlyRadioButton was not found.");
        _dockerResourcesRadioButton = this.FindControl<RadioButton>("DockerResourcesRadioButton") ?? throw new InvalidOperationException("DockerResourcesRadioButton was not found.");
        _deleteFilesRadioButton = this.FindControl<RadioButton>("DeleteFilesRadioButton") ?? throw new InvalidOperationException("DeleteFilesRadioButton was not found.");
        _deleteFilesUnavailableTextBlock = this.FindControl<TextBlock>("DeleteFilesUnavailableTextBlock") ?? throw new InvalidOperationException("DeleteFilesUnavailableTextBlock was not found.");
        _workspaceNameTextBlock = this.FindControl<TextBlock>("WorkspaceNameTextBlock") ?? throw new InvalidOperationException("WorkspaceNameTextBlock was not found.");
        _workspaceRootTextBlock = this.FindControl<TextBlock>("WorkspaceRootTextBlock") ?? throw new InvalidOperationException("WorkspaceRootTextBlock was not found.");
        _workspaceNameTextBlock.Text = prompt.WorkspaceName;
        _workspaceRootTextBlock.Text = prompt.WorkspaceRoot;
        _deleteFilesRadioButton.IsEnabled = prompt.DeleteWorkspaceFilesSupported;
        _deleteFilesUnavailableTextBlock.Text = prompt.DeleteWorkspaceFilesSupported ? string.Empty : prompt.DeleteWorkspaceFilesUnavailableReason;
        _deleteFilesUnavailableTextBlock.IsVisible = !prompt.DeleteWorkspaceFilesSupported && !string.IsNullOrWhiteSpace(prompt.DeleteWorkspaceFilesUnavailableReason);
        _selectedChoice = WorkspaceRemovalChoice.RegistrationOnly;
        Result = null;
        UpdateChoiceVisualState(_selectedChoice);
    }

    public WorkspaceRemovalDecision? Result { get; private set; }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void RegistrationOnlyChecked(object? sender, RoutedEventArgs e)
    {
        _selectedChoice = WorkspaceRemovalChoice.RegistrationOnly;
        UpdateChoiceVisualState(_selectedChoice);
    }

    private void DockerResourcesChecked(object? sender, RoutedEventArgs e)
    {
        _selectedChoice = WorkspaceRemovalChoice.DockerResources;
        UpdateChoiceVisualState(_selectedChoice);
    }

    private void DeleteFilesChecked(object? sender, RoutedEventArgs e)
    {
        _selectedChoice = WorkspaceRemovalChoice.DeleteFiles;
        UpdateChoiceVisualState(_selectedChoice);
    }

    private void CancelClicked(object? sender, RoutedEventArgs e)
    {
        _confirmed = false;
        Result = null;
        Close();
    }

    private void ConfirmClicked(object? sender, RoutedEventArgs e)
    {
        _selectedChoice = GetCurrentChoice();
        _confirmed = true;
        Result = new WorkspaceRemovalDecision { Choice = _selectedChoice };
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_confirmed)
        {
            Result = null;
        }

        base.OnClosing(e);
    }

    private void UpdateChoiceVisualState(WorkspaceRemovalChoice choice)
    {
        SetClasses(_registrationOnlyBorder, choice == WorkspaceRemovalChoice.RegistrationOnly ? ["option-card", "option-card-selected"] : ["option-card"]);
        SetClasses(_dockerResourcesBorder, choice == WorkspaceRemovalChoice.DockerResources ? ["option-card", "option-card-selected"] : ["option-card"]);
        SetClasses(_deleteFilesBorder, choice == WorkspaceRemovalChoice.DeleteFiles ? ["option-card", "option-card-danger", "option-card-danger-selected"] : ["option-card", "option-card-danger"]);
    }

    private static void SetClasses(Control control, IReadOnlyList<string> classes)
    {
        control.Classes.Clear();
        foreach (var @class in classes)
        {
            control.Classes.Add(@class);
        }
    }

    private WorkspaceRemovalChoice GetCurrentChoice()
    {
        if (_deleteFilesRadioButton.IsChecked == true)
        {
            return WorkspaceRemovalChoice.DeleteFiles;
        }

        if (_dockerResourcesRadioButton.IsChecked == true)
        {
            return WorkspaceRemovalChoice.DockerResources;
        }

        if (_registrationOnlyRadioButton.IsChecked == true)
        {
            return WorkspaceRemovalChoice.RegistrationOnly;
        }

        return WorkspaceRemovalChoice.RegistrationOnly;
    }
}
