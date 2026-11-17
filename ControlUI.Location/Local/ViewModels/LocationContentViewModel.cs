using System;
using CommunityToolkit.Mvvm.Input;
using Jamesnet.Wpf.Mvvm;
using ControlUI.Support.Local.Helpers;

namespace ControlUI.Location.Local.ViewModels
{
    public partial class LocationContentViewModel : ObservableBase
    {
        private readonly NavigationService _navigationService;

        public LocationContentViewModel(NavigationService navigationService)
        {
            _navigationService = navigationService;
            _navigationService.NavigationChanged += OnNavigationChanged;
        }

        private void OnNavigationChanged(object? sender, EventArgs e)
        {
            BackCommand.NotifyCanExecuteChanged();
            ForwardCommand.NotifyCanExecuteChanged();
        }

        private bool CanGoBack()
        {
            return _navigationService.CanGoBack;
        }

        private bool CanGoForward()
        {
            return _navigationService.CanGoForward;
        }

        [RelayCommand(CanExecute = nameof(CanGoBack))]
        private void Back()
        {
            _navigationService.GoBack();
        }

        [RelayCommand(CanExecute = nameof(CanGoForward))]
        private void Forward()
        {
            _navigationService.GoForward();
        }
    }
}