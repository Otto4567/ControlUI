using Jamesnet.Wpf.Controls;
using System.Windows;
using ControlUI.Operation.Local.ViewModels;

namespace ControlUI.Operation.UI.Views
{
    public class OperationContentView : JamesContent
    {
        public OperationContentView()
        {
            Loaded += (_, _) => UpdateMonitoring();
            Unloaded += (_, _) => (DataContext as OperationContentViewModel)?.SetMonitoring(false);
            IsVisibleChanged += (_, _) => UpdateMonitoring();
            DataContextChanged += (_, args) =>
            {
                (args.OldValue as OperationContentViewModel)?.SetMonitoring(false);
                UpdateMonitoring();
            };
        }

        private void UpdateMonitoring() => (DataContext as OperationContentViewModel)?.SetMonitoring(IsLoaded && IsVisible);

        static OperationContentView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(OperationContentView),
                new FrameworkPropertyMetadata(typeof(OperationContentView)));
        }
    }
}
