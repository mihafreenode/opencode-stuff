using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OpenCode.Workspace.AppSupport;

namespace OpenCode.Workspace.Avalonia;

public partial class OracleSoftwareNoticeWindow : Window
{
    private readonly Button _confirmButton;
    private readonly CheckBox _acknowledgeCheckBox;

    public OracleSoftwareNoticeWindow()
        : this(new OracleSoftwareNoticePrompt
        {
            Title = string.Empty,
            SubjectName = string.Empty,
            Summary = string.Empty,
            Facts = Array.Empty<string>(),
            AcknowledgementLabel = string.Empty,
            ConfirmLabel = "Continue",
            CancelLabel = "Cancel",
        })
    {
    }

    public OracleSoftwareNoticeWindow(OracleSoftwareNoticePrompt prompt)
    {
        InitializeComponent();
        (this.FindControl<TextBlock>("TitleTextBlock") ?? throw new InvalidOperationException("TitleTextBlock was not found.")).Text = prompt.Title;
        (this.FindControl<TextBlock>("SummaryTextBlock") ?? throw new InvalidOperationException("SummaryTextBlock was not found.")).Text = prompt.Summary;
        (this.FindControl<TextBlock>("SubjectTextBlock") ?? throw new InvalidOperationException("SubjectTextBlock was not found.")).Text = prompt.SubjectName;
        (this.FindControl<ItemsControl>("FactsItemsControl") ?? throw new InvalidOperationException("FactsItemsControl was not found.")).ItemsSource = prompt.Facts.Select(item => new TextBlock { Text = $"- {item}", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
        _acknowledgeCheckBox = this.FindControl<CheckBox>("AcknowledgeCheckBox") ?? throw new InvalidOperationException("AcknowledgeCheckBox was not found.");
        _acknowledgeCheckBox.Content = prompt.AcknowledgementLabel;
        _confirmButton = this.FindControl<Button>("ConfirmButton") ?? throw new InvalidOperationException("ConfirmButton was not found.");
        _confirmButton.Content = prompt.ConfirmLabel;
        (this.FindControl<Button>("CancelButton") ?? throw new InvalidOperationException("CancelButton was not found.")).Content = prompt.CancelLabel;
        UpdateConfirmState();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);

    private void ConfirmClicked(object? sender, RoutedEventArgs e) => Close(true);

    private void AcknowledgeChanged(object? sender, RoutedEventArgs e) => UpdateConfirmState();

    private void UpdateConfirmState() => _confirmButton.IsEnabled = _acknowledgeCheckBox.IsChecked == true;
}
