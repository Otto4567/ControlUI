using Prism.Ioc;
using Prism.Modularity;
using ControlUI.Support.Services;
using ControlUI.Motion;

namespace ControlUI.Properties
{
    internal class HelperModules : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            var workspace = containerProvider.Resolve<ProjectWorkspace>();
            System.Windows.Application.Current.Exit += (_, _) => workspace.Save();
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterSingleton<NavigationService>();
            var app = (App)System.Windows.Application.Current;
            containerRegistry.RegisterInstance(app.EventLog);
            containerRegistry.RegisterInstance(app.MotionConfiguration);
            containerRegistry.RegisterSingleton<ProjectWorkspace>();
            containerRegistry.RegisterInstance<IUserDialogs>(new WpfUserDialogs());
            containerRegistry.RegisterInstance<IMotionService>(app.MotionService);
        }
    }
}
