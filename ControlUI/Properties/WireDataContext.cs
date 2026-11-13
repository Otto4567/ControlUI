using Jamesnet.Wpf.Global.Location;
using ControlUI.Forms.Local.ViewModels;
using ControlUI.Forms.UI.Views;

namespace ControlUI.Properties
{
    internal class WireDataContext : ViewModelLocationScenario
    {
        protected override void Match(ViewModelLocatorCollection items)
        {
            items.Register<ShellWindow, ShellWindowViewModel>();
        }
    }
}