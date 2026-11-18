using Jamesnet.Wpf.Controls;
using System.Windows;
using ControlUI.Forms.UI.Views;

namespace ControlUI
{
    internal class App : JamesApplication
    {
        public App()
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml")
            });
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/HandyControl;component/Themes/Theme.xaml")
            });
        }

        protected override Window CreateShell()
        {
            return new ShellWindow();
        }
    }
}
