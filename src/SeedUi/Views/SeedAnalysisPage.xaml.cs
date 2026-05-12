using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SeedUi.Views;

public partial class SeedAnalysisPage : UserControl
{
    public SeedAnalysisPage()
    {
        InitializeComponent();
    }

    private void InnerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not UIElement element)
        {
            return;
        }

        e.Handled = true;
        var eventArgs = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };

        var parent = FindParentScrollViewer(element);
        parent?.RaiseEvent(eventArgs);
    }

    private static ScrollViewer? FindParentScrollViewer(DependencyObject child)
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }
}
