using System.Windows;
using System.Windows.Controls;

namespace ControlUI.Operation.UI.Units
{
    public class DeviceStatusPanel : Control
    {
        static DeviceStatusPanel()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(DeviceStatusPanel),
                new FrameworkPropertyMetadata(typeof(DeviceStatusPanel)));
        }
    }
}
