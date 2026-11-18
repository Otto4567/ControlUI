using System.Windows;
using System.Windows.Controls;

namespace ControlUI.Operation.UI.Units
{
    public class OperationControlPanel : Control
    {
        static OperationControlPanel()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(OperationControlPanel),
                new FrameworkPropertyMetadata(typeof(OperationControlPanel)));
        }
    }
}
