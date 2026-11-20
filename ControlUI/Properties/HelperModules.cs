using Prism.Ioc;
using Prism.Modularity;
using ControlUI.Support.Services;

namespace ControlUI.Properties
{
    internal class HelperModules : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterSingleton<NavigationService>();
            var app = (App)System.Windows.Application.Current;
            containerRegistry.RegisterInstance(app.EventLog);
            containerRegistry.RegisterInstance(app.MotionConfiguration);
            containerRegistry.RegisterInstance(app.Workspace);
            containerRegistry.RegisterInstance<IUserDialogs>(new WpfUserDialogs());
            containerRegistry.RegisterInstance<IMotionService>(app.MotionService);
        }
    }
}
