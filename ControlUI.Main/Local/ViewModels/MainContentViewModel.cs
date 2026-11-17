using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Jamesnet.Wpf.Controls;
using Jamesnet.Wpf.Mvvm;
using Prism.Ioc;
using ControlUI.Support.Local.Helpers;
using ControlUI.Support.Local.Models;

namespace ControlUI.Main.Local.ViewModels
{
    public partial class MainContentViewModel : ObservableBase, IViewLoadable
    {
        private readonly NavigationService _navigationService;
        private readonly IContainerProvider _containerProvider;

        public ObservableCollection<SideBarItemInfo> Menus { get; }
            = SideBarMenus.Create();

        [ObservableProperty]
        private SideBarItemInfo? _selectedMenu;

        [ObservableProperty]
        private object? _currentContent;

        public MainContentViewModel(
            NavigationService navigationService,
            IContainerProvider containerProvider)
        {
            _navigationService = navigationService;
            _containerProvider = containerProvider;

            _navigationService.NavigationChanged += OnNavigationChanged;
        }

        public void OnLoaded(IViewable view)
        {
            if (_navigationService.CurrentKey is null)
            {
                var firstMenu = Menus.FirstOrDefault();

                if (firstMenu is not null)
                    _navigationService.Navigate(firstMenu.Key);
            }

            UpdateSelectedMenu();
        }

        partial void OnSelectedMenuChanged(SideBarItemInfo? value)
        {
            if (value is null)
                return;

            _navigationService.Navigate(value.Key);
        }

        private void OnNavigationChanged(object? sender, EventArgs e)
        {
            UpdateSelectedMenu();
        }

        private void UpdateSelectedMenu()
        {
            SelectedMenu = Menus.FirstOrDefault(
                menu => menu.Key == _navigationService.CurrentKey);

            CurrentContent = _navigationService.CurrentKey switch
            {
                "operation" =>
                    _containerProvider.Resolve<IViewable>(
                        "OperationContentView"),

                "manual" =>
                    _containerProvider.Resolve<IViewable>(
                        "ManualContentView"),

                "sequence" =>
                    _containerProvider.Resolve<IViewable>(
                        "SequenceContentView"),

                "settings" =>
                    _containerProvider.Resolve<IViewable>(
                        "SettingsContentView"),

                _ => null
            };
        }
    }
}