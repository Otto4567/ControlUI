using System.Windows;
using System.Windows.Controls;

namespace ControlUI.Main.UI.Units
{
    public class PolygonSelector : ListBox
    {
        static PolygonSelector()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(PolygonSelector),
                new FrameworkPropertyMetadata(typeof(PolygonSelector)));
        }
        protected override DependencyObject GetContainerForItemOverride()
        {
            return new PolygonSelectorItem();
        }
    }
}
