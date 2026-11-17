using Jamesnet.Wpf.Controls;
using Prism.Ioc;
using Prism.Modularity;
using ControlUI.Main.UI.Views;
using ControlUI.Location.UI.Views;
using ControlUI.Operation.UI.Views;
using ControlUI.Manual.UI.Views;
using ControlUI.Sequence.UI.Views;
using ControlUI.Settings.UI.Views;

namespace ControlUI.Properties
{
    internal class ViewModules : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterSingleton<IViewable, MainContent>("MainContent");
            containerRegistry.RegisterSingleton<IViewable, LocationContent>("LocationContent");
            containerRegistry.RegisterSingleton<IViewable, OperationContentView>("OperationContentView");
            containerRegistry.RegisterSingleton<IViewable, ManualContentView>("ManualContentView");
            containerRegistry.RegisterSingleton<IViewable, SequenceContentView>("SequenceContentView");
            containerRegistry.RegisterSingleton<IViewable, SettingsContentView>("SettingsContentView");
        }
    }
}