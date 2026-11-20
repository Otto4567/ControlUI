using Jamesnet.Wpf.Global.Location;
using ControlUI.Forms.Local.ViewModels;
using ControlUI.Forms.UI.Views;
using ControlUI.Location.Local.ViewModels;
using ControlUI.Location.UI.Views;
using ControlUI.Main.Local.ViewModels;
using ControlUI.Main.UI.Views;
using ControlUI.Operation.UI.Views;
using ControlUI.Operation.Local.ViewModels;
using ControlUI.Manual.Local.ViewModels;
using ControlUI.Manual.UI.Views;
using ControlUI.Sequence.Local.ViewModels;
using ControlUI.Sequence.UI.Views;
using ControlUI.Settings.Local.ViewModels;
using ControlUI.Settings.UI.Views;

namespace ControlUI.Properties
{
    internal class WireDataContext : ViewModelLocationScenario
    {
        protected override void Match(ViewModelLocatorCollection items)
        {
            items.Register<ShellWindow, ShellWindowViewModel>();
            items.Register<MainContent, MainContentViewModel>();
            items.Register<LocationContent, LocationContentViewModel>();
            items.Register<OperationContentView, OperationContentViewModel>();
            items.Register<ManualContentView, ManualContentViewModel>();
            items.Register<SequenceContentView, SequenceContentViewModel>();
            items.Register<SettingsContentView, SettingsContentViewModel>();
        }
    }
}
