using Jamesnet.Wpf.Global.Location;
using ControlUI.Forms.Local.ViewModels;
using ControlUI.Forms.UI.Views;
using ControlUI.Main.Local.ViewModels;
using ControlUI.Main.UI.Views;

namespace ControlUI.Properties
{
    internal class WireDataContext : ViewModelLocationScenario
    {
        protected override void Match(ViewModelLocatorCollection items)
        {
            items.Register<MainContent, MainContentViewModel>();
            items.Register<ShellWindow, ShellWindowViewModel>();
        }
    }
}