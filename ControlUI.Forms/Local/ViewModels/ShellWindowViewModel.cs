using Jamesnet.Wpf.Controls;
using Jamesnet.Wpf.Mvvm;
using Prism.Ioc;
using Prism.Regions;
using System.Collections.Generic;
using ControlUI.Support.Local.Models;

namespace ControlUI.Forms.Local.ViewModels
{
    public class ShellWindowViewModel : ObservableBase, IViewLoadable
    {
        private readonly IContainerProvider _containerProvider;
        private readonly IRegionManager _regionManager;
        
        public List<FolderInfo> Roots { get; init; }

        public ShellWindowViewModel(IContainerProvider containerProvider,
            IRegionManager regionManager)
        {
            _containerProvider = containerProvider;
            _regionManager = regionManager;
        }

        public void OnLoaded(IViewable view)
        {
            IRegion mainRegion = _regionManager.Regions["MainRegion"];
            IViewable mainContent = _containerProvider.Resolve<IViewable>("MainContent");
            
            if (!mainRegion.Views.Contains(mainContent))
            {
                mainRegion.Add(mainContent);
            }
            mainRegion.Activate(mainContent);
        }
    }
}