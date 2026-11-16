using Jamesnet.Wpf.Controls;
using Prism.Ioc;
using Prism.Modularity;
using ControlUI.Main.UI.Views;
using ControlUI.Location.UI.Views;

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
        }
    }
}