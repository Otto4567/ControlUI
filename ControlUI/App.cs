using System.Windows;
using Jamesnet.Wpf.Controls;
using ControlUI.Forms.UI.Views;

namespace ControlUI
{
    internal class App : JamesApplication
    {
        protected override Window CreateShell()
        {
            return new ShellWindow();
        }
    }
}