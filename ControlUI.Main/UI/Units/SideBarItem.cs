using System.Windows;
using System.Windows.Controls;

namespace ControlUI.Main.UI.Units
{
    public class SideBarItem : ListBoxItem
    {
        static SideBarItem()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(SideBarItem),
                new FrameworkPropertyMetadata(typeof(SideBarItem)));
        }
    }
}