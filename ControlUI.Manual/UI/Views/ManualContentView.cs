using Jamesnet.Wpf.Controls;
using System.Windows;

namespace ControlUI.Manual.UI.Views
{
    public class ManualContentView : JamesContent
    {
        static ManualContentView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ManualContentView),
                new FrameworkPropertyMetadata(typeof(ManualContentView)));
        }
    }
}