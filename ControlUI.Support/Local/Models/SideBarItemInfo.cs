using Jamesnet.Wpf.Controls;

namespace ControlUI.Support.Local.Models
{
    public class SideBarItemInfo
    {
        public string Key { get; }
        public string Title { get; }
        public IconType Icon { get; }

        public SideBarItemInfo(string key, string title, IconType icon)
        {
            Key = key;
            Title = title;
            Icon = icon;
        }
    }
}