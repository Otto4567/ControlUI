using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Jamesnet.Wpf.Controls;
using Jamesnet.Wpf.Mvvm;
using ControlUI.Support.Local.Helpers;
using ControlUI.Support.Local.Models;

namespace ControlUI.Main.Local.ViewModels
{
    public partial class MainContentViewModel : ObservableBase, IViewLoadable
    {
        private readonly NavigationService _navigationService;

        public ObservableCollection<SideBarItemInfo> Menus { get; }
            = SideBarMenus.Create();

        [ObservableProperty]
        private SideBarItemInfo? _selectedMenu;

        public MainContentViewModel(NavigationService navigationService)
        {
            _navigationService = navigationService;
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

        // 사이드바에서 메뉴를 선택했을 때
        partial void OnSelectedMenuChanged(SideBarItemInfo? value)
        {
            if (value is null)
                return;

            _navigationService.Navigate(value.Key);
        }

        // 뒤로·앞으로 또는 새 메뉴로 이동했을 때
        private void OnNavigationChanged(object? sender, EventArgs e)
        {
            UpdateSelectedMenu();
        }

        private void UpdateSelectedMenu()
        {
            SelectedMenu = Menus.FirstOrDefault(
                menu => menu.Key == _navigationService.CurrentKey);
        }
    }
}