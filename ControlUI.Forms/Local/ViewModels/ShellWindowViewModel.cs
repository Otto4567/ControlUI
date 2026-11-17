using Jamesnet.Wpf.Controls;
using Jamesnet.Wpf.Mvvm;
using Prism.Ioc;
using Prism.Regions;
using CommunityToolkit.Mvvm.Input;
using System.Windows;


namespace ControlUI.Forms.Local.ViewModels
{
    public partial class ShellWindowViewModel : ObservableBase, IViewLoadable
    {
        private readonly IContainerProvider _containerProvider;

        private readonly IRegionManager _regionManager;

        public ShellWindowViewModel(IContainerProvider containerProvider, IRegionManager regionManager)
        {
            _containerProvider = containerProvider;
            _regionManager = regionManager;
        }

        public void OnLoaded(IViewable view)
        {
            ImportContent("MainContent", "MainRegion");
            ImportContent("LocationContent", "LocationRegion");
        }

        private void ImportContent(string name, string regionName)
        {
            IViewable content = _containerProvider.Resolve<IViewable>(name);
            IRegion region = _regionManager.Regions[regionName];
            if (!region.Views.Contains(content))
            {
                region.Add(content);
            }
            region.Activate(content);
        }

        [RelayCommand]
        private void ToggleTopmost(Window? window)
        {
            if (window is null) return;

            window.Topmost = !window.Topmost;
        }

        [RelayCommand]
        private void Minimize(Window? window)
        {
            if (window is null) return;

            SystemCommands.MinimizeWindow(window);
        }

        [RelayCommand]
        private void ToggleMaximize(Window? window)
        {
            if (window is null) return;

            if (window.WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(window);
            else
                SystemCommands.MaximizeWindow(window);
        }

        [RelayCommand]
        private void Close(Window? window)
        {
            if (window is null) return;

            SystemCommands.CloseWindow(window);
        }
    }
}