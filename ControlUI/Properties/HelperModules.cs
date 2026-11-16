using Prism.Ioc;
using Prism.Modularity;
using ControlUI.Support.Local.Models;
using ControlUI.Support.Local.Helpers;

namespace ControlUI.Properties
{
    internal class HelperModules : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterSingleton<ColorManager>();
            containerRegistry.RegisterSingleton<DirectoryManager>();
            containerRegistry.RegisterSingleton<FileService>();
            containerRegistry.RegisterSingleton<NavigatorService>();
        }
    }
}