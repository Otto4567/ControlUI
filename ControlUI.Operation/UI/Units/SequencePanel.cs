using System.Windows;
using System.Windows.Controls;

namespace ControlUI.Operation.UI.Units
{
    public class SequencePanel : Control
    {
        static SequencePanel()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(SequencePanel),
                new FrameworkPropertyMetadata(typeof(SequencePanel)));
        }
    }
}
