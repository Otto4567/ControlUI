using System.Windows;
using System.Windows.Controls;

namespace ControlUI.Support.UI.Units
{
    public class TopmostButton : Button
    {
        static TopmostButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(TopmostButton),
                new FrameworkPropertyMetadata(typeof(TopmostButton)));
        }
    }
}