using System.Windows;
using System.Windows.Controls;

namespace ControlUI.Main.UI.Units
{
    public class SideBarView : ListBox
    {
        static SideBarView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(SideBarView),
                new FrameworkPropertyMetadata(typeof(SideBarView)));
        }

        protected override DependencyObject GetContainerForItemOverride()
        {
            return new SideBarItem();
        }

        protected override bool IsItemItsOwnContainerOverride(object item)
        {
            return item is SideBarItem;
        }
    }
}