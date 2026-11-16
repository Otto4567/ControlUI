using System.Windows;
using System.Windows.Controls;

namespace ControlUI.Location.UI.Units
{
    public class LocatorTextBox : TextBox
    {
        static LocatorTextBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(LocatorTextBox),
                new FrameworkPropertyMetadata(typeof(LocatorTextBox)));
        }
    }
}