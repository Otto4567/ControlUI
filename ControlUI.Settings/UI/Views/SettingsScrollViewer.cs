using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ControlUI.Settings.UI.Views;

public sealed class SettingsScrollViewer : ScrollViewer
{
    private int _wheelDelta;

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (e.Handled || ScrollableHeight <= 0 || SystemParameters.WheelScrollLines == 0)
            return;

        for (var element = e.OriginalSource as DependencyObject; element is not null && element != this;
             element = element is Visual or Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
        {
            if (element is ComboBox { IsDropDownOpen: true })
                return;
        }

        // Inner PropertyGrid editors can consume the bubbling wheel event.
        e.Handled = true;
        _wheelDelta += e.Delta;
        int notches = _wheelDelta / Mouse.MouseWheelDeltaForOneLine;
        _wheelDelta %= Mouse.MouseWheelDeltaForOneLine;
        int lines = SystemParameters.WheelScrollLines;
        for (int i = 0; i < Math.Abs(notches); i++)
        {
            if (lines < 0)
            {
                if (notches > 0)
                    PageUp();
                else
                    PageDown();
                continue;
            }
            for (int line = 0; line < lines; line++)
            {
                if (notches > 0)
                    LineUp();
                else
                    LineDown();
            }
        }
    }
}
