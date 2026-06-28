using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia;

public partial class SavePointWindow : Window
{
    private readonly TextBox _messageTextBox;
    private readonly TextBlock _validationMessageTextBlock;

    public SavePointWindow()
        : this(string.Empty)
    {
    }

    public SavePointWindow(string initialMessage)
    {
        InitializeComponent();
        AppWindowIcons.Apply(this);
        _messageTextBox = this.FindControl<TextBox>("MessageTextBox") ?? throw new InvalidOperationException("MessageTextBox was not found.");
        _validationMessageTextBlock = this.FindControl<TextBlock>("ValidationMessageTextBlock") ?? throw new InvalidOperationException("ValidationMessageTextBlock was not found.");
        _messageTextBox.Text = initialMessage;
        _messageTextBox.CaretIndex = _messageTextBox.Text?.Length ?? 0;
    }

    public SavePointDraft? Result { get; private set; }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void CancelClicked(object? sender, RoutedEventArgs e)
    {
        StartupLog.WriteGlobal("Save Point dialog cancelled.");
        Result = null;
        Close();
    }

    private void ConfirmClicked(object? sender, RoutedEventArgs e)
    {
        var message = _messageTextBox.Text?.Trim() ?? string.Empty;
        StartupLog.WriteGlobal($"Save Point dialog confirm clicked. Message length: {message.Length}.");
        if (string.IsNullOrWhiteSpace(message))
        {
            _validationMessageTextBlock.Text = "Enter a Save Point message.";
            StartupLog.WriteGlobal("Save Point dialog rejected empty message.");
            return;
        }

        Result = new SavePointDraft { Message = message };
        StartupLog.WriteGlobal("Save Point dialog accepted message and is closing.");
        Close();
    }
}
