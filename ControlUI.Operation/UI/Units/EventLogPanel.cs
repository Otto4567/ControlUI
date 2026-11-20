using System.Windows;
using System.Windows.Controls;

namespace ControlUI.Operation.UI.Units
{
    public class EventLogPanel : Control
    {
        static EventLogPanel()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(EventLogPanel),
                new FrameworkPropertyMetadata(typeof(EventLogPanel)));
        }
    }
}
