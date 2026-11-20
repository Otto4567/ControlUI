using Jamesnet.Wpf.Controls;
using System.Windows;

namespace ControlUI.Settings.UI.Views
{
    public class SettingsContentView : JamesContent
    {
        static SettingsContentView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(SettingsContentView),
                new FrameworkPropertyMetadata(typeof(SettingsContentView)));
        }
    }
}