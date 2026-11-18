using System.Windows;
using System.Windows.Controls;

namespace ControlUI.Operation.UI.Units
{
    public class SystemStatusPanel : Control
    {
        static SystemStatusPanel()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(SystemStatusPanel),
                new FrameworkPropertyMetadata(typeof(SystemStatusPanel)));
        }
    }
}
