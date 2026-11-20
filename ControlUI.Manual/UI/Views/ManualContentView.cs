using Jamesnet.Wpf.Controls;
using System.Windows;
using ControlUI.Manual.Local.ViewModels;

namespace ControlUI.Manual.UI.Views
{
    public class ManualContentView : JamesContent
    {
        public ManualContentView()
        {
            Loaded += (_, _) => UpdateMonitoring();
            Unloaded += (_, _) => (DataContext as ManualContentViewModel)?.SetMonitoring(false);
            IsVisibleChanged += (_, _) => UpdateMonitoring();
            DataContextChanged += (_, args) =>
            {
                (args.OldValue as ManualContentViewModel)?.SetMonitoring(false);
                UpdateMonitoring();
            };
        }

        private void UpdateMonitoring() => (DataContext as ManualContentViewModel)?.SetMonitoring(IsLoaded && IsVisible);

        static ManualContentView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ManualContentView),
                new FrameworkPropertyMetadata(typeof(ManualContentView)));
        }
    }
}
