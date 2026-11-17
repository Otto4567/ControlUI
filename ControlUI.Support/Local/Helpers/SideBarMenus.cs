using Jamesnet.Wpf.Controls;
using System.Collections.ObjectModel;
using ControlUI.Support.Local.Models;

namespace ControlUI.Support.Local.Helpers
{
    public static class SideBarMenus
    {
        public static ObservableCollection<SideBarItemInfo> Create()
        {
            return new()
            {
                new("operation", "Operation", IconType.Home),
                new("manual", "Manual", IconType.ViewColumn),
                new("sequence", "Sequence", IconType.FormatListBulleted),
                new("settings", "Settings", IconType.Cog)
            };
        }
    }
}