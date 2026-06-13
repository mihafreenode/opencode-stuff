using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCode.Workspace.Manager;

public partial class QuickTutorialImageWindow : Window
{
    private double _zoom = 1.0;

    public QuickTutorialImageWindow()
    {
        InitializeComponent();
        FullImage.LayoutTransform = new ScaleTransform(_zoom, _zoom);
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
        => Close();

    private void ImageScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        e.Handled = true;
        _zoom = e.Delta > 0 ? Math.Min(_zoom + 0.15, 4.0) : Math.Max(_zoom - 0.15, 0.5);
        FullImage.LayoutTransform = new ScaleTransform(_zoom, _zoom);
    }
}
