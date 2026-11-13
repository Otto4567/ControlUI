using System.Windows;
using ControlUI.Support.UI.Units;

namespace ControlUI.Forms.UI.Views
{
    public class ShellWindow : DarkWindow
    {
        static ShellWindow()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ShellWindow),
                new FrameworkPropertyMetadata(typeof(ShellWindow)));
        }
    }
}