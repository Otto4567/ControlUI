using Jamesnet.Wpf.Controls;
using System.Windows;
using ControlUI.Support.UI.Units;

namespace ControlUI
{
    internal class App : JamesApplication
    {
        protected override Window CreateShell()
        {
            return new DarkWindow();
        }
    }
}